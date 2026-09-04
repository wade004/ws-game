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
    }
}
