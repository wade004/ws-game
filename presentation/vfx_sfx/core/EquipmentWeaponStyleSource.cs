using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Presentation.VfxSfx.Contracts;

namespace Presentation.VfxSfx.Core
{
    /// <summary>
    /// <see cref="IWeaponStyleSource"/> 的默认实现（ADR-0017 决策 e）：订阅
    /// <c>item.equipped</c>/<c>item.unequipped</c>（<see cref="ItemEquippedEvent"/>/
    /// <see cref="ItemUnequippedEvent"/>，见 <c>core/carriers/item.EquipmentHost.Equip</c>/
    /// <c>Unequip</c>），按注入的 <see cref="MainHandWeaponTemplateResolver"/> 取该实体当前主手武器
    /// 的物品模板 id，再经 <see cref="IDisplayInfoRegistry.Lookup"/> 取对应
    /// <c>display.map</c> 行的 <see cref="Core.Foundation.DisplayInfo.DisplayInfo.WeaponStyleRef"/>。
    /// <para>
    /// 判断记录（不新增 <c>item.template</c> 字段，复用既有 <c>display.map.weapon_style_ref</c>）：
    /// 09 第 4.4 节"关联方式"已明确——<c>item.template</c> 经其 <c>display_ref</c> 指向的
    /// <c>display.map</c> 行携带 <c>weapon_style_ref</c>；<c>display.map</c> 行的
    /// <c>logical_id</c> 字段就是它所表现的逻辑记录 id（本例即物品模板 id 本身，见
    /// <c>data/_sample/display/display.map.json</c> 示例 <c>display.map.sample_blade</c> 的
    /// <c>logical_id: "item.sample_blade"</c>），因此 <see cref="IDisplayInfoRegistry.Lookup"/>
    /// 直接以物品模板 id 作为 <c>logicalId</c> 参数即可查到该行，不需要先查 <c>item.template</c>
    /// 表拿 <c>display_ref</c> 再转查一次——现有 schema 已经足够，未在 <c>item.template</c> 新增字段
    /// （任务书"若 schema 没有该字段，在 item.template 加可选字段"的前提在本例不成立）。
    /// </para>
    /// <para>
    /// 判断记录（按实体缓存 + 装备事件失效）：<see cref="MainHandWeaponTemplateResolver"/> 与
    /// <see cref="IDisplayInfoRegistry.Lookup"/> 都是只读查询，理论上可以每次调用都重新查一遍；本类型
    /// 仍缓存查询结果——<see cref="GetWeaponStyleRef"/> 预期被高频路径（如 <c>AnimClipResolver</c> 每次
    /// 状态切换都可能查一次）调用，装备状态变化频率远低于状态切换频率，缓存 + 按
    /// <c>item.equipped</c>/<c>item.unequipped</c> 事件失效是更省成本的取舍，同 09 第 6 节
    /// <c>FeedbackBinder</c>/本模块既有解析器一贯"只订阅事件、不轮询"的铁律 P2。
    /// </para>
    /// </summary>
    public sealed class EquipmentWeaponStyleSource : IWeaponStyleSource, IDisposable
    {
        private readonly MainHandWeaponTemplateResolver _mainHandTemplateResolver;
        private readonly IDisplayInfoRegistry _displayInfoRegistry;
        private readonly Dictionary<Id, Id?> _cache = new Dictionary<Id, Id?>();
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        public EquipmentWeaponStyleSource(
            IEventBus bus,
            MainHandWeaponTemplateResolver mainHandTemplateResolver,
            IDisplayInfoRegistry displayInfoRegistry)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _mainHandTemplateResolver = mainHandTemplateResolver ?? throw new ArgumentNullException(nameof(mainHandTemplateResolver));
            _displayInfoRegistry = displayInfoRegistry ?? throw new ArgumentNullException(nameof(displayInfoRegistry));

            _subscriptions.Add(bus.Subscribe<ItemEquippedEvent>(CarriersEventKeys.ItemEquipped, evt => Invalidate(evt.UnitId)));
            _subscriptions.Add(bus.Subscribe<ItemUnequippedEvent>(CarriersEventKeys.ItemUnequipped, evt => Invalidate(evt.UnitId)));
        }

        public Id? GetWeaponStyleRef(Id entityId)
        {
            if (_cache.TryGetValue(entityId, out var cached))
            {
                return cached;
            }

            var templateId = _mainHandTemplateResolver(entityId);
            Id? styleRef = templateId.HasValue ? _displayInfoRegistry.Lookup(templateId.Value)?.WeaponStyleRef : null;
            _cache[entityId] = styleRef;
            return styleRef;
        }

        private void Invalidate(Id unitId) => _cache.Remove(unitId);

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
