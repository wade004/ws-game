using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 换装链的姿势侧接线（手感设计/08 第 1 节"武器族 → 姿势解析回落链"）：订阅 <c>feel.weapon_changed</c>，把单位的新武器族
    /// 设进 <see cref="PoseSelector"/>（<see cref="PoseSelector.SetFamily"/>），之后下一次 <c>idle</c>/<c>move</c>/<c>attack</c>
    /// 姿势解析读到的 <see cref="PoseContext.Family"/> 随之变化。
    /// <para>
    /// 判断记录（桥而不是让 <see cref="PoseSelector"/> 自己订阅）：<see cref="PoseSelector"/> 的类型契约是"不订阅事件、不持有逻辑层
    /// 写入能力"（纯呈现、速度来源由调用方决定），本类把"事件 → 武器族"这一步单独放在装配层可选择性接入的小类里，
    /// <see cref="PoseSelector"/> 的契约不变。
    /// </para>
    /// <para>
    /// 判断记录（只读事件，不回头查逻辑层）：武器族直接取事件携带的 <see cref="FeelWeaponChangedEvent.Family"/>（换装链已对账），
    /// 表现层不自己读装备宿主或手感表，保持"表现只读事件与呈现型视图"。空手事件携带 null，对应清除武器族。
    /// </para>
    /// </summary>
    public sealed class EquipmentPoseBridge : IDisposable
    {
        private readonly PoseSelector _selector;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        public EquipmentPoseBridge(IEventBus bus, PoseSelector selector)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
            _subscriptions.Add(bus.Subscribe<FeelWeaponChangedEvent>(
                RulesEventKeys.FeelWeaponChanged, e => _selector.SetFamily(e.UnitId, e.Family)));
            // 手感落地 M2-B：单位销毁时清理它在姿势选择器里的记账（含武器族），与换装链（EquipmentFeelChain）同一口径——
            // 链在销毁时忘掉对账状态、创建时重新对账并重发 feel.weapon_changed，同 id 重建的单位因此能把武器族补回来。
            _subscriptions.Add(bus.Subscribe<EntityDestroyedEvent>(
                SimEventKeys.EntityDestroyed, e => _selector.Forget(e.EntityId)));
        }

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
