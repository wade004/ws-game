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

        [Fact]
        public void WaitForPlayback_BeginStep_BlocksUntilOnPlaybackFinished()
        {
            var policy = new WaitForPlaybackPacingPolicy();

            Assert.True(policy.IsPlaybackFinished); // 初始状态：无待回放内容。

            policy.BeginStep();
            Assert.False(policy.IsPlaybackFinished);

            policy.OnPlaybackFinished();
            Assert.True(policy.IsPlaybackFinished);
        }
    }
}
