using System;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="SimStep"/> 的直接用例（T-M1）：连续步 dt 校验、离散步字段、相等性/哈希、
    /// <c>ToString</c> 固定不变文化。
    /// </summary>
    public sealed class SimStepTests
    {
        private static readonly Id Hero = new Id("unit.hero");

        [Theory]
        [InlineData(-0.001)]
        [InlineData(-1.0)]
        [InlineData(double.NegativeInfinity)]
        public void Continuous_NegativeDt_ThrowsArgumentException(double dt)
        {
            var ex = Assert.Throws<ArgumentException>(() => SimStep.Continuous(dt));
            Assert.Equal("dt", ex.ParamName);
        }

        /// <summary>复现：<c>dt &lt; 0</c> 对 NaN 为 false，NaN 步长会被接受并经 Tick 毒化全部计时器。</summary>
        [Fact]
        public void Continuous_NaNDt_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => SimStep.Continuous(double.NaN));
        }

        [Fact]
        public void Continuous_ZeroDt_IsValid_AndCarriesNoActorOrPhase()
        {
            var step = SimStep.Continuous(0.0);

            Assert.Equal(SimStepKind.Continuous, step.Kind);
            Assert.Equal(0.0, step.Dt);
            Assert.Null(step.ActorId);
            Assert.Null(step.Phase);
        }

        [Fact]
        public void Discrete_CarriesActorAndPhase_WithZeroDt()
        {
            foreach (StepPhase phase in Enum.GetValues(typeof(StepPhase)))
            {
                var step = SimStep.Discrete(Hero, phase);

                Assert.Equal(SimStepKind.Discrete, step.Kind);
                Assert.Equal(0.0, step.Dt);
                Assert.Equal(Hero, step.ActorId);
                Assert.Equal(phase, step.Phase);
            }
        }

        [Fact]
        public void Equality_SameKindAndFields_IsEqualWithMatchingHash()
        {
            Assert.Equal(SimStep.Continuous(0.25), SimStep.Continuous(0.25));
            Assert.Equal(SimStep.Continuous(0.25).GetHashCode(), SimStep.Continuous(0.25).GetHashCode());
            Assert.True(SimStep.Continuous(0.25) == SimStep.Continuous(0.25));

            var d1 = SimStep.Discrete(Hero, StepPhase.Act);
            var d2 = SimStep.Discrete(Hero, StepPhase.Act);
            Assert.Equal(d1, d2);
            Assert.Equal(d1.GetHashCode(), d2.GetHashCode());
            Assert.True(d1.Equals((object)d2));
        }

        [Fact]
        public void Equality_DifferentKindDtActorOrPhase_IsNotEqual()
        {
            Assert.NotEqual(SimStep.Continuous(0.25), SimStep.Continuous(0.5));
            Assert.NotEqual(SimStep.Continuous(0.0), SimStep.Discrete(Hero, StepPhase.TurnStart));
            Assert.NotEqual(SimStep.Discrete(Hero, StepPhase.Act), SimStep.Discrete(Hero, StepPhase.TurnEnd));
            Assert.NotEqual(SimStep.Discrete(Hero, StepPhase.Act), SimStep.Discrete(new Id("unit.foe"), StepPhase.Act));
            Assert.True(SimStep.Continuous(1) != SimStep.Continuous(2));
            Assert.False(SimStep.Continuous(1).Equals("x"));
        }

        [Fact]
        public void ToString_IsCultureInvariant_ForContinuousAndDiscrete()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal("Continuous(dt=0.5)", SimStep.Continuous(0.5).ToString());
                Assert.Equal("Discrete(actorId=unit.hero, phase=Act)", SimStep.Discrete(Hero, StepPhase.Act).ToString());
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
