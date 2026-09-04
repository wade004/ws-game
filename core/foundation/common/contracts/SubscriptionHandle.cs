using System;

namespace Core.Foundation.Common
{
    /// <summary>
    /// 供需要显式取消的订阅点返回的句柄（例如 02_引擎适配层.md 1.12 节
    /// IRenderer3D.onAnimEvent 的返回值）。Dispose() 幂等：多次调用只会触发一次取消订阅回调。
    /// </summary>
    public sealed class SubscriptionHandle : IDisposable
    {
        private UnsubscribeCallback? _unsubscribe;

        public SubscriptionHandle(UnsubscribeCallback unsubscribe)
        {
            _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        }

        /// <summary>是否已经被 Dispose（即已经取消订阅）。</summary>
        public bool IsDisposed => _unsubscribe == null;

        public void Dispose()
        {
            var callback = _unsubscribe;
            _unsubscribe = null;
            callback?.Invoke();
        }
    }

    /// <summary>SubscriptionHandle 构造时传入的取消订阅回调。</summary>
    public delegate void UnsubscribeCallback();
}
