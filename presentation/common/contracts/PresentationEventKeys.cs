using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Presentation.Common
{
    /// <summary>
    /// 表现层自己发出的事件 key 常量（惯例同 <c>RulesEventKeys</c>/<c>CarriersEventKeys</c>：模块
    /// 自持一份常量，不依赖生成物）。只有一个：<c>presentation.playback_finished</c>——铁律 P2
    /// 明确"表现层只订阅事件……<c>presentation.playback_finished</c> 是表现层唯一允许发出、并被
    /// L0 消费的事件"（见 09_表现层.md 第 1 节 P2、第 6.4 节）。已在 <c>found.event_catalog</c>
    /// 登记（<c>data/_sample/found/found.event_catalog.json</c>），字段列表为空。
    /// </summary>
    public static class PresentationEventKeys
    {
        public static readonly Id PlaybackFinished = new Id("presentation.playback_finished");
    }

    /// <summary>
    /// 表现层回放队列清空后发出（见 09 第 6.4 节、03 第 9 节 <c>PacingPolicy.onPlaybackFinished</c>）。
    /// 不携带、也不允许携带任何会被 L0 解释为"逻辑判定"的数据（见 09 第 6.4 节最后一条），因此本
    /// 类型不声明任何业务字段——只作为节奏门的解除信号。本项目当前只启用连续时间模型（离散模式
    /// 暂不启用，见 <c>core/foundation/sim_loop</c> README），发出方（本模块以外的回放队列实现，
    /// 不在 P4-1 范围内）尚未落地，这里只提供事件类型本身。
    /// </summary>
    public sealed class PlaybackFinishedEvent : IEvent
    {
        public Id Key => PresentationEventKeys.PlaybackFinished;
    }
}
