namespace Core.Rules.Skill
{
    /// <summary>
    /// 本模块的最小诊断出口（与 <c>event_bus.IEventDiagnostics</c>、<c>power_set.IPowerDiagnostics</c>
    /// 同一惯例：L2 规则层模块不强制依赖任何引擎适配层日志接口，默认实现只收集到内存）。
    /// 用于记录：未处理的 <see cref="IEffectExtension"/> 效果、Proc 触发链达到
    /// <see cref="SkillOptions.MaxTriggerDepth"/> 上限、离散步（本项目未启用）、Expr 条件求值异常
    /// 等非致命但值得记录的情形。
    /// </summary>
    public interface ISkillDiagnostics
    {
        void Warn(string message);

        void Error(string message);
    }
}
