using System;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Rules.Combat;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// T-N1-8（ADR-0030 决策 6；04 第 1.1 节表清单 <c>combat.level_diff_table</c> 行）：
    /// <see cref="CombatSchemas.LevelDiffTable"/> 的 schema 覆盖测试（合法记录、缺必填、非法负数
    /// 纵轴、非单调曲线），惯例同 <c>StatWeightSchemaTests.cs</c>。本文件独立装配一个最小注册表，
    /// 不依赖 <see cref="CombatTestSupport"/>（那份夹具面向 <c>Resolver</c> 结算回归，登记了更多
    /// 无关的表）。
    /// </summary>
    public sealed class CombatLevelDiffTableSchemaTests
    {
        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private static DataRegistry BuildRegistry(string rowsJson, bool withMonotonicRule = false)
        {
            var envelope = "{\"table\":\"combat.level_diff_table\",\"schema_version\":1,\"rows\":" + rowsJson + "}";
            var source = new InMemoryDataSource().Add("combat.level_diff_table", envelope);
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(CombatSchemas.LevelDiffTable);
            if (withMonotonicRule)
            {
                registry.RegisterValidationRule(new CurveMonotonicFiniteRule());
            }

            return registry;
        }

        private const string WellFormedRow = @"
        [{
            ""id"": ""combat.level_diff.cov_ok"",
            ""miss_bonus"": [{""x"": -3, ""y"": -0.5}, {""x"": 0, ""y"": 0}, {""x"": 3, ""y"": 0.5}],
            ""crit_suppression"": [{""x"": -3, ""y"": -0.3}, {""x"": 0, ""y"": 0}, {""x"": 3, ""y"": 0.3}],
            ""xp_factor"": [{""x"": -5, ""y"": 0}, {""x"": 0, ""y"": 1}, {""x"": 5, ""y"": 2}],
            ""grey_line"": [{""x"": 1, ""y"": 2}, {""x"": 60, ""y"": 15}]
        }]";

        [Fact]
        public void LevelDiffTable_WellFormed_LoadsWithoutErrors()
        {
            var registry = BuildRegistry(WellFormedRow);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void LevelDiffTable_MissingCritSuppression_ReportsRequiredField()
        {
            var rows = @"
            [{
                ""id"": ""combat.level_diff.cov_missing"",
                ""miss_bonus"": [{""x"": 0, ""y"": 0}],
                ""xp_factor"": [{""x"": 0, ""y"": 1}],
                ""grey_line"": [{""x"": 1, ""y"": 2}]
            }]";

            var registry = BuildRegistry(rows);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "crit_suppression");
        }

        [Fact]
        public void LevelDiffTable_XpFactorNegativeY_ReportsFieldRange()
        {
            var rows = @"
            [{
                ""id"": ""combat.level_diff.cov_negative_xp"",
                ""miss_bonus"": [{""x"": 0, ""y"": 0}],
                ""crit_suppression"": [{""x"": 0, ""y"": 0}],
                ""xp_factor"": [{""x"": 0, ""y"": -1}],
                ""grey_line"": [{""x"": 1, ""y"": 2}]
            }]";

            var registry = BuildRegistry(rows);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field != null && i.Field.StartsWith("xp_factor", StringComparison.Ordinal));
        }

        [Fact]
        public void LevelDiffTable_GreyLineNegativeY_ReportsFieldRange()
        {
            var rows = @"
            [{
                ""id"": ""combat.level_diff.cov_negative_grey"",
                ""miss_bonus"": [{""x"": 0, ""y"": 0}],
                ""crit_suppression"": [{""x"": 0, ""y"": 0}],
                ""xp_factor"": [{""x"": 0, ""y"": 1}],
                ""grey_line"": [{""x"": 1, ""y"": -2}]
            }]";

            var registry = BuildRegistry(rows);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field != null && i.Field.StartsWith("grey_line", StringComparison.Ordinal));
        }

        [Fact]
        public void LevelDiffTable_NonMonotonicMissBonus_ReportsCurveMonotonicFinite()
        {
            // miss_bonus 的 y 在 x=1 处比 x=0 处更小——违反"纵轴沿横轴不递减"（04 第 5 节"曲线单调
            // 有限"）。本模块自己不注册这条规则（见 CombatSchemas.LevelDiffTable 判断记录 3"不新增
            // 专属校验规则，单调有限由全局注册的 CurveMonotonicFiniteRule 统一覆盖"），本用例显式
            // 注册它来验证登记的 CurveSchema.BreakpointsField 元数据确实会被该规则识别、报告。
            var rows = @"
            [{
                ""id"": ""combat.level_diff.cov_non_monotonic"",
                ""miss_bonus"": [{""x"": 0, ""y"": 0.5}, {""x"": 1, ""y"": 0.1}],
                ""crit_suppression"": [{""x"": 0, ""y"": 0}],
                ""xp_factor"": [{""x"": 0, ""y"": 1}],
                ""grey_line"": [{""x"": 1, ""y"": 2}]
            }]";

            var registry = BuildRegistry(rows, withMonotonicRule: true);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "curve_monotonic_finite" && i.Field == "miss_bonus");
        }
    }
}
