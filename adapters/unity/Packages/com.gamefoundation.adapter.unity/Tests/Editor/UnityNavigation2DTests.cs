#nullable enable
using System;
using System.Reflection;
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

        /// <summary>反射调用私有的 <c>SegmentHasClearContact</c>（ADR-0101 剪枝判定用的严格口径），
        /// 供测试直接断言"剪枝后的段与阻挡矩形完全无接触"，不重复实现一份判定逻辑。</summary>
        private static bool InvokeSegmentHasClearContact(UnityNavigation2D nav, Id mapId, Vec2 a, Vec2 b)
        {
            var method = typeof(UnityNavigation2D).GetMethod(
                "SegmentHasClearContact", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "反射目标 SegmentHasClearContact 应存在（ADR-0101 判断记录）");
            return (bool)method!.Invoke(nav, new object[] { mapId, a, b })!;
        }

        private static double PathLength(System.Collections.Generic.IReadOnlyList<Vec2> pts)
        {
            double total = 0;
            for (var i = 0; i < pts.Count - 1; i++)
            {
                total += Vec2.Distance(pts[i], pts[i + 1]);
            }

            return total;
        }

        /// <summary>线段 [a,b] 是否（在参数 t∈[0,1] 范围内）经过点 p——用于断言剪枝后的段不会贴着
        /// 两块对角相接阻挡的共享墙角抄近路（ADR-0101 判断记录 4）。</summary>
        private static bool SegmentPassesThroughPoint(Vec2 a, Vec2 b, Vec2 p, double eps = 1e-9)
        {
            var abx = b.X - a.X;
            var aby = b.Y - a.Y;
            var apx = p.X - a.X;
            var apy = p.Y - a.Y;
            var cross = abx * apy - aby * apx;
            if (Math.Abs(cross) > eps) return false;

            var dot = apx * abx + apy * aby;
            if (dot < -eps) return false;

            var sqrLen = abx * abx + aby * aby;
            if (dot - sqrLen > eps) return false;

            return true;
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

        // 以下为 ADR-0101（消费方反馈第四十六批）新增：网格寻路结果做视线剪枝（string pulling）。
        // 复现：格长 0.5、开阔地零阻挡时，BuildWorldPath 此前把路径拼成 from + 每个格心（含起点格、
        // 终点格）+ to，中段从不裁剪，导致起步回退、停步朝向错、任意角度目标被拆成来回切向的折线。

        [Test]
        public void SmoothPath_OpenAreaNoObstacleAlongLine_CollapsesToExactlyTwoPoints_AndDisablingSmoothingKeepsMultiplePoints()
        {
            // 两块哨兵阻挡矩形远离两条测试直线，只用来把默认网格边界（无阻挡时硬编码的 [-8,8]
            // + 2 边距）扩大到能同时容纳 (10,5) 这个端点，不影响任何一段路径的可通行性——SetBlocking
            // 一旦登记了矩形，网格边界改由全部矩形的包围盒决定（见 BuildGridWithCellSize），不再是
            // 硬编码的 [-8,8]。
            var map = new Id("map.test_smooth_open_area");
            _nav.SetBlocking(map, new[]
            {
                new Rect(new Vec2(-15, -15), new Vec2(-14.9, -14.8)),
                new Rect(new Vec2(15, 15), new Vec2(15.1, 15.2)),
            });
            _nav.BuildNavMesh(map);

            var from = new Vec2(0, 0);
            var toStraight = new Vec2(3, 0);
            var toDiagonal = new Vec2(10, 5);

            // 阳性对照：关闭剪枝时应保留 A* 的原始格心折线（点数 > 2），证明下面的"恰好 2 点"
            // 确实是剪枝的效果，不是这两条路径本来就是直线。
            _nav.SmoothPaths = false;
            var straightUnsmoothed = _nav.FindPath(map, from, toStraight);
            var diagonalUnsmoothed = _nav.FindPath(map, from, toDiagonal);
            Assert.IsNotNull(straightUnsmoothed, "开阔地应能找到路径");
            Assert.IsNotNull(diagonalUnsmoothed, "开阔地应能找到路径");
            Assert.Greater(straightUnsmoothed!.Count, 2,
                "阳性对照：关闭剪枝时 (0,0)->(3,0) 应保留 A* 原始格心折线，不是天然只有 2 点");
            Assert.Greater(diagonalUnsmoothed!.Count, 2,
                "阳性对照：关闭剪枝时 (0,0)->(10,5) 应保留 A* 原始格心折线，不是天然只有 2 点");

            _nav.SmoothPaths = true;
            var straightSmoothed = _nav.FindPath(map, from, toStraight);
            var diagonalSmoothed = _nav.FindPath(map, from, toDiagonal);

            Assert.IsNotNull(straightSmoothed);
            Assert.AreEqual(2, straightSmoothed!.Count, "开阔地视线剪枝后 (0,0)->(3,0) 应退化为 [from,to] 两点直线");
            Assert.AreEqual(from, straightSmoothed[0]);
            Assert.AreEqual(toStraight, straightSmoothed[1]);

            Assert.IsNotNull(diagonalSmoothed);
            Assert.AreEqual(2, diagonalSmoothed!.Count, "任意角度目标点、直线通畅时 (0,0)->(10,5) 也应退化为两点直线");
            Assert.AreEqual(from, diagonalSmoothed[0]);
            Assert.AreEqual(toDiagonal, diagonalSmoothed[1]);
        }

        [Test]
        public void SmoothPath_DetourAroundWall_ReducesPointsKeepsClearSegmentsAndDoesNotLengthenPath()
        {
            // 一整面墙挡住直线，必须绕行——验证剪枝在"确实需要拐弯"的场景下仍然安全：每一段都经得起
            // Raycast 复核、都与阻挡矩形完全无接触（严格口径），点数与总长都不劣于关闭剪枝时。
            var map = new Id("map.test_smooth_detour_wall");
            _nav.SetBlocking(map, new[] { new Rect(new Vec2(-0.5, -3), new Vec2(0.5, 3)) });
            _nav.BuildNavMesh(map);

            var from = new Vec2(-2, 0);
            var to = new Vec2(2, 0);

            _nav.SmoothPaths = false;
            var unsmoothed = _nav.FindPath(map, from, to);
            Assert.IsNotNull(unsmoothed, "墙两侧都有足够绕行空间，应能找到路径");

            _nav.SmoothPaths = true;
            var smoothed = _nav.FindPath(map, from, to);
            Assert.IsNotNull(smoothed);

            // ① 剪枝后每段 Raycast 为 null。
            for (var i = 0; i < smoothed!.Count - 1; i++)
            {
                Assert.IsNull(_nav.Raycast(map, smoothed[i], smoothed[i + 1]),
                    $"剪枝后第 {i} 段不应被 Raycast 判定为受阻");
            }

            // ② 剪枝后每段与所有阻挡矩形完全无接触（SegmentHasClearContact 的严格口径，不是
            // SegmentBlocked 那种"仅内部受阻、贴边擦角放行"的宽松口径）。
            for (var i = 0; i < smoothed.Count - 1; i++)
            {
                Assert.IsTrue(InvokeSegmentHasClearContact(_nav, map, smoothed[i], smoothed[i + 1]),
                    $"剪枝后第 {i} 段应与全部阻挡矩形完全无接触");
            }

            // ③ 点数不多于关闭剪枝时，总长不长于关闭剪枝时。
            Assert.LessOrEqual(smoothed.Count, unsmoothed!.Count, "剪枝不应增加路点数");
            Assert.LessOrEqual(PathLength(smoothed), PathLength(unsmoothed) + 1e-9, "剪枝不应增加路径总长");

            Assert.AreEqual(from, smoothed[0], "首点应精确等于请求的起点");
            Assert.AreEqual(to, smoothed[smoothed.Count - 1], "末点应精确等于请求的终点");
        }

        [Test]
        public void SmoothPath_DiagonalAdjacentBlocks_DoesNotCutThroughSharedCorner()
        {
            // 复用 FindPath_DiagonalMove_DisallowedWhenBothOrthogonalNeighborsBlocked 的四个阻挡
            // 矩形几何（两两对角相接，共享墙角 (0.25,0.25)），但改用从簇外绕行的端点——该测试原本的
            // 端点困在簇内、彻底无路可走，这里改成从簇外一侧走到另一侧，起终点连线 y=x 恰好在数学
            // 意义上从共享角点 (0.25,0.25) 穿过。断言剪枝后的任何一段都不会贴着这个共享角点抄近路
            // （判断记录：SegmentHasClearContact 用严格口径就是为了防住这一类场景，让"禁止切角"
            // 规则不被剪枝越权架空）。
            var map = new Id("map.test_smooth_diagonal_corner");
            _nav.SetBlocking(map, new[]
            {
                new Rect(new Vec2(0.25, 0), new Vec2(0.5, 0.25)),
                new Rect(new Vec2(-0.25, 0), new Vec2(0, 0.25)),
                new Rect(new Vec2(0, 0.25), new Vec2(0.25, 0.5)),
                new Rect(new Vec2(0, -0.25), new Vec2(0.25, 0)),
            });
            _nav.BuildNavMesh(map);

            var from = new Vec2(-1, -1);
            var to = new Vec2(1, 1);
            var sharedCorner = new Vec2(0.25, 0.25);

            Assert.IsFalse(InvokeSegmentHasClearContact(_nav, map, from, to),
                "前置条件：起终点直线应恰好擦过共享墙角、判定为有接触，否则本用例没有覆盖到收紧口径的分支");

            var path = _nav.FindPath(map, from, to);

            Assert.IsNotNull(path, "从簇外绕行应能找到路径（不同于把中心格困死、彻底无路可走的禁止切角场景）");
            for (var i = 0; i < path!.Count - 1; i++)
            {
                Assert.IsFalse(SegmentPassesThroughPoint(path[i], path[i + 1], sharedCorner),
                    $"剪枝后第 {i} 段不应贴着共享墙角 {sharedCorner} 抄近路");
            }
        }
    }
}
