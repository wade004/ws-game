using System;
using Core.Foundation.Common;
using Xunit;

namespace Tests.Foundation.Common
{
    /// <summary>
    /// 格子吸附（ADR-0013 决策 6、04 第 3.1 节 <c>grid_snap</c>，codex 第十八轮）默认策略：
    /// <see cref="GridSnapPolicy"/>（以原点为基准的正方形网格）。
    /// </summary>
    public sealed class GridSnapPolicyTests
    {
        [Theory]
        [InlineData(0.0, 2.0)]
        [InlineData(1.9, 2.0)]
        [InlineData(2.0, 2.0)] // 格子 [2,4) 的左闭端点也落在同一格
        [InlineData(3.9999, 2.0)]
        [InlineData(-0.1, -2.0)] // 格子 [-4,0) 的中心是 -2
        [InlineData(-4.0, -2.0)]
        public void SnapToCellCenter_SingleAxis_SnapsToContainingCellCenter(double value, double expectedCenter)
        {
            var policy = new GridSnapPolicy();
            var result = policy.SnapToCellCenter(new Vec2(value, 0), cellSize: 4.0);

            Assert.Equal(expectedCenter, result.X, 9);
            Assert.Equal(2.0, result.Y, 9); // Y=0 所属格子 [0,4) 的中心
        }

        [Fact]
        public void SnapToCellCenter_AxesAreIndependent()
        {
            var policy = new GridSnapPolicy();
            var result = policy.SnapToCellCenter(new Vec2(5.5, -1.0), cellSize: 4.0);

            Assert.Equal(6.0, result.X, 9); // [4,8) 中心 6
            Assert.Equal(-2.0, result.Y, 9); // [-4,0) 中心 -2
        }

        [Fact]
        public void SnapToCellCenter_AlreadyAtCellCenter_IsIdempotent()
        {
            var policy = new GridSnapPolicy();
            var once = policy.SnapToCellCenter(new Vec2(5.5, -1.0), cellSize: 4.0);
            var twice = policy.SnapToCellCenter(once, cellSize: 4.0);

            Assert.Equal(once, twice);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void SnapToCellCenter_NonPositiveCellSize_Throws(double cellSize)
        {
            var policy = new GridSnapPolicy();
            Assert.Throws<ArgumentOutOfRangeException>(() => policy.SnapToCellCenter(Vec2.Zero, cellSize));
        }
    }
}
