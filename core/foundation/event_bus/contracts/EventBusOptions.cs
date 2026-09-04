namespace Core.Foundation.EventBus
{
    /// <summary>
    /// <see cref="EventBus"/> 的策略配置项（见 01_分层与依赖.md L0 模块表 event_bus 行
    /// "策略配置项"列："事件是否记录审计日志"）。
    /// </summary>
    public sealed class EventBusOptions
    {
        /// <summary>
        /// 严格模式：<see cref="IEventBus.Enqueue"/>/<see cref="IEventBus.PublishImmediate"/>
        /// 遇到未在 <see cref="IEventCatalog"/> 登记的事件 key 时抛出
        /// <see cref="System.InvalidOperationException"/>；关闭时改为记一条警告并照常入队/派发。
        /// 默认 true。
        /// </summary>
        public bool StrictCatalog { get; set; } = true;

        /// <summary>
        /// 是否记录审计日志：为 true 时，每派发一个事件都会把其 key 与派发序号写入
        /// <see cref="IEventAudit"/>。默认 false。
        /// </summary>
        public bool AuditLog { get; set; }

        /// <summary>
        /// <see cref="IEventBus.DispatchPending"/> 单次调用内允许的最大"派发轮次"数：
        /// 每轮派发一批当时已入队的事件，派发过程中新产生的事件进入下一轮，直到队列空
        /// 或达到本限制；超过后停止本次调用并记一条诊断错误，防止事件互相触发导致无限
        /// 递归（未派发完的事件继续留在队列里，等待下一次 DispatchPending 调用）。默认 16。
        /// </summary>
        public int MaxDispatchPasses { get; set; } = 16;
    }
}
