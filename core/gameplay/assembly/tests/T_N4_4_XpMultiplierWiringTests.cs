using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Gameplay.Difficulty;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// T-N4-4（ADR-0033 决策 4"creature.tier_definition 新增 xp_multiplier；diff.tier 新增
    /// xp_multiplier"）：验证 <see cref="GameplayAssembly"/> 真的把
    /// <c>Core.Numbers.Progression.ProgressionOptions.ExtraXpMultiplierProvider</c> 接为"分档倍率 ×
    /// 难度倍率"——用真实 <see cref="GameplayAssembly"/>（含 <c>Carriers.Creatures</c>/
    /// <c>Difficulty</c> 两个依赖）走一次"生成生物 → 应用难度 → 死亡 → 击杀经验发放"的完整链路，
    /// 核对最终入账经验值精确等于 <c>baseAmount × tierMultiplier × difficultyMultiplier</c>（本用例
    /// 未登记 <c>combat.level_diff_table</c>，Δ 系数恒为 1，不参与验证）。惯例同
    /// <c>GameplayAssemblyDeathReloadTests</c>（真实装配 + 手工构造事件驱动，不经战斗管线）。
    /// </summary>
    public sealed class T_N4_4_XpMultiplierWiringTests
    {
        private static readonly Id PlayerId = new Id("unit.n44_player");
        private static readonly Id PlayerFactionId = new Id("fac.n44_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.n44_sample");
        private static readonly Id MapId = new Id("world.n44_map");
        private static readonly Id CreatureTemplateId = new Id("creature.n44_wolf");
        private static readonly Id CreatureTierId = new Id("creature.tier.n44_elite");
        private static readonly Id DifficultyTierId = new Id("diff.tier.n44_hard");

        // 惯例同 GameplayAssemblyDeathReloadTests：RulesAssembly.RegisterUnit 需要 stat.definition/
        // arch.power_type/arch.class 三张表才能把玩家真正接进 Progression（level_curve_ref 非空时）。
        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.n44\", \"max_level\": 2, \"entries\": [" +
            "{\"level\": 1, \"xp_to_next\": 100000, \"growth\": {}}," +
            "{\"level\": 2, \"xp_to_next\": 0, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"arch.class.n44_sample\", \"name_key\": \"l10n.arch.class.n44.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.n44\"}]";

        /// <summary>击杀基数曲线 Evaluate(x)=100x（两点线性，同
        /// <c>core/gameplay/progression_bridge/tests/TestSupport.cs</c> 惯例）：本用例死亡单位等级 6，
        /// baseAmount = Evaluate(6) = 600。</summary>
        private const string XpBaseCurveRows =
            "[{\"id\": \"prog.xp_base_curve.n44\", \"entries\": [{\"x\": 1, \"y\": 100}, {\"x\": 11, \"y\": 1100}]}]";

        /// <summary>id 用 <c>CreatureDeathXpListener.DefaultKillXpSourceId</c> 的约定值
        /// （<c>prog.xp_source.kill</c>）——本用例不显式配置 <c>ProgressionOptions.KillXpSourceId</c>，
        /// 落到默认约定；不登记 <c>level_diff_ref</c>，Δ 系数恒为 1（不参与本用例验证的倍率乘积）。</summary>
        private const string XpSourceRows =
            "[{\"id\": \"prog.xp_source.kill\", \"kind\": \"kill\", \"base_xp\": 1, " +
            "\"base_curve_ref\": \"prog.xp_base_curve.n44\"}]";

        /// <summary>分档经验倍率 1.5（T-N4-4 新增字段）。</summary>
        private const string CreatureTierRows =
            "[{\"id\": \"creature.tier.n44_elite\", \"name_key\": \"l10n.tier.n44\", \"xp_multiplier\": 1.5}]";

        private static string CreatureTemplateRows(Id templateId, Id tierId, int level) =>
            "[{\"id\": \"" + templateId.Value + "\", \"name_key\": \"l10n.creature.n44_wolf\", " +
            "\"level\": " + level + ", \"tier\": \"" + tierId.Value + "\", \"base_stats\": {}, " +
            "\"faction_id\": \"fac.n44_monster\", \"display_ref\": \"display.n44_wolf\"}]";

        /// <summary>难度经验倍率 2（T-N4-4 新增字段）；<c>loot_multiplier</c> 必填字段随手填 1（不
        /// 参与本用例验证）。</summary>
        private const string DifficultyTierRows =
            "[{\"id\": \"diff.tier.n44_hard\", \"name_key\": \"l10n.diff.n44\", " +
            "\"loot_multiplier\": 1.0, \"xp_multiplier\": 2.0}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
        }

        private static Fixture Build()
        {
            var bus = new EventBus(
                Core.Foundation.EventBus.EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("prog.level_curve", Envelope("prog.level_curve", LevelCurveRows))
                .Add("prog.xp_source", Envelope("prog.xp_source", XpSourceRows))
                .Add("prog.xp_base_curve", Envelope("prog.xp_base_curve", XpBaseCurveRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("creature.tier_definition", Envelope("creature.tier_definition", CreatureTierRows))
                .Add("creature.template", Envelope("creature.template", CreatureTemplateRows(CreatureTemplateId, CreatureTierId, level: 6)))
                .Add("diff.tier", Envelope("diff.tier", DifficultyTierRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var saveSystem = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.n44_test")));

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay };
        }

        /// <summary>倍率组 1：分档 ×1.5、难度 ×2 → 击杀经验 = 基数(600) × 1.5 × 2 × Δ(1) = 1800。</summary>
        [Fact]
        public void OnUnitDied_TierAndDifficultyBothConfigured_GrantsBaseAmountTimesBothMultipliers()
        {
            var fx = Build();
            fx.Gameplay.Difficulty.Apply(DifficultyTierId, DifficultyScope.Global, null);

            var creatureId = fx.Gameplay.Carriers.Creatures.Spawn(CreatureTemplateId, MapId, new Vec2(1, 0), 0);

            fx.Bus.Enqueue(new UnitDiedEvent(creatureId, killerId: PlayerId));
            fx.Bus.DispatchPending();

            Assert.Equal(1800, fx.Gameplay.Carriers.Rules.Progression.GetXp(PlayerId));
        }

        /// <summary>倍率组 2：只配置分档 ×1.5、未应用任何难度档位（<see cref="IDifficultyHost.XpMultiplier"/>
        /// 缺省 1.0）→ 击杀经验 = 基数(600) × 1.5 × 1 × Δ(1) = 900——核对难度倍率默认值不会在未
        /// <c>Apply</c> 时意外生效。</summary>
        [Fact]
        public void OnUnitDied_OnlyTierConfigured_DifficultyNotApplied_GrantsBaseAmountTimesTierMultiplierOnly()
        {
            var fx = Build();
            // 故意不调用 fx.Gameplay.Difficulty.Apply——核对 IDifficultyHost.XpMultiplier 未应用任何
            // 档位时的中性默认值 1.0 生效。

            var creatureId = fx.Gameplay.Carriers.Creatures.Spawn(CreatureTemplateId, MapId, new Vec2(1, 0), 0);

            fx.Bus.Enqueue(new UnitDiedEvent(creatureId, killerId: PlayerId));
            fx.Bus.DispatchPending();

            Assert.Equal(900, fx.Gameplay.Carriers.Rules.Progression.GetXp(PlayerId));
        }
    }
}
