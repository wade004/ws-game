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

        // 以下两条为审计 architecture/落地计划/audit-ac3b622-20260909/presentation/
        // presentation-findings.md NAV-110-01/NAV-110-02 的复现测试改写为正确性断言（此前审计副本
        // AuditAc3b622NavigationProbes.cs 的 CandidateA/CandidateB 用 Assert.IsNull(path) 记录"观察到
        // 的候选缺陷现象"；这里改为断言根治后的正确行为：路径非空，且路径每一段都经得起
        // Raycast 复核，与 FindPath_DoubleRectCorner_ReturnsPathWhereEverySegmentPassesRaycast 同一
        // 验收惯例）。

        [Test]
        public void NAV110_01_EndpointCellCenterBlocked_ExactEndpointAndDirectRaycastClear_ReturnsNonNullPath()
        {
            // 对应审计 CandidateA：阻挡矩形 [(0,0),(1.2,1)]，from=(1.21,0.5) 精确落在矩形右侧紧邻处，
            // to=(1.8,0.5)；两端点按 IsWalkable 逐点判定都可行走、直线 Raycast 也不受阻，但 from 所在
            // 采样格中心 (1.125,0.625) 恰好落在矩形内部——根治前 FindPath 会被这一采样格提前拒绝。
            var map = new Id("map.test_nav110_01_endpoint_cell_center_blocked");
            _nav.SetBlocking(map, new[] { new Rect(new Vec2(0, 0), new Vec2(1.2, 1)) });
            var from = new Vec2(1.21, 0.5);
            var to = new Vec2(1.8, 0.5);
            _nav.BuildNavMesh(map);

            Assert.IsTrue(_nav.IsWalkable(map, from), "端点自身按逐点判定应可行走");
            Assert.IsTrue(_nav.IsWalkable(map, to), "端点自身按逐点判定应可行走");
            Assert.IsNull(_nav.Raycast(map, from, to), "两端点间的直线不应被判定为受阻");

            var path = _nav.FindPath(map, from, to);

            Assert.IsNotNull(path, "端点格中心受阻不等于端点不可行走：存在真实可行的接合方式时不应返回 null");
            Assert.AreEqual(from.X, path![0].X, 0.0, "路径首点应精确等于请求的起点");
            Assert.AreEqual(from.Y, path[0].Y, 0.0, "路径首点应精确等于请求的起点");
            Assert.AreEqual(to.X, path[path.Count - 1].X, 0.0, "路径末点应精确等于请求的终点");
            Assert.AreEqual(to.Y, path[path.Count - 1].Y, 0.0, "路径末点应精确等于请求的终点");
            for (var i = 0; i < path.Count - 1; i++)
            {
                Assert.IsNull(_nav.Raycast(map, path[i], path[i + 1]),
                    $"路径第 {i} 段不应被 Raycast 判定为受阻（Raycast 与 FindPath 必须共用同一阻挡判定）");
            }
        }

        [Test]
        public void NAV110_02_ThinWallNarrowerThanGrid_DetourExists_ReturnsNonNullPath()
        {
            // 对应审计 CandidateB：薄阻挡矩形 [(0.1,-0.5),(0.15,0.5)] 宽度仅 0.05，小于主网格默认格子
            // 尺寸 0.25——根治前中心采样网格对薄墙"视而不见"，A* 找到一条直穿候选，被收尾防线拒绝后
            // 直接返回 null，不再尝试绕路。手工绕路 oracle（(-1,0) -> (-0.2,-0.6) -> (0.2,-0.6) ->
            // (1,0)）三段 Raycast 均为 null，证明存在合法绕路。
            var map = new Id("map.test_nav110_02_thin_wall_narrower_than_grid");
            _nav.SetBlocking(map, new[] { new Rect(new Vec2(0.1, -0.5), new Vec2(0.15, 0.5)) });
            var from = new Vec2(-1, 0);
            var to = new Vec2(1, 0);
            _nav.BuildNavMesh(map);

            var oracle = new[] { from, new Vec2(-0.2, -0.6), new Vec2(0.2, -0.6), to };
            for (var i = 0; i < oracle.Length - 1; i++)
            {
                Assert.IsNull(_nav.Raycast(map, oracle[i], oracle[i + 1]),
                    $"oracle 绕路第 {i} 段应为可通行（用于证明真实存在合法绕路）");
            }

            var path = _nav.FindPath(map, from, to);

            Assert.IsNotNull(path, "薄墙存在绕路时不应被误判为无路可走");
            Assert.AreEqual(from.X, path![0].X, 0.0, "路径首点应精确等于请求的起点");
            Assert.AreEqual(from.Y, path[0].Y, 0.0, "路径首点应精确等于请求的起点");
            Assert.AreEqual(to.X, path[path.Count - 1].X, 0.0, "路径末点应精确等于请求的终点");
            Assert.AreEqual(to.Y, path[path.Count - 1].Y, 0.0, "路径末点应精确等于请求的终点");
            for (var i = 0; i < path.Count - 1; i++)
            {
                Assert.IsNull(_nav.Raycast(map, path[i], path[i + 1]),
                    $"路径第 {i} 段不应被 Raycast 判定为受阻（Raycast 与 FindPath 必须共用同一阻挡判定）");
            }
        }

        // 审计 architecture/落地计划/audit-6739f50-20260909/AUDIT_REPORT.md NAV-111-01 的复现测试
        // （见 presentation/navigation-probes.xml/.log 同名场景）：审计副本
        // Audit6739f50NavigationProbes.NAV110_03_NarrowCorridor_ExactPointsAndDirectRaycastClear_ShouldRemainReachable
        // 记录了这条"观察到的候选缺陷现象"，这里按既有惯例（NAV110_01/NAV110_02）改写为断言根治后
        // 正确行为的正式回归用例，纳入本模块基线。

        [Test]
        public void NAV111_01_NarrowCorridorThinnerThanBothGrids_ExactPointsAndDirectRaycastClear_ReturnsNonNullPath()
        {
            // 通道宽度 0.1，严格小于主网格格子尺寸 0.25 与细网格兜底格子尺寸 0.125：无论主网格还是
            // 细网格，都不存在一整行格子（哪怕格中心，哪怕"格子内部与阻挡矩形相交"判定）完全落在
            // 通道内部——ResolveEntryCell 甚至会因为端点周围 8 邻格全部受阻直接判定"无法进入网格"。
            // 但两端点自身按 IsWalkable 逐点判定都可行走，直线 Raycast 也完全清晰（通道内直线与两侧
            // 阻挡矩形毫无接触，不是贴边擦角）——根治前 FindPath 会在网格寻路彻底失败后直接返回 null，
            // 根治后应落到 NAV-111-01 的"直线直达"兜底，返回 [from,to]。
            var map = new Id("map.test_nav111_01_narrow_corridor");
            _nav.SetBlocking(map, new[]
            {
                new Rect(new Vec2(-1, -1), new Vec2(1, 0)),
                new Rect(new Vec2(-1, 0.1), new Vec2(1, 1)),
            });
            var from = new Vec2(-0.5, 0.05);
            var to = new Vec2(0.5, 0.05);
            _nav.BuildNavMesh(map);

            Assert.IsTrue(_nav.IsWalkable(map, from), "端点自身按逐点判定应可行走");
            Assert.IsTrue(_nav.IsWalkable(map, to), "端点自身按逐点判定应可行走");
            Assert.IsNull(_nav.Raycast(map, from, to), "两端点间的直线不应被判定为受阻");

            var path = _nav.FindPath(map, from, to);

            Assert.IsNotNull(path, "比两种网格格子尺寸都窄的合法通道不应被误判为无路可走");
            Assert.AreEqual(from.X, path![0].X, 0.0, "路径首点应精确等于请求的起点");
            Assert.AreEqual(from.Y, path[0].Y, 0.0, "路径首点应精确等于请求的起点");
            Assert.AreEqual(to.X, path[path.Count - 1].X, 0.0, "路径末点应精确等于请求的终点");
            Assert.AreEqual(to.Y, path[path.Count - 1].Y, 0.0, "路径末点应精确等于请求的终点");
            for (var i = 0; i < path.Count - 1; i++)
            {
                Assert.IsNull(_nav.Raycast(map, path[i], path[i + 1]),
                    $"路径第 {i} 段不应被 Raycast 判定为受阻（Raycast 与 FindPath 必须共用同一阻挡判定）");
            }
        }

        [Test]
        public void NAV111_01_StraightLineFallback_DoesNotOverrideDiagonalCornerCuttingBan()
        {
            // 回归防线：NAV-111-01 新增的"直线直达"兜底必须不越权放行 FindPath_DiagonalMove_
            // DisallowedWhenBothOrthogonalNeighborsBlocked 场景——这条对角线的直线 Raycast 恰好也是
            // 清晰的（只在数学意义上的单点擦过两个阻挡矩形的共享墙角），但两个正交邻居格都被封死时
            // 仍应继续禁止"贴墙角切对角线抄近路"，FindPath 必须仍然返回 null（同基线用例断言，逐字
            // 复用其阻挡矩形与两端点，见 SegmentHasClearContact 判断记录）。
            var map = new Id("map.test_nav111_01_no_corner_cut_override");
            _nav.SetBlocking(map, new[]
            {
                new Rect(new Vec2(0.25, 0), new Vec2(0.5, 0.25)),
                new Rect(new Vec2(-0.25, 0), new Vec2(0, 0.25)),
                new Rect(new Vec2(0, 0.25), new Vec2(0.25, 0.5)),
                new Rect(new Vec2(0, -0.25), new Vec2(0.25, 0)),
            });
            var from = new Vec2(0.125, 0.125);
            var to = new Vec2(0.375, 0.375);
            _nav.BuildNavMesh(map);

            Assert.IsNull(_nav.Raycast(map, from, to),
                "前置条件：直线 Raycast 应恰好只擦过共享墙角单点，不判定为受阻（否则本用例没有覆盖到直线兜底分支）");

            var path = _nav.FindPath(map, from, to);

            Assert.IsNull(path, "直线兜底不应越权放行禁止切角的对角墙角场景");
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
