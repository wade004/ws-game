using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.FeedbackBinder.Contracts;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>记录全部调用的 <see cref="IFeedbackSink"/> 假实现，供测试断言"哪个动作以什么参数
    /// 被派发了"，不接触任何真实渲染/音频。</summary>
    internal sealed class RecordingFeedbackSink : IFeedbackSink
    {
        public readonly List<(Id EntityId, Id StyleId, string Text)> FloatingTexts = new List<(Id, Id, string)>();
        public readonly List<(Id VfxId, FeedbackAttachSpec Attach)> PlayVfxCalls = new List<(Id, FeedbackAttachSpec)>();
        public readonly List<(Id SfxId, Vec2? At)> PlaySfxCalls = new List<(Id, Vec2?)>();
        public readonly List<double> Freezes = new List<double>();
        public readonly List<Id> Shakes = new List<Id>();
        public readonly List<(Id EntityId, Id ProfileId)> Flashes = new List<(Id, Id)>();

        public void FloatingText(Id entityId, Id styleId, string text) => FloatingTexts.Add((entityId, styleId, text));

        public void PlayVfx(Id vfxId, FeedbackAttachSpec attach) => PlayVfxCalls.Add((vfxId, attach));

        public void PlaySfx(Id sfxId, Vec2? at)
        {
            PlaySfxCalls.Add((sfxId, at));
            OnPlaySfxCalled?.Invoke();
        }

        /// <summary>N17 测试专用 hook：<see cref="PlaySfx"/> 被调用时同步执行（默认 null），供模拟
        /// "命中冷资源、调用当下就把 <see cref="HasPendingPlayback"/> 置为 true"这一副作用（同真实
        /// <c>CompositeFeedbackSink.PlaySfx</c> 转给 <c>ISfxPlayer.Play</c> 命中未加载完成资源时的
        /// 效果），不需要为此新增一个单独的假实现。</summary>
        public Action? OnPlaySfxCalled;

        public void Freeze(double durationMs) => Freezes.Add(durationMs);

        public void ShakeCamera(Id profileId) => Shakes.Add(profileId);

        public void Flash(Id entityId, Id profileId) => Flashes.Add((entityId, profileId));

        /// <summary>GP-09 新增：测试按需手工置位（模拟"vfx/sfx 首次加载中"），默认 false（同真实
        /// <c>CompositeFeedbackSink</c> 在没有任何冷资源排队时的取值）。</summary>
        public bool HasPendingPlayback { get; set; }

        /// <summary>N17 新增：真实 <c>CompositeFeedbackSink</c> 转发自 <c>IVfxPlayer</c>/
        /// <c>ISfxPlayer</c> 的"pending 计数可能变化"信号；测试用 <see cref="RaisePendingPlaybackChanged"/>
        /// 手工模拟"冷资源加载完成回调"这一刻。</summary>
        public event Action? PendingPlaybackChanged;

        /// <summary>测试专用：模拟一次"资源加载完成/超时清理"回调——调用方应先按需改好
        /// <see cref="HasPendingPlayback"/> 再调用本方法，模拟真实 <c>VfxPlayer</c>/<c>SfxPlayer</c>
        /// "先摘除 pending 条目、再触发事件"的顺序（事件本身不携带数值，见 <see
        /// cref="IFeedbackSink.PendingPlaybackChanged"/> 判断记录）。</summary>
        public void RaisePendingPlaybackChanged() => PendingPlaybackChanged?.Invoke();
    }
}
