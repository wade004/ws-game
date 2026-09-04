namespace Core.Carriers.Item
{
    /// <summary>
    /// 本模块的最小诊断出口（与 <c>power_set.IPowerDiagnostics</c>、<c>event_bus.IEventDiagnostics</c>
    /// 同一惯例：不强制依赖任何 L-1 引擎适配层接口，默认实现只收集到内存，由调用方决定如何呈现）。
    /// 目前用于 <see cref="EquipmentHost"/> 卸下失败（背包已满、<see cref="InventoryFullPolicy.Reject"/>）
    /// 与 <see cref="ItemEffectExtension"/> 处理 <c>create_item</c> 参数缺失/背包已满两类场景。
    /// </summary>
    public interface IItemDiagnostics
    {
        void Warn(string message);
    }
}
