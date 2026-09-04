using Core.Foundation.Common;
using Xunit;

namespace Tests.Foundation.Common
{
    public class Vec2Tests
    {
        [Fact]
        public void Addition_AddsComponents()
        {
            var a = new Vec2(1, 2);
            var b = new Vec2(3, 4);
            Assert.Equal(new Vec2(4, 6), a + b);
        }

        [Fact]
        public void Subtraction_SubtractsComponents()
        {
            var a = new Vec2(5, 7);
            var b = new Vec2(2, 3);
            Assert.Equal(new Vec2(3, 4), a - b);
        }

        [Fact]
        public void ScalarMultiplication_ScalesBothComponents()
        {
            var a = new Vec2(2, -3);
            Assert.Equal(new Vec2(6, -9), a * 3);
            Assert.Equal(new Vec2(6, -9), 3 * a);
        }

        [Fact]
        public void Length_ComputesEuclideanLength()
        {
            var a = new Vec2(3, 4);
            Assert.Equal(5, a.Length, 10);
            Assert.Equal(25, a.SqrLength, 10);
        }

        [Fact]
        public void Distance_ComputesBetweenTwoPoints()
        {
            var a = new Vec2(0, 0);
            var b = new Vec2(3, 4);
            Assert.Equal(5, Vec2.Distance(a, b), 10);
        }

        [Fact]
        public void Dot_ComputesDotProduct()
        {
            var a = new Vec2(1, 2);
            var b = new Vec2(3, 4);
            Assert.Equal(11, a.Dot(b), 10);
        }

        [Fact]
        public void Zero_IsOriginAndAdditiveIdentity()
        {
            var a = new Vec2(5, -2);
            Assert.Equal(a, a + Vec2.Zero);
            Assert.Equal(0, Vec2.Zero.Length, 10);
        }

        [Fact]
        public void Equality_ComparesComponents()
        {
            Assert.Equal(new Vec2(1, 2), new Vec2(1, 2));
            Assert.NotEqual(new Vec2(1, 2), new Vec2(1, 3));
        }
    }
}
