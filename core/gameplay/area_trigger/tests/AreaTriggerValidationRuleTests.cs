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
            registry.RegisterValidationRule(new AreaTriggerShapeKindRule());
            registry.RegisterValidationRule(new AreaTriggerParamsFieldGroupRule());
            return registry.LoadAll();
        }

        [Fact]
        public void ShapeKindRule_IllegalKind_ReportsError()
        {
            var row = AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map");
            var builder = new JsonObjectBuilder();
            foreach (var kv in row)
            {
                builder.Add(kv.Key, kv.Key == "shape" ? J.O(("kind", J.S("triangle"))) : kv.Value);
            }

            var report = Validate(builder.Build());
            Assert.Contains(report.Issues, i => i.Check == "area_trigger_shape_kind" && i.Severity == Core.Foundation.DataRegistry.ValidationSeverity.Error);
        }

        [Fact]
        public void ShapeKindRule_LegalKind_NoIssue()
        {
            var row = AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map");
            var report = Validate(row);
            Assert.DoesNotContain(report.Issues, i => i.Check == "area_trigger_shape_kind");
        }

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
