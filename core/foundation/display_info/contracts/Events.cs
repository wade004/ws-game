using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json、01_分层与依赖.md L0 模块表
    /// <c>display_info</c> 行"主要事件：display_info.reloaded"）。</summary>
    public static class DisplayInfoEventKeys
    {
        public static readonly Id Reloaded = new Id("display_info.reloaded");
    }

    /// <summary>
    /// <see cref="DisplayInfoRegistry.Reload"/> 完成一次索引重建后触发（见 01 模块表
    /// <c>display_info</c> 行）。<c>found.event_catalog.json</c> 该行登记"无字段"（description
    /// 标注"不携带字段"），本类型据此不携带任何字段。
    /// </summary>
    public sealed class DisplayInfoReloadedEvent : IEvent
    {
        public Id Key => DisplayInfoEventKeys.Reloaded;
    }
}
