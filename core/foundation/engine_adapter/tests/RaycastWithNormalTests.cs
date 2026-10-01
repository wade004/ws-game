// RaycastWithNormalTests：INavigation2D.RaycastWithNormal（手感落地 S2b，带碰撞法线的射线查询）的契约覆盖。
// 一类是"复现"——命中点与既有 Raycast 逐位相同、各个面的法线精确等于规则算出的外法线；一类是"不变量"——
// 内角同点命中合并法线、起点在内部给零向量、相切不算命中、默认实现（只会 Raycast 的第三方实现）的近似边界。
using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    public class RaycastWithNormalTests
    {
        private static readonly Id MapId = new Id("map.raycast_normal_test");
        private static readonly double Diag = Math.Sqrt(0.5);

        private static StubNavigation2D Box(double x0, double y0, double x1, double y1)
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(x0, y0), new Vec2(x1, y1)) });
            return nav;
        }

        /// <summary>只实现接口必需成员（Raycast 借用桩），不覆盖 RaycastWithNormal：走接口默认实现的第三方实现。</summary>
        private sealed class RaycastOnlyNavigation : INavigation2D
        {
            public readonly StubNavigation2D Inner = new StubNavigation2D();

            public void BuildNavMesh(Id mapId) => Inner.BuildNavMesh(mapId);

            public bool IsWalkable(Id mapId, Vec2 point) => Inner.IsWalkable(mapId, point);

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to) => Inner.FindPath(mapId, from, to);

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => Inner.Raycast(mapId, from, to);

            public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects) => Inner.SetBlocking(mapId, rects);

            public void Clear(Id mapId) => Inner.Clear(mapId);
        }

        [Fact]
        public void Stub_HitPointIsBitIdenticalToRaycast_AndNoHitIsNullForBoth()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[]
            {
                new Rect(new Vec2(2, -1), new Vec2(3, 1)), new Rect(new Vec2(-4, 2), new Vec2(-1, 5)),
                new Rect(new Vec2(0.5, -6), new Vec2(1.5, -3)),
            });
            var rng = new Random(20261002);
            var hits = 0;
            for (var i = 0; i < 400; i++)
            {
                var from = new Vec2(rng.NextDouble() * 12 - 6, rng.NextDouble() * 12 - 6);
                var to = new Vec2(rng.NextDouble() * 12 - 6, rng.NextDouble() * 12 - 6);
                var plain = nav.Raycast(MapId, from, to);
                var withNormal = nav.RaycastWithNormal(MapId, from, to);
                Assert.Equal(plain.HasValue, withNormal.HasValue);
                if (!plain.HasValue) continue;
                hits++;
                Assert.Equal(BitConverter.DoubleToInt64Bits(plain.Value.X), BitConverter.DoubleToInt64Bits(withNormal!.Value.Point.X));
                Assert.Equal(BitConverter.DoubleToInt64Bits(plain.Value.Y), BitConverter.DoubleToInt64Bits(withNormal.Value.Point.Y));
                // 法线是单位向量，且与射线方向相对（穿入面，不是穿出面）。
                var normal = withNormal.Value.Normal;
                if (!withNormal.Value.HasNormal) continue;
                Assert.InRange(normal.Length, 1.0 - 1e-12, 1.0 + 1e-12);
                Assert.True(normal.Dot(to - from) < 0.0, $"法线应指向射线来向：{normal} 对 {to - from}");
            }

            Assert.True(hits > 20, "夹具：随机线段里要有足够多的命中");
        }

        [Theory]
        [InlineData(-1.0, 0.5, 3.0, 0.5, -1.0, 0.0)] // 从左穿入左面
        [InlineData(6.0, 0.5, 0.0, 0.7, 1.0, 0.0)] // 从右穿入右面
        [InlineData(2.5, -3.0, 2.4, 3.0, 0.0, -1.0)] // 从下穿入下面
        [InlineData(2.5, 4.0, 2.6, -3.0, 0.0, 1.0)] // 从上穿入上面
        [InlineData(0.0, -0.5, 4.0, 1.4, -1.0, 0.0)] // 斜着穿入左面：法线仍是面法线，不随射线方向变
        public void Stub_NormalIsTheOutwardNormalOfTheEnteredFace_WithExactAxisComponents(
            double fx, double fy, double tx, double ty, double nx, double ny)
        {
            var nav = Box(2, -1, 5, 1);
            var hit = nav.RaycastWithNormal(MapId, new Vec2(fx, fy), new Vec2(tx, ty));
            Assert.True(hit.HasValue);
            Assert.Equal(nx, hit!.Value.Normal.X); // 轴向法线分量恰为 ±1/0，不经过归一化运算
            Assert.Equal(ny, hit.Value.Normal.Y);
        }

        [Fact]
        public void Stub_EnteringExactlyThroughACorner_GivesTheDiagonalNormal()
        {
            var nav = Box(2, 1, 3, 2);
            var hit = nav.RaycastWithNormal(MapId, new Vec2(0, 0), new Vec2(4, 2)); // 经过角点 (2,1)
            Assert.True(hit.HasValue);
            Assert.Equal(new Vec2(2, 1), hit!.Value.Point);
            Assert.Equal(-Diag, hit.Value.Normal.X, 12);
            Assert.Equal(-Diag, hit.Value.Normal.Y, 12);
        }

        [Fact]
        public void Stub_TwoRectsHitAtTheSamePoint_MergeTheirNormals_RegardlessOfRegistrationOrder()
        {
            var wallX = new Rect(new Vec2(2, -50), new Vec2(3, 50));
            var wallY = new Rect(new Vec2(-50, 2), new Vec2(50, 3));
            foreach (var rects in new[] { new[] { wallX, wallY }, new[] { wallY, wallX } })
            {
                var nav = new StubNavigation2D();
                nav.SetBlocking(MapId, rects);
                var hit = nav.RaycastWithNormal(MapId, Vec2.Zero, new Vec2(4, 4)); // 恰好打在两面墙的内角 (2,2)
                Assert.True(hit.HasValue);
                Assert.Equal(-Diag, hit!.Value.Normal.X, 12);
                Assert.Equal(-Diag, hit.Value.Normal.Y, 12);
            }

            // 重叠登记（同一面墙被多块矩形覆盖）在同一点命中：相同法线合并后仍恰为 (-1, 0)，不被归一化运算磨掉精度。
            var overlapping = new StubNavigation2D();
            overlapping.SetBlocking(MapId, new[]
            {
                new Rect(new Vec2(2, -2), new Vec2(3, 2)), new Rect(new Vec2(2, -1), new Vec2(4, 3)),
            });
            var through = overlapping.RaycastWithNormal(MapId, new Vec2(0, 0), new Vec2(5, 0.5));
            Assert.Equal(-1.0, through!.Value.Normal.X);
            Assert.Equal(0.0, through.Value.Normal.Y);
        }

        [Fact]
        public void Stub_StartingInsideTheBlocker_HitsAtTheStartWithAZeroNormal()
        {
            var nav = Box(2, -1, 5, 1);
            var hit = nav.RaycastWithNormal(MapId, new Vec2(3, 0), new Vec2(8, 0));
            Assert.True(hit.HasValue);
            Assert.Equal(new Vec2(3, 0), hit!.Value.Point);
            Assert.False(hit.Value.HasNormal);
            Assert.Equal(Vec2.Zero, hit.Value.Normal);
        }

        [Fact]
        public void Stub_TouchingTheBoundaryWithoutEnteringTheInterior_IsNotAHit()
        {
            var nav = Box(2, -1, 5, 1);
            Assert.Null(nav.RaycastWithNormal(MapId, new Vec2(0, 1), new Vec2(8, 1))); // 贴着上边走
            Assert.Null(nav.RaycastWithNormal(MapId, new Vec2(0, 1.5), new Vec2(8, 1.5)));
            Assert.Null(nav.Raycast(MapId, new Vec2(0, 1), new Vec2(8, 1)));
        }

        [Fact]
        public void DefaultImplementation_AxisAlignedWalls_GiveTheFaceNormalAndTheSameHitPoint()
        {
            var nav = new RaycastOnlyNavigation();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(2, -50), new Vec2(3, 50)) });
            foreach (var (from, to, expected) in new[]
            {
                (new Vec2(0, 0), new Vec2(5, 3), new Vec2(-1, 0)),
                (new Vec2(6, -4), new Vec2(0, 2), new Vec2(1, 0)),
            })
            {
                var hit = ((INavigation2D)nav).RaycastWithNormal(MapId, from, to);
                Assert.True(hit.HasValue);
                Assert.Equal(nav.Raycast(MapId, from, to), hit!.Value.Point);
                Assert.Equal(expected, hit.Value.Normal);
            }

            Assert.Null(((INavigation2D)nav).RaycastWithNormal(MapId, new Vec2(0, 0), new Vec2(1, 0)));
        }

        [Fact]
        public void DefaultImplementation_InnerCornerAndDegenerateRay_GiveNoNormal_SoCallersStopInsteadOfGuessing()
        {
            var nav = new RaycastOnlyNavigation();
            nav.SetBlocking(MapId, new[]
            {
                new Rect(new Vec2(2, -50), new Vec2(3, 50)), new Rect(new Vec2(-50, 2), new Vec2(50, 3)),
            });
            var corner = ((INavigation2D)nav).RaycastWithNormal(MapId, Vec2.Zero, new Vec2(4, 4));
            Assert.True(corner.HasValue);
            Assert.False(corner!.Value.HasNormal);
        }

        [Fact]
        public void MergeNormals_SameStaysSame_OppositeCancelToZero_ZeroDefersToTheOther()
        {
            var left = new Vec2(-1, 0);
            Assert.Equal(left, NavRaycastNormals.Merge(left, left));
            Assert.Equal(Vec2.Zero, NavRaycastNormals.Merge(left, new Vec2(1, 0)));
            Assert.Equal(left, NavRaycastNormals.Merge(Vec2.Zero, left));
            Assert.Equal(left, NavRaycastNormals.Merge(left, Vec2.Zero));
            var merged = NavRaycastNormals.Merge(left, new Vec2(0, -1));
            Assert.Equal(1.0, merged.Length, 12);
        }
    }
}
