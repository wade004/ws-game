using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// <see cref="Core.Rules.Combat.CombatHitTableValidationRule"/>/
    /// <see cref="Core.Rules.Combat.CombatResistCurveValidationRule"/> 校验规则测试（见任务书
    /// "设计"一节"校验规则：概率 base 落在 [0,1]；max_reduction ≤ 1；曲线 entries 单调"、"tests"
    /// 一节"校验规则正反各一"）。
    /// </summary>
    public class CombatValidationRuleTests
    {
        private static IDataRegistry MakeRegistry(string hitTableRows, string resistCurveRows)
        {
            var bus = CombatTestSupport.MakeBus();
            var source = new InMemoryDataSource()
                .Add("combat.hit_table_config", "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": " + hitTableRows + "}")
                .Add("combat.resist_curve", "{\"table\": \"combat.resist_curve\", \"schema_version\": 1, \"rows\": " + resistCurveRows + "}");

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(Core.Rules.Combat.CombatSchemas.HitTableConfig);
            registry.RegisterSchema(Core.Rules.Combat.CombatSchemas.ResistCurve);
            registry.RegisterValidationRule(new Core.Rules.Combat.CombatHitTableValidationRule());
            registry.RegisterValidationRule(new Core.Rules.Combat.CombatResistCurveValidationRule());
            return registry;
        }

        private const string ValidHitTableRow = @"[
            { ""id"": ""combat.hit_table.valid"",
              ""miss"": {""enabled"": true, ""base"": 0.05}, ""dodge"": {""enabled"": true, ""base"": 0.1},
              ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
              ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": true, ""base"": 0.2},
              ""crit_multiplier_base"": 2.0 }
        ]";

        private const string InvalidHitTableRow = @"[
            { ""id"": ""combat.hit_table.invalid"",
              ""miss"": {""enabled"": true, ""base"": 1.5}, ""dodge"": {""enabled"": false, ""base"": 0},
              ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
              ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
              ""crit_multiplier_base"": 2.0 }
        ]";

        private const string ValidResistCurveRow = @"[
            { ""id"": ""combat.resist.valid_table"", ""school"": ""school.frost"", ""kind"": ""table"",
              ""entries"": [ {""value"": 0, ""reduction"": 0}, {""value"": 100, ""reduction"": 0.5} ],
              ""max_reduction"": 0.75 }
        ]";

        private const string InvalidResistCurveRow_MaxReductionOutOfRange = @"[
            { ""id"": ""combat.resist.invalid_max"", ""school"": ""school.frost"", ""kind"": ""saturation"",
              ""k"": 50, ""max_reduction"": 1.5 }
        ]";

        private const string InvalidResistCurveRow_NonMonotonicEntries = @"[
            { ""id"": ""combat.resist.invalid_entries"", ""school"": ""school.frost"", ""kind"": ""table"",
              ""entries"": [ {""value"": 100, ""reduction"": 0.5}, {""value"": 50, ""reduction"": 0.8} ],
              ""max_reduction"": 0.75 }
        ]";

        [Fact]
        public void HitTable_ValidBaseRange_PassesValidation()
        {
            var registry = MakeRegistry(ValidHitTableRow, "[]");
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);
        }

        [Fact]
        public void HitTable_BaseOutOfRange_FailsValidation()
        {
            var registry = MakeRegistry(InvalidHitTableRow, "[]");
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            var found = false;
            foreach (var issue in report.Issues)
            {
                if (issue.Check == "hit_table_base_range") found = true;
            }
            Assert.True(found);
        }

        [Fact]
        public void ResistCurve_Valid_PassesValidation()
        {
            var registry = MakeRegistry("[]", ValidResistCurveRow);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);
        }

        [Fact]
        public void ResistCurve_MaxReductionOutOfRange_FailsValidation()
        {
            var registry = MakeRegistry("[]", InvalidResistCurveRow_MaxReductionOutOfRange);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            var found = false;
            foreach (var issue in report.Issues)
            {
                if (issue.Check == "resist_curve_max_reduction_range") found = true;
            }
            Assert.True(found);
        }

        [Fact]
        public void ResistCurve_NonMonotonicEntries_FailsValidation()
        {
            var registry = MakeRegistry("[]", InvalidResistCurveRow_NonMonotonicEntries);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            var found = false;
            foreach (var issue in report.Issues)
            {
                if (issue.Check == "resist_curve_entries_monotonic") found = true;
            }
            Assert.True(found);
        }
    }
}
