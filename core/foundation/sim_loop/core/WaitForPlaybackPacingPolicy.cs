namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="IPacingPolicy"/> 的"等待表现层回放"实现（见 03_运行时骨架.md 第 3.2 节步骤 4、
    /// 第 2 节 <c>playing_back</c> 子态、ADR-0013 决策第 7 条）：离散模式的默认节奏——一个离散步
    /// 产生的事件序列排队回放期间，主循环（<c>GameplayAssembly.Advance</c>）应停在
    /// <c>playing_back</c> 子态，直到表现层发出 <c>presentation.playback_finished</c>、调用方转发
    /// 给 <see cref="OnPlaybackFinished"/> 后才推进下一离散步。
    /// <para>
    /// 判断记录（"是否等待"的判定粒度）：本类型不知道"当前这一步到底有没有产生需要回放的事件"
    /// ——03 原文"一个离散步在 WorldSim.tick 内可能瞬间产生多条事件"用的是"可能"，未规定零事件
    /// 步骤是否也要等待。本实现采用最简单、确定性最强的策略：<see cref="Mode"/> 恒返回
    /// <see cref="PacingMode.WaitForPlayback"/>，每一步都进入等待门，由调用方（表现层/测试）
    /// 在确认无需播放时立即调用 <see cref="OnPlaybackFinished"/>（零事件时可以同步立即调用，
    /// 等价于"不等待"），比"本策略自己猜测是否有可播放事件"更可靠——是否有事件需要回放是
    /// 表现层的知识，不是节奏策略的知识。
    /// </para>
    /// </summary>
    public sealed class WaitForPlaybackPacingPolicy : IPacingPolicy
    {
        private bool _finished = true;

        public PacingMode Mode() => PacingMode.WaitForPlayback;

        /// <summary>调用方（<c>GameplayAssembly.Advance</c>）在推进下一离散步之前调用一次
        /// <see cref="IsPlaybackFinished"/> 判定是否可以推进；<see cref="OnPlaybackFinished"/>
        /// 把该标志置回"已完成"。</summary>
        public bool IsPlaybackFinished => _finished;

        /// <summary>标记"本步即将进入回放"，供 <see cref="IsPlaybackFinished"/> 在
        /// <see cref="OnPlaybackFinished"/> 调用前返回 false（供 <c>GameplayAssembly.Advance</c>
        /// 在每次产生离散步之后调用）。</summary>
        public void BeginStep()
        {
            _finished = false;
        }

        public void OnPlaybackFinished()
        {
            _finished = true;
        }
    }
}
