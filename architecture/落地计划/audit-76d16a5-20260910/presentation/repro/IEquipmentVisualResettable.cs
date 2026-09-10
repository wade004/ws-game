using System.Collections.Generic;

namespace Presentation.Render
{
    /// <summary>
    /// P2-08 根治（architecture/落地计划/audit-c9ff301-20260909/presentation/presentation-findings.md
    /// "同图已有 View 读档外观"）：装备外观可对账能力——<c>save.loaded</c> 全量对账时，同图内继续存活
    /// 的既有 View（<see cref="Presentation.ViewBinding.ViewBinder.OnSaveLoaded"/> 未销毁重建的那部分）
    /// 需要"清空当前已应用的全部装备外观，再按真实装备快照重新应用"这一原子操作，不能靠逐条按物品
    /// 实例 id 反查的常规装备/卸装事件路径补齐——读档后真正生效的实例 id 集合可能与读档前完全不同
    /// （或相同 id 但对应关系已变化），且 <c>item.equipped</c>/<c>item.unequipped</c> 在读档期间本就
    /// 被 <c>IEventBus.SuppressDispatch</c> 丢弃（见 <c>ViewBinder</c> 类型注释），事后没有任何一条
    /// 真实事件可供反查。
    /// <para>
    /// "当前已应用的是什么"这一状态只有 View 自己持有（如
    /// <c>Adapter.Unity.Presentation.UnityModelView</c> 的按物品实例 id 索引的已应用装备表、
    /// <see cref="SpriteViewBase"/> 的按槽位索引的纸娃娃层覆盖表），因此设计成 View 自我对账（清空
    /// 自己已知的全部状态、按传入快照重新应用），而不是外部按"新旧快照做差集，逐条合成事件"的方式
    /// 驱动——差集算法需要知道"当前已应用的是什么"，这正是只有 View 自己知道的信息。
    /// </para>
    /// <para>
    /// 未实现本接口的 View（没有装备外观概念的 gobj/掉落物/AreaTrigger 一类）不受影响——
    /// <c>ViewBinder</c> 的对账逻辑用类型测试（<c>is IEquipmentVisualResettable</c>）静默跳过，同本
    /// 仓库一贯"未装配/不适用的能力静默跳过"惯例。
    /// </para>
    /// </summary>
    public interface IEquipmentVisualResettable
    {
        /// <summary>清空当前已应用的全部装备外观，再按 <paramref name="equipped"/>（真实装备快照，
        /// 通常来自 <see cref="EquipmentSnapshotResolver"/>/<see cref="EquipmentVisualSource.ReplayEquippedForUnit"/>）
        /// 逐条重新应用。<paramref name="equipped"/> 为空集合（含 <c>null</c>）时等价于"全部清空、
        /// 不应用任何外观"，对应空装备存档这一场景。幂等——用同一份快照重复调用得到同样的最终视觉
        /// 结果，不产生随调用次数递增的重复副作用（如重复叠加子模型/图层）。</summary>
        void ResetEquipmentVisuals(IReadOnlyList<EquippedItemRef> equipped);
    }
}
