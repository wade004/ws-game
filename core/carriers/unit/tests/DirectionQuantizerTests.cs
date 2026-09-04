using System;
using Core.Carriers.Unit;
using Xunit;

namespace Tests.Carriers.Unit
{
    public class DirectionQuantizerTests
    {
        [Fact]
        public void Quantize_RejectsUnsupportedDirectionCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DirectionQuantizer.Quantize(0, 6));
        }

        [Theory]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        public void Quantize_ZeroRadians_IsBucketZero(int directionCount)
        {
            Assert.Equal(0, DirectionQuantizer.Quantize(0, directionCount));
        }

        [Fact]
        public void Quantize_FourDirections_CardinalAngles()
        {
            Assert.Equal(0, DirectionQuantizer.Quantize(0, 4));
            Assert.Equal(1, DirectionQuantizer.Quantize(Math.PI / 2, 4));
            Assert.Equal(2, DirectionQuantizer.Quantize(Math.PI, 4));
            Assert.Equal(3, DirectionQuantizer.Quantize(3 * Math.PI / 2, 4));
        }

        [Fact]
        public void Quantize_FourDirections_WrapsNearFullCircleBackToZero()
        {
            var justBelowFullCircle = 2 * Math.PI - 0.01;

            Assert.Equal(0, DirectionQuantizer.Quantize(justBelowFullCircle, 4));
        }

        [Fact]
        public void Quantize_EightDirections_CardinalAndOrdinalAngles()
        {
            var eighth = Math.PI / 4;

            Assert.Equal(0, DirectionQuantizer.Quantize(0, 8));
            Assert.Equal(1, DirectionQuantizer.Quantize(eighth, 8));
            Assert.Equal(2, DirectionQuantizer.Quantize(2 * eighth, 8));
            Assert.Equal(4, DirectionQuantizer.Quantize(4 * eighth, 8));
            Assert.Equal(7, DirectionQuantizer.Quantize(7 * eighth, 8));
        }

        [Fact]
        public void Quantize_SixteenDirections_CardinalAngles()
        {
            var sixteenth = Math.PI / 8;

            Assert.Equal(0, DirectionQuantizer.Quantize(0, 16));
            Assert.Equal(4, DirectionQuantizer.Quantize(4 * sixteenth, 16));
            Assert.Equal(8, DirectionQuantizer.Quantize(8 * sixteenth, 16));
            Assert.Equal(15, DirectionQuantizer.Quantize(15 * sixteenth, 16));
        }

        [Fact]
        public void Quantize_NegativeAngle_NormalizesBeforeBucketing()
        {
            Assert.Equal(DirectionQuantizer.Quantize(2 * Math.PI - Math.PI / 2, 4), DirectionQuantizer.Quantize(-Math.PI / 2, 4));
        }
    }
}
