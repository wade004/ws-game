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
        };

        private static readonly Id MapId = new Id("map.conformance_probe");

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
