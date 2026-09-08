using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// 种族被动光环跨图丢失根治（architecture/落地计划/audit-85f1f4f-20260908，静态候选转已确认）：
    /// <c>RulesAssembly.RegisterUnit(raceId)</c> 经 <c>ArchetypeRegistry.ApplyTo</c> 施加
    /// <c>race.passive_auras</c>，跨图 <see cref="IWorldSim.ClearAll"/>（响应 <c>entity.destroyed</c>
    /// 的 <c>AuraHost</c>）会清空这些运行期光环，而属性修正（<c>race.stat_mods</c>）不受影响——修复前
    /// <c>GameplayAssembly.EnterMap</c> 只重放装备 grants（CR140-02），没有重放种族被动，用真实内容
    /// （非空 <c>arch.race.passive_auras</c>）复现：跨图后种族属性加成还在，被动光环却消失了。
    /// <para>
    /// 本文件同 <c>CR140_02_EquipmentAuraMapClearTests</c> 写法：真实 <c>GameplayAssembly</c> + 真实
    /// <c>WorldSim.ClearAll</c> + 手工重放"常驻壳把玩家实体加回"这一步 + <see
    /// cref="GameplayAssembly.EnterMap"/> 验证：跨图后种族被动光环正确恢复，<c>EnterMap</c> 重复调用
    /// 不会让光环重复叠加（幂等）。
    /// </para>
    /// </summary>
    public sealed class RacePassiveAuraCrossMapTests
    {
        private static readonly Id MapId = new Id("world.race_passive_aura_map");
        private static readonly Id PlayerId = new Id("unit.race_passive_aura_player");
        private static readonly Id PlayerFactionId = new Id("fac.race_passive_aura_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.race_passive_aura_sample");
        private static readonly Id RaceId = new Id("arch.race.race_passive_aura_sample");
        private static readonly Id StatPower = new Id("stat.power");
        private static readonly Id AuraDefId = new Id("skill.aura_def.race_passive_aura_sample");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 1}," +
            "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.race_passive_aura_sample" + "\", \"name_key\": \"l10n.arch.class.race_passive_aura_sample.name\", " +
            "\"primary_stat\": \"stat.power\", \"base_stats\": {\"stat.power\": 1}, " +
            "\"power_types\": [\"arch.power.health\"]}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var auraDefRows = "[{\"id\": \"" + AuraDefId.Value + "\", \"duration\": 60, \"max_stacks\": 1, " +
                "\"effects\": [{\"kind\": \"mod_stat\", \"params\": {" +
                "\"stat\": \"" + StatPower.Value + "\", \"op\": \"flat\", \"value\": 50}}]}]";

            var archRaceRows = "[{\"id\": \"" + RaceId.Value + "\", \"name_key\": \"l10n.arch.race.race_passive_aura_sample.name\", " +
                "\"stat_mods\": {\"" + StatPower.Value + "\": 10}, " +
                "\"passive_auras\": [\"" + AuraDefId.Value + "\"]}]";

            return new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("arch.race", Envelope("arch.race", archRaceRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("skill.aura_def", Envelope("skill.aura_def", auraDefRows))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"));
        }

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

            var source = BuildDataSource();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.race_passive_aura_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0), RaceId = RaceId };
            world.AddEntity(player);
            // 惯例同 UnitPersistable.RaceId 判断记录：调用方在 RegisterUnit 传入 raceId 的同时，
            // 必须同步写入 PlayerUnit.RaceId——框架不会自动同步（分层边界：RulesAssembly/L2 不知道
            // PlayerUnit/L3 这个类型的存在）。
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, RaceId, level: 1);

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay, Player = player };
        }

        [Fact]
        public void RaceStatModsSurviveClearAll_ButPassiveAuraDoesNot_UntilEnterMapReappliesIt()
        {
            var fx = Build();

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1 + 10 + 50, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));

            // 跨图：ClearAll 销毁全部实体（含玩家），触发 entity.destroyed，AuraHost 清空该玩家名下
            // 全部 Aura（含种族被动）；种族属性修正（StatModifierWriter 登记表）不受影响。
            fx.World.ClearAll();
            fx.Bus.DispatchPending();

            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "ClearAll 应当真的清空了种族被动光环（复现前提，不是本方法自己制造的假象）。");
            // 复现关键：种族属性修正（StatModifierWriter 登记表，+10）不受 ClearAll 影响，仍然生效；
            // 光环带来的 mod_stat 效果（+50）确实随光环一起消失——总值应为 1 + 10 = 11，不是清空前的
            // 1 + 10 + 50 = 61，量化证明"属性修正与被动光环生命周期不一致"这一根因。
            Assert.Equal(1 + 10, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));
        }

        [Fact]
        public void RacePassiveAura_CrossMapClear_ReenterMap_ReappliesAura_AndIsIdempotent()
        {
            var fx = Build();
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));

            fx.World.ClearAll();
            fx.Bus.DispatchPending();
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));

            fx.World.AddEntity(fx.Player);
            fx.Bus.DispatchPending();

            fx.Gameplay.EnterMap(MapId, PlayerId);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "核心断言：跨图后种族被动光环应当被 EnterMap 重新施加。");
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));
            Assert.Equal(1 + 10 + 50, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));

            // 幂等：EnterMap 被再次调用（未经历新一轮 ClearAll）不应让光环叠加。
            fx.Gameplay.EnterMap(MapId, PlayerId);
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));
            Assert.Equal(1 + 10 + 50, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));
        }

        /// <summary>未设置种族（<see cref="PlayerUnit.RaceId"/> 为 <c>null</c>，如游戏本身不使用种族
        /// 概念，或读的是引入本字段之前的旧档）时，<see cref="GameplayAssembly.EnterMap"/> 应静默
        /// 跳过重放，不抛异常。</summary>
        [Fact]
        public void EnterMap_PlayerWithoutRaceId_DoesNotThrow()
        {
            var fx = Build();
            fx.Player.RaceId = null;

            fx.World.ClearAll();
            fx.Bus.DispatchPending();
            fx.World.AddEntity(fx.Player);
            fx.Bus.DispatchPending();

            var exception = Record.Exception(() => fx.Gameplay.EnterMap(MapId, PlayerId));
            Assert.Null(exception);
        }
    }
}
