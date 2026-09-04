namespace Core.Gameplay.WorldState
{
    /// <summary>
    /// 本模块的最小诊断出口（与 <c>core/rules/combat.ICombatDiagnostics</c>、
    /// <c>power_set.IPowerDiagnostics</c>、<c>event_bus.IEventDiagnostics</c> 同一惯例：L4 玩法层
    /// 不强制依赖 L-1 引擎适配层接口，默认实现只收集到内存，由调用方决定如何呈现）。目前唯一用途：
    /// <see cref="IWorldState.OnChanged"/> 回调抛出异常时的隔离警告（见该接口注释）。
    /// </summary>
    public interface IWorldStateDiagnostics
    {
        void Warn(string message);
    }
}
