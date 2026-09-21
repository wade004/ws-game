using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;

namespace Presentation.Ui
{
    /// <summary>
    /// <c>interact.nearest.*</c> 路径的解答者（ADR-0062，消费方反馈第五批第 1 条续）：统一的"最近
    /// 可交互目标"表现层查询——<c>interact.nearest.id|kind|distance</c>，三个叶子字段。
    /// <para>
    /// 判断记录（一个统一路径覆盖三类目标，不是只给 loot 开一条孤立路径）：先查现状发现表现层
    /// （<c>presentation/ui</c>）对 gobj/creature 此前也从未提供过"最近可交互目标"查询（见 ADR-0062
    /// 正文"现状核实"一节）——不是"另两类已有、只差 loot"，而是三类都没有。既然要新开一条路径，就
    /// 直接把它设计成三类通用：<c>interact.nearest.kind</c> 返回目标类别（见 <see cref="KindToken"/>），
    /// 接入方判断是不是掉落物只需要 <c>interact.nearest.kind == "loot"</c>，不需要框架为"附近是否有
    /// 可拾取掉落物"这一件事单独开一条 <c>interact.nearest_loot.*</c>。</para>
    /// <para>
    /// 判断记录（无值时返回 null，不记诊断，同 <c>target.*</c> 惯例）：附近没有可交互目标是正常查询
    /// 结果（玩家周围确实空空如也），不是路径错误。
    /// </para>
    /// </summary>
    public sealed class InteractPathProvider : IUiPathProvider
    {
        private readonly Id _unitId;
        private readonly IInteractionTargetRegistry _registry;
        private readonly double? _maxRange;

        /// <summary><paramref name="unitId"/> 是查询锚点（通常是玩家单位 id，同 <see
        /// cref="PlayerPathProvider"/> 惯例——本 Provider 不支持"查任意单位附近"，只服务表现层最常见
        /// 的"玩家自己按 F 键时附近有什么"场景）；<paramref name="maxRange"/> 为 <c>null</c>（默认）
        /// 表示不限制距离——三类目标各自的 <c>interact</c> 意图分流已经在意图处理阶段各自判距（见
        /// <c>GobjOptions.InteractRange</c>/<c>CreatureInteractOptions.InteractRange</c>/
        /// <c>LootOptions.PickupRange</c>），本查询只是"提示玩家附近有什么"，不重复实现一套独立的
        /// 范围策略；确有需要限制提示范围的游戏可显式传入。</summary>
        public InteractPathProvider(Id unitId, IInteractionTargetRegistry registry, double? maxRange = null)
        {
            _unitId = unitId;
            _registry = registry;
            _maxRange = maxRange;
        }

        public string Root => "interact";

        public ExprValue? Resolve(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
            if (remaining.Count == 0 || remaining[0].Index.HasValue || remaining[0].Name != "nearest")
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 在 \"interact\" 之后缺少或不认识 \"nearest\" 子路径");
                return null;
            }

            if (remaining.Count != 2 || remaining[1].Index.HasValue)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 段数或下标形状不符合预期（只支持 interact.nearest.id|kind|distance）");
                return null;
            }

            var hasTarget = _registry.TryFindNearest(_unitId, _maxRange, out var target);

            switch (remaining[1].Name)
            {
                case "id":
                    return hasTarget ? ExprValue.OfId(target.EntityId) : (ExprValue?)null;
                case "kind":
                    return hasTarget ? ExprValue.OfString(KindToken(target.Kind)) : (ExprValue?)null;
                case "distance":
                    return hasTarget ? ExprValue.OfNumber(target.Distance) : (ExprValue?)null;
                default:
                    diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 interact.nearest 子路径关键字 \"{remaining[1].Name}\" 未知（只支持 id/kind/distance）");
                    return null;
            }
        }

        /// <summary>类别 token：直接复用 <see cref="EntityKinds"/> 既有的三个字符串常量（"gobj"/
        /// "creature"/"loot"），不新造一套字符串——与 <see cref="InteractionTargetKind"/> 判断记录
        /// "分类只依赖 Entity.Kind"同一惯例，消费方原本就可能已经认识这三个字符串（如
        /// <c>Entity.Kind</c>/事件字段里出现过）。</summary>
        private static string KindToken(InteractionTargetKind kind) => kind switch
        {
            InteractionTargetKind.GameObject => EntityKinds.Gobj,
            InteractionTargetKind.Creature => EntityKinds.Creature,
            _ => EntityKinds.Loot,
        };
    }
}
