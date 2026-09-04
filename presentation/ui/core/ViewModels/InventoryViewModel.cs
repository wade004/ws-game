using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Presentation.Ui
{
    /// <summary>一个背包格子的快照。</summary>
    public readonly struct InventorySlotSnapshot
    {
        public Id InstanceId { get; }

        public Id TemplateId { get; }

        public int Count { get; }

        public InventorySlotSnapshot(Id instanceId, Id templateId, int count)
        {
            InstanceId = instanceId;
            TemplateId = templateId;
            Count = count;
        }
    }

    /// <summary>
    /// 背包与装备视图模型（见 09_表现层.md 第 7.1 节 UI 组成"背包与装备"）。<see cref="Slots"/>
    /// 经 <c>player.inventory.count</c> + <c>player.inventory[i].*</c> 逐格拉取（见
    /// <see cref="PlayerPathProvider"/>）；<see cref="EquippedSlots"/> 经构造期注入的槽位 id 清单
    /// 逐个查 <c>player.equipment.&lt;slot&gt;</c>。
    /// </summary>
    public sealed class InventoryViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly IReadOnlyList<Id> _equipmentSlotIds;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly List<InventorySlotSnapshot> _slots = new List<InventorySlotSnapshot>();
        private readonly Dictionary<Id, Id> _equippedSlots = new Dictionary<Id, Id>();

        public IReadOnlyList<InventorySlotSnapshot> Slots => _slots;

        /// <summary>槽位 id → 已装备物品实例 id；未装备的槽位不出现在字典里。</summary>
        public IReadOnlyDictionary<Id, Id> EquippedSlots => _equippedSlots;

        public InventoryViewModel(IUiDataSource dataSource, IReadOnlyList<Id> equipmentSlotIds)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _equipmentSlotIds = equipmentSlotIds ?? throw new ArgumentNullException(nameof(equipmentSlotIds));

            _subscriptions.Add(_dataSource.Subscribe(CarriersEventKeys.ItemAdded, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(CarriersEventKeys.ItemRemoved, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(CarriersEventKeys.ItemEquipped, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(CarriersEventKeys.ItemUnequipped, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            _slots.Clear();
            var count = _dataSource.Query("player.inventory.count");
            var n = count.HasValue ? (int)count.Value.AsInt : 0;
            for (var i = 0; i < n; i++)
            {
                var instance = _dataSource.Query($"player.inventory[{i}].instance");
                var template = _dataSource.Query($"player.inventory[{i}].template");
                var itemCount = _dataSource.Query($"player.inventory[{i}].count");
                if (instance.HasValue && template.HasValue && itemCount.HasValue)
                {
                    _slots.Add(new InventorySlotSnapshot(instance.Value.AsId, template.Value.AsId, (int)itemCount.Value.AsInt));
                }
            }

            _equippedSlots.Clear();
            foreach (var slotId in _equipmentSlotIds)
            {
                var equipped = _dataSource.Query($"player.equipment.{slotId}");
                if (equipped.HasValue)
                {
                    _equippedSlots[slotId] = equipped.Value.AsId;
                }
            }
        }

        public void Dispose()
        {
            foreach (var handle in _subscriptions)
            {
                handle.Dispose();
            }
            _subscriptions.Clear();
        }
    }
}
