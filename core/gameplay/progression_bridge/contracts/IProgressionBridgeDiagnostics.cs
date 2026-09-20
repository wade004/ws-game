namespace Core.Gameplay.ProgressionBridge
{
    /// <summary>
    /// 消费方反馈第 6 条根治：<c>core/gameplay/progression_bridge</c> 模块（<see
    /// cref="CreatureDeathXpListener"/>/<see cref="AreaTriggerDiscoveryXpListener"/> 共用）的最小诊断
    /// 出口（惯例同 <c>core/gameplay/common</c> 的 <c>IRewardDiagnostics</c>/<c>core/gameplay/world_state</c>
    /// 的 <c>IWorldStateDiagnostics</c>）：<c>prog.xp_source</c> 来源未登记时记一条警告，不抛异常、
    /// 不阻断死亡结算/触发器进入的其余处理——本模块此前（T-N4-3/T-N4-4）两个监听器从未持有任何诊断
    /// 契约实例，"来源未登记 → 经验静默不发放"这一退化路径完全不可观察（消费方反馈现象"杀怪一直
    /// 0 经验且无任何线索"），本接口补上这个缺口，接线方式见 ADR-0042 决策 1/2。
    /// </summary>
    public interface IProgressionBridgeDiagnostics
    {
        void Warn(string message);
    }
}
