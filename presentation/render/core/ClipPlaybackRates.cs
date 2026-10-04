using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 实体当前应使用的剪辑播放速率（手感设计/01 第 3.5 节、02 第 7 节，ADR-0147）：把"动画与判定时间线不脱节"落成一个只读查询。
    /// <list type="bullet">
    /// <item><b>动作态（Attack/Cast）</b>：速率取进行中时间线动作当前相位的重映射速率（<see cref="ActionStartedEvent.StartupRate"/> 等，
    /// 作者毫秒 ÷ 重映射后实际毫秒），随 <c>action.phase_changed</c> 逐相切换；动作结束/取消清除。没有时间线动作（普通攻击、读条技能）为 1。</item>
    /// <item><b>移动态（Move）</b>：取 <see cref="ILocomotionPresentationSource.GetStrideRate"/>（<c>stride_scale</c> 匹配实际地面速度）。</item>
    /// <item>其余状态为 1。</item>
    /// </list>
    /// 速率变化（动作开始/相位切换/结束、移动参数变化）时触发 <see cref="RateChanged"/>；订阅方（<c>AnimClipResolver</c>）与自己上次应用的值比较，相同不调播放器。
    /// 状态切换时由订阅方在播放新剪辑那一刻直接查 <see cref="GetRate"/>（与 <c>action.started</c>/<c>skill.cast_start</c> 同批到达的先后顺序无关：
    /// 谁后到谁触发一次 <see cref="RateChanged"/>）。纯呈现：只订阅事件、只读状态机状态，不回写判定。
    /// </summary>
    public sealed class ClipPlaybackRates : IDisposable
    {
        private sealed class ActionRates
        {
            public Id CastInstanceId;
            public double Startup = 1.0;
            public double Active = 1.0;
            public double Recovery = 1.0;
            public ActionPhase Phase = ActionPhase.Startup;
        }

        private readonly AnimStateMachine _machine;
        private readonly ILocomotionPresentationSource? _locomotion;
        private readonly Dictionary<Id, ActionRates> _actions = new Dictionary<Id, ActionRates>();
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        public event Action<Id>? RateChanged;

        public ClipPlaybackRates(IEventBus bus, AnimStateMachine machine, ILocomotionPresentationSource? locomotion)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            _locomotion = locomotion;

            _subscriptions.Add(bus.Subscribe<ActionStartedEvent>(RulesEventKeys.ActionStarted, OnStarted));
            _subscriptions.Add(bus.Subscribe<ActionPhaseChangedEvent>(RulesEventKeys.ActionPhaseChanged, OnPhase));
            _subscriptions.Add(bus.Subscribe<ActionFinishedEvent>(RulesEventKeys.ActionFinished, e => OnEnded(e.ActorId, e.CastInstanceId)));
            _subscriptions.Add(bus.Subscribe<ActionCancelledEvent>(RulesEventKeys.ActionCancelled, e => OnEnded(e.ActorId, e.CastInstanceId)));
            if (_locomotion != null) _locomotion.Changed += OnLocomotionChanged;
        }

        /// <summary>该实体在其当前动画状态下的剪辑播放速率（见类型注释）。</summary>
        public double GetRate(Id entityId)
        {
            switch (_machine.GetState(entityId))
            {
                case AnimState.Attack:
                case AnimState.Cast:
                    if (_actions.TryGetValue(entityId, out var a))
                    {
                        switch (a.Phase)
                        {
                            case ActionPhase.Startup: return a.Startup;
                            case ActionPhase.Active: return a.Active;
                            case ActionPhase.Recovery: return a.Recovery;
                        }
                    }
                    return 1.0;
                case AnimState.Move:
                    return _locomotion?.GetStrideRate(entityId) ?? 1.0;
                default:
                    return 1.0;
            }
        }

        private void OnStarted(ActionStartedEvent e)
        {
            _actions[e.ActorId] = new ActionRates
            {
                CastInstanceId = e.CastInstanceId,
                Startup = e.StartupRate,
                Active = e.ActiveRate,
                Recovery = e.RecoveryRate,
                Phase = ActionPhase.Startup,
            };
            RateChanged?.Invoke(e.ActorId);
        }

        private void OnPhase(ActionPhaseChangedEvent e)
        {
            if (_actions.TryGetValue(e.ActorId, out var a) && a.CastInstanceId.Equals(e.CastInstanceId))
            {
                a.Phase = e.Phase;
                RateChanged?.Invoke(e.ActorId);
            }
        }

        private void OnEnded(Id actorId, Id castInstanceId)
        {
            if (_actions.TryGetValue(actorId, out var a) && a.CastInstanceId.Equals(castInstanceId))
            {
                _actions.Remove(actorId);
                RateChanged?.Invoke(actorId);
            }
        }

        private void OnLocomotionChanged(Id entityId) => RateChanged?.Invoke(entityId);

        /// <summary>实体销毁时清理。</summary>
        public void Forget(Id entityId) => _actions.Remove(entityId);

        public void Dispose()
        {
            for (var i = 0; i < _subscriptions.Count; i++) _subscriptions[i].Dispose();
            _subscriptions.Clear();
            if (_locomotion != null) _locomotion.Changed -= OnLocomotionChanged;
        }
    }
}
