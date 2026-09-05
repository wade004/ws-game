namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="IPacingPolicy"/> 的"不等待"实现（见 03_运行时骨架.md 第 3.2 节步骤 4、
    /// ADR-0013 决策第 7 条）：连续模式的默认节奏，离散模式也可选用——调度器产生一个离散步、
    /// <c>world.Tick</c> 完成后立即推进下一步，不等待表现层回放。
    /// </summary>
    public sealed class ImmediatePacingPolicy : IPacingPolicy
    {
        public PacingMode Mode() => PacingMode.Immediate;

        /// <summary><see cref="PacingMode.Immediate"/> 下不存在 <c>playing_back</c> 节奏门，
        /// 本方法是空操作（不抛异常，容忍调用方无条件调用）。</summary>
        public void OnPlaybackFinished()
        {
        }
    }
}
