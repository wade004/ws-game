using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// 事件词汇登记表的只读查询契约，对应数据表 <c>found.event_catalog</c>
    /// （见 01_分层与依赖.md L0 模块表 event_bus 行）。本模块只提供内存构造
    /// （见 <see cref="EventCatalog.FromDefinitions"/>）；从数据文件读取 JSON 并转换成
    /// <see cref="EventDefinition"/> 列表是数据注册表（core/foundation/data_registry，
    /// T1-4）的职责，不属于本模块。
    /// </summary>
    public interface IEventCatalog
    {
        /// <summary>某个事件 key 是否已登记。</summary>
        bool IsRegistered(Id key);

        /// <summary>取某个事件 key 的登记信息；未登记返回 null。</summary>
        EventDefinition? Get(Id key);

        /// <summary>全部已登记事件定义，只读。</summary>
        IReadOnlyList<EventDefinition> All { get; }
    }
}
