using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Gameplay.AreaTrigger;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    public sealed class AreaTriggerShapeGeometryTests
    {
        [Fact]
        public void Circle_PointInsideRadius_Contains()
        {
            var shape = Shape.Circle(new Vec2(10, 10), 5);
            Assert.True(AreaTriggerShapeGeometry.Contains(shape, new Vec2(12, 10)));
        }

        [Fact]
        public void Circle_PointOutsideRadius_DoesNotContain()
        {
            var shape = Shape.Circle(new Vec2(10, 10), 5);
            Assert.False(AreaTriggerShapeGeometry.Contains(shape, new Vec2(20, 10)));
        }

        [Fact]
        public void Rect_PointInsideAxisAligned_Contains()
        {
            var shape = Shape.Rect(new Vec2(0, 0), new Vec2(5, 2), 0);
            Assert.True(AreaTriggerShapeGeometry.Contains(shape, new Vec2(4, 1)));
        }

        [Fact]
        public void Rect_PointOutsideAxisAligned_DoesNotContain()
        {
            var shape = Shape.Rect(new Vec2(0, 0), new Vec2(5, 2), 0);
            Assert.False(AreaTriggerShapeGeometry.Contains(shape, new Vec2(6, 0)));
        }

        [Fact]
        public void Rect_Rotated90Degrees_SwapsAxes()
        {
            // 半宽 5（X）半高 2（Y），旋转 90 度后局部 X 轴对应世界 Y 轴：世界坐标 (0, 4) 落在旋转后
            // 的矩形内（局部坐标变为 (4, 0)，|4|<=5 且 |0|<=2）。
            var shape = Shape.Rect(Vec2.Zero, new Vec2(5, 2), System.Math.PI / 2);
            Assert.True(AreaTriggerShapeGeometry.Contains(shape, new Vec2(0, 4)));
            Assert.False(AreaTriggerShapeGeometry.Contains(shape, new Vec2(4, 0)));
        }

        [Fact]
        public void Cone_PointWithinAngleAndRadius_Contains()
        {
            // 朝向 0（+X 方向），半张角 90 度（Angle=PI），半径 10。
            var shape = Shape.Cone(Vec2.Zero, 0, System.Math.PI, 10);
            Assert.True(AreaTriggerShapeGeometry.Contains(shape, new Vec2(5, 0)));
        }

        [Fact]
        public void Cone_PointOutsideAngle_DoesNotContain()
        {
            // 朝向 0，半张角很窄（Angle=0.1 弧度），点在 +Y 方向应落在扇形外。
            var shape = Shape.Cone(Vec2.Zero, 0, 0.1, 10);
            Assert.False(AreaTriggerShapeGeometry.Contains(shape, new Vec2(0, 5)));
        }

        [Fact]
        public void Cone_PointBeyondRadius_DoesNotContain()
        {
            var shape = Shape.Cone(Vec2.Zero, 0, System.Math.PI, 5);
            Assert.False(AreaTriggerShapeGeometry.Contains(shape, new Vec2(10, 0)));
        }

        [Fact]
        public void Line_PointWithinLengthAndWidth_Contains()
        {
            // 从原点沿 +X 方向延伸 10，宽 4。
            var shape = Shape.Line(Vec2.Zero, 0, 10, 4);
            Assert.True(AreaTriggerShapeGeometry.Contains(shape, new Vec2(5, 1)));
        }

        [Fact]
        public void Line_PointBeforeOrigin_DoesNotContain()
        {
            var shape = Shape.Line(Vec2.Zero, 0, 10, 4);
            Assert.False(AreaTriggerShapeGeometry.Contains(shape, new Vec2(-1, 0)));
        }

        [Fact]
        public void Line_PointBeyondLength_DoesNotContain()
        {
            var shape = Shape.Line(Vec2.Zero, 0, 10, 4);
            Assert.False(AreaTriggerShapeGeometry.Contains(shape, new Vec2(11, 0)));
        }

        [Fact]
        public void Line_PointOutsideWidth_DoesNotContain()
        {
            var shape = Shape.Line(Vec2.Zero, 0, 10, 4);
            Assert.False(AreaTriggerShapeGeometry.Contains(shape, new Vec2(5, 3)));
        }
    }
}
