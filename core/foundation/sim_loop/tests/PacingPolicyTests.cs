using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="ImmediatePacingPolicy"/>/<see cref="WaitForPlaybackPacingPolicy"/>（03 第 3.2 节
    /// 步骤 4、ADR-0013 决策第 7 条）行为测试。
    /// </summary>
    public sealed class PacingPolicyTests
    {
        [Fact]
        public void Immediate_ModeIsImmediate_OnPlaybackFinishedIsNoOp()
        {
            var policy = new ImmediatePacingPolicy();

            Assert.Equal(PacingMode.Immediate, policy.Mode());

            var exception = Record.Exception(() => policy.OnPlaybackFinished());
            Assert.Null(exception);
        }

        [Fact]
        public void WaitForPlayback_ModeIsWaitForPlayback()
        {
            var policy = new WaitForPlaybackPacingPolicy();
            Assert.Equal(PacingMode.WaitForPlayback, policy.Mode());
        }

        /// <summary>
        /// 根治修复（W5c）：<see cref="WaitForPlaybackPacingPolicy.HasPendingPlayback"/> 探针返回
        /// <c>true</c>（确有待回放动作）时，节奏门行为与本类型收口之前完全一致——<see cref="WaitForPlaybackPacingPolicy.BeginStep"/>
        /// 关闭节奏门，只有 <see cref="WaitForPlaybackPacingPolicy.OnPlaybackFinished"/> 才能解除。
        /// </summary>
        [Fact]
        public void WaitForPlayback_BeginStep_ProbeReturnsTrue_BlocksUntilOnPlaybackFinished()
        {
            var policy = new WaitForPlaybackPacingPolicy(hasPendingPlayback: () => true);

            Assert.True(policy.IsPlaybackFinished); // 初始状态：无待回放内容。

            policy.BeginStep();
            Assert.False(policy.IsPlaybackFinished);

            policy.OnPlaybackFinished();
            Assert.True(policy.IsPlaybackFinished);
        }

        /// <summary>
        /// 根治修复（W5c，第三轮审计"离散回放门‘零事件步骤’无自动通知"仍保留项收口）：未接线探针
        /// （构造函数不传参，<see cref="WaitForPlaybackPacingPolicy.HasPendingPlayback"/> 为
        /// <c>null</c>）时，<see cref="WaitForPlaybackPacingPolicy.BeginStep"/> 应立即视为回放完成
        /// ——不需要调用方再调用 <see cref="WaitForPlaybackPacingPolicy.OnPlaybackFinished"/>，这正是
        /// 修复"零反馈离散步永久卡在 playing_back"缺陷的核心行为。
        /// </summary>
        [Fact]
        public void WaitForPlayback_BeginStep_NoProbe_ImmediatelyFinished_DoesNotBlock()
        {
            var policy = new WaitForPlaybackPacingPolicy();

            policy.BeginStep();

            Assert.True(policy.IsPlaybackFinished, "未接线探针时，零反馈步不应停在 playing_back");
        }

        /// <summary>探针已接线但本步确认"没有待回放内容"（返回 <c>false</c>）时，行为与未接线探针
        /// 一致——立即视为回放完成，不需要等待 <see cref="WaitForPlaybackPacingPolicy.OnPlaybackFinished"/>。</summary>
        [Fact]
        public void WaitForPlayback_BeginStep_ProbeReturnsFalse_ImmediatelyFinished_DoesNotBlock()
        {
            var hasPending = false;
            var policy = new WaitForPlaybackPacingPolicy(hasPendingPlayback: () => hasPending);

            policy.BeginStep();

            Assert.True(policy.IsPlaybackFinished, "探针返回 false 时，本步不应停在 playing_back");
        }

        /// <summary>探针可在两次 <see cref="WaitForPlaybackPacingPolicy.BeginStep"/> 之间切换取值
        /// （典型场景：同一场战斗里，一步产生反馈动作、下一步没有），验证本类型每次都重新读取当前值
        /// 而不是缓存构造期的返回值。</summary>
        [Fact]
        public void WaitForPlayback_BeginStep_ProbeValueChangesBetweenSteps_ReflectsLatestValue()
        {
            var hasPending = true;
            var policy = new WaitForPlaybackPacingPolicy(hasPendingPlayback: () => hasPending);

            policy.BeginStep();
            Assert.False(policy.IsPlaybackFinished, "第一步确有待回放内容，应关闭节奏门");
            policy.OnPlaybackFinished();
            Assert.True(policy.IsPlaybackFinished);

            hasPending = false;
            policy.BeginStep();
            Assert.True(policy.IsPlaybackFinished, "第二步没有待回放内容，应立即放行，不需要 OnPlaybackFinished");
        }

        /// <summary>可写属性 <see cref="WaitForPlaybackPacingPolicy.HasPendingPlayback"/>：核心测试/
        /// 未装配表现层的调用方可以在构造之后（不经构造函数参数）直接赋值，同生产代码经
        /// <c>GameplayAssembly.SetPendingPlaybackProbe</c> 的"先占位、后回填"路径。</summary>
        [Fact]
        public void WaitForPlayback_HasPendingPlaybackProperty_CanBeSetAfterConstruction()
        {
            var policy = new WaitForPlaybackPacingPolicy();
            Assert.Null(policy.HasPendingPlayback);

            policy.HasPendingPlayback = () => true;
            policy.BeginStep();

            Assert.False(policy.IsPlaybackFinished, "回填探针后，返回 true 时应关闭节奏门");
        }
    }
}
