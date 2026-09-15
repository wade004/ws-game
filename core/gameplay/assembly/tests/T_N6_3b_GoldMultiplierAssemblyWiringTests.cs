using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// T-N6-3b（N4 遗留第 7 项；ADR-0034 决策 3 延伸；08 第 7.4 节"怪物掉钱 = 当量 ×
    /// econ.gold_base_curve(怪物等级) × 分档倍率 × diff.tier.loot_multiplier"）：验证
    /// <see cref="GameplayAssembly"/> 真的把 <c>creature.tier_definition.gold_multiplier</c> 接进
    /// <c>Core.Gameplay.Loot.LootHost</c> 换算货币掉落条目——用真实 <see cref="GameplayAssembly"/>
    /// （含 <c>Carriers.Creatures</c>/<c>Loot</c>/<c>Economy</c> 三个依赖）走一次"生成生物 → 死亡 →
    /// 击杀入账"的完整链路，核对最终到账货币数量精确等于
    /// <c>当量 × 金币基数(等级) × 分档 gold_multiplier</c>（本用例未应用任何难度档位，
    /// <c>diff.tier.loot_multiplier</c> 恒为 1，不参与验证）。惯例同
    /// <c>T_N4_4_XpMultiplierWiringTests</c>（真实装配 + 手工构造事件驱动，不经战斗管线）。
    /// </summary>
    public sealed class T_N6_3b_GoldMultiplierAssemblyWiringTests
    {
        private static readonly Id PlayerId = new Id("unit.n63b_player");
        private static readonly Id PlayerFactionId = new Id("fac.n63b_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.n63b_sample");
        private static readonly Id MapId = new Id("world.n63b_map");
        private static readonly Id CreatureTemplateId = new Id("creature.n63b_wolf");
        private static readonly Id CreatureTierId = new Id("creature.tier.n63b_elite");
        private static readonly Id LootTableId = new Id("loot.n63b_elite_coin");
        private static readonly Id CurrencyId = new Id("econ.currency.n63b_coin");

        // 惯例同 T_N4_4_XpMultiplierWiringTests：RulesAssembly.RegisterUnit 需要 stat.definition/
        // arch.power_type/arch.class 三张表才能把玩家真正接进 Progression。
        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.n63b\", \"max_level\": 2, \"entries\": [" +
            "{\"level\": 1, \"xp_to_next\": 100000, \"growth\": {}}," +
            "{\"level\": 2, \"xp_to_next\": 0, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"arch.class.n63b_sample\", \"name_key\": \"l10n.arch.class.n63b.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.n63b\"}]";

        /// <summary>金币基数曲线：等级 1 → 2；等级 10 → 8（两点线性，同
        /// <c>T_N4_7_CurrencyLootTests</c>/<c>T_N6_3b_GoldMultiplierLootTests</c> 取值，便于手算
        /// 核对）：本用例死亡单位等级 10，金币基数 = 8。</summary>
        private const string GoldBaseCurveRows =
            "[{\"id\": \"econ.gold_base.default\", \"entries\": [{\"x\": 1, \"y\": 2}, {\"x\": 10, \"y\": 8}]}]";

        private const string CurrencyRows =
            "[{\"id\": \"econ.currency.n63b_coin\", \"name_key\": \"l10n.n63b.coin\", \"display_ref\": \"display.n63b_coin\"}]";

        private const string LootTableRows =
            "[{\"id\": \"loot.n63b_elite_coin\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
            "{\"ref\": \"econ.currency.n63b_coin\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\": 1, \"max\": 1}}" +
            "]}]}]";

        /// <summary>分档金币倍率 1.5（T-N6-3b 新增字段，见 <c>data/_sample/creature/
        /// creature.tier_definition.json</c> 精英档同款示范取值）。</summary>
        private const string CreatureTierRows =
            "[{\"id\": \"creature.tier.n63b_elite\", \"name_key\": \"l10n.tier.n63b\", \"gold_multiplier\": 1.5}]";

        private static string CreatureTemplateRows(Id templateId, Id tierId, Id lootTableId, int level) =>
            "[{\"id\": \"" + templateId.Value + "\", \"name_key\": \"l10n.creature.n63b_wolf\", " +
            "\"level\": " + level + ", \"tier\": \"" + tierId.Value + "\", \"base_stats\": {}, " +
            "\"faction_id\": \"fac.n63b_monster\", \"display_ref\": \"display.n63b_wolf\", " +
            "\"loot_table_ref\": \"" + lootTableId.Value + "\"}]";

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
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("econ.gold_base_curve", Envelope("econ.gold_base_curve", GoldBaseCurveRows))
                .Add("econ.currency", Envelope("econ.currency", CurrencyRows))
                .Add("loot.table", Envelope("loot.table", LootTableRows))
                .Add("creature.tier_definition", Envelope("creature.tier_definition", CreatureTierRows))
                .Add("creature.template", Envelope("creature.template",
                    CreatureTemplateRows(CreatureTemplateId, CreatureTierId, LootTableId, level: 10)))
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
            var saveSystem = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.n63b_test")));

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay };
        }

        /// <summary>精英档 gold_multiplier=1.5 → 击杀入账货币 = 当量(1) × 金币基数(等级 10)=8 ×
        /// 分档倍率 1.5 × diff.tier.loot_multiplier(未应用任何档位，恒 1) = 12。</summary>
        [Fact]
        public void OnUnitDied_EliteTierGoldMultiplierConfigured_DepositsAmountTimesTierMultiplier()
        {
            var fx = Build();

            var creatureId = fx.Gameplay.Carriers.Creatures.Spawn(CreatureTemplateId, MapId, new Vec2(1, 0), 0);

            fx.Bus.Enqueue(new UnitDiedEvent(creatureId, killerId: PlayerId));
            fx.Bus.DispatchPending();

            Assert.Equal(12, fx.Gameplay.Economy.GetBalance(PlayerId, CurrencyId));
        }
    }
}
