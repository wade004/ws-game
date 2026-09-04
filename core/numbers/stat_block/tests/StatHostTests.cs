using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Numbers.StatBlock
{
    public class StatHostTests
    {
        // -----------------------------------------------------------------
        // 公共夹具
        // -----------------------------------------------------------------

        private const string StatDefinitionJson = @"
        {
            ""table"": ""stat.definition"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""stat.test_a"", ""name_key"": ""l10n.stat.test_a.name"", ""group"": ""primary"", ""default_base"": 0 },
                { ""id"": ""stat.test_b"", ""name_key"": ""l10n.stat.test_b.name"", ""group"": ""primary"", ""default_base"": 0 },
                { ""id"": ""stat.test_c"", ""name_key"": ""l10n.stat.test_c.name"", ""group"": ""primary"", ""default_base"": 0, ""min"": 0, ""max"": 100 },
                { ""id"": ""stat.test_d"", ""name_key"": ""l10n.stat.test_d.name"", ""group"": ""primary"", ""default_base"": 0, ""is_rating"": true, ""rating_conversion_ref"": ""stat.rating.test_curve"" },
                { ""id"": ""stat.test_r"", ""name_key"": ""l10n.stat.test_r.name"", ""group"": ""resistance"", ""default_base"": 0 }
            ]
        }";

        // 判断记录（2026-09-05，设计层裁定）：entries[].level 就是单位等级（取代此前"level 是
        // 评级原始值自己的插值断点"的判断），见 StatHost.ConvertRating 源码注释。测试曲线的两个
        // 断点固定在 level 1 与 level 10。
        private const string RatingConversionJson = @"
        {
            ""table"": ""stat.rating_conversion"",
            ""schema_version"": 1,
            ""rows"": [
                {
                    ""id"": ""stat.rating.test_curve"",
                    ""entries"": [
                        { ""level"": 1, ""points_per_percent"": 10 },
                        { ""level"": 10, ""points_per_percent"": 5 }
                    ]
                }
            ]
        }";

        private static readonly Id StatA = new Id("stat.test_a");
        private static readonly Id StatB = new Id("stat.test_b");
        private static readonly Id StatC = new Id("stat.test_c");
        private static readonly Id StatD = new Id("stat.test_d");
        private static readonly Id StatR = new Id("stat.test_r");

        private static IEventBus MakeBus(List<StatChangedEvent> captured)
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                new EventDefinition(StatBlockEventKeys.StatChanged, "stat",
                    new[] { "unitId", "stat", "oldValue", "newValue" }),
            });
            var bus = new EventBus(catalog);
            bus.Subscribe<StatChangedEvent>(StatBlockEventKeys.StatChanged, e => captured.Add(e));
            return bus;
        }

        private static (IDataRegistry Registry, ValidationReport Report) BuildRegistry(
            IEventBus bus,
            string statDefinitionJson,
            string? ratingConversionJson,
            IValidationRule? extraRule = null)
        {
            var source = new InMemoryDataSource().Add("stat.definition", statDefinitionJson);
            if (ratingConversionJson != null)
            {
                source.Add("stat.rating_conversion", ratingConversionJson);
            }

            var registry = new DataRegistry(source, bus);
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(StatSchemas.RatingConversion);
            if (extraRule != null)
            {
                registry.RegisterValidationRule(extraRule);
            }

            var report = registry.LoadAll();
            return (registry, report);
        }

        private static (StatHost Host, List<StatChangedEvent> Captured, IEventBus Bus, ValidationReport Report) BuildHost(
            bool enableRatingConversion = false,
            bool enableResistanceGroup = true,
            LevelLookup? levelLookup = null)
        {
            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (registry, report) = BuildRegistry(bus, StatDefinitionJson, RatingConversionJson, new StatDefinitionValidationRule());
            Assert.False(report.IsBlocking);

            var host = new StatHost(registry, bus, new StatHostOptions
            {
                EnableRatingConversion = enableRatingConversion,
                EnableResistanceGroup = enableResistanceGroup,
                LevelLookup = levelLookup,
            });
            return (host, captured, bus, report);
        }

        // -----------------------------------------------------------------
        // 1~5. 三段式聚合手算用例（验收要求 >= 5 组）
        // -----------------------------------------------------------------

        [Fact]
        public void Aggregate_BaseOnly()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t1");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);

            Assert.Equal(10.0, host.GetStat(unit, StatA), 10);
        }

        [Fact]
        public void Aggregate_BasePlusFlat()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t2");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 5.0, new Id("src.gear1")));

            // (10 + 5) = 15
            Assert.Equal(15.0, host.GetStat(unit, StatA), 10);
        }

        [Fact]
        public void Aggregate_BasePlusFlatPlusPct()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t3");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 5.0, new Id("src.gear1")));
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Pct, 0.20, new Id("src.buff1")));

            // (10 + 5) * (1 + 0.20) = 18
            Assert.Equal(18.0, host.GetStat(unit, StatA), 10);
        }

        [Fact]
        public void Aggregate_TwoDifferentMultGroupsMultiplyAcrossGroups()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t4");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 5.0, new Id("src.gear1")));
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Pct, 0.20, new Id("src.buff1")));
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.10, new Id("src.aura1"), "group_a"));
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.05, new Id("src.aura2"), "group_b"));

            // ((10 + 5) * 1.20) * 1.10 * 1.05 = 18 * 1.10 * 1.05 = 20.79
            Assert.Equal(20.79, host.GetStat(unit, StatA), 8);
        }

        [Fact]
        public void Aggregate_SameMultGroupAddsInsteadOfMultiplying()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t5");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.10, new Id("src.aura1"), "same_group"));
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.20, new Id("src.aura2"), "same_group"));

            // 组内相加: 10 * (1 + 0.10 + 0.20) = 13, 而不是 10 * 1.10 * 1.20 = 13.2
            Assert.Equal(13.0, host.GetStat(unit, StatA), 10);
            Assert.NotEqual(13.2, host.GetStat(unit, StatA), 5);
        }

        // -----------------------------------------------------------------
        // 6. RemoveModifiersBySource：跨多个属性、一次性回退
        // -----------------------------------------------------------------

        [Fact]
        public void RemoveModifiersBySource_RevertsAllAffectedStats()
        {
            var (host, captured, bus, _) = BuildHost();
            var unit = new Id("unit.t6");
            var source = new Id("src.full_gear_piece");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);
            host.SetBase(unit, StatB, 20.0);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 5.0, source));
            host.AddModifier(unit, new StatModifier(StatB, StatModifierOp.Flat, 3.0, source));
            bus.DispatchPending();

            Assert.Equal(15.0, host.GetStat(unit, StatA), 10);
            Assert.Equal(23.0, host.GetStat(unit, StatB), 10);

            captured.Clear();
            host.RemoveModifiersBySource(unit, source);
            bus.DispatchPending();

            Assert.Equal(10.0, host.GetStat(unit, StatA), 10);
            Assert.Equal(20.0, host.GetStat(unit, StatB), 10);
            Assert.Equal(2, captured.Count);
            Assert.Contains(captured, e => e.Stat == StatA && e.OldValue == 15.0 && e.NewValue == 10.0);
            Assert.Contains(captured, e => e.Stat == StatB && e.OldValue == 23.0 && e.NewValue == 20.0);
        }

        // -----------------------------------------------------------------
        // 7. min/max 夹取
        // -----------------------------------------------------------------

        [Fact]
        public void Clamp_MinAndMax()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t7");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatC, 150.0);
            Assert.Equal(100.0, host.GetStat(unit, StatC), 10);

            host.SetBase(unit, StatC, -50.0);
            Assert.Equal(0.0, host.GetStat(unit, StatC), 10);
        }

        // -----------------------------------------------------------------
        // 8~12. 评级换算（2026-09-05 设计层裁定：entries[].level 是单位等级，不是评级原始值）
        // -----------------------------------------------------------------

        [Fact]
        public void RatingConversion_SameRawValueDiffersByUnitLevel()
        {
            var levelByUnit = new Dictionary<Id, int>();
            LevelLookup lookup = unitId => levelByUnit.TryGetValue(unitId, out var lvl) ? lvl : 1;

            var (host, _, _, _) = BuildHost(enableRatingConversion: true, levelLookup: lookup);
            var unitLevel1 = new Id("unit.rating_lvl1");
            var unitLevel10 = new Id("unit.rating_lvl10");
            host.RegisterUnit(unitLevel1);
            host.RegisterUnit(unitLevel10);
            levelByUnit[unitLevel1] = 1;
            levelByUnit[unitLevel10] = 10;

            host.SetBase(unitLevel1, StatD, 50.0);
            host.SetBase(unitLevel10, StatD, 50.0);

            // 同一评级原始值 50：1 级 ppp=10 -> percent=5；10 级 ppp=5 -> percent=10。
            Assert.Equal(5.0, host.GetStat(unitLevel1, StatD), 10);
            Assert.Equal(10.0, host.GetStat(unitLevel10, StatD), 10);
        }

        [Fact]
        public void RatingConversion_MidLevelInterpolatesBetweenEntries()
        {
            LevelLookup lookup = _ => 5; // entries 断点 1..10 之间，t = (5-1)/(10-1) = 4/9

            var (host, _, _, _) = BuildHost(enableRatingConversion: true, levelLookup: lookup);
            var unit = new Id("unit.rating_mid");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatD, 50.0);

            // ppp = 10 + 4/9*(5-10) = 70/9；percent = 50 / (70/9) = 45/7
            Assert.Equal(45.0 / 7.0, host.GetStat(unit, StatD), 10);
        }

        [Fact]
        public void RatingConversion_NoLevelLookup_DefaultsToLevelOne()
        {
            var (host, _, _, _) = BuildHost(enableRatingConversion: true); // 不传 levelLookup
            var unit = new Id("unit.rating_nolookup");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatD, 50.0);

            // 无委托按 1 级处理 -> ppp=10（entries[0]） -> percent = 5
            Assert.Equal(5.0, host.GetStat(unit, StatD), 10);
        }

        [Fact]
        public void RatingConversion_LevelOutOfRangeClampsToEndpoint()
        {
            LevelLookup lookupAboveMax = _ => 999; // 越界高端，取 entries[10].ppp=5
            LevelLookup lookupBelowMin = _ => -5;  // 越界低端，取 entries[1].ppp=10

            var (hostHigh, _, _, _) = BuildHost(enableRatingConversion: true, levelLookup: lookupAboveMax);
            var unitHigh = new Id("unit.rating_high");
            hostHigh.RegisterUnit(unitHigh);
            hostHigh.SetBase(unitHigh, StatD, 50.0);
            Assert.Equal(10.0, hostHigh.GetStat(unitHigh, StatD), 10); // 50/5

            var (hostLow, _, _, _) = BuildHost(enableRatingConversion: true, levelLookup: lookupBelowMin);
            var unitLow = new Id("unit.rating_low");
            hostLow.RegisterUnit(unitLow);
            hostLow.SetBase(unitLow, StatD, 50.0);
            Assert.Equal(5.0, hostLow.GetStat(unitLow, StatD), 10); // 50/10
        }

        [Fact]
        public void RatingConversion_DisabledPassesRawValueThrough()
        {
            var (host, _, _, _) = BuildHost(enableRatingConversion: false);
            var unit = new Id("unit.t10");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatD, 50.0);

            // 未启用评级换算：即使 is_rating=true，也直通原值
            Assert.Equal(50.0, host.GetStat(unit, StatD), 10);
        }

        // -----------------------------------------------------------------
        // 11~12. stat.changed 事件字段与"只在变化时发出"
        // -----------------------------------------------------------------

        [Fact]
        public void StatChanged_FiresOnlyOnActualChange_WithCorrectFields()
        {
            var (host, captured, bus, _) = BuildHost();
            var unit = new Id("unit.t11");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0); // 0 -> 10
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 5.0, new Id("src.gear1"))); // 10 -> 15
            bus.DispatchPending();

            Assert.Equal(2, captured.Count);

            Assert.Equal(unit, captured[0].UnitId);
            Assert.Equal(StatA, captured[0].Stat);
            Assert.Equal(0.0, captured[0].OldValue, 10);
            Assert.Equal(10.0, captured[0].NewValue, 10);

            Assert.Equal(unit, captured[1].UnitId);
            Assert.Equal(StatA, captured[1].Stat);
            Assert.Equal(10.0, captured[1].OldValue, 10);
            Assert.Equal(15.0, captured[1].NewValue, 10);
        }

        [Fact]
        public void SetBase_SameValue_DoesNotFireEvent()
        {
            var (host, captured, bus, _) = BuildHost();
            var unit = new Id("unit.t12");
            host.RegisterUnit(unit);

            // default_base 已经是 0，设成 0 不应触发事件
            host.SetBase(unit, StatA, 0.0);
            bus.DispatchPending();
            Assert.Empty(captured);

            host.SetBase(unit, StatA, 10.0); // 0 -> 10，触发一次
            bus.DispatchPending();
            Assert.Single(captured);

            captured.Clear();
            host.SetBase(unit, StatA, 10.0); // 同值再设一次，不触发
            bus.DispatchPending();
            Assert.Empty(captured);
        }

        // -----------------------------------------------------------------
        // 13. 抗性维度关闭
        // -----------------------------------------------------------------

        [Fact]
        public void ResistanceGroup_DisabledReturnsZeroAndWarns()
        {
            var (host, _, _, _) = BuildHost(enableResistanceGroup: false);
            var unit = new Id("unit.t13");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatR, 30.0);

            Assert.Equal(0.0, host.GetStat(unit, StatR));
            Assert.NotEmpty(host.Warnings);
            Assert.Contains(host.Warnings, w => w.Contains("stat.test_r"));
        }

        [Fact]
        public void ResistanceGroup_EnabledAggregatesNormally()
        {
            var (host, _, _, _) = BuildHost(enableResistanceGroup: true);
            var unit = new Id("unit.t13b");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatR, 30.0);

            Assert.Equal(30.0, host.GetStat(unit, StatR), 10);
        }

        // -----------------------------------------------------------------
        // 14~15. 未注册单位 / 未知属性异常
        // -----------------------------------------------------------------

        [Fact]
        public void UnregisteredUnit_Throws()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.never_registered");

            Assert.Throws<InvalidOperationException>(() => host.GetStat(unit, StatA));
            Assert.Throws<InvalidOperationException>(() => host.SetBase(unit, StatA, 1.0));
            Assert.Throws<InvalidOperationException>(() => host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 1.0, new Id("src.x"))));
            Assert.Throws<InvalidOperationException>(() => host.RemoveModifiersBySource(unit, new Id("src.x")));
        }

        [Fact]
        public void UnknownStat_Throws()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t15");
            host.RegisterUnit(unit);

            var unknownStat = new Id("stat.does_not_exist");
            Assert.Throws<ArgumentException>(() => host.GetStat(unit, unknownStat));
            Assert.Throws<ArgumentException>(() => host.SetBase(unit, unknownStat, 1.0));
            Assert.Throws<ArgumentException>(() => host.AddModifier(unit, new StatModifier(unknownStat, StatModifierOp.Flat, 1.0, new Id("src.x"))));
        }

        // -----------------------------------------------------------------
        // 16~17. IValidationRule 正反
        // -----------------------------------------------------------------

        [Fact]
        public void ValidationRule_ValidData_NoIssuesFromCustomRule()
        {
            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, StatDefinitionJson, RatingConversionJson, new StatDefinitionValidationRule());

            Assert.False(report.IsBlocking);
            foreach (var issue in report.Issues)
            {
                Assert.NotEqual(StatDefinitionValidationRule.CheckMinMaxOrder, issue.Check);
                Assert.NotEqual(StatDefinitionValidationRule.CheckRatingRefRequiresIsRating, issue.Check);
            }
        }

        [Fact]
        public void ValidationRule_RatingRefWithoutIsRating_ReportsError()
        {
            const string badJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""stat.bad_rating"", ""name_key"": ""l10n.stat.bad_rating.name"", ""group"": ""primary"", ""rating_conversion_ref"": ""stat.rating.test_curve"" }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, badJson, RatingConversionJson, new StatDefinitionValidationRule());

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == StatDefinitionValidationRule.CheckRatingRefRequiresIsRating
                                                 && i.RecordKey == "stat.bad_rating");
        }

        [Fact]
        public void ValidationRule_MinGreaterThanMax_ReportsError()
        {
            const string badJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""stat.bad_minmax"", ""name_key"": ""l10n.stat.bad_minmax.name"", ""group"": ""primary"", ""min"": 10, ""max"": 5 }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, badJson, null, new StatDefinitionValidationRule());

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == StatDefinitionValidationRule.CheckMinMaxOrder
                                                 && i.RecordKey == "stat.bad_minmax");
        }

        // -----------------------------------------------------------------
        // 18. 浮点确定性：相同插入顺序两次运行结果逐位相等
        // -----------------------------------------------------------------

        [Fact]
        public void Determinism_SameInsertionOrder_ProducesBitExactResults()
        {
            var (host, _, _, _) = BuildHost();
            var unitX = new Id("unit.determinism_x");
            var unitY = new Id("unit.determinism_y");
            host.RegisterUnit(unitX);
            host.RegisterUnit(unitY);

            void ApplySequence(Id unit)
            {
                host.SetBase(unit, StatA, 10.0);
                host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 3.0, new Id("src.a")));
                host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 2.0, new Id("src.b")));
                host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Pct, 0.1, new Id("src.c")));
                host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.05, new Id("src.d"), "group1"));
                host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.02, new Id("src.e"), "group2"));
            }

            ApplySequence(unitX);
            ApplySequence(unitY);

            var valueX = host.GetStat(unitX, StatA);
            var valueY = host.GetStat(unitY, StatA);

            Assert.Equal(valueX, valueY); // 逐位相等（xunit 对 double 的默认 Equal 是精确比较）
        }
    }
}
