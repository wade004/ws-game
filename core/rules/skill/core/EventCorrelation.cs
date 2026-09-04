using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 判断一个事件是否与某个光环持有者相关（见 <see cref="ProcHost"/>"事件到达时：判断事件是否
    /// 与光环持有者相关"）。判断记录：06 第 8 节事件词汇表给出的强类型事件字段各不相同（有的叫
    /// <c>casterId</c>，有的叫 <c>sourceId</c>/<c>unitId</c>/<c>targetId</c>），任务书拍板"事件字段
    /// 里 unitId/sourceId/casterId/targetId 任一等于持有者"即视为相关——这是最小相关性规则，
    /// 不区分持有者是"发起方"还是"承受方"。由于 01 第 6 节禁止使用反射机制读取任意字段，本类
    /// 对 <see cref="Core.Rules.Common"/> 已登记的强类型事件逐一模式匹配，不做通用反射取值；
    /// 新增事件类型时需要在此补一个 case，遗漏时保守地判定为"不相关"（不触发，不误报）。
    /// </summary>
    internal static class EventCorrelation
    {
        public static bool IsRelatedToHolder(IEvent evt, Id holderId)
        {
            switch (evt)
            {
                case SkillCastStartEvent e: return e.CasterId.Equals(holderId);
                case SkillCastSuccessEvent e: return e.CasterId.Equals(holderId) || Contains(e.Targets, holderId);
                case SkillCastFailedEvent e: return e.CasterId.Equals(holderId);
                case SkillCastInterruptedEvent e: return e.CasterId.Equals(holderId) || e.InterrupterId.Equals(holderId);
                case CombatDamageDealtEvent e: return e.SourceId.Equals(holderId) || e.TargetId.Equals(holderId);
                case CombatHealDoneEvent e: return e.SourceId.Equals(holderId) || e.TargetId.Equals(holderId);
                case AuraAppliedEvent e: return e.TargetId.Equals(holderId) || e.SourceId.Equals(holderId);
                case AuraRemovedEvent e: return e.TargetId.Equals(holderId);
                case AuraStackChangedEvent e: return e.TargetId.Equals(holderId);
                case ProcTriggeredEvent e: return e.UnitId.Equals(holderId);
                case CombatThreatChangedEvent e: return e.UnitId.Equals(holderId) || e.SourceId.Equals(holderId);
                case CombatEnteredEvent e: return e.UnitId.Equals(holderId);
                case CombatLeftEvent e: return e.UnitId.Equals(holderId);
                case UnitDiedEvent e: return e.UnitId.Equals(holderId) || (e.KillerId.HasValue && e.KillerId.Value.Equals(holderId));
                case UnitRespawnedEvent e: return e.UnitId.Equals(holderId);
                case AiStateChangedEvent e: return e.UnitId.Equals(holderId);
                case AiDecisionMadeEvent e: return e.UnitId.Equals(holderId);
                case TargetingResolvedEvent e: return e.UnitId.Equals(holderId);
                default: return false;
            }
        }

        private static bool Contains(System.Collections.Generic.IReadOnlyList<Id> list, Id id)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(id)) return true;
            }

            return false;
        }
    }
}
