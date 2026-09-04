namespace Core.Rules.Combat
{
    /// <summary>
    /// 本模块的最小诊断出口（与 <c>power_set.IPowerDiagnostics</c>、
    /// <c>event_bus.IEventDiagnostics</c> 同一惯例：L2 规则层不强制依赖 L-1 引擎适配层接口，
    /// 默认实现只收集到内存，由调用方决定如何呈现）。用于 <see cref="Resolver"/> 遇到未登记
    /// 属性时的"缺失属性按 0 处理"警告、<see cref="CombatTickHandler"/> 收到离散步时的警告。
    /// </summary>
    public interface ICombatDiagnostics
    {
        void Warn(string message);
    }
}
