using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 装备栏接口（见 07 第 1.3 节 <c>EquipmentHost</c>）。由 <c>core/carriers/item</c> 实现（同
    /// <see cref="IInventoryHost"/>），穿脱流程对属性/技能/外形的联动见 07 第 1.4 节。
    /// </summary>
    public interface IEquipmentHost
    {
        /// <summary>把背包中的 <paramref name="instanceId"/> 穿戴到 <paramref name="slot"/>（见 07 第
        /// 1.3、1.4 节原文签名与流程）。</summary>
        EquipResult Equip(Id unitId, Id instanceId, Id slot);

        /// <summary>卸下 <paramref name="slot"/> 当前装备的物品，返回被卸下物品的引用；该槽位当前无
        /// 装备时返回 null（见 07 第 1.3 节原文签名）。</summary>
        ItemInstanceRef? Unequip(Id unitId, Id slot);

        /// <summary>查询 <paramref name="slot"/> 当前装备的物品引用；无装备时返回 null（见 07 第 1.3
        /// 节原文签名）。</summary>
        ItemInstanceRef? GetEquipped(Id unitId, Id slot);

        /// <summary>补充：列出该单位当前全部已装备槽位到物品引用的映射，供纸娃娃层一次性同步全部
        /// 槽位（而不必按已知槽位枚举逐一调用 <see cref="GetEquipped"/>）。</summary>
        IReadOnlyDictionary<Id, ItemInstanceRef> GetAllEquipped(Id unitId);
    }
}
