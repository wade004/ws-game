namespace Core.Rules.Common
{
    /// <summary>
    /// RC-01 收边补齐（见 <see cref="EffectContext.TriggerChainDepth"/> 类型注释"判断记录"）：
    /// 供 Proc 触发链在事件上随身携带"产生本事件的结算/触发已经历几层触发链"这一深度，替代
    /// <c>CastPipeline</c> 原先失效于跨 <see cref="Core.Foundation.EventBus.IEventBus.Enqueue"/>
    /// 异步派发的 ambient 计数器。只有可能作为 <c>skill.proc_def.trigger_event</c> 且由
    /// <c>CastPipeline</c> 效果结算路径产生的事件才实现本接口（当前为
    /// <c>CombatDamageDealtEvent</c>/<c>CombatHealDoneEvent</c>/<c>AuraAppliedEvent</c>/
    /// <c>ProcTriggeredEvent</c>，见各类型判断记录）；未实现本接口的事件类型视为深度 0（
    /// <see cref="Core.Rules.Skill.EventCorrelation.GetTriggerChainDepth"/> 缺省值），不阻断这些
    /// 事件类型继续作为 Proc 触发源使用，只是不参与跨事件的深度传播（06 文档未要求触发链必须
    /// 覆盖全部事件词汇表词条，见 RC-01 修复范围说明）。
    /// </summary>
    public interface ITriggerChainEvent
    {
        int TriggerChainDepth { get; }
    }
}
