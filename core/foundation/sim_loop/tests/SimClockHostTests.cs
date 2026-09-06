using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    public class SimClockHostTests
    {
        [Fact]
        public void Advance_AccumulatesFixedSteps_TickCountAndAlphaMatchHandComputedValues()
        {
            // 手算（Python 复算，double 精度）：step = 1/60。
            // 每次 Advance(0.05)：acc += 0.05 后恰好可以整除出 3 个 step（0.05*3 ≈ 0.05），
            // 余下的累积器只剩一点浮点噪声（约 6.94e-18），alpha = acc/step ≈ 4.16e-16，
            // 三次调用（tick 累计 3/6/9）结果完全相同，因为每次都是从几乎归零的累积器开始。
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world);
            const double expectedAlpha = 4.163336342344337e-16;

            var alpha1 = clock.Advance(0.05);
            Assert.Equal(3, clock.TickIndex);
            Assert.InRange(alpha1, 0.0, 1.0);
            Assert.Equal(expectedAlpha, alpha1, 9);

            var alpha2 = clock.Advance(0.05);
            Assert.Equal(6, clock.TickIndex);
            Assert.Equal(expectedAlpha, alpha2, 9);

            var alpha3 = clock.Advance(0.05);
            Assert.Equal(9, clock.TickIndex);
            Assert.Equal(expectedAlpha, alpha3, 9);
        }

        [Fact]
        public void Advance_ExceedsMaxCatchUp_ProducesOnlyMaxCatchUpTicks_AndDoesNotKeepCatchingUpAfterward()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world); // 默认 maxCatchUpSteps = 5

            // 手算：Advance(10) 在 step=1/60 下理论上要补 600 个 tick，触发上限只补 5 个，
            // 剩余累积器按判断记录 3 清零，alpha = 0。
            var alpha = clock.Advance(10.0);
            Assert.Equal(5, clock.TickIndex);
            Assert.Equal(0.0, alpha, 9);

            // 清零之后累积器是一个"干净"的起点：紧接着的一次小 Advance 不应该触发任何补跑，
            // 因为它远小于一个步长。
            var alpha2 = clock.Advance(0.001);
            Assert.Equal(5, clock.TickIndex);
            Assert.Equal(0.06, alpha2, 9);
        }

        [Fact]
        public void SetPaused_StopsAdvancing_ThenResumesNormally()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world);

            clock.SetPaused(true);
            Assert.True(clock.IsPaused);

            var alphaWhilePaused = clock.Advance(5.0); // 大的 delta，暂停时应完全不生效
            Assert.Equal(0, clock.TickIndex);
            Assert.Equal(0.0, alphaWhilePaused, 9);

            clock.SetPaused(false);
            Assert.False(clock.IsPaused);

            var alphaAfterResume = clock.Advance(1.0 / 60.0);
            Assert.Equal(1, clock.TickIndex);
            Assert.Equal(0.0, alphaAfterResume, 9);
        }

        [Fact]
        public void SetTimeScale_Half_RequiresTwoAdvancesOfOneStepToProduceOneTick()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world);
            clock.SetTimeScale(0.5);

            var alpha1 = clock.Advance(1.0 / 60.0);
            Assert.Equal(0, clock.TickIndex);
            Assert.Equal(0.5, alpha1, 9);

            var alpha2 = clock.Advance(1.0 / 60.0);
            Assert.Equal(1, clock.TickIndex);
            Assert.Equal(0.0, alpha2, 9);
        }

        [Fact]
        public void SimTimeSeconds_After100Ticks_EqualsTickCountTimesStepSeconds()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world);
            var step = clock.StepSeconds;

            for (var i = 0; i < 100; i++)
            {
                clock.Advance(step);
            }

            Assert.Equal(100, clock.TickIndex);
            Assert.Equal(100 * step, clock.SimTimeSeconds, 12);
        }

        // -----------------------------------------------------------------
        // 离散模式（ADR-0013、03 第 9 节 SimClockHost.advance 离散分支）
        // -----------------------------------------------------------------

        [Fact]
        public void Mode_DefaultsToContinuous()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world);
            Assert.Equal(TimeModelMode.Continuous, clock.Mode);
        }

        [Fact]
        public void Advance_InDiscreteMode_DoesNotTickWorldSim_TickIndexStaysZero()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world) { Mode = TimeModelMode.Discrete };

            clock.Advance(1.0);
            clock.Advance(1.0);

            Assert.Equal(0, clock.TickIndex);
            Assert.Equal(0.0, clock.SimTimeSeconds);
        }

        [Fact]
        public void Advance_InDiscreteMode_ReturnsAlphaInZeroToOneRange()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world) { Mode = TimeModelMode.Discrete };

            var alpha = clock.Advance(0.001);

            Assert.InRange(alpha, 0.0, 1.0);
        }

        [Fact]
        public void Advance_InDiscreteMode_WhilePaused_DoesNotAdvancePhase()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world) { Mode = TimeModelMode.Discrete };
            clock.SetPaused(true);

            var alpha1 = clock.Advance(1.0);
            var alpha2 = clock.Advance(1.0);

            Assert.Equal(alpha1, alpha2);
        }

        [Fact]
        public void SwitchingBackToContinuous_ResumesTickingFromPreservedAccumulator()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world);
            var step = clock.StepSeconds;

            clock.Advance(step * 2); // 2 个连续 tick。
            Assert.Equal(2, clock.TickIndex);

            clock.Mode = TimeModelMode.Discrete;
            clock.Advance(5.0); // 离散模式下不产生任何 tick。
            Assert.Equal(2, clock.TickIndex);

            clock.Mode = TimeModelMode.Continuous;
            clock.Advance(step); // 切回连续模式，继续正常产 tick。
            Assert.Equal(3, clock.TickIndex);
        }

        // W2 收边补齐（A1 审计第 7 节，测试完备性缺口）：ConfigureStep 此前只被构造函数间接
        // 覆盖（全部既有用例都用 new SimClockHost(world) 依赖构造期默认值），没有一处显式调用
        // ConfigureStep 断言"运行期重新配置步长/补偿上限"这条路径。

        [Fact]
        public void ConfigureStep_UpdatesStepSecondsAndMaxCatchUpSteps_AffectingSubsequentAdvance()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world); // 默认 stepSeconds = 1/60，maxCatchUpSteps = 5。

            clock.ConfigureStep(0.1, 2);

            Assert.Equal(0.1, clock.StepSeconds);
            Assert.Equal(2, clock.MaxCatchUpSteps);

            // 运行期改配置后，Advance 的补偿行为立即按新参数生效：0.35 秒理论上要补 3.5 个
            // 0.1 秒步长，触发新的 maxCatchUpSteps=2 上限，只补 2 个 tick。
            var alpha = clock.Advance(0.35);
            Assert.Equal(2, clock.TickIndex);
            Assert.Equal(0.0, alpha, 9);
        }

        [Fact]
        public void ConfigureStep_NonPositiveStepSeconds_ThrowsAndDoesNotChangeExistingConfig()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world);
            var originalStep = clock.StepSeconds;

            Assert.Throws<System.ArgumentException>(() => clock.ConfigureStep(0.0, 5));

            Assert.Equal(originalStep, clock.StepSeconds); // 校验失败不改变既有配置。
        }
    }
}
