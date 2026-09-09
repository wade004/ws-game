using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;

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
    /// <para>
    /// UI-111-01 根治（architecture/落地计划/audit-6739f50-20260909/AUDIT_REPORT.md）：本视图模型此前
    /// 只订阅四个背包/装备业务事件，同图读档（<c>ISaveSystem.RestoreFromSlot</c> 走 <c>Load</c>）
    /// 时该抑制作用域会连带压住这四个业务事件本身的派发（读档不是"重放增量"，见
    /// <c>presentation/view_binding/core/ViewBinder.cs</c> <c>OnSaveLoaded</c> 类型注释同一判断
    /// 记录），本视图模型因此错过刷新时机，<see cref="Slots"/>/<see cref="EquippedSlots"/> 停留在
    /// 读档前快照，与已经是 B 的 <see cref="IUiDataSource"/> 实时查询结果不一致（复现见
    /// <c>architecture/落地计划/audit-6739f50-20260909/core/logs/followup-core-probe.log</c>
    /// <c>INVENTORY-VM-SAME-MAP-LOAD</c> 小节）。根治：额外订阅在该抑制作用域外正常派发的
    /// <c>save.loaded</c>（<see cref="SaveEventKeys.SaveLoaded"/>），命中即整体 <see cref="Refresh"/>，
    /// 与 <c>ViewBinder.OnSaveLoaded</c>/<c>EquipmentWeaponStyleSource</c> 的 <c>save.loaded</c> 全量
    /// 对账同一惯例；重复收到 <c>save.loaded</c> 只是多刷新一次，幂等无副作用，构造函数只调用一次
    /// <c>Subscribe</c>，不重复订阅。
    /// </para>
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
            // UI-111-01 根治：见类型注释——save.loaded 在读档抑制作用域外正常派发，收到后整体重建。
            _subscriptions.Add(_dataSource.Subscribe(SaveEventKeys.SaveLoaded, OnRelevantEvent));

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
