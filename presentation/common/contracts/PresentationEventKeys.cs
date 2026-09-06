using Core.Foundation.Common;

namespace Presentation.Common
{
    /// <summary>
    /// 表现层自己发出的事件 key 常量（惯例同 <c>RulesEventKeys</c>/<c>CarriersEventKeys</c>：模块
    /// 自持一份常量，不依赖生成物）。只有一个：<c>presentation.playback_finished</c>——铁律 P2
    /// 明确"表现层只订阅事件……<c>presentation.playback_finished</c> 是表现层唯一允许发出、并被
    /// L0 消费的事件"（见 09_表现层.md 第 1 节 P2、第 6.4 节）。已在 <c>found.event_catalog</c>
    /// 登记（<c>data/_sample/found/found.event_catalog.json</c>），字段列表为空。
    /// <para>
    /// 判断记录（去重，09 勘误）：本类型此前还声明了一个同名 <c>PlaybackFinishedEvent</c> 事件类，
    /// 与 <c>presentation/feedback_binder/contracts/PlaybackFinishedEvent.cs</c>（
    /// <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/> 实际发布使用的那一个，key 引用
    /// 生成物常量 <c>Core.Foundation.EventBus.EventKeys.PresentationPlaybackFinished</c>，与本类型的
    /// <see cref="PlaybackFinished"/> 是同一字符串值）重复定义、从未被生产代码使用——已删除，只保留
    /// 本常量供文档/测试核对 key 字符串值与生成物是否一致，不再声明第二个 <see cref="IEvent"/> 实现。
    /// </para>
    /// </summary>
    public static class PresentationEventKeys
    {
        public static readonly Id PlaybackFinished = new Id("presentation.playback_finished");
    }
}
