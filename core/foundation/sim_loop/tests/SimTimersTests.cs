using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    public class SimTimersTests
    {
        [Fact]
        public void Timer_ExpiresAfterSixTicksOfOneSixtiethSecond_RemainingMonotonicallyDecreases_CancelClearsAliveness()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            const double step = 1.0 / 60.0;

            var handle = world.Timers.Create(0.1);
            Assert.True(world.Timers.IsAlive(handle));
            Assert.False(world.Timers.IsExpired(handle));

            var previousRemaining = world.Timers.Remaining(handle);

            // 前 5 个 tick：单调递减，且尚未到期（第 5 个 tick 之后仍未到期）。
            for (var i = 1; i <= 5; i++)
            {
                world.Tick(SimStep.Continuous(step));
                var remaining = world.Timers.Remaining(handle);
                Assert.True(remaining < previousRemaining);
                previousRemaining = remaining;
                Assert.False(world.Timers.IsExpired(handle));
            }

            // 第 6 个 tick 之后到期（0.1 秒 ÷ (1/60 秒/tick) = 6 tick）。
            world.Tick(SimStep.Continuous(step));
            Assert.True(world.Timers.IsExpired(handle));
            Assert.True(world.Timers.Remaining(handle) < previousRemaining);

            world.Timers.Cancel(handle);
            Assert.False(world.Timers.IsAlive(handle));
        }

        [Fact]
        public void Timers_AdvanceIndependently_CreateRejectsNegativeDuration()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            const double step = 1.0 / 60.0;

            var shortTimer = world.Timers.Create(step); // 恰好 1 tick 到期
            var longTimer = world.Timers.Create(10 * step); // 远未到期

            world.Tick(SimStep.Continuous(step));

            Assert.True(world.Timers.IsExpired(shortTimer));
            Assert.False(world.Timers.IsExpired(longTimer));

            Assert.Throws<System.ArgumentException>(() => world.Timers.Create(-1.0));
        }
    }
}
