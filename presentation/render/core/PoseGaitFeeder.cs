using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;

namespace Presentation.Render
{
    /// <summary>
    /// 把运动状态的只读速度喂给姿势选择器（手感落地 M2-B，手感设计/04 第 2 节、02 第 7 节）：移动中的单位在每个 tick 结束时按
    /// <c>Unit.MovementState.Motion.SpeedRatio</c>（= 速度 / 该单位自己的基础移速）调用 <see cref="PoseSelector.Observe"/>，
    /// 步态因此由速度档位（idle/walk/run/sprint，阈值与滞回取该单位呈现型手感视图）派生，不再恒为缺省的 walk。
    /// 步态剪辑缺失时的回落（sprint→run→去步态→基础键）由姿势解析器（<c>PoseResolver</c>）沿回落链完成，本类型不重复实现。
    /// <para>
    /// 铁律遵守：纯呈现——只读运动状态与呈现型手感视图，不回写任何判定状态，不改运动代码。
    /// </para>
    /// <para>
    /// 判断记录：<b>只观测"动过的"单位</b>——<c>unit.moved</c> 事件把单位登记进活跃集合，速度比降到 0 并被观测一次（发布 idle）后移出；
    /// 从未移动过的单位不进选择器（<see cref="PoseSelector.GetContext"/> 返回缺省，与未启用手感时逐位一致），也不为静止单位每 tick 白算。
    /// 速度来源取运动学的 <c>SpeedRatio</c> 而不是视图位移差分：位移差分在瞬移、读档定位时会算出虚假高速，且需要帧间位置缓存。
    /// 单位销毁时移出活跃集合（选择器侧的清理由 <see cref="EquipmentPoseBridge"/> 负责）。
    /// </para>
    /// <para>
    /// 移动呈现参数（ADR-0147，手感设计/02 第 7 节）：构造时给了 <see cref="LocomotionPresentation"/> 与步长的重载，在同一次观测里按
    /// <see cref="LocomotionPresentationMath"/> 算出移动剪辑播放速率（<c>stride_scale</c> 匹配实际地面速度）与身体前倾角（<c>lean_deg_per_accel</c>，
    /// 加速度取相邻两次观测的速度比差，再经一阶低通——时间常数 <see cref="LeanSmoothingSeconds"/>——避免逐 tick 抖动）。
    /// 只读呈现型字段与运动状态，不读判定型字段（半属隔离）；单位停下（速度比为 0）那一次观测把参数复位。不给该重载时与此前逐位一致。
    /// </para>
    /// </summary>
    public sealed class PoseGaitFeeder : IDisposable
    {
        private readonly IWorldSim _world;
        private readonly PoseSelector _selector;
        private readonly IFeelResolver _feel;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly HashSet<Id> _active = new HashSet<Id>();
        private readonly List<Id> _scratch = new List<Id>();
        private readonly LocomotionPresentation? _locomotion;
        private readonly double _stepSeconds;
        private readonly Dictionary<Id, (double Ratio, double Lean)> _dynamics = new Dictionary<Id, (double, double)>();

        /// <summary>前倾角的一阶低通时间常数（秒）。</summary>
        public const double LeanSmoothingSeconds = 0.08;

        public PoseGaitFeeder(IEventBus bus, IWorldSim world, PoseSelector selector, IFeelResolver feel)
            : this(bus, world, selector, feel, null, 0.0)
        {
        }

        /// <summary>ADR-0147 新增重载：追加移动呈现参数的输出（<paramref name="locomotion"/> 为 null 即不输出）与固定步长（秒，&gt; 0 才计算前倾角）。</summary>
        public PoseGaitFeeder(
            IEventBus bus, IWorldSim world, PoseSelector selector, IFeelResolver feel, LocomotionPresentation? locomotion, double stepSeconds)
        {
            _locomotion = locomotion;
            _stepSeconds = stepSeconds;
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
            _feel = feel ?? throw new ArgumentNullException(nameof(feel));
            _subscriptions.Add(bus.Subscribe<UnitMovedEvent>(CarriersEventKeys.UnitMoved, e => _active.Add(e.UnitId)));
            _subscriptions.Add(bus.Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, _ => ObserveActive()));
            _subscriptions.Add(bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, e =>
            {
                _active.Remove(e.EntityId);
                _dynamics.Remove(e.EntityId);
                _locomotion?.Forget(e.EntityId);
            }));
        }

        /// <summary>当前仍在观测的单位数（诊断与测试用）。</summary>
        public int ActiveCount => _active.Count;

        private void ObserveActive()
        {
            if (_active.Count == 0) return;
            _scratch.Clear();
            _scratch.AddRange(_active);
            for (var i = 0; i < _scratch.Count; i++)
            {
                var id = _scratch[i];
                var unit = _world.GetEntity(id) as Unit;
                if (unit == null)
                {
                    _active.Remove(id);
                    continue;
                }

                var ratio = unit.MovementState.Motion.SpeedRatio;
                var presenting = _feel.ResolvePresenting(id);
                _selector.Observe(id, ratio, presenting);
                if (_locomotion != null) PublishLocomotion(id, ratio, presenting);
                if (ratio <= 0)
                {
                    _active.Remove(id);
                    _dynamics.Remove(id);
                }
            }
        }

        private void PublishLocomotion(Id id, double ratio, PresentingFeelView presenting)
        {
            if (!(ratio > 0))
            {
                _locomotion!.Set(id, 1.0, 0.0);
                return;
            }

            // 首次观测视为从静止起步（上一次速度比 0）：被移出活跃集合的单位只有速度比降到 0 的，起步加速度是真实的。
            _dynamics.TryGetValue(id, out var dyn);
            var lean = dyn.Lean;
            if (_stepSeconds > 0)
            {
                var degPerAccel = ReadNumber(presenting, FeelFieldNames.LeanDegPerAccel) ?? 0.0;
                var accel = (ratio - dyn.Ratio) / _stepSeconds;
                var target = LocomotionPresentationMath.LeanDeg(accel, degPerAccel);
                var alpha = 1.0 - Math.Exp(-_stepSeconds / LeanSmoothingSeconds);
                lean += (target - lean) * alpha;
            }

            _dynamics[id] = (ratio, lean);

            var gait = _selector.GetContext(id).Gait;
            var reference = LocomotionPresentationMath.ReferenceRatio(
                gait, ReadNumber(presenting, FeelFieldNames.WalkMaxRatio) ?? GaitThresholds.DefaultWalkMaxRatio,
                ReadNumber(presenting, FeelFieldNames.SprintMinRatio));
            var rate = LocomotionPresentationMath.StrideRate(ratio, reference, ReadNumber(presenting, FeelFieldNames.StrideScale) ?? 1.0);
            _locomotion!.Set(id, rate, Math.Round(lean / LocomotionPresentationMath.LeanQuantum) * LocomotionPresentationMath.LeanQuantum);
        }

        private static double? ReadNumber(PresentingFeelView view, string field)
        {
            if (!view.Contains(field)) return null;
            var v = view.GetRaw(field);
            return v.Kind == FeelValueKind.Number ? v.AsNumber() : (double?)null;
        }

        public void Dispose()
        {
            for (var i = 0; i < _subscriptions.Count; i++) _subscriptions[i].Dispose();
            _subscriptions.Clear();
            _active.Clear();
            _dynamics.Clear();
        }
    }
}
