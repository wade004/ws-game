using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
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
    /// <para>
    /// PRES-110-01 根治（architecture/落地计划/audit-ac3b622-20260909/presentation/
    /// presentation-findings.md"EquipmentWeaponStyleSource 的真实读档复核"）：<c>SaveSystem.Load</c>
    /// 把整段"逐段 Load + 失败回滚"包在 <see cref="IEventBus.SuppressDispatch"/> 抑制作用域内（见该
    /// 方法判断记录"读档不是业务事件"），但 <c>EquipmentPersistable.Load</c> 会调用真正的
    /// <c>EquipmentHost.Equip</c>/<c>Unequip</c> 重放装备联动——这两者本身正常派发的
    /// <c>ItemEquipped</c>/<c>ItemUnequipped</c> 事件在抑制作用域内被直接丢弃，本类型仅有的两个失效
    /// 订阅永远收不到它们。因此同图读档（同一批实体 id 延续，不经过 <c>OnEntityCreated</c> 重建
    /// View）之后，<see cref="_cache"/> 仍保留读档前的旧值，与已经真实变化的
    /// <c>EquipmentHost</c> 装备状态不一致（真实探针：<c>followup-core-probe.log</c>
    /// <c>WEAPON-STYLE-CACHE-SAME-MAP-LOAD</c>，读档后 EventBus drain 完毕仍返回旧图 A 的风格而非
    /// 新图 B）。<c>SaveSystem.Load</c> 在该抑制作用域<b>外</b>正常派发的 <c>SaveLoadedEvent</c>
    /// （<c>save.loaded</c>，"本次读档完成了"不是重放，理应正常送达）不受抑制影响——本类型对照
    /// <c>Presentation.ViewBinding.Core.ViewBinder</c> 的 <c>OnSaveLoaded</c> 做法，额外订阅
    /// <c>save.loaded</c> 并整表清空 <see cref="_cache"/>（不是按实体逐个失效：读档时哪些实体的装备
    /// 发生了变化对本类型不可见，唯一安全的做法是让下一次 <see cref="GetWeaponStyleRef"/> 对
    /// <b>全部</b>实体都重新经 <see cref="_mainHandTemplateResolver"/> 查一次真实 <c>EquipmentHost</c>
    /// 状态；清空后下一次查询自然按当前值重新写入缓存，代价是读档后第一次查询各实体各多付一次真实
    /// 解析，同"清空缓存"本身的语义一致，不额外增加长期开销）。
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
            // PRES-110-01 根治：见类型注释——save.loaded 在抑制作用域外正常派发，收到后整表对账清空。
            _subscriptions.Add(bus.Subscribe(SaveEventKeys.SaveLoaded, _ => _cache.Clear()));
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
