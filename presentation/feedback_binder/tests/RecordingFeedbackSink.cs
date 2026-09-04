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

        public void PlaySfx(Id sfxId, Vec2? at) => PlaySfxCalls.Add((sfxId, at));

        public void Freeze(double durationMs) => Freezes.Add(durationMs);

        public void ShakeCamera(Id profileId) => Shakes.Add(profileId);

        public void Flash(Id entityId, Id profileId) => Flashes.Add((entityId, profileId));
    }
}
