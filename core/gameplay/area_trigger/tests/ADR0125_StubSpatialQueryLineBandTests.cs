using System;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    /// <summary>
    /// ADR-0125 第三批探针缺陷：<see cref="StubSpatialQuery.QueryShape"/> 对 <see cref="ShapeKind.Line"/> 此前用"点到线段距离
    /// &lt;= width/2 + r"（胶囊，端点处是圆头），而 05 §3.5 与 <see cref="ShapeGeometry.Contains"/> 把线带定义为矩形带
    /// （端面平直）。修前：零半径实体落在端面外侧 &lt;= width/2 处被桩命中、<see cref="ShapeGeometry.Contains"/> 不命中；带半径
    /// 实体在端面外 gap &gt; r 处也被误命中。期望：与矩形带口径一致——端面外的点不命中，端面内命中，带半径实体按
    /// "圆与矩形带相交"判定（gap &lt;= r 命中）。
    /// </summary>
    public sealed class ADR0125_StubSpatialQueryLineBandTests
    {
        private const double Length = 10;
        private const double Width = 4;
        private static readonly Shape Line = Shape.Line(Vec2.Zero, 0, Length, Width);

        private static System.Collections.Generic.IReadOnlyList<Id> Hits(params (string Id, Vec2 Pos, double Radius)[] entries)
        {
            var q = new StubSpatialQuery();
            foreach (var e in entries)
            {
                q.Register(new Id(e.Id), e.Pos, e.Radius);
            }

            return q.QueryShape(Line, QueryFilter.None);
        }

        [Fact]
        public void ZeroRadiusEntity_PastEndFace_IsNotHit_EvenWithinHalfWidth()
        {
            var past = new Vec2(Length + 1.5, 0); // 端面外 1.5，小于半宽 Width/2：胶囊口径会命中
            Assert.False(ShapeGeometry.Contains(Line, past));

            Assert.DoesNotContain(new Id("unit.past_end"), Hits(("unit.past_end", past, 0)));
        }

        [Fact]
        public void ZeroRadiusEntity_BeforeStartFace_IsNotHit()
        {
            var before = new Vec2(-1.5, 0);
            Assert.False(ShapeGeometry.Contains(Line, before));

            Assert.DoesNotContain(new Id("unit.before_start"), Hits(("unit.before_start", before, 0)));
        }

        [Fact]
        public void EntityWithRadius_GapPastEndFaceGreaterThanRadius_IsNotHit()
        {
            const double radius = 0.5;
            const double gap = 2.5;

            Assert.DoesNotContain(new Id("unit.big_past_end"),
                Hits(("unit.big_past_end", new Vec2(Length + gap, 0), radius)));
        }

        [Fact]
        public void EntityWithRadius_GapPastEndFaceAtMostRadius_IsHit()
        {
            const double radius = 0.5;

            var hits = Hits(
                ("unit.touching_end", new Vec2(Length + radius, 0), radius),       // 恰好相切：闭区间命中
                ("unit.overlapping_end", new Vec2(Length + radius / 2, 1), radius));

            Assert.Contains(new Id("unit.touching_end"), hits);
            Assert.Contains(new Id("unit.overlapping_end"), hits);
        }

        [Fact]
        public void EntityWithRadius_BesideSideEdge_FollowsCircleVsBandOverlap()
        {
            const double radius = 0.5;
            var hits = Hits(
                ("unit.side_touching", new Vec2(5, Width / 2 + radius), radius),
                ("unit.side_clear", new Vec2(5, Width / 2 + radius + 0.01), radius));

            Assert.Contains(new Id("unit.side_touching"), hits);
            Assert.DoesNotContain(new Id("unit.side_clear"), hits);
        }

        [Fact]
        public void PointsInsideBandIncludingEndFaces_AreHit()
        {
            var inside = new[]
            {
                new Vec2(0, 0), new Vec2(Length, 0), new Vec2(Length / 2, 0),
                new Vec2(Length, Width / 2), new Vec2(0, -Width / 2), new Vec2(Length - 0.1, Width / 2 - 0.1),
            };
            var entries = inside.Select((p, i) => ($"unit.in{i}", p, 0.0)).ToArray();

            var hits = Hits(entries);

            Assert.Equal(entries.Length, hits.Count);
            foreach (var p in inside)
            {
                Assert.True(ShapeGeometry.Contains(Line, p));
            }
        }

        /// <summary>旋转后的线带同样是矩形带：沿方向轴的端面外不命中（覆盖 Direction != 0 的局部坐标变换）。</summary>
        [Fact]
        public void RotatedLine_PastEndFace_IsNotHit_AndInsideIsHit()
        {
            var origin = new Vec2(-2, 3);
            const double direction = 0.9;
            var line = Shape.Line(origin, direction, Length, Width);
            Vec2 At(double along, double across)
            {
                var cos = Math.Cos(direction);
                var sin = Math.Sin(direction);
                return new Vec2(origin.X + along * cos - across * sin, origin.Y + along * sin + across * cos);
            }

            var q = new StubSpatialQuery();
            q.Register(new Id("unit.inside"), At(Length / 2, 0.5), 0);
            q.Register(new Id("unit.past_end"), At(Length + 1.5, 0), 0);

            var hits = q.QueryShape(line, QueryFilter.None);

            Assert.Contains(new Id("unit.inside"), hits);
            Assert.DoesNotContain(new Id("unit.past_end"), hits);
        }
    }
}
