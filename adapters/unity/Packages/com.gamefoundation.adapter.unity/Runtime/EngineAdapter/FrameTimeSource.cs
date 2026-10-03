#nullable enable
// FrameTimeSource：动画/特效播放器的可注入帧时间源（M4-W4）。
//
// 背景：UnityFrameAnimPlayer、EffectSequencePlayer 过去只能由 Time.deltaTime 驱动，需要"按确定的模拟时间推进"的驱动方（实验室引擎宿主、
// 按固定步长回放的测试）只能靠内部推进入口绕过去。现在两者都有一个公开、可选的时间源属性：不设置（缺省 null）时每帧用 Time.deltaTime，
// 行为与引入前逐位一致；设置后每次推进取该时间源的帧时长。
//
// 判断记录（时间源只回答"这一步多长"，不决定"何时推进"）：Unity 的播放循环仍然只在真实帧上调用组件的 Update；同步驱动方（一次调用里跑完
// 一整段模拟，中间不让出播放循环）用播放器的公开 Step() / UnityRenderer2D.StepEffects() 逐帧推进，推进量取自注入的时间源。
// 时间源不可为负数（负的帧时长当作 0，避免把动画倒着推）。
using System;

namespace Adapter.Unity.EngineAdapter
{
    /// <summary>帧时间源：回答"下一次推进应当前进多少秒"。</summary>
    public interface IFrameTimeSource
    {
        /// <summary>本次推进的帧时长（秒，非负）。</summary>
        double DeltaSeconds { get; }
    }

    /// <summary>手动设置帧时长的时间源：驱动方每帧 <see cref="SetDelta"/> 一次，再让播放器 Step。</summary>
    public sealed class ManualFrameTimeSource : IFrameTimeSource
    {
        public double DeltaSeconds { get; private set; }

        /// <summary>设置下一次推进的帧时长（秒）；负数与 NaN 按 0 处理。</summary>
        public void SetDelta(double seconds)
        {
            DeltaSeconds = double.IsNaN(seconds) || seconds < 0.0 ? 0.0 : seconds;
        }
    }
}
