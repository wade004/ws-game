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

        // -----------------------------------------------------------------
        // 已装备物品的模板 id 查询（消费方反馈——游戏接入方第五批第 2 条，2026-09-21，见
        // architecture/adr/0063-装备宿主契约补模板id查询.md）：装备面板要显示"槽位名 + 已装备物品
        // 名"，需要已装备物品的模板 id；GetEquipped/GetAllEquipped 只返回 ItemInstanceRef（只有
        // InstanceId），此前只能向下转型到具体类 Core.Carriers.Item.EquipmentHost 调非接口方法
        // GetAllEquippedInstances 才能取到模板 id。
        // -----------------------------------------------------------------

        /// <summary>
        /// <paramref name="slot"/> 当前已装备物品的模板 id；该槽位当前无装备时返回 <c>null</c>——语义
        /// 与 <see cref="GetEquipped"/>"槽位未装备返回 null"完全对应，参数含义同 <see
        /// cref="GetEquipped"/>。
        /// <para>
        /// C# 8 默认接口成员：本默认实现恒返回 <c>null</c>——这是只读查询，返回一个明确的保守默认值
        /// （同 <c>Core.Rules.Common.ISkillHost.Knows</c>/<c>GetSkillNameKey</c> 既有的"查询类默认实现
        /// 允许显式降级"惯例），供未实现本能力的 <see cref="IEquipmentHost"/>（旧版本编译产物、未升级
        /// 的自定义实现）源码/二进制兼容——新增接口成员不破坏既有实现类的编译。生产实现
        /// <c>Core.Carriers.Item.EquipmentHost</c> 用显式接口实现转发到内部已装备账本（与既有公开方法
        /// <c>GetAllEquippedInstances</c> 同一份数据源，行为不变；不用隐式实现是刻意的——隐式实现会把
        /// 新增的公开方法改写成 <c>virtual sealed</c> 的物理 IL 形态，被 <c>toolchain/abi_surface</c>
        /// 静态签名比对误判为破坏，同 <c>ISkillHost</c> 判断记录"ISkillHost 显式接口实现"一节）。任何
        /// 组合/包装 <see cref="IEquipmentHost"/>（若存在）都应显式转发到内层实现，不应悄悄吃掉这个
        /// 降级默认值——同 <c>ISkillHost.GetSkillReadiness</c> 判断记录"框架内
        /// InterfaceDefaultMemberForwardingTests 门禁"。
        /// </para>
        /// </summary>
        Id? GetEquippedTemplateId(Id unitId, Id slot) => null;

        /// <summary>
        /// 补充：该单位当前全部已装备槽位到（实例 id、模板 id）的映射，供装备面板/纸娃娃层一次性
        /// 同步全部槽位的模板 id（不必按已知槽位逐一调用 <see cref="GetEquippedTemplateId"/>）——形态
        /// 对应既有 <see cref="GetAllEquipped"/>（槽位 id → <see cref="ItemInstanceRef"/>）。未装备的
        /// 槽位不出现在字典里。
        /// <para>
        /// C# 8 默认接口成员：本默认实现恒返回空字典——只读查询，允许显式降级，惯例同 <see
        /// cref="GetEquippedTemplateId"/>。生产实现 <c>Core.Carriers.Item.EquipmentHost</c> 用显式接口
        /// 实现转发，理由同 <see cref="GetEquippedTemplateId"/>。任何组合/包装 <see
        /// cref="IEquipmentHost"/>（若存在）都应显式转发到内层实现，不应悄悄吃掉这个降级默认值。
        /// </para>
        /// </summary>
        IReadOnlyDictionary<Id, EquippedItemIdentity> GetAllEquippedIdentities(Id unitId) =>
            new Dictionary<Id, EquippedItemIdentity>();
    }
}
