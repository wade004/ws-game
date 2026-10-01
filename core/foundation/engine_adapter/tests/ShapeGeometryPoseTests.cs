using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    /// <summary>
    /// 手感落地（时间线空间命中）新增的两个几何函数：<see cref="ShapeGeometry.RebaseAt"/>（把形状模板按位姿重新锚定）与
    /// <see cref="ShapeGeometry.ClosestPoint"/>（形状区域内离给定点最近的点，接触点用）。
    /// </summary>
    public sealed class ShapeGeometryPoseTests
    {
        public static IEnumerable<object[]> Templates()
        {
            yield return new object[] { Shape.Circle(Vec2.Zero, 1.5) };
            yield return new object[] { Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0) };
            yield return new object[] { Shape.Cone(Vec2.Zero, 0, Math.PI * 1.5, 2.0) }; // 钝角扇形：边在后方。
            yield return new object[] { Shape.Line(Vec2.Zero, 0, 3.0, 1.0) };
            yield return new object[] { Shape.Rect(Vec2.Zero, new Vec2(1.5, 0.5), 0) };
        }

        private static Vec2 Rotate(Vec2 v, double angle) =>
            new Vec2(v.X * Math.Cos(angle) - v.Y * Math.Sin(angle), v.X * Math.Sin(angle) + v.Y * Math.Cos(angle));

        private static IEnumerable<Vec2> Grid(double extent, double step)
        {
            for (var x = -extent + 0.0137; x <= extent; x += step)
            {
                for (var y = -extent + 0.0291; y <= extent; y += step)
                {
                    yield return new Vec2(x, y);
                }
            }
        }

        [Theory]
        [MemberData(nameof(Templates))]
        public void RebaseAt_QueriesAtAPoseEqualTheTemplateQueriesInTheLocalFrame(Shape template)
        {
            var origin = new Vec2(3.0, -2.0);
            var facing = 0.9;
            var shape = ShapeGeometry.RebaseAt(template, origin, facing);
            Assert.Equal(template.Kind, shape.Kind);

            foreach (var local in Grid(4.0, 0.25))
            {
                var world = origin + Rotate(local, facing);
                Assert.Equal(ShapeGeometry.Contains(template, local), ShapeGeometry.Contains(shape, world));
            }
        }

        [Theory]
        [MemberData(nameof(Templates))]
        public void ClosestPoint_InsidePointsMapToThemselves_OutsidePointsLandInsideAndNoSamplePointIsCloser(Shape template)
        {
            var shape = ShapeGeometry.RebaseAt(template, new Vec2(1.0, 1.0), -0.6);
            var samples = new List<Vec2>();
            foreach (var p in Grid(6.0, 0.1))
            {
                if (ShapeGeometry.Contains(shape, p)) samples.Add(p);
            }

            Assert.NotEmpty(samples);

            foreach (var point in Grid(5.0, 0.7))
            {
                var closest = ShapeGeometry.ClosestPoint(shape, point);
                if (ShapeGeometry.Contains(shape, point))
                {
                    Assert.True((point - closest).Length < 1e-9); // rect/line 经局部坐标往返，浮点误差在 1e-9 内。
                    continue;
                }

                // 最近点在形状区域内（容差内）。
                Assert.True((ShapeGeometry.ClosestPoint(shape, closest) - closest).Length < 1e-6);

                // 不存在比它更近的形状内采样点（网格分辨率 0.1 内的容差）。
                var best = (point - closest).Length;
                foreach (var s in samples)
                {
                    Assert.True((point - s).Length >= best - 0.1 * Math.Sqrt(2) - 1e-9);
                }
            }
        }

        [Fact]
        public void ClosestPoint_Circle_IsOnTheBoundaryTowardsThePoint()
        {
            var shape = Shape.Circle(new Vec2(1, 1), 2.0);
            var closest = ShapeGeometry.ClosestPoint(shape, new Vec2(6, 1));
            Assert.Equal(3.0, closest.X, 9);
            Assert.Equal(1.0, closest.Y, 9);
        }

        [Fact]
        public void ClosestPoint_Cone_OutsideTheAngle_LandsOnTheNearerEdge()
        {
            var shape = Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0); // 边在 +-45 度。
            var closest = ShapeGeometry.ClosestPoint(shape, new Vec2(0, 1.0)); // 90 度方向：最近点在 +45 度边上。
            Assert.Equal(closest.X, closest.Y, 9);
            Assert.Equal(0.5, closest.X, 9);
        }
    }
}
