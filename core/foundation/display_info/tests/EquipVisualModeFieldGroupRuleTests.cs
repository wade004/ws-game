using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    public class EquipVisualModeFieldGroupRuleTests
    {
        private static IReadOnlyList<ValidationIssue> FieldGroupIssues(ValidationReport report) =>
            report.Issues.Where(i => i.Check == "equip_visual_mode_field_group").ToArray();

        // ---------------------------------------------------------------
        // 正例：slot_mesh / socket_attach 各一条合法记录，不产生字段组问题。
        // ---------------------------------------------------------------

        [Fact]
        public void ValidSlotMeshRecord_NoFieldGroupIssues()
        {
            const string row = @"
            {
              ""id"": ""display.equip_visual.test_slot_mesh_valid"",
              ""item_id"": ""item.test_slot_mesh_valid"",
              ""mode"": ""slot_mesh"",
              ""slot_id"": ""slot.head"",
              ""mesh_ref"": ""model.test_helmet""
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.equip_visual"] = "[" + row + "]" },
                new IValidationRule[] { new EquipVisualModeFieldGroupRule() });

            Assert.False(report.IsBlocking);
            Assert.Empty(FieldGroupIssues(report));
        }

        [Fact]
        public void ValidSocketAttachRecord_NoFieldGroupIssues()
        {
            const string row = @"
            {
              ""id"": ""display.equip_visual.test_socket_attach_valid"",
              ""item_id"": ""item.test_socket_attach_valid"",
              ""mode"": ""socket_attach"",
              ""socket_id"": ""socket.main_hand"",
              ""model_ref"": ""model.test_sword""
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.equip_visual"] = "[" + row + "]" },
                new IValidationRule[] { new EquipVisualModeFieldGroupRule() });

            Assert.False(report.IsBlocking);
            Assert.Empty(FieldGroupIssues(report));
        }

        // ---------------------------------------------------------------
        // 反例：slot_mesh 型缺 slot_id / 缺 mesh_ref。
        // ---------------------------------------------------------------

        [Fact]
        public void SlotMeshRecord_MissingSlotId_ReportsRequiredFieldError()
        {
            const string row = @"
            {
              ""id"": ""display.equip_visual.test_slot_mesh_missing_slot_id"",
              ""item_id"": ""item.test_missing_slot_id"",
              ""mode"": ""slot_mesh"",
              ""mesh_ref"": ""model.test_helmet""
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.equip_visual"] = "[" + row + "]" },
                new IValidationRule[] { new EquipVisualModeFieldGroupRule() });

            var issues = FieldGroupIssues(report);
            Assert.Contains(issues, i => i.Field == "slot_id" && i.RecordKey == "display.equip_visual.test_slot_mesh_missing_slot_id");
        }

        [Fact]
        public void SlotMeshRecord_MissingMeshRef_ReportsRequiredFieldError()
        {
            const string row = @"
            {
              ""id"": ""display.equip_visual.test_slot_mesh_missing_mesh_ref"",
              ""item_id"": ""item.test_missing_mesh_ref"",
              ""mode"": ""slot_mesh"",
              ""slot_id"": ""slot.head""
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.equip_visual"] = "[" + row + "]" },
                new IValidationRule[] { new EquipVisualModeFieldGroupRule() });

            var issues = FieldGroupIssues(report);
            Assert.Contains(issues, i => i.Field == "mesh_ref" && i.RecordKey == "display.equip_visual.test_slot_mesh_missing_mesh_ref");
        }

        // ---------------------------------------------------------------
        // 反例：socket_attach 型缺 socket_id / 缺 model_ref。
        // ---------------------------------------------------------------

        [Fact]
        public void SocketAttachRecord_MissingSocketId_ReportsRequiredFieldError()
        {
            const string row = @"
            {
              ""id"": ""display.equip_visual.test_socket_attach_missing_socket_id"",
              ""item_id"": ""item.test_missing_socket_id"",
              ""mode"": ""socket_attach"",
              ""model_ref"": ""model.test_sword""
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.equip_visual"] = "[" + row + "]" },
                new IValidationRule[] { new EquipVisualModeFieldGroupRule() });

            var issues = FieldGroupIssues(report);
            Assert.Contains(issues, i => i.Field == "socket_id" && i.RecordKey == "display.equip_visual.test_socket_attach_missing_socket_id");
        }

        [Fact]
        public void SocketAttachRecord_MissingModelRef_ReportsRequiredFieldError()
        {
            const string row = @"
            {
              ""id"": ""display.equip_visual.test_socket_attach_missing_model_ref"",
              ""item_id"": ""item.test_missing_model_ref"",
              ""mode"": ""socket_attach"",
              ""socket_id"": ""socket.main_hand""
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.equip_visual"] = "[" + row + "]" },
                new IValidationRule[] { new EquipVisualModeFieldGroupRule() });

            var issues = FieldGroupIssues(report);
            Assert.Contains(issues, i => i.Field == "model_ref" && i.RecordKey == "display.equip_visual.test_socket_attach_missing_model_ref");
        }

        // ---------------------------------------------------------------
        // 判断记录锁定：另一模式的字段组同时出现不报错（本规则只检查"必填"，不检查"互斥留空"，
        // 见 EquipVisualModeFieldGroupRule 类型注释判断记录——文档未明确写"另一模式下必须留空"，
        // 按最保守解释不发明额外语义）。
        // ---------------------------------------------------------------

        [Fact]
        public void SlotMeshRecord_WithSocketAttachFieldsAlsoPresent_NoFieldGroupIssues()
        {
            const string row = @"
            {
              ""id"": ""display.equip_visual.test_slot_mesh_with_extra_socket_fields"",
              ""item_id"": ""item.test_extra_socket_fields"",
              ""mode"": ""slot_mesh"",
              ""slot_id"": ""slot.head"",
              ""mesh_ref"": ""model.test_helmet"",
              ""socket_id"": ""socket.main_hand"",
              ""model_ref"": ""model.test_sword""
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.equip_visual"] = "[" + row + "]" },
                new IValidationRule[] { new EquipVisualModeFieldGroupRule() });

            Assert.Empty(FieldGroupIssues(report));
        }
    }
}
