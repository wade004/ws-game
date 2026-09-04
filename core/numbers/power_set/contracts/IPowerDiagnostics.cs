namespace Core.Numbers.PowerSet
{
    /// <summary>
    /// 本模块的最小诊断出口（与 <c>event_bus.IEventDiagnostics</c>、
    /// <c>hook_registry.IHookDiagnostics</c> 同一惯例：L0/L1 基础模块不强制依赖 L-1 引擎适配层
    /// 接口，默认实现只收集到内存，由调用方决定如何呈现）。目前只用于
    /// <see cref="PowerTickHandler"/> 收到离散步时记一条警告（本项目暂不启用离散时间模型，
    /// 见 ADR-0013、落地方案 T1-5）。
    /// </summary>
    public interface IPowerDiagnostics
    {
        void Warn(string message);
    }
}
