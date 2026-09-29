using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    /// <summary>
    /// ADR-0110：未覆盖最近可走点成员的第三方 <see cref="INavigation2D"/> 实现走默认接口实现（只用
    /// <see cref="INavigation2D.IsWalkable"/> 做同心环采样）。本用例锁住默认实现的可观测契约：返回点必可走、
    /// 落在半径内、点本身可走时原样返回、并列时取偏好点一侧、距离与网格精确实现（桩）至多相差一环步长；
    /// 范围内无可走点返回 false。桩/Unity 网格实现与规则期望的逐点一致由
    /// <c>adapters/conformance</c> 的 <c>Navigation2DScenarios</c> 场景共用同一组输入负责。
    /// <para>
    /// 可达查询（<see cref="INavigation2D.TryFindNearestReachable"/>）的默认实现同样是近似：取前
    /// <see cref="NearestWalkableSearch.ReachableProbeLimit"/> 个候选逐个 <c>FindPath</c> 试探。第二条用例锁住它的
    /// 两面：返回点必可 <c>FindPath</c>；前 64 个候选都在不可达一侧时返回 false（精确实现能找到更远的可达点）。
    /// </para>
    /// </summary>
    public sealed class NearestWalkableDefaultImplTests
    {
        private static readonly Id Map = new Id("map.adr0110_default_impl");

        /// <summary>只实现接口必需成员、不覆盖 ADR-0110 三个默认成员的最小第三方实现（<c>FindPath</c> 借用桩的
        /// 直线判定，使"连通"有意义）。</summary>
        private sealed class RectOnlyNavigation : INavigation2D
        {
            private readonly List<Rect> _rects = new List<Rect>();
            private readonly StubNavigation2D _pathing = new StubNavigation2D();

            public void BuildNavMesh(Id mapId) { }

            public bool IsWalkable(Id mapId, Vec2 point)
            {
                foreach (var r in _rects)
                {
                    if (point.X >= r.Min.X && point.X <= r.Max.X && point.Y >= r.Min.Y && point.Y <= r.Max.Y)
                    {
                        return false;
                    }
                }

                return true;
            }

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to) => _pathing.FindPath(mapId, from, to);

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => null;

            public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects)
            {
                _rects.Clear();
                _rects.AddRange(rects);
                _pathing.SetBlocking(mapId, rects);
            }

            public void Clear(Id mapId)
            {
                _rects.Clear();
                _pathing.Clear(mapId);
            }
        }

        [Fact]
        public void DefaultInterfaceImpl_RingSampling_SatisfiesContract_AndStaysWithinOneStepOfGridExactStub()
        {
            var wall = new[] { new Rect(new Vec2(2, -1), new Vec2(4, 1)) };
            INavigation2D defaultImpl = new RectOnlyNavigation();
            defaultImpl.SetBlocking(Map, wall);
            INavigation2D stub = new StubNavigation2D();
            stub.SetBlocking(Map, wall);

            var click = new Vec2(3, 0);
            var prefer = new Vec2(0.5, 0);

            Assert.True(defaultImpl.TryFindNearestWalkable(Map, click, 8.0, prefer, out var found));
            Assert.True(defaultImpl.IsWalkable(Map, found));
            Assert.True(Vec2.Distance(click, found) <= 8.0);
            Assert.True(found.X < 2, $"并列时应取偏好点一侧，实际 {found}");

            Assert.True(stub.TryFindNearestWalkable(Map, click, 8.0, prefer, out var exact));
            Assert.True(
                Vec2.Distance(click, found) <= Vec2.Distance(click, exact) + NearestWalkableSearch.SampledStep,
                $"默认实现的距离 {Vec2.Distance(click, found)} 不应比网格精确实现 {Vec2.Distance(click, exact)} 差超过一环步长");

            // 点本身可走：原样返回；候选列表第一名同样是它，且先清空 results。
            var open = new Vec2(0, 0);
            Assert.True(defaultImpl.TryFindNearestWalkable(Map, open, 8.0, prefer, out var self));
            Assert.Equal(open, self);
            var list = new List<Vec2> { new Vec2(99, 99) };
            var count = defaultImpl.FindNearestWalkableCandidates(Map, click, 8.0, prefer, 5, list);
            Assert.Equal(5, count);
            Assert.Equal(5, list.Count);
            Assert.Equal(found, list[0]);
            Assert.All(list, p => Assert.True(defaultImpl.IsWalkable(Map, p)));

            // 半径内没有可走点：false，walkable 为 default。
            Assert.False(defaultImpl.TryFindNearestWalkable(Map, click, 0.5, prefer, out var none));
            Assert.Equal(default(Vec2), none);
        }

        [Fact]
        public void DefaultInterfaceImpl_TryFindNearestReachable_ReturnsPathableUnitSidePoint_ButGivesUpWhenFirst64CandidatesAreUnreachable()
        {
            // 小隔间（0.5x0.5，封死）紧贴点击点：默认实现按候选逐个试探，前几个在隔间里、FindPath 失败，最终取到单位一侧的点。
            var pocket = new[]
            {
                new Rect(new Vec2(2, -1), new Vec2(2.75, 1)),
                new Rect(new Vec2(3.25, -1), new Vec2(4, 1)),
                new Rect(new Vec2(2.75, -1), new Vec2(3.25, -0.25)),
                new Rect(new Vec2(2.75, 0.25), new Vec2(3.25, 1)),
            };
            INavigation2D defaultImpl = new RectOnlyNavigation();
            defaultImpl.SetBlocking(Map, pocket);
            var from = new Vec2(0.5, 0);
            var click = new Vec2(2.7, 0);

            Assert.True(defaultImpl.TryFindNearestReachable(Map, from, click, 8.0, out var reachable));
            Assert.NotNull(defaultImpl.FindPath(Map, from, reachable));
            Assert.True(reachable.X < 2, $"应取单位所在一侧的点，实际 {reachable}");

            // 点击点落在一个大封闭房间正中：房间内可走候选远多于 64 个且全部不可达，默认实现找不到房间外的
            // 可达点（近似的代价）；精确实现（桩）能在半径内找到房间外贴墙的可达点。
            var room = new[]
            {
                new Rect(new Vec2(-2, -2), new Vec2(-1.5, 2)),
                new Rect(new Vec2(1.5, -2), new Vec2(2, 2)),
                new Rect(new Vec2(-1.5, -2), new Vec2(1.5, -1.5)),
                new Rect(new Vec2(-1.5, 1.5), new Vec2(1.5, 2)),
            };
            defaultImpl.SetBlocking(Map, room);
            INavigation2D stub = new StubNavigation2D();
            stub.SetBlocking(Map, room);
            var outside = new Vec2(-4, 0);
            var center = new Vec2(0, 0);

            Assert.True(defaultImpl.IsWalkable(Map, center));
            Assert.Null(defaultImpl.FindPath(Map, outside, center));
            Assert.False(defaultImpl.TryFindNearestReachable(Map, outside, center, 8.0, out var none));
            Assert.Equal(default(Vec2), none);

            Assert.True(stub.TryFindNearestReachable(Map, outside, center, 8.0, out var exact));
            Assert.NotNull(stub.FindPath(Map, outside, exact));
            Assert.True(exact.X < -2, $"精确实现应给出房间外贴墙的可达点，实际 {exact}");
        }
    }
}
