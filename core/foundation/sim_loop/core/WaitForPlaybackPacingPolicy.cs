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
    /// 在确认无需播放时立即调用 <see cref="OnPlaybackFinished"/>——是否有事件需要回放是表现层的
    /// 知识，不是节奏策略的知识，本类型不代为猜测。
    /// </para>
    /// <para>
    /// 判断记录（零事件步骤的通知责任，第三轮收口澄清）：零事件时"立即调用 <see cref="OnPlaybackFinished"/>
    /// 等价于不等待"这句话描述的是调用方应当满足的契约（本类型对此不做任何隐式兜底），不是本类型
    /// 自动具备的行为——具体由调用方在"确认这一步没有把任何反馈动作排进播放队列"时主动调用一次。
    /// 生产接线（<c>Presentation.FeedbackBinder.PlaybackQueue.Finished</c>）目前只在队列"由非空变
    /// 空"的边沿触发（见该类型注释），一个从未变过非空的队列不会触发该边沿；这意味着零事件步骤在
    /// 当前接线下并不会被自动通知，是已知局限而非本类型的契约保证（见
    /// <c>Adapter.Unity.Shell.FrameworkResidentHost.OnFrameTick</c> 判断记录"已知局限"一节、
    /// 落地计划"第三轮审计与修复"小节记录），留待后续任务在 <c>core/gameplay/assembly</c> 补一个
    /// 结构性兜底（如 <c>GameplayAssembly.Advance</c> 在 <c>BeginStep</c> 之后发现队列确实为空时
    /// 直接自调用，不依赖表现层事件）。
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
