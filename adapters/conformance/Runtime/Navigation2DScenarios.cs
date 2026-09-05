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
    }
}
