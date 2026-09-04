using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Core.Gameplay.Loot
{
    /// <summary>掉落在地面的拾取物运行期实体（见 05 第 1.6 节 <c>DroppedLoot</c> 字段表）。</summary>
    public sealed class DroppedLootEntity : Entity
    {
        public override string Kind => "loot";

        /// <summary>掉落物内容（见 05 第 1.6 节 <c>items</c>）；<see cref="LootHost.PickUp"/> 会随着
        /// 部分拾取逐步移除已拾走的堆叠，全部拾完后本列表为空并触发实体销毁。</summary>
        public List<ItemStack> Items { get; }

        /// <summary>单人拾取规则的归属提示（见 05 第 1.6 节 <c>ownerHint</c>）：本架构单机场景无实际
        /// 分配意义，仅为兼容掉落生成管线数据结构而保留（08 第 1.2 节原文）。</summary>
        public Id? OwnerHint { get; set; }

        /// <summary>过期时间（游戏内时间，见 05 第 1.6 节 <c>expireAt</c>）；null 表示不过期。</summary>
        public double? ExpireAt { get; set; }

        public DroppedLootEntity(Id entityId, Id mapId, IEnumerable<ItemStack> items, Id? ownerHint, double? expireAt)
            : base(entityId, mapId)
        {
            Items = new List<ItemStack>(items ?? Array.Empty<ItemStack>());
            OwnerHint = ownerHint;
            ExpireAt = expireAt;
        }
    }
}
