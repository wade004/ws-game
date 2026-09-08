#nullable enable
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class UnityNavigation2DTests
    {
        private static readonly Id MapId = new Id("map.test_arena");

        private UnityNavigation2D _nav = null!;

        [SetUp]
        public void SetUp()
        {
            _nav = new UnityNavigation2D();
        }

        [Test]
        public void IsWalkable_NoBlockingRects_DefaultsTrue()
        {
            Assert.IsTrue(_nav.IsWalkable(MapId, new Vec2(3, 3)));
        }

        [Test]
        public void SetBlocking_ContractMethod_MakesPointInsideUnwalkable()
        {
            // 契约方法（INavigation2D / ADR-0016 决策 7）：此前的非契约便捷方法 RegisterBlockingRect
            // 已删除，调用方自行收集矩形列表后一次性调用 SetBlocking。
            _nav.SetBlocking(MapId, new[] { new Rect(new Vec2(0, 0), new Vec2(2, 2)) });

            Assert.IsFalse(_nav.IsWalkable(MapId, new Vec2(1, 1)));
            Assert.IsTrue(_nav.IsWalkable(MapId, new Vec2(5, 5)));
        }

        [Test]
        public void SetBlocking_CalledAgain_ReplacesPreviousBatch_DoesNotAccumulate()
        {
            _nav.SetBlocking(MapId, new[] { new Rect(new Vec2(0, 0), new Vec2(2, 2)) });
            Assert.IsFalse(_nav.IsWalkable(MapId, new Vec2(1, 1)));

            _nav.SetBlocking(MapId, new[] { new Rect(new Vec2(10, 10), new Vec2(12, 12)) });

            Assert.IsTrue(_nav.IsWalkable(MapId, new Vec2(1, 1)), "第二次 SetBlocking 应整批替换，不是追加");
            Assert.IsFalse(_nav.IsWalkable(MapId, new Vec2(11, 11)));
        }

        [Test]
        public void Clear_ContractMethod_RemovesAllBlockingForMap()
        {
            _nav.SetBlocking(MapId, new[] { new Rect(new Vec2(0, 0), new Vec2(2, 2)) });

            _nav.Clear(MapId);

            Assert.IsTrue(_nav.IsWalkable(MapId, new Vec2(1, 1)));
        }

        [Test]
        public void FindPath_NoObstacle_ReturnsPathFromStartToGoal()
        {
            _nav.BuildNavMesh(MapId);

            var path = _nav.FindPath(MapId, new Vec2(-3, -3), new Vec2(3, 3));

            Assert.IsNotNull(path);
            Assert.Greater(path!.Count, 0);
            Assert.Less(Vec2.Distance(path[0], new Vec2(-3, -3)), 1.0);
            Assert.Less(Vec2.Distance(path[path.Count - 1], new Vec2(3, 3)), 1.0);
        }

        [Test]
        public void FindPath_GoalFullyEnclosed_ReturnsNull()
        {
            // 用四条互相咬合、无缝隙的阻挡矩形围成一个实心方框，把起点完全困在框内
            // （上下两条横贯整个宽度，覆盖四角，避免 8 方向寻路从对角缝隙穿出）。一次性经
            // SetBlocking 整批登记（不再是逐条 RegisterBlockingRect 追加）。
            _nav.SetBlocking(MapId, new[]
            {
                new Rect(new Vec2(-6, 4), new Vec2(6, 6)),    // 上（含四角）
                new Rect(new Vec2(-6, -6), new Vec2(6, -4)),  // 下（含四角）
                new Rect(new Vec2(-6, -4), new Vec2(-4, 4)),  // 左
                new Rect(new Vec2(4, -4), new Vec2(6, 4)),    // 右
            });
            _nav.BuildNavMesh(MapId);

            var path = _nav.FindPath(MapId, new Vec2(0, 0), new Vec2(10, 10));

            Assert.IsNull(path);
        }

        [Test]
        public void Raycast_HitsBlockingRect_ReturnsEntryPoint()
        {
            _nav.SetBlocking(MapId, new[] { new Rect(new Vec2(2, -1), new Vec2(4, 1)) });

            var hit = _nav.Raycast(MapId, new Vec2(0, 0), new Vec2(10, 0));

            Assert.IsNotNull(hit);
            Assert.AreEqual(2.0, hit!.Value.X, 0.001);
        }

        [Test]
        public void Raycast_NoObstacle_ReturnsNull()
        {
            Assert.IsNull(_nav.Raycast(MapId, new Vec2(0, 0), new Vec2(10, 0)));
        }

        // 以下测试为游戏侧 1.8.0 PlayMode 验收后新增（见 UnityNavigation2D 类型顶部判断记录 1/2/3）。

        [Test]
        public void FindPath_ArbitraryPoints_HeadAndTailMatchRequestExactly()
        {
            var map = new Id("map.test_endpoints_exact");
            var from = new Vec2(-3.1, -2.4);
            var to = new Vec2(3.2, 2.1);

            var path = _nav.FindPath(map, from, to);

            Assert.IsNotNull(path, "开阔无阻挡的两点之间应能找到路径");
            Assert.AreEqual(from.X, path![0].X, 0.0, "路径首点 X 应精确等于请求的起点");
            Assert.AreEqual(from.Y, path[0].Y, 0.0, "路径首点 Y 应精确等于请求的起点");
            Assert.AreEqual(to.X, path[path.Count - 1].X, 0.0, "路径末点 X 应精确等于请求的终点");
            Assert.AreEqual(to.Y, path[path.Count - 1].Y, 0.0, "路径末点 Y 应精确等于请求的终点");
        }

        [Test]
        public void FindPath_ZeroLength_ReturnsSingleElementPathWithExactPoint()
        {
            var map = new Id("map.test_zero_length");
            var point = new Vec2(2.5, -1.5);

            var path = _nav.FindPath(map, point, point);

            Assert.IsNotNull(path);
            Assert.AreEqual(1, path!.Count, "起止点相同时路径应恰好包含一个元素");
            Assert.AreEqual(point.X, path[0].X, 0.0);
            Assert.AreEqual(point.Y, path[0].Y, 0.0);
        }

        [Test]
        public void FindPath_UnwalkableEndpoint_ReturnsNull()
        {
            var map = new Id("map.test_unwalkable_endpoint");
            _nav.SetBlocking(map, new[] { new Rect(new Vec2(8, 8), new Vec2(10, 10)) });
            _nav.BuildNavMesh(map);

            var path = _nav.FindPath(map, new Vec2(0, 0), new Vec2(9, 9));

            Assert.IsNull(path, "终点落在阻挡矩形内部（不可行走）时应返回 null");
        }

        [Test]
        public void FindPath_DiagonalMove_DisallowedWhenBothOrthogonalNeighborsBlocked()
        {
            var map = new Id("map.test_corner_cut");
            _nav.SetBlocking(map, new[]
            {
                new Rect(new Vec2(0.25, 0), new Vec2(0.5, 0.25)),   // 中心格右邻
                new Rect(new Vec2(-0.25, 0), new Vec2(0, 0.25)),    // 中心格左邻
                new Rect(new Vec2(0, 0.25), new Vec2(0.25, 0.5)),   // 中心格上邻
                new Rect(new Vec2(0, -0.25), new Vec2(0.25, 0)),    // 中心格下邻
            });
            _nav.BuildNavMesh(map);

            // 中心格 (0.125,0.125) 的四个正交邻居全部被封死；四个对角邻居本身可行走，但每一次
            // 对角移动都至少有一侧正交邻居格被封死——禁止切角时中心格应彻底无法离开（四面楚歌），
            // FindPath 必须返回 null；若实现允许切角，会错误地找到一条穿对角缝隙的短路径。
            var path = _nav.FindPath(map, new Vec2(0.125, 0.125), new Vec2(0.375, 0.375));

            Assert.IsNull(path, "禁止切角：中心格四个正交邻居都被封死时，不应允许经对角缝隙抄近路");
        }

        [Test]
        public void FindPath_DoubleRectCorner_ReturnsPathWhereEverySegmentPassesRaycast()
        {
            var map = new Id("map.test_double_rect_corner");
            _nav.SetBlocking(map, new[]
            {
                new Rect(new Vec2(0, -0.25), new Vec2(0.25, 0.25)),
                new Rect(new Vec2(-0.25, 0), new Vec2(0.25, 0.25)),
            });
            _nav.BuildNavMesh(map);

            var path = _nav.FindPath(map, new Vec2(-1, -1), new Vec2(1, 1));

            Assert.IsNotNull(path, "Unity 网格 A* 应能绕开这个 L 形拐角找到一条路径");
            for (var i = 0; i < path!.Count - 1; i++)
            {
                Assert.IsNull(_nav.Raycast(map, path[i], path[i + 1]),
                    $"路径第 {i} 段不应被 Raycast 判定为受阻（Raycast 与 FindPath 必须共用同一阻挡判定）");
            }
        }

        [Test]
        public void Raycast_SegmentTangentToRectBoundary_IsNotBlocked()
        {
            var map = new Id("map.test_tangent");
            _nav.SetBlocking(map, new[] { new Rect(new Vec2(0, 0), new Vec2(2, 2)) });

            // 沿矩形下边界水平走一段（贴边，不进入内部）：不应判定受阻。
            var hit = _nav.Raycast(map, new Vec2(-1, 0), new Vec2(3, 0));

            Assert.IsNull(hit, "仅贴着阻挡矩形边界走、不进入内部，不应判定为受阻");
        }

        [Test]
        public void GetBlockingVersion_IncrementsOnSetBlockingClearAndBuildNavMesh()
        {
            var map = new Id("map.test_version");
            Assert.AreEqual(0, _nav.GetBlockingVersion(map), "从未变动过的地图版本号应为 0");

            _nav.SetBlocking(map, new[] { new Rect(new Vec2(0, 0), new Vec2(1, 1)) });
            Assert.AreEqual(1, _nav.GetBlockingVersion(map), "SetBlocking 应使版本号递增");

            _nav.BuildNavMesh(map);
            Assert.AreEqual(2, _nav.GetBlockingVersion(map), "BuildNavMesh 应使版本号递增");

            _nav.Clear(map);
            Assert.AreEqual(3, _nav.GetBlockingVersion(map), "Clear 应使版本号递增");

            var otherMap = new Id("map.test_version_other");
            Assert.AreEqual(0, _nav.GetBlockingVersion(otherMap), "不同地图的版本号应互不影响");
        }
    }
}
