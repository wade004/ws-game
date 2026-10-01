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
    /// <para>
    /// 判断记录（收口遗留修复 A7）：① 订阅回调抛异常与 <c>EventBus</c> 同口径——隔离，经
    /// <see cref="IAppLifecycleDiagnostics.Error"/> 记一条（含异常对象），其余回调照常通知，宿主状态不回滚、
    /// 异常不再外抛给 <see cref="RequestTransition"/>/<see cref="PushSubState"/>/<see cref="PopSubState"/> 的调用方
    /// （此前后续订阅者被跳过、异常直接穿透）。② 回调内重入迁移（回调里再 <see cref="RequestTransition"/>/
    /// <see cref="PushSubState"/>/<see cref="PopSubState"/>）：状态/子状态栈立即落定（<see cref="GetState"/> 立即反映），
    /// 但通知排入队列，等当前这条通知对全部订阅者派发完再按发生顺序送达——此前嵌套迁移的通知会先于外层通知
    /// 送达排在触发重入者之后的订阅者（顺序倒置）。总线事件 <c>app.state_changed</c> 仍在迁移发生时立即发布（不入队）。
    /// </para>
    /// </summary>
    public sealed class AppStateHost : IAppStateHost
    {
        private readonly IEventBus _bus;
        private readonly AppStateMachineConfig _config;
        private readonly IAppLifecycleDiagnostics _diagnostics;

        /// <summary>诊断出口只读暴露（ABI 只新增只读属性，见 architecture/adr/0042-诊断契约统一转发到宿主控制台.md）：
        /// 供 adapters/unity 侧统一诊断转发机制轮询本实例累积的 Warnings/Errors，不改变本类型任何既有公开签名。</summary>
        public IAppLifecycleDiagnostics Diagnostics => _diagnostics;

        private readonly List<SubStateId> _subStateStack = new List<SubStateId>();
        private readonly List<StateChangedSubscription> _stateChangedSubscribers = new List<StateChangedSubscription>();
        private readonly List<SubStateChangedSubscription> _subStateChangedSubscribers = new List<SubStateChangedSubscription>();

        /// <summary>待送达的订阅通知（按发生顺序）；<see cref="_notifying"/> 为 true 表示正在逐条送达，
        /// 此时新入队的通知由外层送达循环接手，见类型判断记录 A7。</summary>
        private readonly Queue<Action> _notifications = new Queue<Action>();
        private bool _notifying;

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
            Notify(() => RaiseStateChanged(old, target));

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
            Notify(() => RaiseSubStateChanged(current, sub));
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

            Notify(() => RaiseSubStateChanged(old, current));
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

        /// <summary>入队一条通知并（若当前没有外层送达循环）立即按顺序送达；见类型判断记录 A7。</summary>
        private void Notify(Action notification)
        {
            _notifications.Enqueue(notification);
            if (_notifying)
            {
                return;
            }

            _notifying = true;
            try
            {
                while (_notifications.Count > 0)
                {
                    _notifications.Dequeue()();
                }
            }
            finally
            {
                _notifying = false;
            }
        }

        private void RaiseStateChanged(AppState oldState, AppState newState)
        {
            var snapshot = _stateChangedSubscribers.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].IsCancelled)
                {
                    continue;
                }

                try
                {
                    snapshot[i].Callback(oldState, newState);
                }
                catch (Exception ex)
                {
                    _diagnostics.Error(
                        $"OnStateChanged 订阅者处理 \"{oldState}\" → \"{newState}\" 时抛出异常 " +
                        $"{ex.GetType().FullName}: {ex.Message}，已跳过继续通知其它订阅者",
                        ex);
                }
            }
        }

        private void RaiseSubStateChanged(SubStateId? oldSubState, SubStateId? newSubState)
        {
            var snapshot = _subStateChangedSubscribers.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].IsCancelled)
                {
                    continue;
                }

                try
                {
                    snapshot[i].Callback(oldSubState, newSubState);
                }
                catch (Exception ex)
                {
                    _diagnostics.Error(
                        $"OnSubStateChanged 订阅者处理 \"{oldSubState}\" → \"{newSubState}\" 时抛出异常 " +
                        $"{ex.GetType().FullName}: {ex.Message}，已跳过继续通知其它订阅者",
                        ex);
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
