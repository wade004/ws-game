using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Presentation.Render
{
    /// <summary>
    /// PR130-07 根治新增：model 型装备外观的默认"物品实例 id -&gt; <see cref="EquipVisualDef"/>"供给
    /// 入口（同 <c>Presentation.VfxSfx.Core.EquipmentWeaponStyleSource</c> 是同一类问题的姊妹实现，
    /// 该类型解决"实体 -&gt; weaponStyleRef"，本类型解决"物品实例 -&gt; EquipVisualDef"）——doc-code-matrix
    /// 此前明确记录"UnityViewFactory 构造函数没有 equipVisual 参数……默认 factory 仍缺入口"这一装配
    /// 缺口，本类型是补上该入口的默认实现，装配方经 <see cref="VisualByItemInstanceId"/> 把结果传给
    /// <c>Adapter.Unity.Presentation.UnityViewFactory</c> 的 <c>equipVisualByItemInstanceId</c>
    /// 构造参数。
    /// <para>
    /// 判断记录（物品实例 -&gt; 模板 id 的解析来源）：<c>item.equipped</c>/<c>item.unequipped</c>
    /// （<see cref="ItemEquippedEvent"/>/<see cref="ItemUnequippedEvent"/>）只携带物品实例 id，不携带
    /// 模板 id；<c>item.added</c>（<see cref="ItemAddedEvent"/>）在物品进入背包时携带
    /// <see cref="ItemAddedEvent.ItemTemplateId"/>，且按框架既有物品流程（先加入背包、再装备）必然先于
    /// 对应的 <c>item.equipped</c> 触发——本类型据此订阅 <c>item.added</c> 累积一张"实例 id -&gt; 模板
    /// id"表（含跨堆叠场景的 <see cref="ItemAddedEvent.Removals"/> 逐项记录，覆盖一次 <c>AddItem</c>
    /// 同时命中多个实例的情形），<c>item.equipped</c> 触发时据此反查模板 id，再查
    /// <paramref name="catalogByTemplateId"/>（<c>display.equip_visual.item_id</c> 一栏，见
    /// <see cref="EquipVisualDef.ItemId"/> 类型注释"指向 item.template"）取得对应外观定义。查不到
    /// （物品在本类型订阅生效前就已经加入背包——如存档恢复的初始库存，或该模板本就没有声明
    /// <c>display.equip_visual</c> 行）时该次装备不产生任何视觉外观，是已知简化，同 09 第 1 节表现层
    /// "缺表现资源不阻断游戏"一贯宽容策略——不影响游戏逻辑本身的装备生效。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="VisualByItemInstanceId"/> 是活字典，不是每次访问都重新计算的快照）：
    /// <c>Adapter.Unity.Presentation.UnityViewFactory</c>/<c>UnityModelView</c> 只在各自构造期各持有
    /// 一次这个只读接口引用，此后不会重新索取——本类型据此把内部可变 <see cref="Dictionary{TKey,TValue}"/>
    /// 直接以 <see cref="IReadOnlyDictionary{TKey,TValue}"/> 接口暴露（同一个对象引用），装备/卸装事件
    /// 触发的增删对已经持有该引用的全部消费方即时可见，不需要任何轮询或重新订阅。
    /// </para>
    /// </summary>
    public sealed class EquipmentVisualSource : IDisposable
    {
        private readonly IReadOnlyDictionary<Id, EquipVisualDef> _catalogByTemplateId;
        private readonly Dictionary<Id, Id> _templateIdByItemInstanceId = new Dictionary<Id, Id>();
        private readonly Dictionary<Id, EquipVisualDef> _visualByItemInstanceId = new Dictionary<Id, EquipVisualDef>();
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly EquipmentSnapshotResolver? _snapshotResolver;

        /// <summary>见类型注释"活字典"判断记录：直接传给
        /// <c>UnityViewFactory</c> 的 <c>equipVisualByItemInstanceId</c> 构造参数。</summary>
        public IReadOnlyDictionary<Id, EquipVisualDef> VisualByItemInstanceId => _visualByItemInstanceId;

        /// <param name="bus">事件来源。</param>
        /// <param name="catalogByTemplateId"><c>display.equip_visual</c> 按 <see cref="EquipVisualDef.ItemId"/>
        /// （物品模板 id）索引的目录——装配方通常经 <c>display.equip_visual</c> 全表逐行
        /// <see cref="EquipVisualDef.FromRecord"/> 后按 <c>ItemId</c> 建表传入（一个模板声明多条行时
        /// 后一条覆盖前一条，同本仓库其余"内容表只读、装配期加载一次"目录一贯的"后写覆盖"简化）。</param>
        /// <param name="snapshotResolver">第九方审核任务书"第 0 步"补齐：可选的"单位 id -&gt; 当前全部
        /// 已装备物品"查询，供 <see cref="ReplayEquippedForUnit"/> 使用（见该方法判断记录）。未提供
        /// （默认 null）时 <see cref="ReplayEquippedForUnit"/> 恒返回空集合，行为与改动前完全一致。</param>
        public EquipmentVisualSource(
            IEventBus bus,
            IReadOnlyDictionary<Id, EquipVisualDef> catalogByTemplateId,
            EquipmentSnapshotResolver? snapshotResolver = null)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _catalogByTemplateId = catalogByTemplateId ?? throw new ArgumentNullException(nameof(catalogByTemplateId));
            _snapshotResolver = snapshotResolver;

            _subscriptions.Add(bus.Subscribe<ItemAddedEvent>(CarriersEventKeys.ItemAdded, OnItemAdded));
            _subscriptions.Add(bus.Subscribe<ItemEquippedEvent>(CarriersEventKeys.ItemEquipped, OnItemEquipped));
            _subscriptions.Add(bus.Subscribe<ItemUnequippedEvent>(CarriersEventKeys.ItemUnequipped, OnItemUnequipped));
        }

        /// <summary>
        /// 第九方审核任务书"第 0 步"补齐："新 View 初始装备外观重放"缺口的根治入口：查询
        /// <paramref name="unitId"/> 当前全部已装备物品（经构造期注入的 <see cref="_snapshotResolver"/>，
        /// 通常底层是 <c>EquipmentHost.GetAllEquippedInstances</c>），逐条把"实例 id -&gt; 模板 id"
        /// 记进 <see cref="_templateIdByItemInstanceId"/>（同 <see cref="OnItemAdded"/> 的记账方式，
        /// 保证后续真正的 <c>item.unequipped</c> 事件仍能正常反查）、并按模板 id 查 <see
        /// cref="_catalogByTemplateId"/> 直接把结果写进 <see cref="_visualByItemInstanceId"/>——不依赖
        /// <c>item.added</c> 是否曾经为这个实例触发过（<c>InventoryHost.InjectInstance</c> 不发
        /// <c>item.added</c> 这条已知缺口因此被绕开：本方法直接从查询结果拿模板 id，不经过
        /// <c>item.added</c> 这一跳）。
        /// <para>
        /// 返回值供调用方（<c>UnityViewFactory.CreateView</c>）针对新创建的 View 逐条合成
        /// <c>ItemEquippedEvent</c> 调用 <c>view.OnEvent</c>——View 自身的 <c>OnEvent</c> 处理逻辑
        /// 此时能在 <see cref="_visualByItemInstanceId"/> 里查到刚刚写入的条目，从而在 View 刚创建时
        /// 就应用一次外观，不需要等待下一次真正的装备/卸装事件。<see cref="_snapshotResolver"/> 未注入
        /// （默认 null）或该单位没有任何已装备物品时返回空集合。
        /// </para>
        /// </summary>
        public IReadOnlyList<EquippedItemRef> ReplayEquippedForUnit(Id unitId)
        {
            if (_snapshotResolver == null)
            {
                return Array.Empty<EquippedItemRef>();
            }

            var equipped = _snapshotResolver(unitId);
            if (equipped == null || equipped.Count == 0)
            {
                return Array.Empty<EquippedItemRef>();
            }

            for (var i = 0; i < equipped.Count; i++)
            {
                var item = equipped[i];
                _templateIdByItemInstanceId[item.ItemInstanceId] = item.TemplateId;
                if (_catalogByTemplateId.TryGetValue(item.TemplateId, out var def))
                {
                    _visualByItemInstanceId[item.ItemInstanceId] = def;
                }
            }

            return equipped;
        }

        private void OnItemAdded(ItemAddedEvent evt)
        {
            for (var i = 0; i < evt.Removals.Count; i++)
            {
                _templateIdByItemInstanceId[evt.Removals[i].InstanceId] = evt.ItemTemplateId;
            }
        }

        private void OnItemEquipped(ItemEquippedEvent evt)
        {
            if (_templateIdByItemInstanceId.TryGetValue(evt.ItemInstanceId, out var templateId)
                && _catalogByTemplateId.TryGetValue(templateId, out var def))
            {
                _visualByItemInstanceId[evt.ItemInstanceId] = def;
            }
        }

        private void OnItemUnequipped(ItemUnequippedEvent evt) => _visualByItemInstanceId.Remove(evt.ItemInstanceId);

        public void Dispose()
        {
            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }
            _subscriptions.Clear();
        }
    }
}
