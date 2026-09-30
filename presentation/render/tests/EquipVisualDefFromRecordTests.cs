using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary>
    /// T-M14（ADR-0125）：<see cref="EquipVisualDef.FromRecord"/> 的正常与错误路径（缺字段、类型不符、未知 mode）。
    /// 直接用 <see cref="DataRecord"/> 构造函数喂绕过校验的行。
    /// </summary>
    public sealed class EquipVisualDefFromRecordTests
    {
        private static DataRecord Raw(string json) =>
            new DataRecord(DisplaySchemas.EquipVisual, "display.equip_visual.t", null, (JsonObject)JsonReader.Parse(json));

        [Fact]
        public void FromRecord_SlotMeshRow_ParsesModeAndOptionalFields()
        {
            var def = EquipVisualDef.FromRecord(Raw(
                "{\"id\":\"display.equip_visual.a\",\"item_id\":\"item.sword\",\"mode\":\"slot_mesh\"," +
                "\"slot_id\":\"slot.main_hand\",\"mesh_ref\":\"res.mesh_sword\"}"));

            Assert.Equal(new Id("display.equip_visual.a"), def.Id);
            Assert.Equal(new Id("item.sword"), def.ItemId);
            Assert.Equal(EquipVisualMode.SlotMesh, def.Mode);
            Assert.Equal(new Id("slot.main_hand"), def.SlotId);
            Assert.Equal(new Id("res.mesh_sword"), def.MeshRef);
            Assert.Null(def.SocketId);
            Assert.Null(def.ModelRef);
        }

        [Fact]
        public void FromRecord_SocketAttachRow_ParsesModeAndOptionalFields()
        {
            var def = EquipVisualDef.FromRecord(Raw(
                "{\"id\":\"display.equip_visual.b\",\"item_id\":\"item.sword\",\"mode\":\"socket_attach\"," +
                "\"socket_id\":\"socket.hand_r\",\"model_ref\":\"res.model_sword\"}"));

            Assert.Equal(EquipVisualMode.SocketAttach, def.Mode);
            Assert.Equal(new Id("socket.hand_r"), def.SocketId);
            Assert.Equal(new Id("res.model_sword"), def.ModelRef);
            Assert.Null(def.SlotId);
            Assert.Null(def.MeshRef);
        }

        [Theory]
        [InlineData("{\"item_id\":\"item.sword\",\"mode\":\"slot_mesh\"}", "id")]
        [InlineData("{\"id\":\"display.equip_visual.a\",\"mode\":\"slot_mesh\"}", "item_id")]
        [InlineData("{\"id\":\"display.equip_visual.a\",\"item_id\":\"item.sword\"}", "mode")]
        public void FromRecord_MissingRequiredField_ThrowsDataFieldException_NamingTheField(string rowJson, string expectedField)
        {
            var ex = Assert.Throws<DataFieldException>(() => EquipVisualDef.FromRecord(Raw(rowJson)));

            Assert.Equal(expectedField, ex.Field);
            Assert.Equal("display.equip_visual", ex.Table);
        }

        [Theory]
        [InlineData("{\"id\":5,\"item_id\":\"item.sword\",\"mode\":\"slot_mesh\"}", "id")]
        [InlineData("{\"id\":\"Bad Id\",\"item_id\":\"item.sword\",\"mode\":\"slot_mesh\"}", "id")]
        [InlineData("{\"id\":\"display.equip_visual.a\",\"item_id\":true,\"mode\":\"slot_mesh\"}", "item_id")]
        [InlineData("{\"id\":\"display.equip_visual.a\",\"item_id\":\"item.sword\",\"mode\":1}", "mode")]
        [InlineData("{\"id\":\"display.equip_visual.a\",\"item_id\":\"item.sword\",\"mode\":null}", "mode")]
        public void FromRecord_WrongTypeRequiredField_ThrowsDataFieldException_NamingTheField(string rowJson, string expectedField)
        {
            var ex = Assert.Throws<DataFieldException>(() => EquipVisualDef.FromRecord(Raw(rowJson)));

            Assert.Equal(expectedField, ex.Field);
        }

        [Theory]
        [InlineData("explode")]
        [InlineData("")]
        [InlineData("SLOT_MESH")] // 区分大小写
        [InlineData("slot_mesh ")]
        public void FromRecord_UnknownMode_ThrowsDataFieldException_OnModeField(string mode)
        {
            var ex = Assert.Throws<DataFieldException>(() => EquipVisualDef.FromRecord(Raw(
                "{\"id\":\"display.equip_visual.a\",\"item_id\":\"item.sword\",\"mode\":\"" + mode + "\"}")));

            Assert.Equal("mode", ex.Field);
            Assert.Contains("未知的 mode 取值", ex.Message);
        }
    }
}
