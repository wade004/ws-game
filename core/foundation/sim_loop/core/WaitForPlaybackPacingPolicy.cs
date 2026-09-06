using System;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="IPacingPolicy"/> 的"等待表现层回放"实现（见 03_运行时骨架.md 第 3.2 节步骤 4、
    /// 第 2 节 <c>playing_back</c> 子态、ADR-0013 决策第 7 条）：离散模式的默认节奏——一个离散步
    /// 产生的事件序列排队回放期间，主循环（<c>GameplayAssembly.Advance</c>）应停在
    /// <c>playing_back</c> 子态，直到表现层发出 <c>presentation.playback_finished</c>、调用方转发
    /// 给 <see cref="OnPlaybackFinished"/> 后才推进下一离散步。
    /// <para>
    /// 根治修复（W5c 收官修复波，第三轮审计"仍保留项"收口）：本类型现可选注入
    /// <see cref="HasPendingPlayback"/> 探针——"当前这一步到底有没有产生需要回放的动作"这项知识
    /// 不属于本类型（属于表现层的播放队列），<see cref="BeginStep"/> 因此按探针结果决定是否真的
    /// 需要进入等待：<c>HasPendingPlayback == null || !HasPendingPlayback()</c> 时（未接线，或接线
    /// 后确认本步没有任何待回放动作）立即视为回放完成，<see cref="IsPlaybackFinished"/> 恒为
    /// <c>true</c>，调用方（<c>GameplayAssembly.Advance</c>）据此不进入 <c>playing_back</c>、直接
    /// 继续推进下一离散步；探针返回 <c>true</c>（确有待回放动作）时才真正关闭节奏门，等待
    /// <see cref="OnPlaybackFinished"/>。语义：回放门只在"确实有东西要回放"时关闭，与 03 第 3.2
    /// 节步骤 4 原文"等待表现层回放完毕再推进"一致（该步骤隐含的前提是"有回放"；没有回放时自然
    /// 无需等待，不需要勘误 03 原文）。
    /// </para>
    /// <para>
    /// 判断记录（探针由谁接线、何时接线）：<see cref="HasPendingPlayback"/> 是可写属性而不是构造
    /// 期必填参数——本类型的典型生产构造点是 <c>GameplayAssembly</c> 内部（见其构造函数第 10.5
    /// 步），彼时表现层（<c>Presentation.Assembly.PresentationAssembly</c>）尚未构造出来，拿不到
    /// 真正的播放队列；<c>PresentationAssembly</c> 依赖已构造完成的 <c>GameplayAssembly</c>（L5 在
    /// L0～L4 之上），因此改为"先占位、后回填"——<c>PresentationAssembly</c> 装配好
    /// <c>FeedbackBinder</c>（播放队列随之就绪）之后，经
    /// <see cref="Core.Gameplay.Assembly.GameplayAssembly.SetPendingPlaybackProbe"/> 这一窄方法把
    /// <c>() =&gt; Feedback.Queue.PendingCount &gt; 0</c> 接进来，同本类型一贯"未接线时不影响装配
    /// 成功、只是退化"的取舍——未接线（<c>null</c>，如只装配了 <c>core</c>/<c>gameplay</c> 两层、
    /// 没有表现层的核心测试/纯逻辑场景）时本类型不知道"有没有要回放的东西"，按"没有"处理（立即
    /// 完成），不会因为没有表现层而永久卡死——这也是本类型在生产接线完成前（构造 <c>Presentation
    /// Assembly</c> 之前，理论上短暂存在的窗口期）唯一合理的默认行为，实际生产链路里
    /// <c>Advance</c> 从不会在这段窗口期被调用（游戏主循环总是等两个装配根都构造完成才开始跑）。
    /// </para>
    /// </summary>
    public sealed class WaitForPlaybackPacingPolicy : IPacingPolicy
    {
        private bool _finished = true;

        /// <summary>可选探针："当前是否存在尚未回放完的表现动作"（典型实现：表现层播放队列的
        /// <c>PendingCount &gt; 0</c>）。默认 <c>null</c>（未接线，见类型判断记录）；调用方（生产
        /// 代码经 <see cref="Core.Gameplay.Assembly.GameplayAssembly.SetPendingPlaybackProbe"/>，
        /// 测试可直接赋值）随时可重新赋值——本类型每次 <see cref="BeginStep"/> 都重新读取当前值，
        /// 不缓存。</summary>
        public Func<bool>? HasPendingPlayback { get; set; }

        public WaitForPlaybackPacingPolicy(Func<bool>? hasPendingPlayback = null)
        {
            HasPendingPlayback = hasPendingPlayback;
        }

        public PacingMode Mode() => PacingMode.WaitForPlayback;

        /// <summary>调用方（<c>GameplayAssembly.Advance</c>）在推进下一离散步之前调用一次
        /// <see cref="IsPlaybackFinished"/> 判定是否可以推进；<see cref="OnPlaybackFinished"/>
        /// 把该标志置回"已完成"。</summary>
        public bool IsPlaybackFinished => _finished;

        /// <summary>标记"本步即将进入回放"（供 <c>GameplayAssembly.Advance</c> 在每次产生离散步
        /// 之后调用）：先按 <see cref="HasPendingPlayback"/> 探针判定这一步是否确有待回放内容——
        /// 没有（探针未接线，或接线后返回 <c>false</c>）时直接把 <see cref="IsPlaybackFinished"/>
        /// 置为 <c>true</c>（不需要 <see cref="OnPlaybackFinished"/> 再解一次，等价于"不等待"，
        /// 根治修复见类型判断记录）；确有时置为 <c>false</c>，等待 <see cref="OnPlaybackFinished"/>
        /// 解除。</summary>
        public void BeginStep()
        {
            _finished = HasPendingPlayback == null || !HasPendingPlayback();
        }

        public void OnPlaybackFinished()
        {
            _finished = true;
        }
    }
}
