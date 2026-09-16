using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Xunit;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// T-N4-5（ADR-0033 决策 7"升级回满：<c>progression.level_up</c> 触发生命与资源回满，由资源池
    /// 订阅实现"；06 第 2.5 节同条）：验证的是 <see cref="Core.Rules.Assembly.RulesAssembly"/>
    /// 生产装配本身是否真的把 <see cref="LevelUpEvent"/> 接到了 <see cref="Core.Numbers.PowerSet.IPowerHost.RefillAll"/>
    /// 上（惯例同 <c>PowerMaxRecomputeWiringTests</c>/<c>ProgressionRestoreRatingRecomputeTests</c>：
    /// 底层方法各自的正确性另见 <c>core/numbers/power_set/tests/PowerHostTests.cs</c> 的
    /// <c>RefillAll_*</c> 系列用例，本文件只管装配根是否接线，以及 <c>ProgressionOptions.RefillOnLevelUp</c>
    /// 开关是否被真正消费）。用一个独立的最小化 <see cref="Core.Rules.Assembly.RulesAssembly"/> 夹具
    /// （不复用 <c>FightWorldBuilder</c>——那份夹具的 <c>prog.curve.sample</c> 曲线 <c>max_level=1</c>，
    /// 无法真实触发升级，见该文件判断记录）。
    /// </summary>
    public sealed class T_N4_5_LevelUpRefillIntegrationTests
    {
        private static readonly Id Unit = new Id("unit.n4_5_refill");
        private static readonly Id CurveId = new Id("prog.curve.n4_5_refill");
        private static readonly Id XpSourceId = new Id("prog.xp_source.n4_5_refill");
        private static readonly Id HealthPower = new Id("arch.power.n4_5_health");
        private static readonly Id ManaPower = new Id("arch.power.n4_5_mana");
        private static readonly Id ComboPower = new Id("arch.power.n4_5_combo"); // 积累型：start_full=false

        // 2026-09-16 深度复审 D-M2 回归专用：上限来源是属性（max_source.kind=stat）且该属性靠等级
        // 曲线成长——报告复现的正是"资源池挂属性、属性靠等级成长"这个最常见配置组合下的时序缺陷。
        private static readonly Id StatCurveId = new Id("prog.curve.n4_5_refill_stat");
        private static readonly Id VitStat = new Id("stat.n4_5_vit");
        private static readonly Id StatHealthPower = new Id("arch.power.n4_5_health_stat");

        private const string ProgLevelCurveJson = @"
        {
            ""table"": ""prog.level_curve"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""prog.curve.n4_5_refill"", ""max_level"": 3,
                  ""entries"": [
                      { ""level"": 1, ""xp_to_next"": 100, ""growth"": {} },
                      { ""level"": 2, ""xp_to_next"": 100, ""growth"": {} },
                      { ""level"": 3, ""xp_to_next"": 0, ""growth"": {} }
                  ] },
                { ""id"": ""prog.curve.n4_5_refill_stat"", ""max_level"": 4,
                  ""entries"": [
                      { ""level"": 1, ""xp_to_next"": 100, ""growth"": {} },
                      { ""level"": 2, ""xp_to_next"": 100, ""growth"": { ""stat.n4_5_vit"": 50 } },
                      { ""level"": 3, ""xp_to_next"": 100, ""growth"": { ""stat.n4_5_vit"": 30 } },
                      { ""level"": 4, ""xp_to_next"": 0, ""growth"": {} }
                  ] }
            ]
        }";

        private const string PowerTypeJson = @"
        {
            ""table"": ""arch.power_type"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""arch.power.n4_5_health"", ""name_key"": ""l10n.power.n4_5_health.name"",
                  ""max_source"": { ""kind"": ""fixed"", ""value"": 100 }, ""start_full"": true },
                { ""id"": ""arch.power.n4_5_mana"", ""name_key"": ""l10n.power.n4_5_mana.name"",
                  ""max_source"": { ""kind"": ""fixed"", ""value"": 50 }, ""start_full"": true },
                { ""id"": ""arch.power.n4_5_combo"", ""name_key"": ""l10n.power.n4_5_combo.name"",
                  ""max_source"": { ""kind"": ""fixed"", ""value"": 5 }, ""start_full"": false },
                { ""id"": ""arch.power.n4_5_health_stat"", ""name_key"": ""l10n.power.n4_5_health_stat.name"",
                  ""max_source"": { ""kind"": ""stat"", ""stat"": ""stat.n4_5_vit"" }, ""start_full"": true }
            ]
        }";

        private const string StatDefinitionWithVitJson = @"
        {
            ""table"": ""stat.definition"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""stat.n4_5_vit"", ""name_key"": ""l10n.stat.n4_5_vit.name"", ""category"": ""primary"" }
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

        private static Core.Rules.Assembly.RulesAssembly Build(out IEventBus bus, ProgressionOptions? progressionOptions)
        {
            var busLocal = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", StatDefinitionWithVitJson)
                .Add("prog.level_curve", ProgLevelCurveJson)
                .Add("arch.power_type", PowerTypeJson)
                .Add("combat.hit_table_config", HitTableJson)
                .Add("combat.resist_curve", ResistCurveJson);

            var registry = new DataRegistry(source, busLocal, new DataRegistryOptions { FailOnUnknownTable = false });
            Core.Rules.Assembly.RulesSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(busLocal);
            var units = new ProgressionRestoreRatingRecomputeTestsUnitAccessStub();
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            var rules = new Core.Rules.Assembly.RulesAssembly(
                busLocal, registry, rng, units, spatial, world,
                navigation: null,
                statOptions: new Core.Numbers.StatBlock.StatHostOptions(),
                combatOptions: null,
                skillOptions: null,
                targetingOptions: null,
                aiOptions: null,
                extraSchemas: null,
                staticImmunity: null,
                effectExtension: null,
                autoRegisterTickHandlers: true,
                projectileSpawner: null,
                discreteTurnIndexProvider: null,
                discreteRoundIndexProvider: null,
                discreteCurrentActorProvider: null,
                levelSync: null,
                progressionOptions: progressionOptions);

            bus = busLocal;
            return rules;
        }

        [Fact]
        public void LevelUp_RefillsAllStartFullPowerPools_AndFiresOnePowerChangedPerPool_AccumulationTypeUntouched()
        {
            var rules = Build(out var bus, progressionOptions: null); // RefillOnLevelUp 缺省 true。

            rules.Stats.RegisterUnit(Unit);
            rules.Progression.RegisterUnit(Unit, CurveId);
            rules.Powers.RegisterUnit(Unit, new[] { HealthPower, ManaPower, ComboPower });

            rules.Powers.ModifyPower(Unit, HealthPower, -60, new Id("test.setup")); // 100 -> 40
            rules.Powers.ModifyPower(Unit, ManaPower, -20, new Id("test.setup")); // 50 -> 30
            rules.Powers.ModifyPower(Unit, ComboPower, 2, new Id("test.setup")); // 0 -> 2
            bus.DispatchPending();

            var changed = new List<PowerChangedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));

            // 100 经验，曲线 1 级门槛 100——恰好升到 2 级，触发一次 LevelUpEvent。
            rules.Progression.AddXp(Unit, XpSourceId, 100);
            bus.DispatchPending();

            Assert.Equal(2, rules.Progression.GetLevel(Unit));

            Assert.Equal(100, rules.Powers.GetPower(Unit, HealthPower));
            Assert.Equal(50, rules.Powers.GetPower(Unit, ManaPower));
            Assert.Equal(2, rules.Powers.GetPower(Unit, ComboPower)); // 积累型：原样不动。

            var healthChanged = Assert.Single(changed, e => e.PowerType == HealthPower);
            Assert.Equal(40, healthChanged.OldValue);
            Assert.Equal(100, healthChanged.NewValue);

            var manaChanged = Assert.Single(changed, e => e.PowerType == ManaPower);
            Assert.Equal(30, manaChanged.OldValue);
            Assert.Equal(50, manaChanged.NewValue);

            Assert.DoesNotContain(changed, e => e.PowerType == ComboPower);
        }

        [Fact]
        public void LevelUp_RefillOnLevelUpFalse_DoesNotRefill()
        {
            var options = new ProgressionOptions { RefillOnLevelUp = false };
            var rules = Build(out var bus, progressionOptions: options);

            rules.Stats.RegisterUnit(Unit);
            rules.Progression.RegisterUnit(Unit, CurveId);
            rules.Powers.RegisterUnit(Unit, new[] { HealthPower, ManaPower });

            rules.Powers.ModifyPower(Unit, HealthPower, -60, new Id("test.setup")); // 100 -> 40
            bus.DispatchPending();

            var changed = new List<PowerChangedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));

            rules.Progression.AddXp(Unit, XpSourceId, 100); // 升到 2 级。
            bus.DispatchPending();

            Assert.Equal(2, rules.Progression.GetLevel(Unit));
            Assert.Equal(40, rules.Powers.GetPower(Unit, HealthPower)); // 未回满。
            Assert.Empty(changed);
        }

        // -----------------------------------------------------------------
        // 2026-09-16 深度复审 D-M2 回归：max_source.kind=stat 且该属性有非零 growth 时，升级回满
        // 必须回满到"这一级成长生效之后"的新上限，不能停在成长生效前的旧上限（复审报告复现：修复前
        // Max/Current 停在 0，成长写入后才追到 50，RefillAll 早已跑完）。
        // -----------------------------------------------------------------

        [Fact]
        public void LevelUp_MaxSourceIsStatWithGrowth_RefillsToNewMaxAfterGrowthApplied()
        {
            var rules = Build(out var bus, progressionOptions: null); // RefillOnLevelUp 缺省 true。

            rules.Stats.RegisterUnit(Unit);
            rules.Progression.RegisterUnit(Unit, StatCurveId);
            rules.Powers.RegisterUnit(Unit, new[] { StatHealthPower });
            bus.DispatchPending();

            // 升级前：stat.n4_5_vit 无基础值、无成长修正，恒为 0——Max/Current 均为 0。
            Assert.Equal(0, rules.Stats.GetStat(Unit, VitStat));
            Assert.Equal(0, rules.Powers.GetPowerMax(Unit, StatHealthPower));
            Assert.Equal(0, rules.Powers.GetPower(Unit, StatHealthPower));

            // 100 经验，曲线 1 级门槛 100——恰好升到 2 级，2 级 growth.stat.n4_5_vit=50。
            rules.Progression.AddXp(Unit, XpSourceId, 100);
            bus.DispatchPending();

            Assert.Equal(2, rules.Progression.GetLevel(Unit));
            Assert.Equal(50, rules.Stats.GetStat(Unit, VitStat)); // 成长确实写入了。
            // 修复前：这里会读到 0（RefillAll 执行那一刻 Max 还是成长生效前的旧值）。
            Assert.Equal(50, rules.Powers.GetPowerMax(Unit, StatHealthPower));
            Assert.Equal(50, rules.Powers.GetPower(Unit, StatHealthPower));
        }

        /// <summary>跨两级：一次 <c>AddXp</c> 触发两次 <see cref="LevelUpEvent"/>（2 级、3 级分别都有
        /// growth），确认"成长只聚合写入一次"（<c>ProgressionHostTests
        /// .AddXp_CrossesTwoLevels_FiresTwoLevelUpEvents_AndWritesCumulativeGrowthOnce</c> 既有契约）
        /// 与"升级回满到新上限"同时成立——升级回满这条链路不是只在跨一级时才正确。</summary>
        [Fact]
        public void LevelUp_CrossTwoLevelsInOneAddXp_MaxSourceIsStatWithGrowth_RefillsToFinalCumulativeMax()
        {
            var rules = Build(out var bus, progressionOptions: null);

            rules.Stats.RegisterUnit(Unit);
            rules.Progression.RegisterUnit(Unit, StatCurveId);
            rules.Powers.RegisterUnit(Unit, new[] { StatHealthPower });
            bus.DispatchPending();

            var levelUpCount = 0;
            bus.Subscribe<LevelUpEvent>(ProgressionEventKeys.LevelUp, e => levelUpCount++);

            // 200 经验：1 级门槛 100、2 级门槛 100——一次 AddXp 连升两级（1→2→3），2/3 级 growth 累计
            // 50+30=80。
            rules.Progression.AddXp(Unit, XpSourceId, 200);
            bus.DispatchPending();

            Assert.Equal(3, rules.Progression.GetLevel(Unit));
            Assert.Equal(2, levelUpCount); // 逐级发布两次 LevelUpEvent，既有契约不变。
            Assert.Equal(80, rules.Stats.GetStat(Unit, VitStat)); // 累计成长只聚合写入一次。
            Assert.Equal(80, rules.Powers.GetPowerMax(Unit, StatHealthPower));
            Assert.Equal(80, rules.Powers.GetPower(Unit, StatHealthPower));
        }

        /// <summary>最小化的 <see cref="Core.Rules.Common.IUnitAccess"/> 假实现——惯例同
        /// <c>ProgressionRestoreRatingRecomputeTests.StubUnitAccess</c>，本文件独立定义一份避免跨
        /// 测试类耦合内部私有类型。</summary>
        private sealed class ProgressionRestoreRatingRecomputeTestsUnitAccessStub : Core.Rules.Common.IUnitAccess
        {
            public bool Exists(Id unitId) => true;
            public IReadOnlyList<Id> AllUnits => new[] { Unit };
            public Vec2 GetPosition(Id unitId) => Vec2.Zero;
            public void SetPosition(Id unitId, Vec2 position) { }
            public Id GetFaction(Id unitId) => new Id("fac.n4_5");
            public int GetLevel(Id unitId) => 1;
            public double GetFacing(Id unitId) => 0;
            public bool IsAlive(Id unitId) => true;
            public void SetAlive(Id unitId, bool alive) { }
            public Id? GetTemplateId(Id unitId) => null;
            public IReadOnlyList<Id> GetTags(Id unitId) => System.Array.Empty<Id>();
        }
    }
}
