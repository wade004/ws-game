#nullable enable
// SpatialQueryScenarios：ISpatialQuery 契约一致性场景（见 02_引擎适配层.md 第 1.9 节 /
// ADR-0016 决策 7）。
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class SpatialQueryScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<ISpatialQuery>> All = new[]
        {
            new ConformanceScenario<ISpatialQuery>("Register后QueryRadius能查到自己", Register_ThenQueryRadius_FindsSelf),
            new ConformanceScenario<ISpatialQuery>("Unregister后不再出现在查询结果中", Unregister_RemovesFromResults),
            new ConformanceScenario<ISpatialQuery>("QueryFilter_必须标签与排除标签生效", QueryFilter_RequiredAndExcludedTags),
            new ConformanceScenario<ISpatialQuery>("Nearest返回最近对象_Clear后为空", Nearest_FindsClosest_ClearEmptiesIndex),
            new ConformanceScenario<ISpatialQuery>("QueryCone_扇形范围内命中范围外不命中", QueryCone_FindsWithinArc_ExcludesOutside),
            new ConformanceScenario<ISpatialQuery>("QueryLine_线段附近命中远处不命中", QueryLine_FindsNearSegment_ExcludesFar),
        };

        private static IEnumerator Register_ThenQueryRadius_FindsSelf(ISpatialQuery query, IConformanceAssert assert, ConformanceContext ctx)
        {
            var id = new Id("obj.conformance_register_probe");
            query.Register(id, new Vec2(10, 10), radius: 0.5, tags: System.Array.Empty<string>());

            var results = query.QueryRadius(new Vec2(10, 10), radius: 1.0, filter: QueryFilter.None);
            assert.True(Contains(results, id), "刚登记的对象应出现在覆盖其位置的半径查询结果中");

            query.Unregister(id);
            yield break;
        }

        private static IEnumerator Unregister_RemovesFromResults(ISpatialQuery query, IConformanceAssert assert, ConformanceContext ctx)
        {
            var id = new Id("obj.conformance_unregister_probe");
            query.Register(id, new Vec2(20, 20), radius: 0.5, tags: System.Array.Empty<string>());
            query.Unregister(id);

            var results = query.QueryRadius(new Vec2(20, 20), radius: 5.0, filter: QueryFilter.None);
            assert.False(Contains(results, id), "Unregister 之后的对象不应再出现在查询结果中");
            yield break;
        }

        private static IEnumerator QueryFilter_RequiredAndExcludedTags(ISpatialQuery query, IConformanceAssert assert, ConformanceContext ctx)
        {
            var friendId = new Id("obj.conformance_friend_probe");
            var enemyId = new Id("obj.conformance_enemy_probe");
            var center = new Vec2(30, 30);
            query.Register(friendId, center, radius: 0.1, tags: new[] { "faction_friend" });
            query.Register(enemyId, center, radius: 0.1, tags: new[] { "faction_enemy" });

            var requiredFriend = query.QueryRadius(center, 1.0, new QueryFilter(requiredTags: new[] { "faction_friend" }));
            assert.True(Contains(requiredFriend, friendId), "RequiredTags 命中的对象应出现在结果中");
            assert.False(Contains(requiredFriend, enemyId), "RequiredTags 未命中的对象不应出现在结果中");

            var excludeEnemy = query.QueryRadius(center, 1.0, new QueryFilter(excludedTags: new[] { "faction_enemy" }));
            assert.True(Contains(excludeEnemy, friendId), "ExcludedTags 未命中（不含排除标签）的对象应出现在结果中");
            assert.False(Contains(excludeEnemy, enemyId), "ExcludedTags 命中（含排除标签）的对象不应出现在结果中");

            query.Unregister(friendId);
            query.Unregister(enemyId);
            yield break;
        }

        private static IEnumerator Nearest_FindsClosest_ClearEmptiesIndex(ISpatialQuery query, IConformanceAssert assert, ConformanceContext ctx)
        {
            var nearId = new Id("obj.conformance_near_probe");
            var farId = new Id("obj.conformance_far_probe");
            query.Register(nearId, new Vec2(1, 0), radius: 0.1, tags: System.Array.Empty<string>());
            query.Register(farId, new Vec2(100, 0), radius: 0.1, tags: System.Array.Empty<string>());

            var nearest = query.Nearest(Vec2.Zero, QueryFilter.None);
            assert.NotNull(nearest, "有已登记对象时 Nearest 不应返回 null");
            assert.Equal(nearId, nearest!.Value, "Nearest 应返回距离查询点更近的那个对象");

            query.Clear();
            var afterClear = query.QueryRadius(Vec2.Zero, 1000.0, QueryFilter.None);
            assert.Equal(0, afterClear.Count, "Clear 之后半径覆盖全图的查询应不返回任何对象");
            yield break;
        }

        /// <summary>W3b 审计发现补齐：<see cref="ISpatialQuery.QueryCone"/> 此前未被任何场景覆盖。
        /// <c>direction</c>/<c>angle</c> 按弧度处理（见 <c>Adapters.Stub.StubSpatialQuery</c> 顶部
        /// 判断记录"文档未注明单位，由调用方与实现方自行约定一致"），<c>angle</c> 是锥形的完整张角
        /// （半角 = angle/2，同该类型 <c>IsInCone</c> 判断记录）。本场景固定朝 +X 方向（direction=0）、
        /// 90° 全张角，验证"轴线上、射程内"命中，"轴线上但超出射程"与"90° 偏轴（超出半角）"均不命中——
        /// 后两者分别独立验证 range 与 angle 两个维度的裁剪，不依赖具体实现的内部几何算法一致，只
        /// 断言契约描述的边界行为。</summary>
        private static IEnumerator QueryCone_FindsWithinArc_ExcludesOutside(ISpatialQuery query, IConformanceAssert assert, ConformanceContext ctx)
        {
            var origin = new Vec2(40, 40);
            var inConeId = new Id("obj.conformance_cone_in");
            var outOfRangeId = new Id("obj.conformance_cone_out_of_range");
            var outOfAngleId = new Id("obj.conformance_cone_out_of_angle");

            query.Register(inConeId, origin + new Vec2(5, 0), radius: 0.1, tags: System.Array.Empty<string>());
            query.Register(outOfRangeId, origin + new Vec2(20, 0), radius: 0.1, tags: System.Array.Empty<string>());
            query.Register(outOfAngleId, origin + new Vec2(0, 5), radius: 0.1, tags: System.Array.Empty<string>());

            var results = query.QueryCone(origin, direction: 0.0, angle: System.Math.PI / 2.0, range: 10.0, filter: QueryFilter.None);

            assert.True(Contains(results, inConeId), "轴线上、射程内的对象应当命中");
            assert.False(Contains(results, outOfRangeId), "轴线上但超出射程的对象不应命中");
            assert.False(Contains(results, outOfAngleId), "90 度偏轴（超出 90 度全张角的一半）的对象不应命中");

            query.Unregister(inConeId);
            query.Unregister(outOfRangeId);
            query.Unregister(outOfAngleId);
            yield break;
        }

        /// <summary>W3b 审计发现补齐：<see cref="ISpatialQuery.QueryLine"/> 此前未被任何场景覆盖——
        /// 验证"线段附近（在对象自身注册半径内）命中，远处不命中"这一最小契约行为，不假设具体的
        /// 点到线段距离算法实现细节。</summary>
        private static IEnumerator QueryLine_FindsNearSegment_ExcludesFar(ISpatialQuery query, IConformanceAssert assert, ConformanceContext ctx)
        {
            var from = new Vec2(60, 60);
            var to = new Vec2(70, 60);
            var nearId = new Id("obj.conformance_line_near");
            var farId = new Id("obj.conformance_line_far");

            query.Register(nearId, new Vec2(65, 60.05), radius: 0.5, tags: System.Array.Empty<string>());
            query.Register(farId, new Vec2(65, 65), radius: 0.5, tags: System.Array.Empty<string>());

            var results = query.QueryLine(from, to, QueryFilter.None);

            assert.True(Contains(results, nearId), "紧贴线段（在自身注册半径内）的对象应当命中");
            assert.False(Contains(results, farId), "远离线段（超出自身注册半径）的对象不应命中");

            query.Unregister(nearId);
            query.Unregister(farId);
            yield break;
        }

        private static bool Contains(IReadOnlyList<Id> results, Id id)
        {
            for (var i = 0; i < results.Count; i++)
            {
                if (results[i].Equals(id)) return true;
            }
            return false;
        }
    }
}
