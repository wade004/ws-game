using Adapters.Stub;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    public class StubClockTests
    {
        [Fact]
        public void Now_StartsAtZero()
        {
            var clock = new StubClock();
            Assert.Equal(0.0, clock.Now());
        }

        [Fact]
        public void Now_IsMonotonicAcrossAdvances()
        {
            var clock = new StubClock();
            clock.Advance(0.05);
            var afterFirst = clock.Now();
            clock.Advance(0.05);
            var afterSecond = clock.Now();
            clock.Advance(0.05);
            var afterThird = clock.Now();

            Assert.True(afterFirst > 0);
            Assert.True(afterSecond > afterFirst);
            Assert.True(afterThird > afterSecond);
            Assert.Equal(0.15, afterThird, 9);
        }

        [Fact]
        public void Advance_UpdatesDeltaSeconds()
        {
            var clock = new StubClock();
            clock.Advance(0.05);
            Assert.Equal(0.05, clock.GetDeltaSeconds(), 9);
        }

        [Fact]
        public void RequestFixedStep_TriggersExpectedCallCountAndLeavesRemainder()
        {
            var clock = new StubClock();
            const double stepSeconds = 1.0 / 60.0;
            var callCount = 0;
            clock.RequestFixedStep(stepSeconds, delta =>
            {
                Assert.Equal(stepSeconds, delta, 9);
                callCount++;
            });

            // 三次 Advance(0.05) 共推进 0.15 秒；0.15 / (1/60) = 9 步整除，累积器应回到 0。
            clock.Advance(0.05);
            clock.Advance(0.05);
            clock.Advance(0.05);

            Assert.Equal(9, callCount);
            Assert.Equal(0.0, clock.GetFixedStepAccumulator(0), 9);
        }

        [Fact]
        public void RequestFixedStep_AccumulatesPartialRemainder()
        {
            var clock = new StubClock();
            const double stepSeconds = 1.0 / 60.0;
            var callCount = 0;
            clock.RequestFixedStep(stepSeconds, _ => callCount++);

            // 0.02 秒 < 1/60（约 0.0167），先触发一次固定步（不满两步），累积器剩余 0.02 - 1/60。
            clock.Advance(0.02);

            Assert.Equal(1, callCount);
            Assert.Equal(0.02 - stepSeconds, clock.GetFixedStepAccumulator(0), 9);
        }

        [Fact]
        public void OnFrame_TriggersOncePerAdvanceWithGivenDelta()
        {
            var clock = new StubClock();
            var received = new System.Collections.Generic.List<double>();
            clock.OnFrame(delta => received.Add(delta));

            clock.Advance(0.05);
            clock.Advance(0.03);

            Assert.Equal(new[] { 0.05, 0.03 }, received);
        }
    }
}
