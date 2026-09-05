using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 战斗模块对外契约（见 06 第 4.7/7 节 <c>CombatHost</c>）。由 <c>core/rules/combat</c> 实现，
    /// 供 skill（效果结算触发）与 ai（进出战斗判定、Rotation 条件）调用。
    /// </summary>
    public interface ICombatHost
    {
        ResolveResult ResolveEffect(EffectContext context);

        IThreatTable GetThreatTable(Id unitId);

        bool IsInCombat(Id unitId);

        /// <summary>
        /// 补充：通知一次"战斗事件"发生在该单位身上（见 06 第 4.5 节"进入战斗：造成/受到伤害、被
        /// 仇恨表记录、主动使用带 combat-only 限制的技能，任一条件满足即置位 combatState = in"）。
        /// 由 skill/combat 内部在造成伤害、被记录仇恨等时机调用，驱动进战状态与脱战计时器复位。
        /// <paramref name="hostileId"/>（收边任务补齐 <c>combat.entered</c> 的 <c>hostileId</c>
        /// 字段，见 06 第 8 节事件词汇表）：本次触发进战的交互对方 id，只在该单位真正"首次"进战
        /// （<see cref="IsInCombat"/> 由 false 变 true 的这一次调用）时被写入
        /// <c>CombatEnteredEvent.HostileId</c>；已在战中的单位再次调用本方法不会重新触发事件，
        /// 传入的 <paramref name="hostileId"/> 被忽略。调用方不知道/不关心具体交互对方时可省略
        /// （默认 null，<c>CombatEnteredEvent.HostileId</c> 相应为 null，向后兼容既有调用点）。
        /// </summary>
        void NotifyCombatEvent(Id unitId, Id? hostileId = null);

        /// <summary>
        /// 补充：按 <paramref name="timeUnits"/> 推进脱战计时（见 06 第 4.5 节"脱离战斗：一段时间内
        /// （可配置）未产生新的战斗事件...置位 combatState = out"）。由主循环每步调用，具体判定时长
        /// 是策略配置项，不在本契约中固定数值。
        /// </summary>
        void Update(double timeUnits);
    }
}
