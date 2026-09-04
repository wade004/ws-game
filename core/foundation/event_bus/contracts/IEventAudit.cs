using Core.Foundation.Common;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// 审计日志接收端。<see cref="EventBusOptions.AuditLog"/> 为 true 时，
    /// <see cref="EventBus"/> 每派发一个事件（无论经 <see cref="IEventBus.DispatchPending"/>
    /// 批处理还是 <see cref="IEventBus.PublishImmediate"/> 立即派发）都会调用一次
    /// <see cref="Record"/>（见 01_分层与依赖.md event_bus 行策略配置项"事件是否记录审计日志"）。
    /// 默认实现见 <see cref="InMemoryEventAudit"/>；具体存储（写文件、上报后台）由宿主提供
    /// 自己的实现并注入 <see cref="EventBus"/> 构造函数。
    /// </summary>
    public interface IEventAudit
    {
        /// <summary>记一条派发记录：事件 key 与派发序号（从 0 起，跨 DispatchPending/PublishImmediate
        /// 调用全局递增，不按每次调用重置）。</summary>
        void Record(Id key, long sequence);
    }
}
