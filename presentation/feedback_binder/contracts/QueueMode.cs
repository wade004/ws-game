namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>播放队列模式（见 09_表现层.md 第 6.4 节"离散模式下的动作回放"）。</summary>
    public enum QueueMode
    {
        /// <summary>动作触发即立即执行，不进队列、不发 <c>presentation.playback_finished</c>
        /// （本项目当前时间模型为连续模式，见 ADR-0013，是默认值）。</summary>
        Immediate,

        /// <summary>动作按到达顺序逐条入队顺序回放，队列清空后发
        /// <c>presentation.playback_finished</c>（供未来接入离散/回合制模式时启用）。</summary>
        Sequential,
    }
}
