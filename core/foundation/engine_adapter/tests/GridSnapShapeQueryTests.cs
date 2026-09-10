using System;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    /// <summary>
    /// 格子吸附（ADR-0013 决策 6、04 第 3.1 节 <c>grid_snap</c>，codex 第十八轮）："范围形状按格子
    /// 中心采样"这条承诺的几何机制：<see cref="ShapeGeometry.Contains"/>（点是否在形状内）、
    /// <see cref="Shape.Expand"/>（广相位外扩）、<see cref="GridSnapShapeQuery.QueryShapeAtCellCenters"/>
    /// （两阶段查询组合）。
    /// </summary>
    public sealed class GridSnapShapeQueryTests
    {
        [Fact]
        public void Contains_Circle_InsideAndOutside()
        {
            var shape = Shape.Circle(new Vec2(0, 0), 5.0);

            Assert.True(ShapeGeometry.Contains(shape, new Vec2(4.9, 0)));
            Assert.True(ShapeGeometry.Contains(shape, new Vec2(5.0, 0))); // 边界含端点
            Assert.False(ShapeGeometry.Contains(shape, new Vec2(5.1, 0)));
        }

        [Fact]
        public void Contains_Rect_RespectsRotation()
        {
            // 未旋转矩形：半宽高 (2, 1)，(1.5, 0.9) 应在内，旋转 90 度后局部坐标互换，原本在内的点
            // 变成在外（半宽高本身不变，但点相对矩形局部坐标系的位置变了）。
            var unrotated = Shape.Rect(new Vec2(0, 0), new Vec2(2, 1), rotation: 0);
            Assert.True(ShapeGeometry.Contains(unrotated, new Vec2(1.5, 0.9)));

            var rotated90 = Shape.Rect(new Vec2(0, 0), new Vec2(2, 1), rotation: Math.PI / 2);
            Assert.False(ShapeGeometry.Contains(rotated90, new Vec2(1.5, 0.9)));
        }

        [Fact]
        public void Expand_Circle_GrowsRadiusByMargin()
        {
            var shape = Shape.Circle(new Vec2(1, 2), 5.0);
            var expanded = shape.Expand(3.0);

            Assert.Equal(ShapeKind.Circle, expanded.Kind);
            Assert.Equal(new Vec2(1, 2), expanded.Origin);
            Assert.Equal(8.0, expanded.Radius, 9);
        }

        [Fact]
        public void Expand_Rect_GrowsHalfExtentsByMargin()
        {
            var shape = Shape.Rect(new Vec2(0, 0), new Vec2(2, 1), rotation: 0.3);
            var expanded = shape.Expand(1.0);

            Assert.Equal(3.0, expanded.HalfExtents.X, 9);
            Assert.Equal(2.0, expanded.HalfExtents.Y, 9);
            Assert.Equal(0.3, expanded.Rotation, 9); // 旋转角不受外扩影响
        }

        // -----------------------------------------------------------------
        // GridSnapShapeQuery.QueryShapeAtCellCenters：任务书要求的"实际位置在格子边缘、格子中心
        // 在范围外/内"用例——同一份候选坐标，未吸附查询与吸附查询给出不同结果，证明采样口径确实
        // 变了，不是简单地对既有查询结果多余包一层。
        // -----------------------------------------------------------------

        [Fact]
        public void QueryShapeAtCellCenters_RawPositionInside_CellCenterOutside_ExcludesCandidate()
        {
            // 矩形半宽高 5.8：候选原始坐标 (5.79, 0) 在矩形内（5.79 <= 5.8）；cellSize=4 时它所属
            // 格子 [4,8) 的中心是 6.0，6.0 > 5.8，格子中心落在矩形外。
            var shape = Shape.Rect(new Vec2(0, 0), new Vec2(5.8, 5.8), rotation: 0);
            var spatial = new StubSpatialQuery();
            var candidateId = new Id("unit.edge_inside");
            var candidatePos = new Vec2(5.79, 0);
            spatial.Register(candidateId, candidatePos, radius: 0);

            // 对照组：未吸附的既有查询应该命中——证明这不是候选压根查不到的问题，而是吸附之后
            // 判定口径变了。
            var rawResult = spatial.QueryShape(shape, QueryFilter.None);
            Assert.Contains(candidateId, rawResult);

            var snappedResult = GridSnapShapeQuery.QueryShapeAtCellCenters(
                spatial, shape, QueryFilter.None, id => candidatePos, new GridSnapPolicy(), cellSize: 4.0);

            Assert.DoesNotContain(candidateId, snappedResult);
        }

        [Fact]
        public void QueryShapeAtCellCenters_RawPositionOutside_CellCenterInside_IncludesCandidate()
        {
            // 矩形半宽高 6.5：候选原始坐标 (7.0, 0) 在矩形外（7.0 > 6.5）；cellSize=4 时它所属格子
            // [4,8) 的中心是 6.0，6.0 <= 6.5，格子中心落在矩形内。未吸附的查询本身就会漏掉这个候选
            // （不是"判定为假"，是广相位查询压根不会返回它）——这正是 Shape.Expand 存在的理由。
            var shape = Shape.Rect(new Vec2(0, 0), new Vec2(6.5, 6.5), rotation: 0);
            var spatial = new StubSpatialQuery();
            var candidateId = new Id("unit.edge_outside");
            var candidatePos = new Vec2(7.0, 0);
            spatial.Register(candidateId, candidatePos, radius: 0);

            var rawResult = spatial.QueryShape(shape, QueryFilter.None);
            Assert.DoesNotContain(candidateId, rawResult);

            var snappedResult = GridSnapShapeQuery.QueryShapeAtCellCenters(
                spatial, shape, QueryFilter.None, id => candidatePos, new GridSnapPolicy(), cellSize: 4.0);

            Assert.Contains(candidateId, snappedResult);
        }

        [Fact]
        public void QueryShapeAtCellCenters_NoCandidatesNearShape_ReturnsEmpty()
        {
            var shape = Shape.Circle(new Vec2(0, 0), 5.0);
            var spatial = new StubSpatialQuery();
            spatial.Register(new Id("unit.far"), new Vec2(1000, 1000), radius: 0);

            var result = GridSnapShapeQuery.QueryShapeAtCellCenters(
                spatial, shape, QueryFilter.None, _ => new Vec2(1000, 1000), new GridSnapPolicy(), cellSize: 4.0);

            Assert.Empty(result);
        }
    }
}
