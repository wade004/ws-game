using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Numbers.Progression;
using Xunit;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// R08 收口（外部审计 5e779c6，P2；见 <c>Core.Numbers.Progression.ProgressionHost.RestoreState</c>/
    /// <c>ProgressionRestoredEvent</c>/<c>Core.Rules.Assembly.RulesAssembly</c> 构造函数"RC-06 收边
    /// 补齐"判断记录）：读档恢复等级（<c>ProgressionPersistable.Load</c> → <c>RestoreState</c>）
    /// 此前不发布任何事件，<c>StatHost</c> 的评级换算属性缓存（<c>RecomputeRatingStats</c>）只在
    /// <c>progression.level_up</c>（真实升级）时才重算，读档后会一直停留在读档前的值（通常是刚
    /// 构造时按默认等级 1 算出的）。惯例同 <c>PowerMaxRecomputeWiringTests</c>：验证的是
    /// <see cref="Core.Rules.Assembly.RulesAssembly"/> 生产装配本身是否接线，不是底层方法各自的
    /// 正确性（那部分见 <c>core/numbers/stat_block/tests/StatHostTests.cs</c>
    /// <c>RecomputeRatingStats_AfterLevelLookupChanges_UpdatesCacheAndFiresStatChanged</c>）。用一个
    /// 独立的最小化 <see cref="Core.Rules.Assembly.RulesAssembly"/> 夹具（不复用
    /// <c>FightWorldBuilder</c>——那份夹具没有声明任何评级换算属性，本条需要专门追加）。
    /// </summary>
    public sealed class ProgressionRestoreRatingRecomputeTests
    {
        private static readonly Id Unit = new Id("unit.r08_restore");
        private static readonly Id CurveId = new Id("prog.curve.r08");
        private static readonly Id StatRating = new Id("stat.r08_rating");

        private const string StatDefinitionJson = @"
        {
            ""table"": ""stat.definition"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""stat.r08_rating"", ""name_key"": ""l10n.stat.r08_rating.name"", ""group"": ""primary"",
                  ""default_base"": 0, ""is_rating"": true, ""rating_conversion_ref"": ""stat.rating.r08_curve"" }
            ]
        }";

        // 判断记录同 StatHostTests：entries[].level 就是单位等级（不是评级原始值自己的断点）。
        private const string RatingConversionJson = @"
        {
            ""table"": ""stat.rating_conversion"",
            ""schema_version"": 1,
            ""rows"": [
                {
                    ""id"": ""stat.rating.r08_curve"",
                    ""entries"": [
                        { ""level"": 1, ""points_per_percent"": 10 },
                        { ""level"": 10, ""points_per_percent"": 5 }
                    ]
                }
            ]
        }";

        private const string ProgLevelCurveJson = @"
        {
            ""table"": ""prog.level_curve"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""prog.curve.r08"", ""max_level"": 10,
                  ""entries"": [
                      { ""level"": 1, ""xp_to_next"": 0, ""growth"": {} },
                      { ""level"": 2, ""xp_to_next"": 0, ""growth"": {} },
                      { ""level"": 3, ""xp_to_next"": 0, ""growth"": {} },
                      { ""level"": 4, ""xp_to_next"": 0, ""growth"": {} },
                      { ""level"": 5, ""xp_to_next"": 0, ""growth"": {} },
                      { ""level"": 6, ""xp_to_next"": 0, ""growth"": {} },
                      { ""level"": 7, ""xp_to_next"": 0, ""growth"": {} },
                      { ""level"": 8, ""xp_to_next"": 0, ""growth"": {} },
                      { ""level"": 9, ""xp_to_next"": 0, ""growth"": {} },
                      { ""level"": 10, ""xp_to_next"": 0, ""growth"": {} }
                  ] }
            ]
        }";

        private const string HitTableJson = @"
        {
            ""table"": ""combat.hit_table_config"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""combat.hit_table.default"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 }
            ]
        }";

        private const string ResistCurveJson = @"{ ""table"": ""combat.resist_curve"", ""schema_version"": 1, ""rows"": [] }";

        private static Core.Rules.Assembly.RulesAssembly Build(out IEventBus bus)
        {
            var busLocal = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", StatDefinitionJson)
                .Add("stat.rating_conversion", RatingConversionJson)
                .Add("prog.level_curve", ProgLevelCurveJson)
                .Add("combat.hit_table_config", HitTableJson)
                .Add("combat.resist_curve", ResistCurveJson);

            var registry = new DataRegistry(source, busLocal, new DataRegistryOptions { FailOnUnknownTable = false });
            Core.Rules.Assembly.RulesSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(busLocal);
            var units = new StubUnitAccess();
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            var rules = new Core.Rules.Assembly.RulesAssembly(
                busLocal, registry, rng, units, spatial, world,
                statOptions: new Core.Numbers.StatBlock.StatHostOptions { EnableRatingConversion = true });

            bus = busLocal;
            return rules;
        }

        [Fact]
        public void RestoreState_AutomaticallyRecomputesRatingStat_WithoutManualRecomputeCall()
        {
            var rules = Build(out var bus);

            rules.Stats.RegisterUnit(Unit);
            rules.Progression.RegisterUnit(Unit, CurveId);
            rules.Stats.SetBase(Unit, StatRating, 50.0);
            bus.DispatchPending();

            // 等级 1：points_per_percent=10 -> 50/10=5%。
            Assert.Equal(5.0, rules.Stats.GetStat(Unit, StatRating), 9);

            // 读档恢复到等级 10——不经过 AddXp，不会发布 progression.level_up。
            rules.Progression.RestoreState(Unit, CurveId, level: 10, xp: 0);
            bus.DispatchPending();

            // 修复前：evt 从未发布，评级换算属性缓存停留在等级 1 算出的 5%，即便 GetLevel 已经
            // 正确返回 10（RestoreState 本身已经把 Progression 内部状态置位）。
            // 修复后：等级 10：points_per_percent=5 -> 50/5=10%。
            Assert.Equal(10.0, rules.Stats.GetStat(Unit, StatRating), 9);
        }

        [Fact]
        public void RestoreState_DoesNotPublishLevelUpEvent()
        {
            var rules = Build(out var bus);
            rules.Stats.RegisterUnit(Unit);
            rules.Progression.RegisterUnit(Unit, CurveId);

            LevelUpEvent? received = null;
            bus.Subscribe<LevelUpEvent>(ProgressionEventKeys.LevelUp, evt => received = evt);

            rules.Progression.RestoreState(Unit, CurveId, level: 7, xp: 0);

            Assert.Null(received); // 语义诚实：读档不是"升级"，不应误发 progression.level_up。
        }

        [Fact]
        public void RestoreState_PublishesProgressionRestoredEvent_WithRestoredLevel()
        {
            var rules = Build(out var bus);
            rules.Stats.RegisterUnit(Unit);
            rules.Progression.RegisterUnit(Unit, CurveId);

            ProgressionRestoredEvent? received = null;
            bus.Subscribe<ProgressionRestoredEvent>(ProgressionEventKeys.StateRestored, evt => received = evt);

            rules.Progression.RestoreState(Unit, CurveId, level: 7, xp: 0);

            Assert.NotNull(received);
            Assert.Equal(Unit, received!.UnitId);
            Assert.Equal(7, received.Level);
        }

        /// <summary>最小化的 <see cref="Core.Rules.Common.IUnitAccess"/> 假实现——本条只关心
        /// <see cref="Core.Rules.Assembly.RulesAssembly"/> 构造与 Stats/Progression 两个宿主的接线，
        /// 不涉及任何空间/单位存在性查询。</summary>
        private sealed class StubUnitAccess : Core.Rules.Common.IUnitAccess
        {
            public bool Exists(Id unitId) => true;
            public System.Collections.Generic.IReadOnlyList<Id> AllUnits => new[] { Unit };
            public Vec2 GetPosition(Id unitId) => Vec2.Zero;
            public void SetPosition(Id unitId, Vec2 position) { }
            public Id GetFaction(Id unitId) => new Id("fac.r08");
            public int GetLevel(Id unitId) => 1;
            public double GetFacing(Id unitId) => 0;
            public bool IsAlive(Id unitId) => true;
            public void SetAlive(Id unitId, bool alive) { }
            public Id? GetTemplateId(Id unitId) => null;
            public System.Collections.Generic.IReadOnlyList<Id> GetTags(Id unitId) => System.Array.Empty<Id>();
        }
    }
}
