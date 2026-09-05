namespace Core.Foundation.SimLoop
{
    // ============================================================================
    // 离散时间模型（回合制）落地（ADR-0013，见 architecture/03_运行时骨架.md 第 3.2、9 节）。
    // 默认实现见 core/ImmediatePacingPolicy.cs、core/WaitForPlaybackPacingPolicy.cs。
    // ============================================================================

    /// <summary>节奏模式（见 03 第 3.2 节步骤 4、ADR-0013 决策第 7 条）：<see cref="Immediate"/>
    /// 不等待表现层回放（连续模式默认），<see cref="WaitForPlayback"/> 等待表现层发出
    /// <c>presentation.playback_finished</c> 才推进下一离散步（离散模式默认）。</summary>
    public enum PacingMode
    {
        Immediate,
        WaitForPlayback
    }

    /// <summary>
    /// 节奏策略（见 03 第 3.2、9 节签名）：决定离散模式下调度器是否等待表现层回放完毕
    /// 再推进下一离散步。
    /// </summary>
    public interface IPacingPolicy
    {
        /// <summary>当前节奏模式。</summary>
        PacingMode Mode();

        /// <summary>表现层发出 <c>presentation.playback_finished</c> 后由主循环调用，
        /// 解除 <c>playing_back</c> 节奏门（见 03 第 9 节）。</summary>
        void OnPlaybackFinished();
    }
}
