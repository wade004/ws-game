using Core.Foundation.Common;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// 反馈动作的出口（见 09_表现层.md 第 6.1 节六种 <c>FeedbackAction</c>）：<c>FeedbackBinder</c>
    /// 只负责"哪个事件、满足什么条件、按什么顺序触发哪些动作"，具体动作怎么落地（飘字用哪套 UI
    /// 控件池、顿帧怎么影响 tick 节奏、震屏具体强度换算）留给注入的实现——本模块只提供把 vfx/sfx
    /// 转发给 <c>Presentation.VfxSfx</c> 播放器的默认实现 <c>CompositeFeedbackSink</c>，其余四种
    /// 动作转给调用方注入的委托（见该类型）。
    /// </summary>
    public interface IFeedbackSink
    {
        void FloatingText(Id entityId, Id styleId, string text);

        void PlayVfx(Id vfxId, FeedbackAttachSpec attach);

        void PlaySfx(Id sfxId, Vec2? at);

        void Freeze(double durationMs);

        void ShakeCamera(Id profileId);

        void Flash(Id entityId, Id profileId);
    }
}
