using System.Collections.Generic;
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
        /// 缺口 10：sprite 型下取代此前"直接当最终资源 Id 使用"的已知简化——ADR-0071 决策 1 拍板：
        /// 本字段与身体层 <c>display.map.sprite_set_id</c> 同一位置，是"该装备层的资源集引用"，不是
        /// 最终资源 Id。04 仍未给本字段定义按方向拆分的子结构（不拆 schema，见该 ADR"为什么不加子
        /// 结构"），语义上的"按方向拆分"改由运行期经 <c>SpriteViewBase.ResolveEquipLayerResourceId</c>
        /// 承担——与身体层 <c>ResolveLayerResourceId</c> 共用同一套方向档位换算公式（<c>StripCategoryPrefix</c>
        /// 取资源集名字 + 方向裸档位名 + 层名），使装备层与身体层一样随朝向切换素材，不再只整体跟随
        /// 精灵实例翻转。</summary>
        public Id? MeshRef { get; }

        public Id? SocketId { get; }

        public Id? ModelRef { get; }

        /// <summary>装备面板纸娃娃预览区使用的方向档（<c>preview_direction</c>，手感设计/08 第 4 节、ADR-0123，
        /// 方向槽位 id 如 <c>dir.front_side_r</c>）；缺省为 null，表示取姿势集的正面档。纯新增可选字段。</summary>
        public Id? PreviewDirection { get; }

        /// <summary>逐方向层序（<c>behind_directions</c>，手感设计/08 第 5 节、ADR-0152）：该装备层在这些方向档
        /// （方向槽位 id，如 <c>dir.back</c>）上画在全部其余纸娃娃层之后（身体后面）；其余方向保持现行顺序。
        /// 缺省为空 = 所有方向都是现行顺序（逐位不变）。纯新增可选字段。</summary>
        public IReadOnlyList<Id> BehindDirections { get; }

        public EquipVisualDef(Id id, Id itemId, EquipVisualMode mode, Id? slotId, Id? meshRef, Id? socketId, Id? modelRef)
            : this(id, itemId, mode, slotId, meshRef, socketId, modelRef, null)
        {
        }

        /// <summary>带预览方向档的重载（ABI 只新增：保留七参数构造并转调本重载）。</summary>
        public EquipVisualDef(Id id, Id itemId, EquipVisualMode mode, Id? slotId, Id? meshRef, Id? socketId, Id? modelRef,
            Id? previewDirection)
            : this(id, itemId, mode, slotId, meshRef, socketId, modelRef, previewDirection, null)
        {
        }

        /// <summary>带逐方向层序的重载（ABI 只新增：保留八参数构造并转调本重载）。</summary>
        public EquipVisualDef(Id id, Id itemId, EquipVisualMode mode, Id? slotId, Id? meshRef, Id? socketId, Id? modelRef,
            Id? previewDirection, IReadOnlyList<Id>? behindDirections)
        {
            Id = id;
            ItemId = itemId;
            Mode = mode;
            SlotId = slotId;
            MeshRef = meshRef;
            SocketId = socketId;
            ModelRef = modelRef;
            PreviewDirection = previewDirection;
            BehindDirections = behindDirections ?? System.Array.Empty<Id>();
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
            var previewDirection = record.TryGetId("preview_direction", out var previewVal) ? (Id?)previewVal : null;
            var behind = record.TryGetIdList("behind_directions", out var behindList) ? behindList : null;
            return new EquipVisualDef(id, itemId, mode, slotId, meshRef, socketId, modelRef, previewDirection, behind);
        }

        private static EquipVisualMode ParseMode(DataRecord record, string value) => value switch
        {
            "slot_mesh" => EquipVisualMode.SlotMesh,
            "socket_attach" => EquipVisualMode.SocketAttach,
            _ => throw new DataFieldException(record.Table.Name, record.Key, "mode", $"未知的 mode 取值：\"{value}\""),
        };
    }
}
