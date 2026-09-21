using System;
using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 消费方反馈（游戏接入方第五批第 2 条，ADR-0063《装备宿主契约补模板 id 查询》）：装备面板要显示
    /// "槽位名 + 已装备物品名"，需要已装备物品的模板 id，此前只能向下转型到具体类
    /// <c>Core.Carriers.Item.EquipmentHost</c> 调非接口方法 <c>GetAllEquippedInstances</c>。本类型是
    /// <see cref="IEquipmentHost.GetAllEquippedIdentities"/> 的元素类型——一个已装备物品的实例 id +
    /// 模板 id 二元组，供 <see cref="IEquipmentHost.GetAllEquipped"/>（只有实例 id）不够用、但又不需要
    /// <see cref="EquippedWeaponSummary"/> 携带的品质/词缀那么多信息的场景使用。
    /// <para>
    /// 判断记录（不复用 <see cref="EquippedWeaponSummary"/>）：<see cref="EquippedWeaponSummary"/> 只有
    /// 三个身份字段（模板/品质/词缀），没有实例 id——它回答的是"这个槽位挂的是什么"，不回答"是哪一件
    /// 具体实例"；本类型回答的是后者（含实例 id），供需要按实例 id 做进一步查询（如与
    /// <see cref="IEquipmentHost.GetAllEquipped"/> 返回的 <see cref="ItemInstanceRef"/> 对账）的调用方
    /// 使用，两个类型定位不同，不是互相替代关系。
    /// </para>
    /// <para>
    /// 判断记录（不携带品质/词缀）：消费方反馈只点名"模板 id"，装备面板"槽位名 + 物品名"这一诉求本身
    /// 也只需要模板 id（名字由游戏拿模板 id 自己查 <c>item.template</c> 表）；同批消费方反馈的路径层
    /// 新增（<c>player.equipment.&lt;slot&gt;.template</c>）与背包侧 <c>player.inventory[i].*</c> 既有
    /// 口径一致（同样不给品质/名称，只给模板 id），本类型与之对齐，不额外携带品质/词缀造成两条查询
    /// 路径（契约方法 vs 路径层）语义不对称。
    /// </para>
    /// </summary>
    public readonly struct EquippedItemIdentity : IEquatable<EquippedItemIdentity>
    {
        public Id InstanceId { get; }

        public Id TemplateId { get; }

        public EquippedItemIdentity(Id instanceId, Id templateId)
        {
            InstanceId = instanceId;
            TemplateId = templateId;
        }

        public bool Equals(EquippedItemIdentity other) =>
            InstanceId.Equals(other.InstanceId) && TemplateId.Equals(other.TemplateId);

        public override bool Equals(object? obj) => obj is EquippedItemIdentity other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return InstanceId.GetHashCode() * 397 ^ TemplateId.GetHashCode();
            }
        }

        public override string ToString() => $"{InstanceId} (template={TemplateId})";

        public static bool operator ==(EquippedItemIdentity left, EquippedItemIdentity right) => left.Equals(right);

        public static bool operator !=(EquippedItemIdentity left, EquippedItemIdentity right) => !left.Equals(right);
    }
}
