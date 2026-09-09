namespace Core.Numbers.PowerSet
{
    /// <summary>
    /// 本模块的最小诊断出口（与 <c>event_bus.IEventDiagnostics</c>、
    /// <c>hook_registry.IHookDiagnostics</c> 同一惯例：L0/L1 基础模块不强制依赖 L-1 引擎适配层
    /// 接口，默认实现只收集到内存，由调用方决定如何呈现）。目前只用于
    /// <see cref="PowerTickHandler"/> 收到离散步时记一条警告——本处理器只支持连续步推进资源
    /// 回复/衰减，离散步下是否换算为按回合结算由游戏策略决定（DOC-111-03 根治，
    /// architecture/落地计划/audit-6739f50-20260909，P3，取代已废止的旧判断记录"本项目暂不
    /// 启用离散时间模型"——ADR-0013 与 sim_loop 现已提供基础离散调度，这条只是
    /// <see cref="PowerTickHandler"/> 自己的连续-only 边界，见该类型判断记录）。
    /// </summary>
    public interface IPowerDiagnostics
    {
        void Warn(string message);
    }
}
