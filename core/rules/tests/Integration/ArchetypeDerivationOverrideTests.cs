using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// T-N1-4（ADR-0030 决策 2"职业模板可覆盖派生系数（<c>arch.class.derivation_overrides</c>，
    /// 可选）"）：验证的是 <see cref="Core.Rules.Assembly.RulesAssembly"/> 生产装配本身是否接线——
    /// <c>ArchetypeRegistry.ApplyTo</c>（单位首次注册）与 <c>RulesAssembly.ReloadArchetypeAndRace</c>
    /// （换职业/读档恢复，见该方法判断记录"本方法是换职业与读档恢复共用的唯一路径"）两条路径都会把
    /// 当前职业的 <c>derivation_overrides</c> 写入 <c>StatHost</c>、触发派生属性重算。底层机制本身
    /// （<c>StatHost.SetDerivationCoefficientOverrides</c> 的覆盖查找、失效传播）的单测见
    /// <c>core/numbers/stat_block/tests/StatHostTests.cs</c>；<c>ArchetypeRegistry.ApplyTo</c> 转发
    /// 覆盖列表给具名委托的单测见 <c>core/numbers/archetype/tests/ArchetypeRegistryTests.cs</c>。惯例
    /// 同 <c>ProgressionRestoreRatingRecomputeTests</c>：用一个独立的最小化 <see
    /// cref="Core.Rules.Assembly.RulesAssembly"/> 夹具（不复用 <c>FightWorldBuilder</c>）。
    /// </summary>
    public sealed class ArchetypeDerivationOverrideTests
    {
        private static readonly Id StatMight = new Id("stat.n1_4_might");
        private static readonly Id StatPower = new Id("stat.n1_4_power");

        private static readonly Id ClassWarriorLike = new Id("arch.class.n1_4_warrior_like");
        private static readonly Id ClassMageLike = new Id("arch.class.n1_4_mage_like");
        private static readonly Id ClassDefaultLike = new Id("arch.class.n1_4_default_like");

        // stat.n1_4_power 的默认系数（derived_from）是 1.0——三个职业共用同一份 stat.definition，
        // 只有 derivation_overrides 不同（战士 3.0、法师 0.5、default_like 不覆盖，走默认 1.0）。
        private const string StatDefinitionJson = @"
        {
            ""table"": ""stat.definition"",
            ""schema_version"": 2,
            ""rows"": [
                { ""id"": ""stat.n1_4_might"", ""name_key"": ""l10n.stat.n1_4_might.name"", ""category"": ""primary"", ""default_base"": 0 },
                { ""id"": ""stat.n1_4_power"", ""name_key"": ""l10n.stat.n1_4_power.name"", ""category"": ""derived"",
                  ""derived_from"": [ { ""stat"": ""stat.n1_4_might"", ""coefficient"": 1.0 } ] }
            ]
        }";

        // PowerHost.RegisterUnit 要求 power_types 非空（见 PowerHost.cs:101），三个职业统一引用同一
        // 个最小资源类型定义 arch.power.n1_4_energy（下方 ArchPowerTypeJson）。
        private const string ArchClassJson = @"
        {
            ""table"": ""arch.class"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""arch.class.n1_4_warrior_like"", ""name_key"": ""l10n.arch.class.n1_4_warrior_like.name"",
                  ""primary_stat"": ""stat.n1_4_might"", ""base_stats"": { ""stat.n1_4_might"": 10 },
                  ""power_types"": [ ""arch.power.n1_4_energy"" ],
                  ""derivation_overrides"": [ { ""stat"": ""stat.n1_4_power"", ""source"": ""stat.n1_4_might"", ""coefficient"": 3.0 } ] },
                { ""id"": ""arch.class.n1_4_mage_like"", ""name_key"": ""l10n.arch.class.n1_4_mage_like.name"",
                  ""primary_stat"": ""stat.n1_4_might"", ""base_stats"": { ""stat.n1_4_might"": 10 },
                  ""power_types"": [ ""arch.power.n1_4_energy"" ],
                  ""derivation_overrides"": [ { ""stat"": ""stat.n1_4_power"", ""source"": ""stat.n1_4_might"", ""coefficient"": 0.5 } ] },
                { ""id"": ""arch.class.n1_4_default_like"", ""name_key"": ""l10n.arch.class.n1_4_default_like.name"",
                  ""primary_stat"": ""stat.n1_4_might"", ""base_stats"": { ""stat.n1_4_might"": 10 },
                  ""power_types"": [ ""arch.power.n1_4_energy"" ] }
            ]
        }";

        private const string ArchPowerTypeJson = @"
        {
            ""table"": ""arch.power_type"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""arch.power.n1_4_energy"", ""name_key"": ""l10n.arch.power.n1_4_energy.name"",
                  ""max_source"": { ""kind"": ""fixed"", ""value"": 100 } }
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
                .Add("arch.class", ArchClassJson)
                .Add("arch.power_type", ArchPowerTypeJson)
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
                statOptions: new Core.Numbers.StatBlock.StatHostOptions());

            bus = busLocal;
            return rules;
        }

        // 1/2：同一主属性值（might=10），战士/法师两个职业各自的 derivation_overrides 覆盖不同系数，
        // 得到不同的派生属性值——power = might * coefficient。
        [Fact]
        public void ApplyTo_WarriorAndMageOverrideDifferentCoefficients_ProduceDifferentDerivedValues()
        {
            var rulesWarrior = Build(out _);
            var unitWarrior = new Id("unit.n1_4_warrior");
            rulesWarrior.RegisterUnit(unitWarrior, ClassWarriorLike, raceId: null);

            var rulesMage = Build(out _);
            var unitMage = new Id("unit.n1_4_mage");
            rulesMage.RegisterUnit(unitMage, ClassMageLike, raceId: null);

            // 战士：10 * 3.0 = 30；法师：10 * 0.5 = 5——同一主属性值下两个职业得到不同派生值。
            Assert.Equal(30.0, rulesWarrior.Stats.GetStat(unitWarrior, StatPower), 9);
            Assert.Equal(5.0, rulesMage.Stats.GetStat(unitMage, StatPower), 9);
            Assert.NotEqual(
                rulesWarrior.Stats.GetStat(unitWarrior, StatPower),
                rulesMage.Stats.GetStat(unitMage, StatPower));
        }

        // 2/2：一个职业覆盖系数，另一个职业未登记 derivation_overrides、走 stat.definition.derived_from
        // 登记的默认系数（1.0）——同一主属性值下两者同样得到不同派生值。
        [Fact]
        public void ApplyTo_OneClassOverrides_OtherClassUsesDefaultCoefficient_ProduceDifferentDerivedValues()
        {
            var rulesWarrior = Build(out _);
            var unitWarrior = new Id("unit.n1_4_warrior_vs_default");
            rulesWarrior.RegisterUnit(unitWarrior, ClassWarriorLike, raceId: null);

            var rulesDefault = Build(out _);
            var unitDefault = new Id("unit.n1_4_default");
            rulesDefault.RegisterUnit(unitDefault, ClassDefaultLike, raceId: null);

            // 战士覆盖：10 * 3.0 = 30；default_like 未覆盖，走 derived_from 默认系数：10 * 1.0 = 10。
            Assert.Equal(30.0, rulesWarrior.Stats.GetStat(unitWarrior, StatPower), 9);
            Assert.Equal(10.0, rulesDefault.Stats.GetStat(unitDefault, StatPower), 9);
        }

        // 读档恢复触发派生重算：ReloadArchetypeAndRace 是 GameplayAssembly.IDerivedStateRebuilder.
        // OnSectionLoaded 对 player.race_id 段读档恢复时调用的唯一路径（见该方法判断记录）。这里直接
        // 调用该方法模拟"存档只回填了 PlayerUnit.ArchetypeId 字段、StatHost 尚不知道这个单位的职业"
        // 之后紧接着的读档恢复重建——previousClassId=null 对应 GameplayAssembly._previousArchetypeId
        // 在本单位第一次触发 OnSectionLoaded 时的初始状态。
        [Fact]
        public void ReloadArchetypeAndRace_AfterBareRegistration_AppliesClassDerivationOverride()
        {
            var rules = Build(out var bus);
            var unit = new Id("unit.n1_4_restore");

            // 模拟读档：StatHost 只知道这个单位存在，还没应用任何职业（真实存档流程里 PlayerUnit 的
            // ArchetypeId 字段已经由 SaveSystem 直接反序列化回填，不经过 ArchetypeRegistry.ApplyTo）。
            rules.Stats.RegisterUnit(unit);

            rules.ReloadArchetypeAndRace(unit, ClassWarriorLike, raceId: null, previousClassId: null, previousRaceId: null);
            bus.DispatchPending();

            // 读档恢复后，派生属性按该职业（战士，覆盖系数 3.0）重算：10 * 3.0 = 30，而不是默认系数
            // 1.0 算出的 10——证明 ReloadArchetypeAndRace 确实触发了派生系数覆盖的写入与重算。
            Assert.Equal(30.0, rules.Stats.GetStat(unit, StatPower), 9);
        }

        /// <summary>最小化的 <see cref="Core.Rules.Common.IUnitAccess"/> 假实现，同
        /// <c>ProgressionRestoreRatingRecomputeTests.StubUnitAccess</c>。</summary>
        private sealed class StubUnitAccess : Core.Rules.Common.IUnitAccess
        {
            public bool Exists(Id unitId) => true;
            public System.Collections.Generic.IReadOnlyList<Id> AllUnits => System.Array.Empty<Id>();
            public Vec2 GetPosition(Id unitId) => Vec2.Zero;
            public void SetPosition(Id unitId, Vec2 position) { }
            public Id GetFaction(Id unitId) => new Id("fac.n1_4");
            public int GetLevel(Id unitId) => 1;
            public double GetFacing(Id unitId) => 0;
            public bool IsAlive(Id unitId) => true;
            public void SetAlive(Id unitId, bool alive) { }
            public Id? GetTemplateId(Id unitId) => null;
            public System.Collections.Generic.IReadOnlyList<Id> GetTags(Id unitId) => System.Array.Empty<Id>();
        }
    }
}
