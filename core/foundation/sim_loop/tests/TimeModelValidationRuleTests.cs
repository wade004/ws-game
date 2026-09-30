using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// 测试覆盖梳理 T-H1（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节）：
    /// <see cref="TimeModelValidationRule"/> 的 8 条检查（04 第 3.1 节"mode: discrete 时必填"系列
    /// 约束，数据校验是 11 第 8 节第一条门槛）此前全仓库零测试；<see cref="TimeModelDefinition.FromRecord"/>
    /// 的离散字段解析与未知 <c>initiative_policy</c> 分支同样零测试。
    /// <para>
    /// 反例设计：每条检查一个反例，其余字段全部合法，断言整份校验报告恰好只有这一条命中
    /// （条数与检查 id 都比对，不只是"包含"），避免规则误把别的情形一并报出或漏报后被别条掩盖。
    /// 全部经真实 <see cref="DataRegistry"/> 走 <c>LoadAll</c>（只注册 <see cref="TimeModelSchema.Table"/>
    /// 与本规则，<c>initiative_stat</c> 的引用目标表 <c>stat.definition</c> 用一张自造的最小表代替，
    /// 本模块不依赖上层数值模块）。<see cref="TimeModelDefinition.FromRecord"/> 的"未知
    /// <c>initiative_policy</c>"分支无法经 <c>LoadAll</c> 到达（枚举取值会先被 schema 拦下），直接构造
    /// <see cref="DataRecord"/> 验证。
    /// </para>
    /// </summary>
    public sealed class TimeModelValidationRuleTests
    {
        private const string StatTable = "stat.definition";

        private const string InitiativeStatId = "stat.tmv_initiative";

        private static readonly TableSchema StatDefinitionStub = new TableSchema(
            name: StatTable,
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[] { new FieldSchema("id", FieldKind.Id, required: true) });

        private static string TimeModelJson(params string[] rows) => @"
        {
            ""table"": ""found.time_model"",
            ""schema_version"": 1,
            ""rows"": [ " + string.Join(",\n", rows) + @" ]
        }";

        private static string StatJson => @"
        {
            ""table"": """ + StatTable + @""",
            ""schema_version"": 1,
            ""rows"": [ { ""id"": """ + InitiativeStatId + @""" } ]
        }";

        /// <summary>一条离散战斗记录行；<paramref name="extraFields"/> 为额外（或覆盖用的）字段片段，
        /// 形如 <c>"\"seconds_per_turn\": 6"</c>。基线只含 id/scope/mode，不含任何离散必填字段，
        /// 由每个用例按需补齐——避免"基线已带了某字段、反例想去掉它"时要做字符串删除。</summary>
        private static string DiscreteRow(string idName, params string[] fields)
        {
            var parts = new List<string>
            {
                $"\"id\": \"found.time_model.{idName}\"",
                "\"scope\": \"combat\"",
                "\"mode\": \"discrete\"",
            };
            parts.AddRange(fields);
            return "{ " + string.Join(", ", parts) + " }";
        }

        private const string ValidSecondsPerTurn = "\"seconds_per_turn\": 6";
        private const string ValidPolicyFixedOrder = "\"initiative_policy\": \"fixed_order\"";
        private const string ValidMovementDistance = "\"movement_budget_rule\": \"distance\"";

        private static ValidationReport Run(params string[] rows)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("found.time_model", TimeModelJson(rows))
                .Add(StatTable, StatJson);

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatDefinitionStub);
            registry.RegisterSchema(TimeModelSchema.Table);
            registry.RegisterValidationRule(new TimeModelValidationRule());
            return registry.LoadAll();
        }

        private static void AssertOnly(ValidationReport report, string check, string recordKey, string field)
        {
            var issue = Assert.Single(report.Issues);
            Assert.Equal(check, issue.Check);
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
            Assert.Equal("found.time_model", issue.Table);
            Assert.Equal(recordKey, issue.RecordKey);
            Assert.Equal(field, issue.Field);
            Assert.True(report.IsBlocking);
        }

        // ---- 全合法零命中 ---------------------------------------------------------

        [Fact]
        public void AllLegal_DiscreteFixedOrderDistance_NoIssues()
        {
            var report = Run(DiscreteRow("ok_fixed", ValidSecondsPerTurn, ValidPolicyFixedOrder, ValidMovementDistance));

            Assert.Empty(report.Issues);
            Assert.False(report.IsBlocking);
        }

        [Fact]
        public void AllLegal_EveryOptionalFieldPresent_NoIssues()
        {
            // 覆盖全部可选字段同时出现：initiative_stat 策略 + action_points 移动规则 + 额度 + 单价 + grid_snap。
            var report = Run(DiscreteRow(
                "ok_full",
                ValidSecondsPerTurn,
                "\"initiative_policy\": \"initiative_stat\"",
                $"\"initiative_stat\": \"{InitiativeStatId}\"",
                "\"movement_budget_rule\": \"action_points\"",
                "\"action_points_per_turn\": 3",
                "\"movement_action_cost_per_unit\": 0.5",
                "\"grid_snap\": { \"cell_size\": 1 }"));

            Assert.Empty(report.Issues);
            Assert.False(report.IsBlocking);
        }

        [Theory]
        [InlineData("initiative_stat")]
        [InlineData("action_points")]
        [InlineData("fixed_order")]
        [InlineData("atb")]
        public void AllLegal_EveryDeclaredInitiativePolicy_AcceptedByRule(string policy)
        {
            // atb 是预留扩展位（TimeModelSchema.InitiativePolicyValues 注释）：校验器认可，不在本规则拦截。
            var fields = new List<string> { ValidSecondsPerTurn, $"\"initiative_policy\": \"{policy}\"", ValidMovementDistance };
            if (policy == "initiative_stat")
            {
                fields.Add($"\"initiative_stat\": \"{InitiativeStatId}\"");
            }

            var report = Run(DiscreteRow("ok_policy", fields.ToArray()));

            Assert.Empty(report.Issues);
        }

        [Fact]
        public void ContinuousMode_SkipsEveryDiscreteCheck_EvenWithIllegalDiscreteFields()
        {
            // 非 discrete 直接 continue：连续记录上带着的离散字段（即便非法）不被本规则检查。
            var report = Run(@"{ ""id"": ""found.time_model.cont"", ""scope"": ""exploration"", ""mode"": ""continuous"",
                ""seconds_per_turn"": 0, ""action_points_per_turn"": -1, ""movement_budget_rule"": ""action_points"" }");

            Assert.DoesNotContain(report.Issues, i => i.Check.StartsWith("time_model_", StringComparison.Ordinal));
        }

        // ---- 8 条检查各一个反例 -----------------------------------------------------

        [Fact]
        public void Check1_DiscreteWithoutSecondsPerTurn_ReportsOnlyThat()
        {
            var report = Run(DiscreteRow("bad1", ValidPolicyFixedOrder, ValidMovementDistance));

            AssertOnly(report, TimeModelValidationRule.CheckDiscreteRequiresSecondsPerTurn,
                "found.time_model.bad1", "seconds_per_turn");
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-6")]
        [InlineData("-0.5")]
        public void Check2_SecondsPerTurnNotPositive_ReportsOnlyThat(string value)
        {
            var report = Run(DiscreteRow(
                "bad2", $"\"seconds_per_turn\": {value}", ValidPolicyFixedOrder, ValidMovementDistance));

            AssertOnly(report, TimeModelValidationRule.CheckSecondsPerTurnPositive,
                "found.time_model.bad2", "seconds_per_turn");
        }

        [Fact]
        public void Check3_DiscreteWithoutInitiativePolicy_ReportsOnlyThat()
        {
            var report = Run(DiscreteRow("bad3", ValidSecondsPerTurn, ValidMovementDistance));

            AssertOnly(report, TimeModelValidationRule.CheckDiscreteRequiresInitiativePolicy,
                "found.time_model.bad3", "initiative_policy");
        }

        [Fact]
        public void Check4_InitiativeStatPolicyWithoutInitiativeStat_ReportsOnlyThat()
        {
            var report = Run(DiscreteRow(
                "bad4", ValidSecondsPerTurn, "\"initiative_policy\": \"initiative_stat\"", ValidMovementDistance));

            AssertOnly(report, TimeModelValidationRule.CheckInitiativeStatPolicyRequiresInitiativeStat,
                "found.time_model.bad4", "initiative_stat");
        }

        [Fact]
        public void Check4_NonInitiativeStatPolicyWithoutInitiativeStat_IsFine()
        {
            // 反向：只有 initiative_policy == initiative_stat 才要求 initiative_stat。
            var report = Run(DiscreteRow(
                "ok4", ValidSecondsPerTurn, "\"initiative_policy\": \"action_points\"", ValidMovementDistance));

            Assert.Empty(report.Issues);
        }

        [Fact]
        public void Check5_DiscreteWithoutMovementBudgetRule_ReportsOnlyThat()
        {
            var report = Run(DiscreteRow("bad5", ValidSecondsPerTurn, ValidPolicyFixedOrder));

            AssertOnly(report, TimeModelValidationRule.CheckDiscreteRequiresMovementBudgetRule,
                "found.time_model.bad5", "movement_budget_rule");
        }

        [Fact]
        public void Check6_ActionPointsMovementRuleWithoutCostPerUnit_ReportsOnlyThat()
        {
            var report = Run(DiscreteRow(
                "bad6", ValidSecondsPerTurn, ValidPolicyFixedOrder, "\"movement_budget_rule\": \"action_points\""));

            AssertOnly(report, TimeModelValidationRule.CheckActionPointsMovementRuleRequiresCostPerUnit,
                "found.time_model.bad6", "movement_action_cost_per_unit");
        }

        [Fact]
        public void Check6_DistanceMovementRuleWithoutCostPerUnit_IsFine()
        {
            var report = Run(DiscreteRow("ok6", ValidSecondsPerTurn, ValidPolicyFixedOrder, ValidMovementDistance));

            Assert.Empty(report.Issues);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        public void Check7_MovementActionCostPerUnitNotPositive_ReportsOnlyThat(string value)
        {
            var report = Run(DiscreteRow(
                "bad7", ValidSecondsPerTurn, ValidPolicyFixedOrder,
                "\"movement_budget_rule\": \"action_points\"", $"\"movement_action_cost_per_unit\": {value}"));

            AssertOnly(report, TimeModelValidationRule.CheckMovementActionCostPerUnitPositive,
                "found.time_model.bad7", "movement_action_cost_per_unit");
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-2")]
        public void Check8_ActionPointsPerTurnNotPositive_ReportsOnlyThat(string value)
        {
            var report = Run(DiscreteRow(
                "bad8", ValidSecondsPerTurn, ValidPolicyFixedOrder, ValidMovementDistance,
                $"\"action_points_per_turn\": {value}"));

            AssertOnly(report, TimeModelValidationRule.CheckActionPointsPerTurnPositive,
                "found.time_model.bad8", "action_points_per_turn");
        }

        // ---- 组合与多记录 -----------------------------------------------------------

        [Fact]
        public void DiscreteWithNoRequiredFields_ReportsEachMissingRequirementOnce()
        {
            // 三个"必填缺失"互相独立，各报一次，不因一条命中而短路其余。
            var report = Run(DiscreteRow("bare"));

            var expected = new[]
            {
                TimeModelValidationRule.CheckDiscreteRequiresSecondsPerTurn,
                TimeModelValidationRule.CheckDiscreteRequiresInitiativePolicy,
                TimeModelValidationRule.CheckDiscreteRequiresMovementBudgetRule,
            };
            Assert.Equal(expected.OrderBy(c => c, StringComparer.Ordinal), report.Issues.Select(i => i.Check).OrderBy(c => c, StringComparer.Ordinal));
        }

        [Fact]
        public void MultipleRecords_OnlyTheIllegalOneIsReported_WithItsOwnKey()
        {
            var report = Run(
                DiscreteRow("good", ValidSecondsPerTurn, ValidPolicyFixedOrder, ValidMovementDistance),
                DiscreteRow("broken", ValidSecondsPerTurn, ValidPolicyFixedOrder, ValidMovementDistance, "\"action_points_per_turn\": 0"),
                @"{ ""id"": ""found.time_model.explore"", ""scope"": ""exploration"", ""mode"": ""continuous"" }");

            AssertOnly(report, TimeModelValidationRule.CheckActionPointsPerTurnPositive,
                "found.time_model.broken", "action_points_per_turn");
        }

        [Fact]
        public void EightChecks_HaveDistinctIds_AllPrefixedWithTimeModel()
        {
            var ids = new[]
            {
                TimeModelValidationRule.CheckDiscreteRequiresSecondsPerTurn,
                TimeModelValidationRule.CheckDiscreteRequiresInitiativePolicy,
                TimeModelValidationRule.CheckInitiativeStatPolicyRequiresInitiativeStat,
                TimeModelValidationRule.CheckDiscreteRequiresMovementBudgetRule,
                TimeModelValidationRule.CheckSecondsPerTurnPositive,
                TimeModelValidationRule.CheckActionPointsMovementRuleRequiresCostPerUnit,
                TimeModelValidationRule.CheckActionPointsPerTurnPositive,
                TimeModelValidationRule.CheckMovementActionCostPerUnitPositive,
            };

            Assert.Equal(8, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.All(ids, id => Assert.StartsWith("time_model_", id, StringComparison.Ordinal));
        }

        // ---- TimeModelDefinition.FromRecord ---------------------------------------

        private static DataRecord Record(string json)
        {
            var raw = (JsonObject)JsonReader.Parse(json);
            var key = ((JsonString)raw["id"]).Value;
            return new DataRecord(TimeModelSchema.Table, key, Id.Parse(key), raw);
        }

        [Fact]
        public void FromRecord_NullRecord_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => TimeModelDefinition.FromRecord(null!));
        }

        [Fact]
        public void FromRecord_ContinuousMinimalRecord_UsesDocumentedDefaults()
        {
            var def = TimeModelDefinition.FromRecord(Record(
                @"{ ""id"": ""found.time_model.fr_cont"", ""scope"": ""exploration"", ""mode"": ""continuous"" }"));

            Assert.Equal(Id.Parse("found.time_model.fr_cont"), def.Id);
            Assert.Equal("exploration", def.Scope);
            Assert.Equal(TimeModelMode.Continuous, def.Mode);
            // 04 第 3.1 节默认值：seconds_per_turn 默认 6，action_points_per_turn 默认 1，其余可选字段为空。
            Assert.Equal(6.0, def.SecondsPerTurn);
            Assert.Equal(InitiativePolicy.FixedOrder, def.InitiativePolicy);
            Assert.Null(def.InitiativeStat);
            Assert.Null(def.MovementBudgetRule);
            Assert.Equal(1.0, def.ActionPointsPerTurn);
            Assert.Null(def.MovementActionCostPerUnit);
            Assert.Null(def.GridSnapCellSize);
        }

        [Fact]
        public void FromRecord_DiscreteFullRecord_ParsesEveryDiscreteField()
        {
            const double secondsPerTurn = 9.5;
            const double actionPoints = 4;
            const double costPerUnit = 0.25;
            const double cellSize = 2;

            var def = TimeModelDefinition.FromRecord(Record(
                @"{ ""id"": ""found.time_model.fr_full"", ""scope"": ""combat"", ""mode"": ""discrete"",
                    ""seconds_per_turn"": 9.5, ""initiative_policy"": ""initiative_stat"",
                    ""initiative_stat"": """ + InitiativeStatId + @""", ""movement_budget_rule"": ""action_points"",
                    ""action_points_per_turn"": 4, ""movement_action_cost_per_unit"": 0.25,
                    ""grid_snap"": { ""cell_size"": 2 } }"));

            Assert.Equal("combat", def.Scope);
            Assert.Equal(TimeModelMode.Discrete, def.Mode);
            Assert.Equal(secondsPerTurn, def.SecondsPerTurn);
            Assert.Equal(InitiativePolicy.InitiativeStat, def.InitiativePolicy);
            Assert.Equal(Id.Parse(InitiativeStatId), def.InitiativeStat);
            Assert.Equal("action_points", def.MovementBudgetRule);
            Assert.Equal(actionPoints, def.ActionPointsPerTurn);
            Assert.Equal(costPerUnit, def.MovementActionCostPerUnit);
            Assert.Equal(cellSize, def.GridSnapCellSize);
        }

        [Theory]
        [InlineData("initiative_stat", InitiativePolicy.InitiativeStat)]
        [InlineData("action_points", InitiativePolicy.ActionPoints)]
        [InlineData("fixed_order", InitiativePolicy.FixedOrder)]
        [InlineData("atb", InitiativePolicy.Atb)]
        public void FromRecord_EveryKnownInitiativePolicyText_MapsToItsEnumValue(string text, InitiativePolicy expected)
        {
            var def = TimeModelDefinition.FromRecord(Record(
                @"{ ""id"": ""found.time_model.fr_policy"", ""scope"": ""combat"", ""mode"": ""discrete"",
                    ""initiative_policy"": """ + text + @""" }"));

            Assert.Equal(expected, def.InitiativePolicy);
        }

        [Fact]
        public void FromRecord_InitiativePolicyTextsCoverExactlyTheSchemaEnumValues()
        {
            // schema 声明的合法取值与 FromRecord 的映射表必须同步：每个合法值都能被解析（不抛未知取值）。
            foreach (var text in TimeModelSchema.InitiativePolicyValues)
            {
                var def = TimeModelDefinition.FromRecord(Record(
                    @"{ ""id"": ""found.time_model.fr_sync"", ""scope"": ""combat"", ""mode"": ""discrete"",
                        ""initiative_policy"": """ + text + @""" }"));
                Assert.True(Enum.IsDefined(typeof(InitiativePolicy), def.InitiativePolicy));
            }
        }

        [Fact]
        public void FromRecord_UnknownInitiativePolicy_ThrowsDataFieldExceptionNamingTableRecordField()
        {
            const string unknown = "round_robin";

            var ex = Assert.Throws<DataFieldException>(() => TimeModelDefinition.FromRecord(Record(
                @"{ ""id"": ""found.time_model.fr_unknown"", ""scope"": ""combat"", ""mode"": ""discrete"",
                    ""initiative_policy"": """ + unknown + @""" }")));

            Assert.Equal("found.time_model", ex.Table);
            Assert.Equal("found.time_model.fr_unknown", ex.RecordKey);
            Assert.Equal("initiative_policy", ex.Field);
            Assert.Contains(unknown, ex.Message);
        }

        [Theory]
        [InlineData(@"{ ""id"": ""found.time_model.fr_noscope"", ""mode"": ""continuous"" }", "scope")]
        [InlineData(@"{ ""id"": ""found.time_model.fr_nomode"", ""scope"": ""combat"" }", "mode")]
        public void FromRecord_MissingRequiredScopeOrMode_ThrowsDataFieldExceptionNamingField(string json, string field)
        {
            var ex = Assert.Throws<DataFieldException>(() => TimeModelDefinition.FromRecord(Record(json)));

            Assert.Equal(field, ex.Field);
        }
    }
}
