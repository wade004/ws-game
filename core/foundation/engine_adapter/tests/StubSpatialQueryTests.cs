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
    }
}
