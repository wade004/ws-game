namespace Core.Gameplay.Loot
{
    /// <summary>
    /// 消费方反馈同构问题第三处根治（2026-09-20，同批 <c>core/gameplay/progression_bridge</c> 的
    /// <c>IProgressionBridgeDiagnostics</c> 判断记录）：<c>core/gameplay/loot</c> 模块（<see
    /// cref="CreatureDeathLootListener"/> 专用）的最小诊断出口：死亡单位的 <c>creature.template</c>
    /// 查询失败（未登记的模板 id，或记录存在但字段非法）时记一条警告，不抛异常、不阻断死亡结算的
    /// 其余处理——本模块此前（T-N2-8b 起）<see cref="CreatureDeathLootListener"/> 从未持有任何诊断
    /// 契约实例，"模板查询失败 → 掉落静默不产出"这一退化路径完全不可观察（用户侧表现"杀怪不掉
    /// 东西且无任何线索"，与 progression_bridge 的"杀怪 0 经验且无线索"是同一个病），本接口补上
    /// 这个缺口，接线方式见 ADR-0042 决策 1/2。
    /// </summary>
    public interface ILootDiagnostics
    {
        void Warn(string message);
    }
}
