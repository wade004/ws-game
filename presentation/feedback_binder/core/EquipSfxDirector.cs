using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Presentation.FeedbackBinder.Contracts;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary>
    /// 装备/卸装音效（手感设计/08 第 4 节 <c>item.template.equip_sfx_ref</c>，ADR-0153）：订阅 <c>item.equipped</c>
    /// 与 <c>item.unequipped</c>，物品模板声明了 <c>equip_sfx_ref</c> 时，经与脚步/打击同一条出声通路
    /// （<see cref="IFeedbackSink.PlaySfx"/>，ADR-0148）在持有者位置播放该 <c>sfx.def</c> 行；没有声明的模板不产生任何调用。
    /// <para>
    /// 判断记录（模板来源）：两个事件只带物品实例 id。事件经总线排队、在帧内冲刷时投递，投递那一刻装备状态已经落定——
    /// 装备事件对应的物品在装备槽里、卸装事件对应的物品已回到背包——所以优先用装配方注入的 <see cref="EquipItemTemplateResolver"/>
    /// 现查（背包与装备槽都查）；查不到再退回本类型订阅 <c>item.added</c> 累积的"实例→模板"表（物品进背包时携带模板 id）。
    /// 两处都查不到（如存档恢复的初始库存且已被丢弃）则该次静默无声，同表现层"缺表现资源不阻断游戏"的一贯宽容。
    /// </para>
    /// <para>
    /// 判断记录（只在有声明时装配）：目录里没有任何模板声明 <c>equip_sfx_ref</c> 时装配方不构造本类型，总线上不新增订阅，
    /// 行为与引入本类型之前逐位一致。被替换的旧装备（装备到已占槽位时回到背包）按 <c>item.equipped</c> 只出一次声，
    /// 与 <c>item.unequipped</c> 的发射口径（只对显式卸装发）一致。
    /// </para>
    /// </summary>
    public sealed class EquipSfxDirector : IDisposable
    {
        private readonly IFeedbackSink _sink;
        private readonly IReadOnlyDictionary<Id, Id> _sfxByTemplate;
        private readonly EquipItemTemplateResolver? _resolver;
        private readonly Func<Id, Vec2?>? _position;
        private readonly Dictionary<Id, Id> _templateByInstance = new Dictionary<Id, Id>();
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private bool _disposed;

        /// <summary>已播放的装备/卸装音效次数（测试与诊断用）。</summary>
        public int PlayedCount { get; private set; }

        /// <param name="bus">事件来源。</param>
        /// <param name="sink">出声通路（与打击/脚步同一个 <see cref="IFeedbackSink"/>）。</param>
        /// <param name="sfxByTemplate">物品模板 id → <c>equip_sfx_ref</c>（只含声明了的模板）。</param>
        /// <param name="resolver">按（单位, 物品实例）现查模板 id；null 时只用 <c>item.added</c> 累积表。</param>
        /// <param name="position">单位 id → 世界位置（出声位置）；null 时位置缺省。</param>
        public EquipSfxDirector(
            IEventBus bus, IFeedbackSink sink, IReadOnlyDictionary<Id, Id> sfxByTemplate,
            EquipItemTemplateResolver? resolver = null, Func<Id, Vec2?>? position = null)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _sfxByTemplate = sfxByTemplate ?? throw new ArgumentNullException(nameof(sfxByTemplate));
            _resolver = resolver;
            _position = position;
            _subscriptions.Add(bus.Subscribe<ItemAddedEvent>(CarriersEventKeys.ItemAdded, OnItemAdded));
            _subscriptions.Add(bus.Subscribe<ItemEquippedEvent>(CarriersEventKeys.ItemEquipped, e => Play(e.UnitId, e.ItemInstanceId)));
            _subscriptions.Add(bus.Subscribe<ItemUnequippedEvent>(CarriersEventKeys.ItemUnequipped, e => Play(e.UnitId, e.ItemInstanceId)));
        }

        private void OnItemAdded(ItemAddedEvent evt)
        {
            for (var i = 0; i < evt.Removals.Count; i++)
            {
                _templateByInstance[evt.Removals[i].InstanceId] = evt.ItemTemplateId;
            }
        }

        private void Play(Id unitId, Id instanceId)
        {
            if (_disposed) return;
            Id? templateId = _resolver?.Invoke(unitId, instanceId);
            if (templateId == null && _templateByInstance.TryGetValue(instanceId, out var remembered))
            {
                templateId = remembered;
            }
            if (templateId == null || !_sfxByTemplate.TryGetValue(templateId.Value, out var sfxId))
            {
                return;
            }
            PlayedCount++;
            _sink.PlaySfx(sfxId, _position?.Invoke(unitId));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }
            _subscriptions.Clear();
        }
    }

    /// <summary>按（单位, 物品实例）查物品模板 id：先背包、再装备槽；查不到返回 null。装配方包一层
    /// <c>IInventoryHost.FindInstance</c> 与 <c>EquipmentHost.GetAllEquippedInstances</c>。</summary>
    public delegate Id? EquipItemTemplateResolver(Id unitId, Id itemInstanceId);
}
