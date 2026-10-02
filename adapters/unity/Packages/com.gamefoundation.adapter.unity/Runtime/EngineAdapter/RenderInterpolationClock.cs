#nullable enable
// RenderInterpolationClock：渲染帧插值系数（NF2）。
//
// 问题：Unity 宿主在每次 FixedUpdate 里恰好把一个完整步长（Time.fixedDeltaTime）交给 GameplayAssembly.Advance，
// 核心侧累积器每次都被整步耗尽，Gameplay.InterpolationAlpha 因而恒接近 0，且只在固定步更新——渲染帧率高于
// 物理帧率时，同一个固定步区间内的所有渲染帧读到同一个值，视图停在"上一步位置"，位置按固定步频一格一格跳，
// 没有帧间插值。
//
// 判断记录：
//   1) 插值系数在适配层按引擎时间自算：alpha = (当前引擎时间 - 最近一次真正推进模拟的时间) / 固定步长，夹到 [0,1]。
//      用的是随 Time.timeScale 缩放的引擎时间（Time.timeAsDouble）——固定步本身受 timeScale 驱动，两者同源，
//      慢动作下插值同步变慢；timeScale=0 时时间不动、alpha 不动。
//   2) 只有"本次固定步确实调用了 Gameplay.Advance"才记一次步时刻：节奏门（回放未完/非 InWorld）跳过推进时不记，
//      alpha 扫到 1 后停住——不会在状态没变的区间里反复"上一步→这一步"来回插值产生抖动。
//   3) 离散模式（核心给 1.0："按当前已提交状态渲染，不做帧间插值"）与从未推进过的初始状态原样沿用核心值，
//      不覆盖。
//   4) 不改核心：核心 InterpolationAlpha 语义（"Advance 之后的累积器占比"）不变，本类型只是适配层在
//      "宿主恰好整步喂入"这一驱动方式下自己算渲染系数。
using System;

namespace Adapter.Unity.EngineAdapter
{
    internal sealed class RenderInterpolationClock
    {
        private double _lastAdvanceTime = double.NegativeInfinity;

        /// <summary>记一次"模拟真的往前推进了一步"，参数为此刻的引擎时间（秒）。</summary>
        internal void NoteAdvance(double engineTimeSeconds) => _lastAdvanceTime = engineTimeSeconds;

        /// <summary>渲染帧插值系数。<paramref name="coreAlpha"/> 为核心侧 InterpolationAlpha（离散模式/未推进过时原样返回）。</summary>
        internal double Evaluate(double engineTimeSeconds, double stepSeconds, bool continuousMode, double coreAlpha)
        {
            if (!continuousMode || double.IsNegativeInfinity(_lastAdvanceTime) || !(stepSeconds > 0))
            {
                return coreAlpha;
            }

            var alpha = (engineTimeSeconds - _lastAdvanceTime) / stepSeconds;
            return alpha < 0 ? 0 : (alpha > 1 ? 1 : alpha);
        }
    }
}
