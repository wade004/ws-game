namespace Core.Carriers.Creature
{
    /// <summary>
    /// 本模块的最小诊断出口（惯例同 <c>core/carriers/gobj</c> 的 <c>IGobjDiagnostics</c>：L3 载体层
    /// 模块不强制依赖任何引擎适配层日志接口，默认实现只收集到内存）。ADR-0051 新增：记录
    /// <c>Core.Carriers.Creature.CreatureInteractionHost.Interact</c> 运行时的非致命降级情形（目标
    /// 生物实例未登记、生物没有配置 <c>gossip_menu_ref</c>、已配置但未注入
    /// <see cref="CreatureInteractOptions.GossipOpener"/> 等），不允许静默返回一个"看起来正常"的值。
    /// </summary>
    public interface ICreatureDiagnostics
    {
        void Warn(string message);

        void Error(string message);
    }
}
