using System;
using Presentation.Common;
using Xunit;

namespace Tests.PresentationCommon
{
    public class DirectionTests
    {
        [Fact]
        public void FromQuantized_ZeroRadians_Index0()
        {
            var direction = Direction.FromQuantized(0.0, 8);

            Assert.Equal(0, direction.Index);
            Assert.Equal(8, direction.DirectionCount);
            Assert.Equal(0.0, direction.RawRadians);
        }

        [Fact]
        public void FromQuantized_UsesSameAlgorithmAsDirectionQuantizer()
        {
            var raw = Math.PI / 3; // 60 度
            var expectedIndex = Core.Carriers.Unit.DirectionQuantizer.Quantize(raw, 8);

            var direction = Direction.FromQuantized(raw, 8);

            Assert.Equal(expectedIndex, direction.Index);
        }

        [Fact]
        public void Continuous_KeepsRawRadians_IndexAndCountZero()
        {
            var direction = Direction.Continuous(1.23);

            Assert.Equal(1.23, direction.RawRadians);
            Assert.Equal(0, direction.Index);
            Assert.Equal(0, direction.DirectionCount);
        }

        [Fact]
        public void Equality_SameFields_AreEqual()
        {
            var a = new Direction(1.0, 2, 8);
            var b = new Direction(1.0, 2, 8);

            Assert.Equal(a, b);
            Assert.True(a == b);
        }

        [Fact]
        public void Equality_DifferentIndex_AreNotEqual()
        {
            var a = new Direction(1.0, 2, 8);
            var b = new Direction(1.0, 3, 8);

            Assert.NotEqual(a, b);
            Assert.True(a != b);
        }
    }
}
