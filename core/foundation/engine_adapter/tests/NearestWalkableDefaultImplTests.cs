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
    /// </summary>
    public sealed class NearestWalkableDefaultImplTests
    {
        private static readonly Id Map = new Id("map.adr0110_default_impl");

        /// <summary>只实现接口必需成员、不覆盖 ADR-0110 两个默认成员的最小第三方实现。</summary>
        private sealed class RectOnlyNavigation : INavigation2D
        {
            private readonly List<Rect> _rects = new List<Rect>();

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

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to) => null;

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => null;

            public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects)
            {
                _rects.Clear();
                _rects.AddRange(rects);
            }

            public void Clear(Id mapId) => _rects.Clear();
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
    }
}
