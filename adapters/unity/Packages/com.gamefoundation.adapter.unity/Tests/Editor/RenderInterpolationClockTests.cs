#nullable enable
// RenderInterpolationClockTests：NF2——渲染帧插值系数。期望值全部由规则 (now - lastAdvance)/step 算出。
using Adapter.Unity.EngineAdapter;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Editor
{
    [Category("module:engine_adapter")]
    public sealed class RenderInterpolationClockTests
    {
        private const double Step = 0.02;

        /// <summary>复现 + 不变量：宿主整步喂入时核心 alpha 恒为 0（coreAlpha=0）。同一固定步区间内的多个渲染帧
        /// 应得到随时间线性增长的系数（改动前每帧都是 0）。</summary>
        [Test]
        public void ContinuousMode_RenderFramesBetweenSteps_AlphaGrowsLinearlyWithEngineTime()
        {
            var clock = new RenderInterpolationClock();
            clock.NoteAdvance(1.0);

            var previous = -1.0;
            for (var i = 0; i <= 4; i++)
            {
                var now = 1.0 + i * (Step / 4.0);
                var alpha = clock.Evaluate(now, Step, continuousMode: true, coreAlpha: 0.0);
                Assert.AreEqual(i / 4.0, alpha, 1e-9, $"第 {i} 帧");
                Assert.Greater(alpha, previous, "区间内严格递增");
                previous = alpha;
            }
        }

        /// <summary>不变量：超过一个步长（节奏门暂停了推进，或帧率低于物理帧率）夹到 1 并停住，不回绕；
        /// 新的一步推进后从 0 重新开始。</summary>
        [Test]
        public void NoNewAdvance_AlphaClampsAtOne_ThenRestartsAfterNextAdvance()
        {
            var clock = new RenderInterpolationClock();
            clock.NoteAdvance(1.0);

            Assert.AreEqual(1.0, clock.Evaluate(1.0 + Step * 3, Step, true, 0.0), 0.0);
            Assert.AreEqual(1.0, clock.Evaluate(1.0 + Step * 30, Step, true, 0.0), 0.0);

            clock.NoteAdvance(1.0 + Step * 30);
            Assert.AreEqual(0.0, clock.Evaluate(1.0 + Step * 30, Step, true, 0.0), 0.0);
            Assert.AreEqual(0.5, clock.Evaluate(1.0 + Step * 30.5, Step, true, 0.0), 1e-9);
        }

        /// <summary>不变量：时间倒退（引擎时间被重置）夹到 0，不出现负系数。</summary>
        [Test]
        public void EngineTimeGoesBackwards_AlphaClampsAtZero()
        {
            var clock = new RenderInterpolationClock();
            clock.NoteAdvance(5.0);
            Assert.AreEqual(0.0, clock.Evaluate(4.0, Step, true, 0.0), 0.0);
        }

        /// <summary>不变量：离散模式与从未推进过的初始状态原样沿用核心值（离散恒 1.0：按已提交状态渲染，不插值）。</summary>
        [Test]
        public void DiscreteModeOrNeverAdvanced_ReturnsCoreAlphaUnchanged()
        {
            var clock = new RenderInterpolationClock();
            Assert.AreEqual(1.0, clock.Evaluate(10.0, Step, continuousMode: true, coreAlpha: 1.0), 0.0, "从未推进过");

            clock.NoteAdvance(9.99);
            Assert.AreEqual(1.0, clock.Evaluate(10.0, Step, continuousMode: false, coreAlpha: 1.0), 0.0, "离散模式");
            Assert.AreEqual(0.37, clock.Evaluate(10.0, Step, continuousMode: false, coreAlpha: 0.37), 0.0, "离散模式原样透传");
        }
    }
}
