// StubNavigation2DTests：INavigation2D 契约的新增/勘误条款（02 第 1.8 节勘误——GetBlockingVersion、
// FindPath 端点契约、"内部相交才受阻、边界/角点相切不算受阻"的统一可通行规则）在 StubNavigation2D
// 上的专项覆盖。这些场景不进 adapters/conformance/Runtime/Navigation2DScenarios（该目录由另一并行
// 任务维护，见 core/carriers/unit README 判断记录"不改 adapters/conformance"），只验证桩实现自身。
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Adapters.Stub;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    public class StubNavigation2DTests
    {
        private static readonly Id MapId = new Id("map.stub_nav_test");

        // -----------------------------------------------------------------
        // GetBlockingVersion：按 mapId 独立计数，BuildNavMesh/SetBlocking/Clear 均递增。
        // -----------------------------------------------------------------

        [Fact]
        public void GetBlockingVersion_FreshMap_ReturnsZero()
        {
            var nav = new StubNavigation2D();
            Assert.Equal(0, nav.GetBlockingVersion(new Id("map.never_touched")));
        }

        [Fact]
        public void GetBlockingVersion_IncrementsOnSetBlocking_Clear_BuildNavMesh()
        {
            var nav = new StubNavigation2D();
            Assert.Equal(0, nav.GetBlockingVersion(MapId));

            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(0, 0), new Vec2(1, 1)) });
            var afterSetBlocking = nav.GetBlockingVersion(MapId);
            Assert.True(afterSetBlocking > 0);

            nav.Clear(MapId);
            var afterClear = nav.GetBlockingVersion(MapId);
            Assert.True(afterClear > afterSetBlocking);

            nav.BuildNavMesh(MapId);
            var afterBuild = nav.GetBlockingVersion(MapId);
            Assert.True(afterBuild > afterClear);
        }

        [Fact]
        public void GetBlockingVersion_IsIndependentPerMap()
        {
            var nav = new StubNavigation2D();
            var otherMap = new Id("map.other");

            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(0, 0), new Vec2(1, 1)) });

            Assert.True(nav.GetBlockingVersion(MapId) > 0);
            Assert.Equal(0, nav.GetBlockingVersion(otherMap));
        }

        // -----------------------------------------------------------------
        // FindPath 端点契约（02 第 1.8 节勘误）。
        // -----------------------------------------------------------------

        [Fact]
        public void FindPath_OpenPoints_ReturnsExactEndpoints()
        {
            var nav = new StubNavigation2D();
            var from = new Vec2(0, 0);
            var to = new Vec2(3, 4);

            var path = nav.FindPath(MapId, from, to);

            Assert.NotNull(path);
            Assert.Equal(from, path![0]);
            Assert.Equal(to, path[path.Count - 1]);
        }

        [Fact]
        public void FindPath_ZeroLengthTarget_ReturnsSingleElementPath()
        {
            var nav = new StubNavigation2D();
            var point = new Vec2(5, 5);

            var path = nav.FindPath(MapId, point, point);

            Assert.NotNull(path);
            Assert.Single(path!);
            Assert.Equal(point, path![0]);
        }

        [Fact]
        public void FindPath_WithinEpsilonButNotIdentical_TreatedAsZeroLength()
        {
            var nav = new StubNavigation2D();
            var from = new Vec2(0, 0);
            var to = new Vec2(1e-7, 0); // < 1e-6 契约阈值

            var path = nav.FindPath(MapId, from, to);

            Assert.NotNull(path);
            Assert.Single(path!);
            Assert.Equal(from, path![0]);
        }

        [Fact]
        public void FindPath_FromUnwalkable_ReturnsNull()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(-1, -1), new Vec2(1, 1)) });

            Assert.Null(nav.FindPath(MapId, Vec2.Zero, new Vec2(10, 10)));
        }

        [Fact]
        public void FindPath_ToUnwalkable_ReturnsNull()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(-1, -1), new Vec2(1, 1)) });

            Assert.Null(nav.FindPath(MapId, new Vec2(10, 10), Vec2.Zero));
        }

        [Fact]
        public void FindPath_UnwalkableEndpoint_TakesPrecedenceOverZeroLength()
        {
            // 判断记录（见 StubNavigation2D.FindPath 文档注释"端点契约"）：起终点重合但那一点本身
            // 不可行走时仍应返回 null，不应该因为"零长度"规则而返回 [from]。
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(-1, -1), new Vec2(1, 1)) });

            Assert.Null(nav.FindPath(MapId, Vec2.Zero, Vec2.Zero));
        }

        [Fact]
        public void FindPath_SegmentBlocked_ReturnsNull()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(4, -1), new Vec2(5, 1)) });

            Assert.Null(nav.FindPath(MapId, Vec2.Zero, new Vec2(10, 0)));
        }

        [Fact]
        public void FindPath_EverySegment_RaycastIsNull_WhenPathReturned()
        {
            // 契约（02 第 1.8 节勘误）：FindPath 成功返回的路径，其每一段 Raycast 必为 null——
            // 二者共用同一套阻挡判定。
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(40, -1), new Vec2(41, 1)) }); // 不在路径上

            var path = nav.FindPath(MapId, Vec2.Zero, new Vec2(10, 0));

            Assert.NotNull(path);
            for (var i = 0; i < path!.Count - 1; i++)
            {
                Assert.Null(nav.Raycast(MapId, path[i], path[i + 1]));
            }
        }

        // -----------------------------------------------------------------
        // 统一可通行规则：线段与阻挡区域内部相交才受阻，仅与边界/角点相切不算受阻。
        // -----------------------------------------------------------------

        [Fact]
        public void Raycast_SegmentAlongRectEdge_IsNotBlocked()
        {
            // 水平线段恰好贴着阻挡矩形的上边界（y = max.Y）滑过，全程未进入矩形内部。
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(-1, -1), new Vec2(1, 1)) });

            var hit = nav.Raycast(MapId, new Vec2(-5, 1), new Vec2(5, 1));

            Assert.Null(hit);
        }

        [Fact]
        public void Raycast_SegmentThroughInterior_IsBlocked()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(-1, -1), new Vec2(1, 1)) });

            var hit = nav.Raycast(MapId, new Vec2(-5, 0), new Vec2(5, 0));

            Assert.NotNull(hit);
        }

        [Fact]
        public void Raycast_SegmentGrazingCorner_IsNotBlocked()
        {
            // 45 度线段恰好只经过矩形的一个角点 (2,2)，不进入内部。
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(0, 0), new Vec2(2, 2)) });

            var hit = nav.Raycast(MapId, new Vec2(0, 4), new Vec2(4, 0));

            Assert.Null(hit);
        }

        [Fact]
        public void Raycast_SegmentEndpointTouchesCorner_IsNotBlocked()
        {
            // 线段终点恰好落在矩形角点上，全程都在矩形外部/边界，不曾进入内部。
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(0, 0), new Vec2(2, 2)) });

            var hit = nav.Raycast(MapId, new Vec2(-5, -5), new Vec2(0, 0));

            Assert.Null(hit);
        }

        [Fact]
        public void FindPath_SegmentAlongRectEdge_IsNotBlocked()
        {
            // 与 Raycast 共用同一判定：贴边线段对应的 FindPath 不应因"相切"而返回 null。
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(-1, -1), new Vec2(1, 1)) });

            var path = nav.FindPath(MapId, new Vec2(-5, 1), new Vec2(5, 1));

            Assert.NotNull(path);
        }
    }
}
