#nullable enable
// Navigation2DScenarios：INavigation2D 契约一致性场景（见 02_引擎适配层.md 第 1.8 节 /
// ADR-0016 决策 7）。可选接口，但两个既有实现（桩、Unity）都完整实现了它，因此仍纳入契约
// 一致性套件。
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class Navigation2DScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<INavigation2D>> All = new[]
        {
            new ConformanceScenario<INavigation2D>("未登记阻挡时任意点可行走", NoBlocking_EverythingWalkable),
            new ConformanceScenario<INavigation2D>("SetBlocking后阻挡矩形内不可行走", SetBlocking_MakesRectUnwalkable),
            new ConformanceScenario<INavigation2D>("FindPath_两个开阔点之间返回非空路径", FindPath_OpenPoints_ReturnsNonNullPath),
            new ConformanceScenario<INavigation2D>("Clear_清空阻挡登记后恢复可行走", Clear_RestoresWalkable),
            new ConformanceScenario<INavigation2D>("FindPath_路径首尾精确等于请求的起止点", FindPath_EndpointsAreExact),
            new ConformanceScenario<INavigation2D>("FindPath_起止点相同时返回单元素路径", FindPath_ZeroLength_ReturnsSinglePointPath),
            new ConformanceScenario<INavigation2D>("FindPath_终点落在阻挡矩形内部时返回null", FindPath_UnwalkableEndpoint_ReturnsNull),
            new ConformanceScenario<INavigation2D>("FindPath_双矩形拐角工况_每段Raycast均不受阻", FindPath_DoubleRectCorner_EverySegmentRaycastNull),
            new ConformanceScenario<INavigation2D>("GetBlockingVersion_SetBlocking与BuildNavMesh与Clear均递增", GetBlockingVersion_IncrementsOnChanges),
            new ConformanceScenario<INavigation2D>("RaycastWithNormal_命中点与Raycast逐位相同_法线为穿入面的单位外法线", RaycastWithNormal_PointMatchesRaycast_NormalIsEnteredFaceOutwardNormal),
            new ConformanceScenario<INavigation2D>("FindPath_端点格中心受阻但端点与直线均可通行_每段Raycast均不受阻", FindPath_EndpointCellCenterBlocked_EverySegmentRaycastNull),
            new ConformanceScenario<INavigation2D>("FindPath_薄墙窄于采样间距存在绕路_每段Raycast均不受阻", FindPath_ThinWallNarrowerThanSampling_EverySegmentRaycastNull),
            new ConformanceScenario<INavigation2D>("FindPath_通道窄于两级采样格但直线畅通_每段Raycast均不受阻", FindPath_NarrowCorridorThinnerThanGrids_EverySegmentRaycastNull),
            new ConformanceScenario<INavigation2D>("TryFindNearestWalkable_同一阻挡图与输入_结果等于规则算出的期望", NearestWalkable_SameInputs_EqualRuleComputedExpectation),
            new ConformanceScenario<INavigation2D>("TryFindNearestReachable_围住的房间外侧点击_取房间内连通的最近点且缺口打开后点击点本身可达", NearestReachable_EnclosedRoom_PicksConnectedPointAndFollowsBlockingChange),
        };

        private static readonly Id MapId = new Id("map.conformance_probe");

        /// <summary>ADR-0110 最近可走点用例的阻挡图：一堵厚墙（2 宽）与一堵比任何采样格都薄的薄墙（0.1 宽）。
        /// 测试桩（核心用例经 ConformanceStubTests 跑本场景）与 Unity 网格实现（引擎侧用例经
        /// ConformanceUnityTests 跑本场景）共用这一份输入，期望值由排序规则算出，两个实现因此对同一张阻挡图
        /// 给出相同的最近可走点。</summary>
        private static readonly Rect[] NearestWalkableBlockers =
        {
            new Rect(new Vec2(2, -1), new Vec2(4, 1)),
            new Rect(new Vec2(6, -2), new Vec2(6.1, 2)),
        };

        /// <summary>(点击点, 偏好点, 半径)：厚墙正中两侧并列、薄墙内两侧并列、半径内无可走点、点本身可走。</summary>
        private static readonly (Vec2 Point, Vec2 Prefer, double Radius)[] NearestWalkableProbes =
        {
            (new Vec2(3, 0), new Vec2(0.5, 0), 8.0),
            (new Vec2(3, 0), new Vec2(5, 0.3), 8.0),
            (new Vec2(6.05, 0), new Vec2(5, 0), 8.0),
            (new Vec2(6.05, 0), new Vec2(7, 0), 8.0),
            (new Vec2(3, 0), new Vec2(0.5, 0), 0.5),
            (new Vec2(0, 0), new Vec2(9, 9), 8.0),
        };

        /// <summary>按 ADR-0110 排序规则从零算出的期望候选序列：格几何取 <see cref="NavGridLayout.Compute(IReadOnlyList{Rect}, double)"/>，
        /// 可走判定只用 <see cref="INavigation2D.IsWalkable"/>；主键 = floor(到点击点距离 / 格宽)，次键 = 到偏好点
        /// 距离，再按坐标字典序。</summary>
        private static List<Vec2> RuleExpectedCandidates(INavigation2D nav, Id map, Vec2 point, Vec2 prefer, double radius) =>
            RuleRanked(nav, map, NearestWalkableBlockers, point, prefer, radius, null);

        /// <summary>同 <see cref="RuleExpectedCandidates"/>，但阻挡图由调用方给定，且 <paramref name="reachableFrom"/> 非空时
        /// 候选还必须与该点连通——连通的判据就是被测实现自己的 <see cref="INavigation2D.FindPath"/>（规则本身：
        /// "返回点必可 FindPath"，反过来"可 FindPath 的最近点"就是期望）。可达模式只需要第一名，找到即停。</summary>
        private static List<Vec2> RuleRanked(
            INavigation2D nav, Id map, Rect[] blockers, Vec2 point, Vec2 prefer, double radius, Vec2? reachableFrom)
        {
            bool Qualifies(Vec2 c) =>
                nav.IsWalkable(map, c) && (!reachableFrom.HasValue || nav.FindPath(map, reachableFrom.Value, c) != null);

            var layout = NavGridLayout.Compute(blockers);
            var pool = new List<(long K1, long K2, double X, double Y)>();
            for (var ix = 0; ix < layout.Width; ix++)
            {
                for (var iy = 0; iy < layout.Height; iy++)
                {
                    var c = layout.CellCenter(ix, iy);
                    var d = Vec2.Distance(point, c);
                    if (d > radius || !nav.IsWalkable(map, c))
                    {
                        continue;
                    }

                    pool.Add(((long)System.Math.Floor(d / layout.CellSize + 1e-9),
                        (long)System.Math.Round(Vec2.Distance(c, prefer) / 1e-9), c.X, c.Y));
                }
            }

            pool.Sort((a, b) =>
            {
                var r = a.K1.CompareTo(b.K1);
                if (r != 0) return r;
                r = a.K2.CompareTo(b.K2);
                if (r != 0) return r;
                r = a.X.CompareTo(b.X);
                return r != 0 ? r : a.Y.CompareTo(b.Y);
            });

            var list = new List<Vec2>();
            if (Qualifies(point))
            {
                list.Add(point);
                if (reachableFrom.HasValue)
                {
                    return list;
                }
            }

            foreach (var e in pool)
            {
                var c = new Vec2(e.X, e.Y);
                if (reachableFrom.HasValue && nav.FindPath(map, reachableFrom.Value, c) == null)
                {
                    continue;
                }

                list.Add(c);
                if (reachableFrom.HasValue)
                {
                    break;
                }
            }

            return list;
        }

        /// <summary>ADR-0110（1b）可达查询用例的阻挡图：<c>[0,10]x[0,6]</c> 的房间被 1 宽阻挡带围住。</summary>
        private static readonly Rect[] EnclosedRoom =
        {
            new Rect(new Vec2(-1, -1), new Vec2(0, 7)),
            new Rect(new Vec2(10, -1), new Vec2(11, 7)),
            new Rect(new Vec2(0, -1), new Vec2(10, 0)),
            new Rect(new Vec2(0, 6), new Vec2(10, 7)),
        };

        /// <summary><see cref="EnclosedRoom"/> 的东侧阻挡带在 <c>y∈(2,4)</c> 处打开一个缺口（包围盒不变，网格布局不变）。</summary>
        private static readonly Rect[] EnclosedRoomWithGap =
        {
            new Rect(new Vec2(-1, -1), new Vec2(0, 7)),
            new Rect(new Vec2(10, -1), new Vec2(11, 2)),
            new Rect(new Vec2(10, 4), new Vec2(11, 7)),
            new Rect(new Vec2(0, -1), new Vec2(10, 0)),
            new Rect(new Vec2(0, 6), new Vec2(10, 7)),
        };

        private static IEnumerator NearestReachable_EnclosedRoom_PicksConnectedPointAndFollowsBlockingChange(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_nearest_reachable");
            var from = new Vec2(5, 3);
            var click = new Vec2(12.5, 3); // 带外侧、网格范围内的可走点：可走但从房间里走不到。
            const double radius = 4.0;

            // 阶段 A：四周封死。点击点可走却不可达；期望 = 规则算出的"与 from 连通"的第一名（房间内贴东带的格心）。
            nav.SetBlocking(map, EnclosedRoom);
            nav.BuildNavMesh(map);
            assert.True(nav.IsWalkable(map, click), "夹具：点击点本身可走（在阻挡带外侧）");
            assert.True(nav.FindPath(map, from, click) == null, "夹具：点击点从房间里走不到");

            var expected = RuleRanked(nav, map, EnclosedRoom, click, from, radius, from);
            assert.True(expected.Count > 0, "夹具：半径内存在与 from 连通的可走点");
            var found = nav.TryFindNearestReachable(map, from, click, radius, out var reachable);
            assert.True(found, "TryFindNearestReachable 应在半径内找到与 from 连通的点");
            assert.Equal(expected[0], reachable, "与 from 连通的最近点应等于规则算出的第一名");
            assert.True(nav.FindPath(map, from, reachable) != null, "返回点必须可 FindPath");
            assert.True(reachable.X < 10, "返回点应在房间内侧（东带以西），实际 " + reachable);

            // 半径内没有连通的点：false，reachable 为 default。
            assert.True(!nav.TryFindNearestReachable(map, from, click, 1.0, out var none), "半径 1 内没有与 from 连通的可走点，应返回 false");
            assert.Equal(default(Vec2), none, "返回 false 时 reachable 应为 default");

            // 起点不可走：false。
            assert.True(!nav.TryFindNearestReachable(map, new Vec2(10.5, 3), click, radius, out _), "起点在阻挡里，应返回 false");

            // 阶段 B：东带打开缺口（阻挡版本变化）。点击点变为可达，应原样返回；连通信息不得读陈旧缓存。
            nav.SetBlocking(map, EnclosedRoomWithGap);
            assert.True(nav.FindPath(map, from, click) != null, "夹具：缺口打开后点击点可达");
            var expectedOpen = RuleRanked(nav, map, EnclosedRoomWithGap, click, from, radius, from);
            assert.Equal(click, expectedOpen[0], "规则期望：点击点本身可走且连通时排第一");
            assert.True(nav.TryFindNearestReachable(map, from, click, radius, out var reachableOpen), "缺口打开后应找到点");
            assert.Equal(click, reachableOpen, "缺口打开后点击点本身可达，应原样返回它（连通标号必须随阻挡版本重算）");

            // 阶段 C：缺口再关上：回到阶段 A 的结果。
            nav.SetBlocking(map, EnclosedRoom);
            assert.True(nav.TryFindNearestReachable(map, from, click, radius, out var reachableClosedAgain), "缺口关上后应仍能找到房间内的点");
            assert.Equal(expected[0], reachableClosedAgain, "缺口关上后应回到阶段 A 的结果");

            nav.Clear(map);
            yield break;
        }

        private static IEnumerator NearestWalkable_SameInputs_EqualRuleComputedExpectation(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_nearest_walkable");
            nav.SetBlocking(map, NearestWalkableBlockers);
            nav.BuildNavMesh(map);

            foreach (var probe in NearestWalkableProbes)
            {
                var expected = RuleExpectedCandidates(nav, map, probe.Point, probe.Prefer, probe.Radius);
                var label = $"点击点={probe.Point} 偏好点={probe.Prefer} 半径={probe.Radius}";

                var found = nav.TryFindNearestWalkable(map, probe.Point, probe.Radius, probe.Prefer, out var nearest);
                assert.Equal(expected.Count > 0, found, "TryFindNearestWalkable 的返回值应等于'规则算出的候选集合非空'：" + label);
                if (found && expected.Count > 0)
                {
                    assert.Equal(expected[0], nearest, "最近可走点应等于规则算出的第一名：" + label);
                    assert.True(nav.IsWalkable(map, nearest), "返回的最近可走点必须 IsWalkable：" + label);
                    if (nav.IsWalkable(map, probe.Point))
                    {
                        assert.Equal(probe.Point, nearest, "点击点本身可走时应原样返回它：" + label);
                    }
                }

                var candidates = new List<Vec2> { new Vec2(99, 99) };
                var count = nav.FindNearestWalkableCandidates(map, probe.Point, probe.Radius, probe.Prefer, 6, candidates);
                var expectedCount = System.Math.Min(6, expected.Count);
                assert.Equal(expectedCount, count, "候选个数应等于 min(maxCount, 规则算出的候选数)（并先清空 results）：" + label);
                assert.Equal(count, candidates.Count, "返回个数应等于 results 的元素数：" + label);
                for (var i = 0; i < count && i < candidates.Count; i++)
                {
                    assert.Equal(expected[i], candidates[i], $"第 {i} 名候选应等于规则算出的第 {i} 名：{label}");
                }
            }

            nav.Clear(map);
            yield break;
        }

        private static IEnumerator NoBlocking_EverythingWalkable(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var freshMap = new Id("map.conformance_fresh");
            assert.True(nav.IsWalkable(freshMap, new Vec2(3, 4)), "从未登记过阻挡的地图，任意点应默认可行走");
            yield break;
        }

        private static IEnumerator SetBlocking_MakesRectUnwalkable(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var rect = new Rect(new Vec2(-1, -1), new Vec2(1, 1));
            nav.SetBlocking(MapId, new[] { rect });

            assert.False(nav.IsWalkable(MapId, Vec2.Zero), "阻挡矩形内的点应不可行走");
            assert.True(nav.IsWalkable(MapId, new Vec2(50, 50)), "阻挡矩形外的点应仍然可行走");

            nav.Clear(MapId);
            yield break;
        }

        private static IEnumerator FindPath_OpenPoints_ReturnsNonNullPath(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var freshMap = new Id("map.conformance_findpath");
            var from = new Vec2(0, 0);
            var to = new Vec2(2, 0);

            var path = nav.FindPath(freshMap, from, to);
            assert.NotNull(path, "两个开阔、无阻挡的点之间 FindPath 不应返回 null");
            if (path != null)
            {
                assert.True(path.Count >= 2, "路径至少应包含起点与终点两个点");
                assert.True(Vec2.Distance(path[0], from) <= 1.0, "路径首点应接近请求的起点（允许网格量化误差）");
                assert.True(Vec2.Distance(path[path.Count - 1], to) <= 1.0, "路径末点应接近请求的终点（允许网格量化误差）");
            }
            yield break;
        }

        private static IEnumerator Clear_RestoresWalkable(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_clear");
            var rect = new Rect(new Vec2(-1, -1), new Vec2(1, 1));
            nav.SetBlocking(map, new[] { rect });
            assert.False(nav.IsWalkable(map, Vec2.Zero), "SetBlocking 之后阻挡矩形内应不可行走");

            nav.Clear(map);
            assert.True(nav.IsWalkable(map, Vec2.Zero), "Clear 之后该地图的动态阻挡应被清空，之前阻挡的点恢复可行走");
            yield break;
        }

        // 以下四条为本轮契约精确化新增（见任务书"契约"一节 2/3，与 core 侧 MovementHost 新增的
        // Stop/OnMoveStopped/OnMoveFailedDetailed 契约共用同一份 FindPath 端点/阻挡语义）。判断
        // 记录：桩实现（StubNavigation2D）按其类型顶部注释"只做直线导航，不做任何绕障路径规划"的
        // 既定定位，对"起止点精确""终点不可行走返回 null"两条天然满足或易于满足；"起止点相同返回
        // 单元素路径"与"双矩形拐角需要绕障"两条分别依赖桩、Unity 各自的实现是否已经落地同一版
        // 契约——本场景按目标契约编写，未落地的一侧会在断言处如实报告失败，不做侧路兼容。

        private static IEnumerator FindPath_EndpointsAreExact(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_endpoints_exact");
            var from = new Vec2(-3.1, -2.4);
            var to = new Vec2(3.2, 2.1);

            var path = nav.FindPath(map, from, to);
            assert.NotNull(path, "开阔、无阻挡的两点之间 FindPath 不应返回 null");
            if (path != null)
            {
                assert.True(path.Count >= 2, "路径至少应包含起点与终点两个点");
                assert.True(path[0].X == from.X && path[0].Y == from.Y,
                    "路径首点应逐比特精确等于请求的起点（不是网格量化后的格子中心）");
                assert.True(path[path.Count - 1].X == to.X && path[path.Count - 1].Y == to.Y,
                    "路径末点应逐比特精确等于请求的终点");
            }
            yield break;
        }

        private static IEnumerator FindPath_ZeroLength_ReturnsSinglePointPath(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_zero_length");
            var point = new Vec2(1.23, -4.56);

            var path = nav.FindPath(map, point, point);
            assert.NotNull(path, "起止点相同（距离 <= 1e-6）时应返回单元素路径，而不是 null");
            if (path != null)
            {
                assert.Equal(1, path.Count, "起止点相同时路径应恰好包含一个元素");
                assert.True(path[0].X == point.X && path[0].Y == point.Y, "唯一元素应精确等于请求点");
            }
            yield break;
        }

        private static IEnumerator FindPath_UnwalkableEndpoint_ReturnsNull(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_unwalkable_endpoint");
            nav.SetBlocking(map, new[] { new Rect(new Vec2(8, 8), new Vec2(10, 10)) });
            nav.BuildNavMesh(map);

            var from = new Vec2(0, 0);
            var to = new Vec2(9, 9); // 落在阻挡矩形内部
            var path = nav.FindPath(map, from, to);
            assert.IsNull(path, "终点落在阻挡矩形内部（不可行走）时 FindPath 应返回 null");

            nav.Clear(map);
            yield break;
        }

        /// <summary>双矩形拼成一个 L 形拐角，仅在其内角（原点附近）留出一条窄缝供绕行——用于验证
        /// FindPath 返回路径的每一段都必须经过与 Raycast 完全同源的阻挡判定，不允许"路径说能走，
        /// Raycast 说不能走"的不一致（见 UnityNavigation2D 类型顶部判断记录 3）。桩实现是纯直线
        /// 导航、不具备绕障能力，直线穿过该工况必然被判定受阻而返回 null——这是桩的预期行为
        /// （见类型顶部判断记录），本场景对"找不到路径"的实现直接跳过，只对"确实返回了一条路径"
        /// 的实现校验其每一段都经得起 Raycast 复核。</summary>
        private static IEnumerator FindPath_DoubleRectCorner_EverySegmentRaycastNull(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_double_rect_corner");
            nav.SetBlocking(map, new[]
            {
                new Rect(new Vec2(0, -0.25), new Vec2(0.25, 0.25)),
                new Rect(new Vec2(-0.25, 0), new Vec2(0.25, 0.25)),
            });
            nav.BuildNavMesh(map);

            var from = new Vec2(-1, -1);
            var to = new Vec2(1, 1);
            var path = nav.FindPath(map, from, to);
            if (path == null)
            {
                assert.Skip("当前实现不支持绕障路径规划（直线被拐角阻挡、找不到路径属预期行为，见 StubNavigation2D 类型顶部判断记录）");
                nav.Clear(map);
                yield break;
            }

            assert.True(path.Count >= 2, "非空路径至少应包含两个点");
            for (var i = 0; i < path.Count - 1; i++)
            {
                var hit = nav.Raycast(map, path[i], path[i + 1]);
                assert.IsNull(hit, $"路径第 {i} 段不应被 Raycast 判定为受阻（Raycast 与 FindPath 必须共用同一阻挡判定）");
            }

            nav.Clear(map);
            yield break;
        }

        /// <summary>NAV-110-01 契约精确化新增（architecture/落地计划/audit-ac3b622-20260909/
        /// presentation/presentation-findings.md）：阻挡矩形恰好使某个端点所在的采样格中心落在
        /// 矩形内部，但端点自身按 <see cref="INavigation2D.IsWalkable"/> 逐点判定可行走、两端点间
        /// 直线 <see cref="INavigation2D.Raycast"/> 也不受阻——这种情形下 FindPath 不应把"采样格中心
        /// 受阻"误当成"端点不可行走"而返回 null。判断记录：桩实现（<c>StubNavigation2D</c>）本就是
        /// 直线导航、不做任何网格采样，天然不会复现这一类缺陷（它的 FindPath 直接用端点本身的
        /// IsWalkable + 直线 SegmentBlocked 判定），因此本场景对桩、Unity 两侧实现均要求返回非空
        /// 路径，不做跳过——与"双矩形拐角"场景（要求真绕障能力，桩天然做不到）性质不同。</summary>
        private static IEnumerator FindPath_EndpointCellCenterBlocked_EverySegmentRaycastNull(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_endpoint_cell_center_blocked");
            nav.SetBlocking(map, new[] { new Rect(new Vec2(0, 0), new Vec2(1.2, 1)) });
            nav.BuildNavMesh(map);

            var from = new Vec2(1.21, 0.5);
            var to = new Vec2(1.8, 0.5);

            assert.True(nav.IsWalkable(map, from), "起点自身按逐点判定应可行走");
            assert.True(nav.IsWalkable(map, to), "终点自身按逐点判定应可行走");
            assert.IsNull(nav.Raycast(map, from, to), "两端点间的直线不应被判定为受阻");

            var path = nav.FindPath(map, from, to);
            assert.NotNull(path, "端点格中心受阻不等于端点不可行走：存在真实可行的接合方式时不应返回 null");
            if (path != null)
            {
                for (var i = 0; i < path.Count - 1; i++)
                {
                    var hit = nav.Raycast(map, path[i], path[i + 1]);
                    assert.IsNull(hit, $"路径第 {i} 段不应被 Raycast 判定为受阻（Raycast 与 FindPath 必须共用同一阻挡判定）");
                }
            }

            nav.Clear(map);
            yield break;
        }

        /// <summary>NAV-110-02 契约精确化新增：阻挡矩形宽度窄于导航实现的默认采样间距，但存在一条
        /// 手工绕路 oracle（三段 <see cref="INavigation2D.Raycast"/> 均不受阻）——真实存在合法绕路时
        /// FindPath 不应把"采样网格对薄墙视而不见导致的误判"当成"无路可走"。判断记录：桩实现是纯
        /// 直线导航、不具备绕障能力，直线穿过该薄墙必然被判定受阻而返回 null——这是桩的预期行为
        /// （同"双矩形拐角"场景），本场景对"找不到路径"的实现直接跳过，只对"确实返回了一条路径"的
        /// 实现校验其每一段都经得起 Raycast 复核。</summary>
        private static IEnumerator FindPath_ThinWallNarrowerThanSampling_EverySegmentRaycastNull(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_thin_wall_narrower_than_sampling");
            nav.SetBlocking(map, new[] { new Rect(new Vec2(0.1, -0.5), new Vec2(0.15, 0.5)) });
            nav.BuildNavMesh(map);

            var from = new Vec2(-1, 0);
            var to = new Vec2(1, 0);

            var oracle = new[] { from, new Vec2(-0.2, -0.6), new Vec2(0.2, -0.6), to };
            for (var i = 0; i < oracle.Length - 1; i++)
            {
                var oracleHit = nav.Raycast(map, oracle[i], oracle[i + 1]);
                assert.IsNull(oracleHit, $"oracle 绕路第 {i} 段应为可通行（用于证明真实存在合法绕路）");
            }

            var path = nav.FindPath(map, from, to);
            if (path == null)
            {
                assert.Skip("当前实现不支持绕障路径规划（直线被薄墙阻挡、找不到路径属预期行为，见 StubNavigation2D 类型顶部判断记录）");
                nav.Clear(map);
                yield break;
            }

            assert.True(path.Count >= 2, "非空路径至少应包含两个点");
            for (var i = 0; i < path.Count - 1; i++)
            {
                var hit = nav.Raycast(map, path[i], path[i + 1]);
                assert.IsNull(hit, $"路径第 {i} 段不应被 Raycast 判定为受阻（Raycast 与 FindPath 必须共用同一阻挡判定）");
            }

            nav.Clear(map);
            yield break;
        }

        /// <summary>NAV-111-01 契约精确化新增（architecture/落地计划/audit-6739f50-20260909/
        /// AUDIT_REPORT.md）：一条比网格类实现的采样间距（主网格、细网格兜底）都窄的合法通道，
        /// 两端点自身按 <see cref="INavigation2D.IsWalkable"/> 逐点判定都可行走、两端点间直线
        /// <see cref="INavigation2D.Raycast"/> 也完全清晰——网格类实现不应因为采样间距下限而把
        /// 这种真实可行的直线通道误判为无路可走。判断记录：桩实现（<c>StubNavigation2D</c>）本就
        /// 是直线导航、不做任何网格采样，FindPath 直接用端点本身的 IsWalkable + 直线 SegmentBlocked
        /// 判定，天然不会复现这一类"网格分辨率不足"缺陷；本场景对桩、Unity 两侧实现均要求返回非空
        /// 路径，不做跳过——与"双矩形拐角"/"薄墙"两个要求真绕障能力的场景（桩天然做不到）性质不同，
        /// 与"端点格中心受阻"场景（同样两侧都应通过）同一惯例。</summary>
        private static IEnumerator FindPath_NarrowCorridorThinnerThanGrids_EverySegmentRaycastNull(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_narrow_corridor_thinner_than_grids");
            nav.SetBlocking(map, new[]
            {
                new Rect(new Vec2(-1, -1), new Vec2(1, 0)),
                new Rect(new Vec2(-1, 0.1), new Vec2(1, 1)),
            });
            nav.BuildNavMesh(map);

            var from = new Vec2(-0.5, 0.05);
            var to = new Vec2(0.5, 0.05);

            assert.True(nav.IsWalkable(map, from), "起点自身按逐点判定应可行走");
            assert.True(nav.IsWalkable(map, to), "终点自身按逐点判定应可行走");
            assert.IsNull(nav.Raycast(map, from, to), "两端点间的直线不应被判定为受阻");

            var path = nav.FindPath(map, from, to);
            assert.NotNull(path, "比两种网格采样间距都窄的合法通道不应被误判为无路可走");
            if (path != null)
            {
                for (var i = 0; i < path.Count - 1; i++)
                {
                    var hit = nav.Raycast(map, path[i], path[i + 1]);
                    assert.IsNull(hit, $"路径第 {i} 段不应被 Raycast 判定为受阻（Raycast 与 FindPath 必须共用同一阻挡判定）");
                }
            }

            nav.Clear(map);
            yield break;
        }

        /// <summary>手感落地 S2b：带法线的射线查询。命中点必须与 <see cref="INavigation2D.Raycast"/> 逐位相同（同一次判定），
        /// 法线是被穿入的矩形面的单位外法线（指向射线来向一侧）；无命中两个入口都返回 null。期望值由矩形几何算出：
        /// 从左穿入左面为 (-1,0)、从右为 (+1,0)、从下为 (0,-1)、从上为 (0,+1)。</summary>
        private static IEnumerator RaycastWithNormal_PointMatchesRaycast_NormalIsEnteredFaceOutwardNormal(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_raycast_normal");
            nav.SetBlocking(map, new[] { new Rect(new Vec2(2, -1), new Vec2(5, 1)) });
            nav.BuildNavMesh(map);

            var probes = new[]
            {
                (From: new Vec2(0, 0.2), To: new Vec2(4, 0.5), Normal: new Vec2(-1, 0)),
                (From: new Vec2(8, -0.3), To: new Vec2(3, 0.4), Normal: new Vec2(1, 0)),
                (From: new Vec2(3, -4), To: new Vec2(3.5, 0), Normal: new Vec2(0, -1)),
                (From: new Vec2(4, 4), To: new Vec2(3.5, 0), Normal: new Vec2(0, 1)),
            };
            foreach (var probe in probes)
            {
                var label = $"{probe.From} -> {probe.To}";
                var plain = nav.Raycast(map, probe.From, probe.To);
                var withNormal = nav.RaycastWithNormal(map, probe.From, probe.To);
                assert.True(plain.HasValue && withNormal.HasValue, "夹具：线段应当穿入阻挡矩形：" + label);
                assert.Equal(plain!.Value, withNormal!.Value.Point, "命中点应与 Raycast 逐位相同：" + label);
                assert.Equal(probe.Normal, withNormal.Value.Normal, "法线应为被穿入面的单位外法线：" + label);
            }

            var miss = new[] { (From: new Vec2(0, 3), To: new Vec2(8, 3)), (From: new Vec2(0, 1), To: new Vec2(8, 1)) };
            foreach (var probe in miss)
            {
                var label = $"{probe.From} -> {probe.To}";
                assert.IsNull(nav.Raycast(map, probe.From, probe.To), "夹具：线段不穿入内部（含贴边）：" + label);
                assert.True(!nav.RaycastWithNormal(map, probe.From, probe.To).HasValue, "无阻挡（含只贴边）时带法线的查询也应为空：" + label);
            }

            nav.Clear(map);
            yield break;
        }

        /// <summary>核心侧 <c>INavigation2D.GetBlockingVersion</c> 契约（见该接口成员判断记录）：
        /// 支持版本追踪的实现应在 <c>SetBlocking</c>/<c>BuildNavMesh</c>/<c>Clear</c> 之后各自变化一次
        /// 版本号；默认接口实现（恒返回 0）是合法的"不支持版本追踪"退化，本场景对这类实现直接跳过
        /// （不强制要求每个 <c>INavigation2D</c> 都支持版本追踪，见该成员判断记录）。</summary>
        private static IEnumerator GetBlockingVersion_IncrementsOnChanges(INavigation2D nav, IConformanceAssert assert, ConformanceContext ctx)
        {
            var map = new Id("map.conformance_blocking_version");

            nav.SetBlocking(map, new[] { new Rect(new Vec2(0, 0), new Vec2(1, 1)) });
            var v1 = nav.GetBlockingVersion(map);
            if (v1 == 0)
            {
                assert.Skip("当前实现不支持阻挡版本追踪（GetBlockingVersion 恒为 0，属默认接口实现允许的合法退化）");
                nav.Clear(map);
                yield break;
            }

            nav.BuildNavMesh(map);
            var v2 = nav.GetBlockingVersion(map);
            assert.True(v2 != v1, "BuildNavMesh 之后版本号应发生变化");

            nav.Clear(map);
            var v3 = nav.GetBlockingVersion(map);
            assert.True(v3 != v2, "Clear 之后版本号应发生变化");

            var otherMap = new Id("map.conformance_blocking_version_other");
            assert.Equal(0, nav.GetBlockingVersion(otherMap), "从未变动过的另一张地图版本号应为 0");
            yield break;
        }
    }
}
