using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// <c>kind: sprite</c> 型外形的一条锚点定义（见 09_表现层.md 第 3.3.1 节"锚点表"）。
    /// GP-PRES-05 收口之前，<see cref="SpriteInfo.AnchorPoints"/> 的值直接就是一个裸
    /// <see cref="Vec2"/>（等价于本类型只保留 <see cref="Offset"/> 一个字段），09 文档承诺的
    /// <c>parent_layer</c>（必填）与 <c>offset_by_direction</c>（可选，按方向档位覆盖偏移）两个
    /// 字段在运行期模型里完全没有落点，内容若使用它们会被 parser 当成未知字段忽略或结构不匹配
    /// 直接解析失败（见 <see cref="DisplayInfo.FromRecord"/> 判断记录），方向差异也只能靠整体
    /// <c>flipX</c> 镜像，得不到文档承诺的方向覆盖（见
    /// <c>architecture/落地计划/audit-20260907/gameplay-presentation.md</c> GP-PRES-07）。
    /// <para>
    /// 判断记录（不单独保留 <c>anchor_id</c> 字段）：09 表格把 <c>anchor_id</c> 列为锚点表的一个
    /// 字段，但运行期 <see cref="SpriteInfo.AnchorPoints"/> 本就是"<c>anchor_id</c> 裸名 → 锚点值"
    /// 的字典（键即 id），本类型不重复保留一份等价信息；调用方已经持有键，不需要从值对象里再读
    /// 一次。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="ParentLayer"/> 用 <see cref="string"/> 不用 <see cref="Id"/>）：
    /// <see cref="SpriteInfo.PaperdollLayers"/> 本身就是 <c>IReadOnlyList&lt;string&gt;</c>（层名
    /// 允许不满足 <see cref="Id"/> 要求的"至少一个点分段"格式，仓库现有 <c>data/_sample</c> 的层名
    /// 就是裸名如 <c>"body"</c>/<c>"hand_main"</c>，不是 <c>"layer.body"</c> 这种点分形式）——
    /// <see cref="ParentLayer"/> 引用的正是这份层名列表里的一个元素，类型必须与被引用集合一致，
    /// 用 <see cref="Id"/> 会在这类合法的裸层名上构造期直接抛异常。<see cref="OffsetByDirection"/>
    /// 的键则确实是方向槽位 <see cref="Id"/>（如 <c>"dir.side_r"</c>，与
    /// <c>display.map.mirror_pairs.direction_slot</c> 同一套点分 id 空间），两者引用的集合本身
    /// 格式不同，不能用同一个类型硬套。
    /// </para>
    /// </summary>
    public sealed class AnchorDef
    {
        /// <summary>所属层名（09 表格 <c>parent_layer</c>，必填），引用
        /// <see cref="SpriteInfo.PaperdollLayers"/> 里的一个元素。当前运行期实现的纸娃娃层之间
        /// 没有各自独立的局部变换（各层共享同一个精灵实例的 transform，只按列表顺序决定层内 z 序，
        /// 见 <c>presentation/render/core/SpriteViewBase</c>），本字段目前只作为内容登记的结构化
        /// 元数据保留（供将来"锚点偏移相对所属层局部坐标而非整体精灵坐标"这类扩展使用，或供内容
        /// 工具/编辑器做锚点归属校验），当前 <see cref="ResolveOffset"/> 计算世界坐标时不读取本
        /// 字段——这与文档字面"相对父层原点的偏移"存在实现简化，已按现有纸娃娃管线的真实几何模型
        /// 如实标注，不假装做了本管线做不到的事情。</summary>
        public string ParentLayer { get; }

        /// <summary>相对父层原点的默认偏移（09 表格 <c>offset</c>，必填）——没有匹配
        /// <see cref="OffsetByDirection"/> 覆盖时的取值。</summary>
        public Vec2 Offset { get; }

        /// <summary>按方向档位覆盖偏移（09 表格 <c>offset_by_direction</c>，可选，默认空字典，
        /// 缺省用 <see cref="Offset"/>）。键是方向槽位 id（<c>IRenderConventionHost.ResolveDirectionSlot</c>
        /// 返回的 <c>SlotId</c>，与 <c>display.map.mirror_pairs</c>/<c>direction_slot</c> 同一套 id
        /// 空间），见 <see cref="ResolveOffset"/>。</summary>
        public IReadOnlyDictionary<Id, Vec2> OffsetByDirection { get; }

        public AnchorDef(string parentLayer, Vec2 offset, IReadOnlyDictionary<Id, Vec2>? offsetByDirection = null)
        {
            ParentLayer = parentLayer;
            Offset = offset;
            OffsetByDirection = offsetByDirection ?? EmptyOffsetByDirection;
        }

        /// <summary>按 09 表格"<c>offset_by_direction</c>：按方向档位覆盖偏移，缺省用 offset"的
        /// 字面语义解析：<paramref name="directionSlotId"/> 在 <see cref="OffsetByDirection"/> 里
        /// 有覆盖值就用覆盖值，否则回退 <see cref="Offset"/>。不做镜像/翻转（那是调用方
        /// <c>IAnchorQuery</c> 实现按 <c>flipX</c> 另行处理的职责，见 09 第 3.2 节镜像规则与本类型
        /// 判断记录"镜像与方向覆盖是两个独立维度"）。</summary>
        public Vec2 ResolveOffset(Id directionSlotId) =>
            OffsetByDirection.TryGetValue(directionSlotId, out var overridden) ? overridden : Offset;

        private static readonly IReadOnlyDictionary<Id, Vec2> EmptyOffsetByDirection = new Dictionary<Id, Vec2>();
    }
}
