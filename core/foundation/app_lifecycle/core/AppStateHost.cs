using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// <see cref="IAppStateHost"/> 的默认实现。订阅列表在派发前复制为数组快照后再遍历，
    /// 以支持"回调执行期间取消订阅"而不影响本次正在进行的派发（与 event_bus 的
    /// <c>EventBus</c>、hook_registry 的 <c>HookRegistry</c> 同一惯例）。
    /// </summary>
    public sealed class AppStateHost : IAppStateHost
    {
        private readonly IEventBus _bus;
        private readonly AppStateMachineConfig _config;
        private readonly IAppLifecycleDiagnostics _diagnostics;

        private readonly List<SubStateId> _subStateStack = new List<SubStateId>();
        private readonly List<StateChangedSubscription> _stateChangedSubscribers = new List<StateChangedSubscription>();
        private readonly List<SubStateChangedSubscription> _subStateChangedSubscribers = new List<SubStateChangedSubscription>();

        /// <summary>初始状态 <see cref="AppState.Boot"/>（见 03 第 9 节、任务书"初始状态 Boot"）。</summary>
        private AppState _state = AppState.Boot;

        public AppStateHost(IEventBus bus, AppStateMachineConfig? config = null, IAppLifecycleDiagnostics? diagnostics = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _config = config ?? AppStateMachineConfig.Default();
            _diagnostics = diagnostics ?? new InMemoryAppLifecycleDiagnostics();
        }

        public AppState GetState() => _state;

        public bool RequestTransition(AppState target)
        {
            if (!_config.IsTransitionAllowed(_state, target))
            {
                _diagnostics.Warn($"非法主状态转移：\"{_state}\" → \"{target}\"，已拒绝，状态未改变");
                return false;
            }

            var old = _state;
            _state = target;
            UpdateSubStateStackOnMainTransition(old, target);

            _bus.PublishImmediate(new AppStateChangedEvent(old, target));
            RaiseStateChanged(old, target);

            return true;
        }

        /// <summary>进入 InWorld 时自动置 Explore；离开 InWorld 时清空栈（见 03 第 2 节
        /// InWorld 子状态说明、任务书"子状态规则"）。</summary>
        private void UpdateSubStateStackOnMainTransition(AppState old, AppState target)
        {
            if (target == AppState.InWorld)
            {
                _subStateStack.Clear();
                _subStateStack.Add(SubStateId.Explore);
            }
            else if (old == AppState.InWorld)
            {
                _subStateStack.Clear();
            }
        }

        public bool PushSubState(SubStateId sub)
        {
            if (_state != AppState.InWorld)
            {
                _diagnostics.Warn($"PushSubState 只允许在 InWorld 主状态下调用，当前主状态是 \"{_state}\"");
                return false;
            }

            // 结构性不变量：主状态为 InWorld 时栈至少有 Explore（见 UpdateSubStateStackOnMainTransition）。
            var current = _subStateStack[_subStateStack.Count - 1];

            if (!_config.IsSubTransitionAllowed(current, sub))
            {
                _diagnostics.Warn($"非法子状态转移：\"{current}\" → \"{sub}\"，已拒绝");
                return false;
            }

            _subStateStack.Add(sub);
            RaiseSubStateChanged(current, sub);
            return true;
        }

        public bool PopSubState()
        {
            if (_state != AppState.InWorld)
            {
                _diagnostics.Warn($"PopSubState 只允许在 InWorld 主状态下调用，当前主状态是 \"{_state}\"");
                return false;
            }

            if (_subStateStack.Count <= 1)
            {
                _diagnostics.Warn("PopSubState：栈底 Explore 不可弹出");
                return false;
            }

            var old = _subStateStack[_subStateStack.Count - 1];
            _subStateStack.RemoveAt(_subStateStack.Count - 1);
            var current = _subStateStack[_subStateStack.Count - 1];

            RaiseSubStateChanged(old, current);
            return true;
        }

        public SubStateId? CurrentSubState =>
            _subStateStack.Count > 0 ? _subStateStack[_subStateStack.Count - 1] : (SubStateId?)null;

        public IReadOnlyList<SubStateId> SubStateStack => _subStateStack;

        public SubscriptionHandle OnStateChanged(StateChangedCallback callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            var subscription = new StateChangedSubscription(callback);
            _stateChangedSubscribers.Add(subscription);
            return new SubscriptionHandle(() =>
            {
                subscription.IsCancelled = true;
                _stateChangedSubscribers.Remove(subscription);
            });
        }

        public SubscriptionHandle OnSubStateChanged(SubStateChangedCallback callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            var subscription = new SubStateChangedSubscription(callback);
            _subStateChangedSubscribers.Add(subscription);
            return new SubscriptionHandle(() =>
            {
                subscription.IsCancelled = true;
                _subStateChangedSubscribers.Remove(subscription);
            });
        }

        public bool RequestExit()
        {
            if (_state != AppState.MainMenu)
            {
                _diagnostics.Warn($"RequestExit 只允许在 MainMenu 状态下调用，当前状态是 \"{_state}\"");
                return false;
            }

            IsExitRequested = true;
            return true;
        }

        public bool IsExitRequested { get; private set; }

        private void RaiseStateChanged(AppState oldState, AppState newState)
        {
            var snapshot = _stateChangedSubscribers.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                if (!snapshot[i].IsCancelled)
                {
                    snapshot[i].Callback(oldState, newState);
                }
            }
        }

        private void RaiseSubStateChanged(SubStateId? oldSubState, SubStateId? newSubState)
        {
            var snapshot = _subStateChangedSubscribers.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                if (!snapshot[i].IsCancelled)
                {
                    snapshot[i].Callback(oldSubState, newSubState);
                }
            }
        }

        private sealed class StateChangedSubscription
        {
            public StateChangedCallback Callback { get; }

            public bool IsCancelled;

            public StateChangedSubscription(StateChangedCallback callback)
            {
                Callback = callback;
            }
        }

        private sealed class SubStateChangedSubscription
        {
            public SubStateChangedCallback Callback { get; }

            public bool IsCancelled;

            public SubStateChangedSubscription(SubStateChangedCallback callback)
            {
                Callback = callback;
            }
        }
    }
}
