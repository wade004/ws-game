using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Ai
{
    /// <summary>
    /// AI 施法的路由钩子（ADR-0143，手感设计/01 第 2.3 节）：缺省 AI 直接调 <c>ISkillHost.CastSkill</c>，不经输入缓冲——AI 在动作后摇里"想放下一个技能"时直接被动作锁拒绝、
    /// 没有玩家那样的"提前输入、后摇结束接上"。装配层（可选开关，缺省关）设置本路由后，AI 的施法决策改为提交进该行动者的输入缓冲，由缓冲在步骤 1 按
    /// 同一套优先级、过期与取消窗口规则取用，与玩家共用同一条路径。
    /// <para>
    /// 判断记录（一 tick 延迟）：AI 在步骤 2 决策，缓冲在下一个 tick 的步骤 1 取用，所以经缓冲的 AI 施法比直接施法晚一个 tick 才真正开始——这是开关缺省关的原因
    /// （打开会让 AI 的出手时机整体后移一 tick，既有基线不能静默改变）。
    /// </para>
    /// </summary>
    public interface IAiCastRouter
    {
        /// <summary>
        /// 尝试把 <paramref name="unitId"/> 的一次施法决策提交进缓冲。返回 true 表示已提交（AI 视为本次决策成功，不再直接施法）；
        /// false 表示路由不接管该单位（调用方回落到直接施法）。
        /// </summary>
        bool TrySubmit(Id unitId, Id skillId, IReadOnlyList<Id> targets);
    }
}
