#nullable enable
using Adapter.Unity.EngineAdapter;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class UnityClockTests
    {
        private UnityClock _clock = null!;

        [SetUp]
        public void SetUp()
        {
            _clock = new UnityClock();
        }

        [Test]
        public void TickFrame_InvokesOnFrameCallback_WithGivenDelta()
        {
            double? received = null;
            _clock.OnFrame(delta => received = delta);

            _clock.TickFrame(0.016);

            Assert.AreEqual(0.016, received);
            Assert.AreEqual(0.016, _clock.GetDeltaSeconds());
        }

        [Test]
        public void TickFixedStep_FiresExactlyWhenAccumulatorReachesStep()
        {
            var fireCount = 0;
            _clock.RequestFixedStep(0.02, step => fireCount++);

            _clock.TickFixedStep(0.015); // 未到步长，不触发
            Assert.AreEqual(0, fireCount);

            _clock.TickFixedStep(0.01); // 累计 0.025 >= 0.02，触发一次，余 0.005
            Assert.AreEqual(1, fireCount);
        }

        [Test]
        public void TickFixedStep_MultipleStepsInOneTick_FiresMultipleTimes()
        {
            var fireCount = 0;
            _clock.RequestFixedStep(0.02, step => fireCount++);

            _clock.TickFixedStep(0.07); // 0.07 / 0.02 = 3 次整步，余 0.01

            Assert.AreEqual(3, fireCount);
        }

        [Test]
        public void RequestFixedStep_NonPositiveStep_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _clock.RequestFixedStep(0, step => { }));
        }

        [Test]
        public void Now_IsMonotonicNonDecreasing()
        {
            var first = _clock.Now();
            var second = _clock.Now();

            Assert.GreaterOrEqual(second, first);
        }
    }
}
