using System;
using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 一组同模板物品的堆叠数量（见 07 第 1 节 <c>InventoryHost.listItems</c> 返回值
    /// <c>List&lt;ItemStack&gt;</c>、05 第 1.6 节 <c>DroppedLoot.items</c>）。与 <see cref="ItemInstance"/>
    /// 的区别：本类型不带运行期实例 id，只描述"这个模板、这么多个"，用于掉落结果、堆叠展示等不需要
    /// 区分具体实例（词缀、耐久等）的场景。
    /// </summary>
    public readonly struct ItemStack : IEquatable<ItemStack>
    {
        public Id TemplateId { get; }

        public int Count { get; }

        public ItemStack(Id templateId, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "ItemStack.Count 必须为正数");
            }

            TemplateId = templateId;
            Count = count;
        }

        public bool Equals(ItemStack other) => TemplateId.Equals(other.TemplateId) && Count == other.Count;

        public override bool Equals(object? obj) => obj is ItemStack other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (TemplateId.GetHashCode() * 397) ^ Count;
            }
        }

        public override string ToString() => $"ItemStack({TemplateId} x{Count})";

        public static bool operator ==(ItemStack left, ItemStack right) => left.Equals(right);

        public static bool operator !=(ItemStack left, ItemStack right) => !left.Equals(right);
    }
}
