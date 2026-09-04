using System;
using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 一个物品实例的不透明引用句柄（见 07 第 1.3 节 <c>EquipmentHost.getEquipped</c> 返回值
    /// <c>Optional&lt;ItemInstanceRef&gt;</c>）。只携带实例 id，不暴露 <see cref="ItemInstance"/> 内部
    /// 状态（堆叠数、扩展字段等属于 <see cref="IInventoryHost"/> 的职责，句柄本身不重复携带），
    /// 惯例同 <c>core/rules/common</c> 的 <c>AuraInstanceRef</c>。
    /// </summary>
    public readonly struct ItemInstanceRef : IEquatable<ItemInstanceRef>
    {
        public Id InstanceId { get; }

        public ItemInstanceRef(Id instanceId)
        {
            InstanceId = instanceId;
        }

        public bool Equals(ItemInstanceRef other) => InstanceId.Equals(other.InstanceId);

        public override bool Equals(object? obj) => obj is ItemInstanceRef other && Equals(other);

        public override int GetHashCode() => InstanceId.GetHashCode();

        public override string ToString() => InstanceId.ToString();

        public static bool operator ==(ItemInstanceRef left, ItemInstanceRef right) => left.Equals(right);

        public static bool operator !=(ItemInstanceRef left, ItemInstanceRef right) => !left.Equals(right);
    }
}
