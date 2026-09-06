using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    public class StubSpatialQueryTests
    {
        [Fact]
        public void QueryRadius_ReturnsOnlyObjectsWithinRadius_SortedById()
        {
            var query = new StubSpatialQuery();
            query.Register(new Id("unit.c"), new Vec2(1, 0), 0);
            query.Register(new Id("unit.a"), new Vec2(0, 0), 0);
            query.Register(new Id("unit.far"), new Vec2(100, 100), 0);

            var result = query.QueryRadius(new Vec2(0, 0), 2.0, QueryFilter.None);

            Assert.Equal(new[] { new Id("unit.a"), new Id("unit.c") }, result);
        }

        [Fact]
        public void QueryRadius_ConsidersEntityOwnRadius()
        {
            var query = new StubSpatialQuery();
            // 中心距离查询中心 5，自身半径 4，5 - 4 = 1 <= 查询半径 2，应命中。
            query.Register(new Id("unit.big"), new Vec2(5, 0), 4.0);

            var result = query.QueryRadius(new Vec2(0, 0), 2.0, QueryFilter.None);

            Assert.Single(result);
            Assert.Equal(new Id("unit.big"), result[0]);
        }

        [Fact]
        public void QueryRadius_RespectsTagFilter()
        {
            var query = new StubSpatialQuery();
            query.Register(new Id("unit.enemy"), new Vec2(0, 0), 0, new[] { "hostile" });
            query.Register(new Id("unit.friend"), new Vec2(0, 0), 0, new[] { "friendly" });

            var result = query.QueryRadius(new Vec2(0, 0), 5.0, new QueryFilter(requiredTags: new[] { "hostile" }));

            Assert.Equal(new[] { new Id("unit.enemy") }, result);
        }

        [Fact]
        public void QueryRect_ReturnsObjectsOverlappingAabb_SortedById()
        {
            var query = new StubSpatialQuery();
            query.Register(new Id("gobj.inside"), new Vec2(5, 5), 0);
            query.Register(new Id("gobj.edge"), new Vec2(11, 11), 1.5); // 中心在矩形外，但自身半径覆盖到矩形角点（距离约 1.41）
            query.Register(new Id("gobj.outside"), new Vec2(50, 50), 0);

            var result = query.QueryRect(new Vec2(0, 0), new Vec2(10, 10), QueryFilter.None);

            Assert.Equal(new[] { new Id("gobj.edge"), new Id("gobj.inside") }, result);
        }

        [Fact]
        public void QueryShape_Rect_DispatchesToRotatedRectTest()
        {
            var query = new StubSpatialQuery();
            query.Register(new Id("gobj.center"), new Vec2(0, 0), 0);
            query.Register(new Id("gobj.far"), new Vec2(100, 0), 0);

            var shape = Shape.Rect(Vec2.Zero, new Vec2(5, 5), 0);
            var result = query.QueryShape(shape, QueryFilter.None);

            Assert.Equal(new[] { new Id("gobj.center") }, result);
        }

        [Fact]
        public void Nearest_ReturnsClosestMatchingObject()
        {
            var query = new StubSpatialQuery();
            query.Register(new Id("unit.near"), new Vec2(1, 0), 0);
            query.Register(new Id("unit.mid"), new Vec2(3, 0), 0);
            query.Register(new Id("unit.far"), new Vec2(10, 0), 0);

            var nearest = query.Nearest(new Vec2(0, 0), QueryFilter.None);

            Assert.Equal(new Id("unit.near"), nearest);
        }

        [Fact]
        public void Nearest_ReturnsNullWhenNoneRegistered()
        {
            var query = new StubSpatialQuery();
            Assert.Null(query.Nearest(Vec2.Zero, QueryFilter.None));
        }

        // W2 收边补齐（A1 审计第 7 节，测试完备性缺口）：QueryCone/QueryLine 此前只在 QueryShape
        // 内部分发时被自身间接调用（QueryCone 还被 ProjectileHost 弹道命中判定间接覆盖），没有
        // 一条把它们本身作为断言主语的直接场景。

        [Fact]
        public void QueryCone_ReturnsOnlyObjectsWithinAngleAndRange_SortedById()
        {
            var query = new StubSpatialQuery();
            // direction=0（朝 +x 轴），angle=90 度扇形（弧度制，见文件顶部判断记录）。
            query.Register(new Id("unit.ahead"), new Vec2(5, 0), 0); // 正前方，命中。
            query.Register(new Id("unit.behind"), new Vec2(-5, 0), 0); // 正后方，角度超限，不命中。
            query.Register(new Id("unit.too_far"), new Vec2(100, 0), 0); // 正前方但超出 range，不命中。

            var result = query.QueryCone(Vec2.Zero, direction: 0, angle: System.Math.PI / 2, range: 10, QueryFilter.None);

            Assert.Equal(new[] { new Id("unit.ahead") }, result);
        }

        [Fact]
        public void QueryCone_RespectsTagFilter()
        {
            var query = new StubSpatialQuery();
            query.Register(new Id("unit.enemy"), new Vec2(5, 0), 0, new[] { "hostile" });
            query.Register(new Id("unit.friend"), new Vec2(5, 0), 0, new[] { "friendly" });

            var result = query.QueryCone(
                Vec2.Zero, direction: 0, angle: System.Math.PI / 2, range: 10,
                new QueryFilter(requiredTags: new[] { "hostile" }));

            Assert.Equal(new[] { new Id("unit.enemy") }, result);
        }

        [Fact]
        public void QueryLine_ReturnsObjectsWithinRadiusOfSegment_SortedById()
        {
            var query = new StubSpatialQuery();
            query.Register(new Id("unit.near_line"), new Vec2(5, 0.5), 1.0); // 垂距 0.5 <= 半径 1，命中。
            query.Register(new Id("unit.off_line"), new Vec2(5, 20), 1.0); // 远离线段，不命中。
            query.Register(new Id("unit.beyond_segment"), new Vec2(50, 0), 1.0); // 在直线延长线上但超出线段端点范围，不命中。

            var result = query.QueryLine(new Vec2(0, 0), new Vec2(10, 0), QueryFilter.None);

            Assert.Equal(new[] { new Id("unit.near_line") }, result);
        }

        [Fact]
        public void QueryLine_RespectsTagFilter()
        {
            var query = new StubSpatialQuery();
            query.Register(new Id("unit.enemy"), new Vec2(5, 0), 0.5, new[] { "hostile" });
            query.Register(new Id("unit.friend"), new Vec2(5, 0), 0.5, new[] { "friendly" });

            var result = query.QueryLine(
                new Vec2(0, 0), new Vec2(10, 0), new QueryFilter(requiredTags: new[] { "hostile" }));

            Assert.Equal(new[] { new Id("unit.enemy") }, result);
        }
    }
}
