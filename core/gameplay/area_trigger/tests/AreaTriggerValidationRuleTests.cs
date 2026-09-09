using System.Linq;
using Core.Foundation.Common.Json;
using Core.Gameplay.AreaTrigger;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    public sealed class AreaTriggerValidationRuleTests
    {
        private static Core.Foundation.DataRegistry.ValidationReport Validate(JsonObject row)
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus, row);
            registry.RegisterValidationRule(new AreaTriggerParamsFieldGroupRule());
            return registry.LoadAll();
        }

        // ShapeKindRule_IllegalKind_ReportsError / ShapeKindRule_LegalKind_NoIssue 已迁移至
        // AreaTriggerSchemaCoverageTests（ADR-0019 / F1b：AreaTriggerShapeKindRule 整条退役，
        // shape.kind 合法性改由 AreaTriggerSchemas.ShapeSchema 的 VariantSchema 内置
        // variant_discriminator 检查覆盖，不再需要注册独立的 IValidationRule；见
        // AreaTriggerValidationRules.cs 顶部退役记录）。

        [Fact]
        public void ParamsFieldGroupRule_MapTransitionMissingTargetMap_ReportsError()
        {
            var row = J.O(
                ("id", J.S("area.sample_door")),
                ("map_id", J.S("world.sample_map")),
                ("shape", AreaTriggerTestSupport.CircleShape(0, 0, 5)),
                ("trigger_type", J.S("map_transition")),
                ("params", J.O()));

            var report = Validate(row);
            Assert.Contains(report.Issues, i => i.Check == "area_trigger_params_field_group" && i.Severity == Core.Foundation.DataRegistry.ValidationSeverity.Error);
        }

        [Fact]
        public void ParamsFieldGroupRule_EncounterStartMissingEncounterRef_ReportsError()
        {
            var row = J.O(
                ("id", J.S("area.sample_boss")),
                ("map_id", J.S("world.sample_map")),
                ("shape", AreaTriggerTestSupport.CircleShape(0, 0, 5)),
                ("trigger_type", J.S("encounter_start")),
                ("params", J.O()));

            var report = Validate(row);
            Assert.Contains(report.Issues, i => i.Check == "area_trigger_params_field_group");
        }

        [Fact]
        public void ParamsFieldGroupRule_ScriptMissingHookId_ReportsError()
        {
            var row = J.O(
                ("id", J.S("area.sample_trap_zone")),
                ("map_id", J.S("world.sample_map")),
                ("shape", AreaTriggerTestSupport.CircleShape(0, 0, 5)),
                ("trigger_type", J.S("script")),
                ("params", J.O()));

            var report = Validate(row);
            Assert.Contains(report.Issues, i => i.Check == "area_trigger_params_field_group");
        }

        [Fact]
        public void ParamsFieldGroupRule_QuestExploreEmptyParams_NoIssue()
        {
            var row = AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map");
            var report = Validate(row);
            Assert.DoesNotContain(report.Issues, i => i.Check == "area_trigger_params_field_group");
        }
    }
}
