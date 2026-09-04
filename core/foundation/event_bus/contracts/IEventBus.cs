using Core.Foundation.Common;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// 事件总线契约（见 01_分层与依赖.md L0 模块表 event_bus 行"契约接口名：EventBus"、
    /// 03_运行时骨架.md 第 4.2 节 tick 第 7 步"事件派发"、落地方案与分阶段计划.md 第 4.2 节
    /// "同步派发 + tick 末批处理"）。事件总线承载全架构一切跨层/跨模块的事件通知（见 01 第 8 节
    /// 跨层调用三种合法方式之二），自身不发任何业务事件，也不认识任何具体业务事件类型，只认
    /// <see cref="IEvent.Key"/>。
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        /// 按 key 订阅：处理函数会收到任意实现 <see cref="IEvent"/> 且 <see cref="IEvent.Key"/>
        /// 等于 <paramref name="key"/> 的事件实例。同一 key 下多个订阅者按 Subscribe 调用顺序
        /// 被依次派发调用。
        /// </summary>
        SubscriptionHandle Subscribe(Id key, EventHandler handler);

        /// <summary>
        /// 按 key 订阅，并要求事件实例是具体类型 <typeparamref name="T"/>。若某次派发的事件
        /// Key 匹配但运行时类型不是 <typeparamref name="T"/>，按策略跳过该订阅者的这次调用并
        /// 记一条警告，不影响其它订阅者与派发流程（默认策略，见 EventBusOptions）。
        /// </summary>
        SubscriptionHandle Subscribe<T>(Id key, EventHandler<T> handler) where T : IEvent;

        /// <summary>把事件加入待处理队列，不立即派发（tick 内产生的事件走这里，
        /// tick 末尾统一由 <see cref="DispatchPending"/> 批量派发）。</summary>
        void Enqueue(IEvent evt);

        /// <summary>
        /// 按入队顺序派发全部待处理事件（供 tick 第 7 步"事件派发"调用）。派发过程中
        /// 订阅者通过 <see cref="Enqueue"/> 新产生的事件，会在同一次 <see cref="DispatchPending"/>
        /// 调用内继续被派发，直到队列清空；超过 <see cref="EventBusOptions.MaxDispatchPasses"/>
        /// 时停止并记一条诊断错误。返回本次调用实际派发的事件总数。
        /// </summary>
        int DispatchPending();

        /// <summary>
        /// 同步立即派发单个事件，不经过队列（供非 tick 上下文使用，例如应用状态机、
        /// 场景路由发出的事件）。
        /// </summary>
        void PublishImmediate(IEvent evt);
    }
}
