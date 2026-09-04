using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// <see cref="IEventAudit"/> 的默认实现：把每条派发记录收集到内存列表。
    /// 供测试断言与不需要持久化审计日志的场景使用；需要写文件/上报后台的宿主
    /// 应自行实现 <see cref="IEventAudit"/> 并注入 <see cref="EventBus"/> 构造函数。
    /// </summary>
    public sealed class InMemoryEventAudit : IEventAudit
    {
        private readonly List<EventAuditRecord> _records = new List<EventAuditRecord>();

        public IReadOnlyList<EventAuditRecord> Records => _records;

        public void Record(Id key, long sequence) => _records.Add(new EventAuditRecord(key, sequence));
    }

    /// <summary>单条审计记录：事件 key 与全局递增的派发序号。</summary>
    public readonly struct EventAuditRecord
    {
        public Id Key { get; }

        public long Sequence { get; }

        public EventAuditRecord(Id key, long sequence)
        {
            Key = key;
            Sequence = sequence;
        }
    }
}
