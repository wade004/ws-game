using Core.Foundation.Common;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// 全部事件的最小契约（见 01_分层与依赖.md L0 模块表 event_bus 行、
    /// 06_规则层_属性技能战斗AI.md 第 8 节事件词汇表）。事件总线本身不认识任何具体业务
    /// 事件类型，只认 <see cref="Key"/>；具体事件类型、携带的字段由发布方（各业务模块）
    /// 自行定义并实现本接口，本模块不得对任何具体事件类型编译期依赖。
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// 事件词汇表 key，必须已在 <see cref="IEventCatalog"/> 中登记
        /// （对应数据表 <c>found.event_catalog</c>，见 04_数据与内容管线.md 第 1.1 节）。
        /// </summary>
        Id Key { get; }
    }
}
