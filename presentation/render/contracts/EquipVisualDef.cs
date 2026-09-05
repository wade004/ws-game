using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Presentation.Render
{
    /// <summary>装备呈现方式（见 04_数据与内容管线.md 第 7.1.2 节 <c>display.equip_visual.mode</c>）：
    /// 替换槽位网格，或作为独立模型挂接到挂点。</summary>
    public enum EquipVisualMode
    {
        SlotMesh,
        SocketAttach,
    }

    /// <summary>
    /// <c>display.equip_visual</c> 一条记录的不可变运行期视图（04 第 7.1.2 节字段表；
    /// <c>Core.Foundation.DisplayInfo.DisplaySchemas.EquipVisual</c> 登记了该表的字段级
    /// schema，本类型是本模块内解析出的强类型消费视图，同 <c>VfxDef</c>/<c>SfxDef</c> 惯例）。
    /// </summary>
    public sealed class EquipVisualDef
    {
        public Id Id { get; }

        /// <summary>指向 <c>item.template</c>。</summary>
        public Id ItemId { get; }

        public EquipVisualMode Mode { get; }

        /// <summary><see cref="EquipVisualMode.SlotMesh"/> 时非空，对应 <c>display.map</c> 的
        /// <c>slots</c>（model 型）——缺口 10：<see cref="Presentation.Render.SpriteViewBase"/>
        /// 复用本字段承载"纸娃娃层名"（sprite 型没有独立的槽位概念，借用同一张表，见该类型
        /// <c>OnEvent</c> 判断记录）。</summary>
        public Id? SlotId { get; }

        /// <summary><see cref="EquipVisualMode.SlotMesh"/> 时非空，model 型下是替换网格资源引用；
        /// 缺口 10：sprite 型下 <c>SpriteViewBase</c> 直接把本字段当作该纸娃娃层的资源 Id 使用
        /// （不再经 <c>ResolveLayerResourceId</c> 的方向档位换算——04 未给 <c>mesh_ref</c> 定义按方向
        /// 拆分的子结构，本字段视为该层的唯一资源，是已知简化，见判断记录）。</summary>
        public Id? MeshRef { get; }

        public Id? SocketId { get; }

        public Id? ModelRef { get; }

        public EquipVisualDef(Id id, Id itemId, EquipVisualMode mode, Id? slotId, Id? meshRef, Id? socketId, Id? modelRef)
        {
            Id = id;
            ItemId = itemId;
            Mode = mode;
            SlotId = slotId;
            MeshRef = meshRef;
            SocketId = socketId;
            ModelRef = modelRef;
        }

        /// <summary>从一条已加载的 <c>display.equip_visual</c> <see cref="DataRecord"/> 构造（假设记录
        /// 已通过校验，缺失必填字段按 <see cref="DataRecord"/> 惯例抛 <see cref="DataFieldException"/>）。</summary>
        public static EquipVisualDef FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var itemId = record.GetId("item_id");
            var mode = ParseMode(record, record.GetString("mode"));
            var slotId = record.TryGetId("slot_id", out var slotVal) ? (Id?)slotVal : null;
            var meshRef = record.TryGetId("mesh_ref", out var meshVal) ? (Id?)meshVal : null;
            var socketId = record.TryGetId("socket_id", out var socketVal) ? (Id?)socketVal : null;
            var modelRef = record.TryGetId("model_ref", out var modelVal) ? (Id?)modelVal : null;
            return new EquipVisualDef(id, itemId, mode, slotId, meshRef, socketId, modelRef);
        }

        private static EquipVisualMode ParseMode(DataRecord record, string value) => value switch
        {
            "slot_mesh" => EquipVisualMode.SlotMesh,
            "socket_attach" => EquipVisualMode.SocketAttach,
            _ => throw new DataFieldException(record.Table.Name, record.Key, "mode", $"未知的 mode 取值：\"{value}\""),
        };
    }
}
