using System;
using System.Collections.Generic;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;

namespace Presentation.Render
{
    /// <summary>
    /// 把竖直运动服务的只读状态喂给姿势选择器（ADR-0130 追加决定"空中姿势"）：每个 tick 结束时，腾空单位按竖直速度发布
    /// <see cref="AirPhase.Rise"/>（向上）/<see cref="AirPhase.Fall"/>（向下或顶点）；刚落地的单位发布 <see cref="AirPhase.Land"/>
    /// 保持 <c>landHoldTicks</c> 个 tick，之后清回 <see cref="AirPhase.None"/>。姿势解析（<c>jump.rise/fall/land</c>、<c>hit.air</c>、
    /// <c>attack.air[.&lt;family&gt;]</c>）与回落链由 <c>PoseResolver</c> 完成，本类型只提供阶段。
    /// <para>
    /// 铁律遵守：纯呈现——只读 <see cref="IVerticalMotion"/>，不回写任何判定状态。
    /// 判断记录：落地保持窗口是纯呈现常量（默认 8 个 tick），不进手感档案——它只决定"落地姿势播多久"，不影响任何判定。
    /// </para>
    /// </summary>
    public sealed class AirPoseFeeder : IDisposable
    {
        public const int DefaultLandHoldTicks = 8;

        private readonly IVerticalMotion _motion;
        private readonly PoseSelector _selector;
        private readonly int _landHoldTicks;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly HashSet<Id> _inAir = new HashSet<Id>();
        private readonly Dictionary<Id, int> _landing = new Dictionary<Id, int>();
        private readonly List<Id> _scratch = new List<Id>();

        public AirPoseFeeder(IEventBus bus, IVerticalMotion motion, PoseSelector selector, int landHoldTicks = DefaultLandHoldTicks)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _motion = motion ?? throw new ArgumentNullException(nameof(motion));
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
            if (landHoldTicks < 0) throw new ArgumentOutOfRangeException(nameof(landHoldTicks));
            _landHoldTicks = landHoldTicks;
            _subscriptions.Add(bus.Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, _ => Observe()));
            _subscriptions.Add(bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, e =>
            {
                _inAir.Remove(e.EntityId);
                _landing.Remove(e.EntityId);
            }));
        }

        /// <summary>当前在观测的腾空 + 落地保持单位数（诊断与测试用）。</summary>
        public int ActiveCount => _inAir.Count + _landing.Count;

        /// <summary>立即按当前竖直状态观测一次（每个 tick 结束时由事件调用；也可由测试手动驱动）。</summary>
        public void Observe()
        {
            // 1) 落地保持倒计时（先扣减已有的，本 tick 刚落地的在第 3 步才登记，不在本次扣减）。
            if (_landing.Count > 0)
            {
                _scratch.Clear();
                foreach (var pair in _landing) _scratch.Add(pair.Key);
                for (var i = 0; i < _scratch.Count; i++)
                {
                    var id = _scratch[i];
                    var left = _landing[id] - 1;
                    if (left <= 0)
                    {
                        _landing.Remove(id);
                        _selector.SetAirPhase(id, AirPhase.None);
                    }
                    else
                    {
                        _landing[id] = left;
                    }
                }
            }

            // 2) 腾空单位：按竖直速度发布上升/下降。
            var now = _motion.AirborneUnits();
            var nowSet = new HashSet<Id>();
            for (var i = 0; i < now.Count; i++)
            {
                var id = now[i];
                nowSet.Add(id);
                _landing.Remove(id);
                _inAir.Add(id);
                _selector.SetAirPhase(id, _motion.GetVerticalSpeed(id) > 0.0 ? AirPhase.Rise : AirPhase.Fall);
            }

            // 3) 上一次在空中、这次不在了 = 刚落地。
            _scratch.Clear();
            foreach (var id in _inAir)
            {
                if (!nowSet.Contains(id)) _scratch.Add(id);
            }
            for (var i = 0; i < _scratch.Count; i++)
            {
                var id = _scratch[i];
                _inAir.Remove(id);
                if (_landHoldTicks > 0)
                {
                    _landing[id] = _landHoldTicks;
                    _selector.SetAirPhase(id, AirPhase.Land);
                }
                else
                {
                    _selector.SetAirPhase(id, AirPhase.None);
                }
            }
        }

        public void Dispose()
        {
            for (var i = 0; i < _subscriptions.Count; i++) _subscriptions[i].Dispose();
            _subscriptions.Clear();
            _inAir.Clear();
            _landing.Clear();
        }
    }
}
