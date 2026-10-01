using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 宽限窗口的条件求值端（手感设计/01 第 2.4 节）：游戏在 <c>found.grace_condition</c> 声明条件名与 Expr，
    /// 求值（宿主为行动者上下文，如"当前目标在射程内"）由上层用自己的表达式宿主实现本接口；L0 的 <see cref="GraceTracker"/>
    /// 只负责每 tick 采样与"最近一次为真"的记录。
    /// </summary>
    public interface IGraceConditionEvaluator
    {
        /// <summary>条件 <paramref name="conditionId"/> 对行动者 <paramref name="actorId"/> 此刻是否成立。</summary>
        bool Evaluate(Id actorId, Id conditionId);
    }

    /// <summary>
    /// 宽限窗口只读查询（手感设计/01 第 2.4 节）：施法管线相应步骤（如步骤 7 距离）在条件当前为假、但
    /// <c>now - lastTrueTick &lt;= grace_ticks</c>（手感档案输入组 <c>grace_ms</c> 换算）时视为满足。
    /// 宽限只放宽"接受"判断，不改变随后结算的几何。
    /// </summary>
    public interface IGraceQuery
    {
        /// <summary>条件当前是否成立，或虽已失效但仍在宽限窗口内。从未成立过恒为 false。</summary>
        bool IsSatisfied(Id actorId, Id conditionId);

        /// <summary>条件当前为假、但仍处于宽限窗口内（即"若没有宽限本该被拒绝"的那段时间）。</summary>
        bool IsInGrace(Id actorId, Id conditionId);

        /// <summary>条件最近一次成立的模拟 tick；从未成立返回 -1。</summary>
        long LastTrueTick(Id actorId, Id conditionId);

        /// <summary><paramref name="conditionIds"/> 全部满足（空列表恒为 true，即没有宽限条件时无约束）。</summary>
        bool AreAllSatisfied(Id actorId, IReadOnlyList<Id> conditionIds);
    }
}
