using System;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Rules.Assembly
{
    /// <summary>
    /// ADR-0178 样板游戏接入时暴露的缺口：职业 <c>base_stats</c> 里出现带评级换算曲线的百分比类属性（被动闪避点数）时，
    /// <c>RulesAssembly.RegisterUnit</c> 在登记等级之前就写基础属性，评级换算要按单位等级求值而单位还没登记等级，
    /// 整个单位注册抛 <c>ArgumentException</c>（"单位未通过 RegisterUnit 注册"）。
    /// 复现 + 不变量各一条：
    /// <list type="bullet">
    /// <item>复现：带 <c>level_curve_ref</c> 的职业、<c>base_stats</c> 含评级属性，注册单位不抛异常，值 = 点数 ÷ 评级换算除数。</item>
    /// <item>不变量：评级换算曲线随等级变化时，单位按<b>起始等级</b>（不是 1 级）换算——起始 10 级的单位，除数取 10 级那一点。</item>
    /// </list>
    /// </summary>
    public sealed class RulesAssemblyRatingStatAtRegistrationTests
    {
        private static readonly Id Dodge = new Id("stat.rsr_dodge");
        private static readonly Id Unit = new Id("unit.rsr_unit");
        private static readonly Id Class = new Id("arch.class.rsr_hero");
        private static readonly Id MobClass = new Id("arch.class.rsr_mob");

        private const double Points = 10;
        private const double DivisorLevel1 = 100;
        private const double DivisorLevel10 = 200;

        private const string StatDefinitionJson = @"
        {
            ""table"": ""stat.definition"", ""schema_version"": 2,
            ""rows"": [
                { ""id"": ""stat.rsr_power"", ""name_key"": ""l10n.stat.rsr_power.name"", ""category"": ""primary"", ""default_base"": 0 },
                { ""id"": ""stat.rsr_dodge"", ""name_key"": ""l10n.stat.rsr_dodge.name"", ""category"": ""percent"", ""group"": ""secondary"",
                  ""default_base"": 0, ""conversion_ref"": ""stat.rating.rsr_dodge"", ""clamp"": { ""min"": 0, ""max"": 0.5 } }
            ]
        }";

        // 评级换算：1 级每 100 点折 100%（除数 100），10 级每 200 点折 100%（除数 200），中间线性插值。
        private const string RatingConversionJson = @"
        {
            ""table"": ""stat.rating_conversion"", ""schema_version"": 2,
            ""rows"": [
                { ""id"": ""stat.rating.rsr_dodge"", ""entries"": [ { ""x"": 1, ""y"": 100 }, { ""x"": 10, ""y"": 200 } ] }
            ]
        }";

        private const string LevelCurveJson = @"
        {
            ""table"": ""prog.level_curve"", ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""prog.curve.rsr"", ""max_level"": 10,
                  ""entries"": [
                    { ""level"": 1, ""xp_to_next"": 10, ""growth"": {} },
                    { ""level"": 2, ""xp_to_next"": 10, ""growth"": {} },
                    { ""level"": 3, ""xp_to_next"": 10, ""growth"": {} },
                    { ""level"": 4, ""xp_to_next"": 10, ""growth"": {} },
                    { ""level"": 5, ""xp_to_next"": 10, ""growth"": {} },
                    { ""level"": 6, ""xp_to_next"": 10, ""growth"": {} },
                    { ""level"": 7, ""xp_to_next"": 10, ""growth"": {} },
                    { ""level"": 8, ""xp_to_next"": 10, ""growth"": {} },
                    { ""level"": 9, ""xp_to_next"": 10, ""growth"": {} },
                    { ""level"": 10, ""xp_to_next"": 0, ""growth"": {} }
                  ] }
            ]
        }";

        private const string ArchPowerTypeJson = @"
        {
            ""table"": ""arch.power_type"", ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""arch.power.rsr_energy"", ""name_key"": ""l10n.arch.power.rsr_energy.name"",
                  ""max_source"": { ""kind"": ""fixed"", ""value"": 100 } }
            ]
        }";

        private const string ArchClassJson = @"
        {
            ""table"": ""arch.class"", ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""arch.class.rsr_hero"", ""name_key"": ""l10n.arch.class.rsr_hero.name"",
                  ""primary_stat"": ""stat.rsr_power"", ""base_stats"": { ""stat.rsr_dodge"": 10 },
                  ""power_types"": [ ""arch.power.rsr_energy"" ], ""level_curve_ref"": ""prog.curve.rsr"" },
                { ""id"": ""arch.class.rsr_mob"", ""name_key"": ""l10n.arch.class.rsr_mob.name"",
                  ""primary_stat"": ""stat.rsr_power"", ""base_stats"": { ""stat.rsr_dodge"": 10 },
                  ""power_types"": [ ""arch.power.rsr_energy"" ] }
            ]
        }";

        private const string HitTableJson = @"
        {
            ""table"": ""combat.hit_table_config"", ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""combat.hit_table.default"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 }
            ]
        }";

        private const string ResistCurveJson = @"{ ""table"": ""combat.resist_curve"", ""schema_version"": 1, ""rows"": [] }";

        private static Core.Rules.Assembly.RulesAssembly Build()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", StatDefinitionJson)
                .Add("stat.rating_conversion", RatingConversionJson)
                .Add("prog.level_curve", LevelCurveJson)
                .Add("arch.class", ArchClassJson)
                .Add("arch.power_type", ArchPowerTypeJson)
                .Add("combat.hit_table_config", HitTableJson)
                .Add("combat.resist_curve", ResistCurveJson);

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            Core.Rules.Assembly.RulesSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            return new Core.Rules.Assembly.RulesAssembly(
                bus, registry, new RngHost(1), new Tests.Rules.Combat.FakeUnitAccess(), new StubSpatialQuery(), new WorldSim(bus),
                statOptions: new Core.Numbers.StatBlock.StatHostOptions(),
                autoRegisterTickHandlers: false);
        }

        [Fact]
        public void RegisterUnit_ClassBaseStatsContainRatingStat_DoesNotThrow_AndConvertsAtLevelOne()
        {
            var rules = Build();

            var ex = Record.Exception(() => rules.RegisterUnit(Unit, Class, raceId: null));

            Assert.Null(ex);
            Assert.Equal(Points / DivisorLevel1, rules.Stats.GetStat(Unit, Dodge), 9);
        }

        [Fact]
        public void RegisterUnit_ClassWithoutLevelCurve_RatingStatConvertsAtLevelOne_NotThrow()
        {
            var rules = Build();

            var ex = Record.Exception(() => rules.RegisterUnit(Unit, MobClass, raceId: null));

            Assert.Null(ex);
            Assert.False(rules.Progression.IsRegistered(Unit), "不带 level_curve_ref 的职业不登记等级");
            Assert.Equal(Points / DivisorLevel1, rules.Stats.GetStat(Unit, Dodge), 9);
        }

        [Fact]
        public void RegisterUnit_StartLevelTen_ConvertsWithTheLevelTenDivisor_NotLevelOne()
        {
            var rules = Build();

            rules.RegisterUnit(Unit, Class, raceId: null, level: 10);

            Assert.Equal(10, rules.Progression.GetLevel(Unit));
            Assert.Equal(Points / DivisorLevel10, rules.Stats.GetStat(Unit, Dodge), 9);
        }
    }
}
