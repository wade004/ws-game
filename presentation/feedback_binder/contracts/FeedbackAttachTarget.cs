namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// 反馈动作以"哪个实体/哪个坐标系"为挂接基准（见 09_表现层.md 第 6.1 节
    /// <c>play_vfx</c> 的 <c>attach: source|target|world</c>、<c>flash</c> 的
    /// <c>target: source|target</c>）——与 <c>Presentation.VfxSfx.Contracts.VfxAttachMode</c>
    /// （world/anchor/socket/screen，L-1 引擎侧的挂接形状）是两个不同的轴：本枚举描述"取事件里
    /// 哪一方实体的位置/锚点"，由 <c>FeedbackBinder</c>/<c>CompositeFeedbackSink</c> 换算成
    /// <c>VfxAttach</c> 后再调用 <c>IVfxPlayer.Spawn</c>。
    /// </summary>
    public enum FeedbackAttachTarget
    {
        Source,
        Target,

        /// <summary>仅 <c>play_vfx</c> 合法；<c>flash</c> 没有 world 选项（09 §6.1 原文
        /// <c>Flash(profileId, target: source|target)</c> 未列 world），见 <see cref="FeedbackAction"/>
        /// 各子类构造函数的校验。</summary>
        World,
    }
}
