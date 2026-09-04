using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// 表现层唯一允许发出、并被 L0 消费的事件（见 09_表现层.md 第 1 节铁律 P2、第 6.4 节
    /// "队列清空后，表现层发出 presentation.playback_finished 事件...只作为节奏门的解除信号"）。
    /// 不携带任何字段（不构成对 P2"表现层只订阅"的例外，见铁律表 P2 说明）。
    /// <para>
    /// 判断记录（并行任务协调，见任务书"并行注意"）：事件 key 常量
    /// <c>Core.Foundation.EventBus.EventKeys.PresentationPlaybackFinished</c> 已经由
    /// <c>found.event_catalog</c>/生成脚本登记（<c>core/foundation/event_bus/generated/EventKeys.g.cs</c>
    /// 第 169 行），说明另一任务已经把这个事件 key 落到事件词汇表；但强类型 <see cref="IEvent"/>
    /// 事件类（如本类型）尚未出现在 <c>presentation/common</c>（截至本模块开发时该目录只有一个
    /// 占位 <c>LayerMarker.cs</c>）。按任务书指示，本模块先在
    /// <c>feedback_binder/contracts</c> 落地这一最小事件类，直接复用已登记的
    /// <see cref="EventKeys.PresentationPlaybackFinished"/> 常量作为 <see cref="Key"/>（不重复定义
    /// 一份新的 key 常量）；集成阶段若 <c>presentation/common</c> 提供了同名/等价类型，删除本类型、
    /// 改用其类型即可，事件 key 本身不受影响。
    /// </para>
    /// </summary>
    public sealed class PlaybackFinishedEvent : IEvent
    {
        public Id Key => EventKeys.PresentationPlaybackFinished;
    }
}
