using System.Collections.Generic;
using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 第九方审核任务书"第 0 步"补齐：一件已装备物品的最小快照，只携带
    /// <see cref="EquipmentVisualSource.ReplayEquippedForUnit"/> 重放外观所需的三个 id（槽位、物品
    /// 实例、物品模板），不携带任何 <c>core/carriers/item</c> 的具体类型（同 <see
    /// cref="MainHandWeaponTemplateResolver"/> 一贯"窄契约、不反向耦合到某一种具体的装备存储形状"
    /// 惯例——本类型是 <c>Presentation.VfxSfx.Contracts.MainHandWeaponTemplateResolver</c> 判断记录
    /// 同一思路在"重放全部已装备物品"这一场景下的推广）。
    /// </summary>
    public readonly struct EquippedItemRef
    {
        public Id Slot { get; }

        public Id ItemInstanceId { get; }

        public Id TemplateId { get; }

        public EquippedItemRef(Id slot, Id itemInstanceId, Id templateId)
        {
            Slot = slot;
            ItemInstanceId = itemInstanceId;
            TemplateId = templateId;
        }
    }

    /// <summary>
    /// 按单位 id 查询其当前全部已装备物品的窄契约委托，供 <see cref="EquipmentVisualSource"/> 在
    /// View 创建（跨图新 View）/存档恢复（<c>InventoryHost.InjectInstance</c> 不经 <c>item.equipped</c>
    /// 的路径）时重放既有装备外观（见该类型 <see cref="EquipmentVisualSource.ReplayEquippedForUnit"/>
    /// 判断记录）。本模块——presentation/render——不直接依赖 <c>core/carriers/item</c> 的具体装备宿主
    /// 实现，由装配层通常包一层
    /// <c>EquipmentHost.GetAllEquippedInstances(unitId)</c>（<c>CarriersAssembly.Equipment</c> 已是
    /// 公开只读查询，<c>GameplayAssembly.Carriers.Equipment</c> 链路无需改动 core 层）逐项转换成
    /// <see cref="EquippedItemRef"/> 构造并注入。查不到该单位的装备状态（单位不存在、或确实没有任何
    /// 已装备物品）时返回空集合，不抛异常（同表现层一贯"缺表现资源不阻断游戏"的宽容策略）。
    /// </summary>
    public delegate IReadOnlyList<EquippedItemRef> EquipmentSnapshotResolver(Id unitId);
}
