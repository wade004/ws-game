using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Gameplay.AreaTrigger;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    /// <summary>
    /// T-H12（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节；11 章 §6 勘误②"几何边界值须与非边界使用同一套判定口径"）：
    /// "点是否落在形状内"的三份同一几何实现——<see cref="ShapeGeometry"/>（L-1 公开）、
    /// <see cref="AreaTriggerShapeGeometry"/>（L4 公开）、<c>Core.Gameplay.Encounter.EncounterShapeMath</c>
    /// （L4 <c>internal</c>，全仓无 InternalsVisibleTo，这里经反射取同名 <c>Contains</c> 静态方法，不改生产代码）——
    /// 在恰好落在边界上的点必须给出相同判定；并把 <see cref="StubSpatialQuery"/> 的范围查询与
    /// <see cref="ShapeGeometry.Contains"/> 的口径钉在一起。
    /// <para>
    /// <b>口径（写在用例里，不另立文档）</b>：所有边界<b>含</b>（闭区间）——
    /// 圆：<c>distance &lt;= radius</c>；矩形：局部坐标 <c>|x| &lt;= halfX</c> 且 <c>|y| &lt;= halfY</c>（边上与角上都算内）；
    /// 扇形：<c>distance &lt;= radius</c> 且 <c>|angleDelta| &lt;= angle/2</c>，顶点（<c>distance &lt; 1e-9</c>）恒为内；
    /// 线带：局部 <c>0 &lt;= x &lt;= length</c> 且 <c>|y| &lt;= width/2</c>（起点与终点端面都算内）；
    /// 旋转形状的角点/边点由浮点旋转决定，见下。
    /// </para>
    /// <para>
    /// <b>期望值怎么来</b>：用例里的边界点全部由形状参数算出（半径 × 方向、局部坐标经旋转矩阵回到世界坐标、
    /// 半张角由 direction ± angle/2 算出），不写裸坐标。分两类断言——① 能在浮点下精确表示的边界点（轴对齐、
    /// 3-4-5 整数三角形、旋转 90 度的轴上点……）期望"含"；② 经任意角度旋转、边界是否被含取决于浮点舍入的点，
    /// 只断言三份实现两两一致（它们的算术表达式逐位相同，必须给出同一结果），另给这些边界沿法向内移 / 外移 1e-9
    /// 的点，期望"内 / 外"（远大于舍入误差，判定无歧义）。
    /// </para>
    /// </summary>
    public sealed class T_H12_ShapeGeometryBoundaryConsistencyTests
    {
        private const int Inside = 1;
        private const int Outside = 0;
        private const int AgreementOnly = -1;

        /// <summary>沿法向内移 / 外移的量：远大于双精度舍入（约 1e-15），远小于形状尺度。</summary>
        private const double Eps = 1e-9;

        private sealed class Case
        {
            public string Name = "";
            public Shape Shape;
            public Vec2 Point;
            public int Expected;
        }

        private static readonly Dictionary<string, Case> Cases = BuildCases();

        public static IEnumerable<object[]> CaseNames() => Cases.Keys.Select(k => new object[] { k });

        private static readonly Func<Shape, Vec2, bool> EncounterContains = ResolveEncounterContains();

        private static Func<Shape, Vec2, bool> ResolveEncounterContains()
        {
            var type = typeof(AreaTriggerShapeGeometry).Assembly.GetType("Core.Gameplay.Encounter.EncounterShapeMath", throwOnError: true)!;
            var method = type.GetMethod("Contains", new[] { typeof(Shape), typeof(Vec2) })!;
            return (Func<Shape, Vec2, bool>)Delegate.CreateDelegate(typeof(Func<Shape, Vec2, bool>), method);
        }

        private static Vec2 Rotate(Vec2 v, double angle) =>
            new Vec2(v.X * Math.Cos(angle) - v.Y * Math.Sin(angle), v.X * Math.Sin(angle) + v.Y * Math.Cos(angle));

        private static Vec2 FromPolar(Vec2 origin, double angle, double distance) =>
            new Vec2(origin.X + distance * Math.Cos(angle), origin.Y + distance * Math.Sin(angle));

        private static Dictionary<string, Case> BuildCases()
        {
            var cases = new Dictionary<string, Case>();
            void Add(string name, Shape shape, Vec2 point, int expected) =>
                cases.Add(name, new Case { Name = name, Shape = shape, Point = point, Expected = expected });

            // ---------------- 圆：恰在半径上（含） ----------------
            var c = new Vec2(2, -1);
            const double r = 5;
            var circle = Shape.Circle(c, r);
            Add("circle/on_radius_east", circle, new Vec2(c.X + r, c.Y), Inside);
            Add("circle/on_radius_north", circle, new Vec2(c.X, c.Y + r), Inside);
            Add("circle/on_radius_west", circle, new Vec2(c.X - r, c.Y), Inside);
            Add("circle/on_radius_3_4_5", circle, new Vec2(c.X + 3, c.Y + 4), Inside); // 3²+4²=5²，sqrt(25)=5 精确
            Add("circle/on_radius_3_4_5_negative", circle, new Vec2(c.X - 3, c.Y - 4), Inside);
            Add("circle/just_inside_radius", circle, new Vec2(c.X + r - Eps, c.Y), Inside);
            Add("circle/just_outside_radius", circle, new Vec2(c.X + r + Eps, c.Y), Outside);
            Add("circle/center", circle, c, Inside);
            foreach (var theta in new[] { 0.3, 1.0, 2.5, -2.0 })
            {
                Add($"circle/on_radius_theta_{theta}", circle, FromPolar(c, theta, r), AgreementOnly);
                Add($"circle/inside_radius_theta_{theta}", circle, FromPolar(c, theta, r - Eps), Inside);
                Add($"circle/outside_radius_theta_{theta}", circle, FromPolar(c, theta, r + Eps), Outside);
            }

            var zeroCircle = Shape.Circle(c, 0);
            Add("circle/zero_radius_at_center", zeroCircle, c, Inside);
            Add("circle/zero_radius_off_center", zeroCircle, new Vec2(c.X + Eps, c.Y), Outside);

            // ---------------- 矩形（轴对齐）：边上与角上 ----------------
            var ro = new Vec2(1, 2);
            var half = new Vec2(3, 1.5);
            var rect = Shape.Rect(ro, half, 0);
            Add("rect/edge_right_mid", rect, new Vec2(ro.X + half.X, ro.Y), Inside);
            Add("rect/edge_left_mid", rect, new Vec2(ro.X - half.X, ro.Y), Inside);
            Add("rect/edge_top_mid", rect, new Vec2(ro.X, ro.Y + half.Y), Inside);
            Add("rect/edge_bottom_mid", rect, new Vec2(ro.X, ro.Y - half.Y), Inside);
            Add("rect/corner_top_right", rect, new Vec2(ro.X + half.X, ro.Y + half.Y), Inside);
            Add("rect/corner_bottom_left", rect, new Vec2(ro.X - half.X, ro.Y - half.Y), Inside);
            Add("rect/just_outside_right", rect, new Vec2(ro.X + half.X + Eps, ro.Y), Outside);
            Add("rect/just_outside_top", rect, new Vec2(ro.X, ro.Y + half.Y + Eps), Outside);
            Add("rect/just_outside_corner", rect, new Vec2(ro.X + half.X + Eps, ro.Y + half.Y + Eps), Outside);
            Add("rect/just_inside_corner", rect, new Vec2(ro.X + half.X - Eps, ro.Y + half.Y - Eps), Inside);
            Add("rect/zero_extent_at_origin", Shape.Rect(ro, Vec2.Zero, 0), ro, Inside);
            Add("rect/zero_extent_off_origin", Shape.Rect(ro, Vec2.Zero, 0), new Vec2(ro.X + Eps, ro.Y), Outside);

            // ---------------- 矩形（旋转 90 度，原点在世界原点）：局部 X 轴 = 世界 +Y ----------------
            var rect90 = Shape.Rect(Vec2.Zero, new Vec2(3, 1), Math.PI / 2);
            Add("rect90/edge_along_axis_top", rect90, new Vec2(0, 3), Inside);
            Add("rect90/edge_along_axis_bottom", rect90, new Vec2(0, -3), Inside);
            Add("rect90/edge_across_axis_right", rect90, new Vec2(1, 0), Inside);
            Add("rect90/edge_across_axis_left", rect90, new Vec2(-1, 0), Inside);
            Add("rect90/just_outside_along_axis", rect90, new Vec2(0, 3 + Eps), Outside);
            Add("rect90/just_outside_across_axis", rect90, new Vec2(1 + Eps, 0), Outside);
            Add("rect90/unrotated_extent_is_outside", rect90, new Vec2(3, 0), Outside);
            // 世界角点：cos(π/2)≈6e-17 的舍入决定是否被含——只比较三份实现。
            Add("rect90/corner_ne", rect90, new Vec2(1, 3), AgreementOnly);
            Add("rect90/corner_nw", rect90, new Vec2(-1, 3), AgreementOnly);
            Add("rect90/corner_se", rect90, new Vec2(1, -3), AgreementOnly);
            Add("rect90/corner_sw", rect90, new Vec2(-1, -3), AgreementOnly);

            // ---------------- 矩形（任意角度旋转）：边点 / 角点由局部坐标回算 ----------------
            var rot = 0.6;
            var rro = new Vec2(-3, 4);
            var rhalf = new Vec2(2, 1);
            var rotRect = Shape.Rect(rro, rhalf, rot);
            Vec2 W(double lx, double ly) { var o = Rotate(new Vec2(lx, ly), rot); return new Vec2(rro.X + o.X, rro.Y + o.Y); }
            Add("rect_rot/edge_x_on", rotRect, W(rhalf.X, 0.3), AgreementOnly);
            Add("rect_rot/edge_x_inside", rotRect, W(rhalf.X - Eps, 0.3), Inside);
            Add("rect_rot/edge_x_outside", rotRect, W(rhalf.X + Eps, 0.3), Outside);
            Add("rect_rot/edge_y_on", rotRect, W(-0.4, rhalf.Y), AgreementOnly);
            Add("rect_rot/edge_y_inside", rotRect, W(-0.4, rhalf.Y - Eps), Inside);
            Add("rect_rot/edge_y_outside", rotRect, W(-0.4, rhalf.Y + Eps), Outside);
            Add("rect_rot/corner_on", rotRect, W(rhalf.X, rhalf.Y), AgreementOnly);
            Add("rect_rot/corner_inside", rotRect, W(rhalf.X - Eps, rhalf.Y - Eps), Inside);
            Add("rect_rot/corner_outside", rotRect, W(rhalf.X + Eps, rhalf.Y + Eps), Outside);
            Add("rect_rot/corner_neg_on", rotRect, W(-rhalf.X, -rhalf.Y), AgreementOnly);
            Add("rect_rot/center", rotRect, rro, Inside);

            // ---------------- 扇形：半径上 / 半角上 / 顶点 / 接缝 ----------------
            var co = new Vec2(1, 1);
            var cone = Shape.Cone(co, 0, Math.PI / 2, 10); // 朝 +X，全张角 90 度，半角 45 度
            Add("cone/on_radius_axis", cone, new Vec2(co.X + 10, co.Y), Inside);
            Add("cone/just_beyond_radius", cone, new Vec2(co.X + 10 + Eps, co.Y), Outside);
            Add("cone/on_half_angle_upper", cone, new Vec2(co.X + 4, co.Y + 4), Inside); // atan2(4,4)==π/4==angle/2
            Add("cone/on_half_angle_lower", cone, new Vec2(co.X + 4, co.Y - 4), Inside);
            Add("cone/apex", cone, co, Inside);
            Add("cone/apex_tolerance_behind_inside", cone, new Vec2(co.X - 5e-10, co.Y), Inside); // distance<1e-9 视为顶点
            Add("cone/behind_beyond_apex_tolerance", cone, new Vec2(co.X - 2e-9, co.Y), Outside);
            Add("cone/radius_and_half_angle_corner", cone, FromPolar(co, Math.PI / 4, 10), AgreementOnly);
            Add("cone/just_inside_half_angle", cone, FromPolar(co, Math.PI / 4 - Eps, 5), Inside);
            Add("cone/just_outside_half_angle", cone, FromPolar(co, Math.PI / 4 + Eps, 5), Outside);

            var dir = 1.0;
            var ang = 0.8;
            var oblique = Shape.Cone(co, dir, ang, 7);
            Add("cone_oblique/on_upper_edge", oblique, FromPolar(co, dir + ang / 2, 4), AgreementOnly);
            Add("cone_oblique/on_lower_edge", oblique, FromPolar(co, dir - ang / 2, 4), AgreementOnly);
            Add("cone_oblique/inside_upper_edge", oblique, FromPolar(co, dir + ang / 2 - Eps, 4), Inside);
            Add("cone_oblique/outside_upper_edge", oblique, FromPolar(co, dir + ang / 2 + Eps, 4), Outside);
            Add("cone_oblique/inside_lower_edge", oblique, FromPolar(co, dir - ang / 2 + Eps, 4), Inside);
            Add("cone_oblique/outside_lower_edge", oblique, FromPolar(co, dir - ang / 2 - Eps, 4), Outside);
            Add("cone_oblique/on_radius_on_axis", oblique, FromPolar(co, dir, 7), AgreementOnly);
            Add("cone_oblique/just_beyond_radius", oblique, FromPolar(co, dir, 7 + Eps), Outside);

            // ±π 接缝：direction 靠近 π，张角跨过接缝；点的 atan2 落在 -π 一侧。
            var seam = Shape.Cone(Vec2.Zero, Math.PI - 0.1, 0.4, 10);
            Add("cone_seam/inside_across_seam", seam, FromPolar(Vec2.Zero, -Math.PI + 0.05, 3), Inside);
            Add("cone_seam/on_edge_across_seam", seam, FromPolar(Vec2.Zero, Math.PI - 0.1 + 0.2, 3), AgreementOnly);
            Add("cone_seam/outside_across_seam", seam, FromPolar(Vec2.Zero, Math.PI - 0.1 + 0.2 + Eps, 3), Outside);
            // direction 超出 (-π, π]：判定按归一化角差，与 direction 取 direction-2π 等价。
            var bigDir = Shape.Cone(Vec2.Zero, 7.0, 0.4, 10);
            Add("cone_dir_over_2pi/inside", bigDir, FromPolar(Vec2.Zero, 7.0 - 2 * Math.PI, 3), Inside);
            Add("cone_dir_over_2pi/outside", bigDir, FromPolar(Vec2.Zero, 7.0 - 2 * Math.PI + 0.2 + Eps, 3), Outside);
            var negDir = Shape.Cone(Vec2.Zero, -10.0, 0.4, 10);
            Add("cone_dir_below_minus_2pi/inside", negDir, FromPolar(Vec2.Zero, -10.0 + 4 * Math.PI, 3), Inside);

            // 反射大扇形（全张角 270 度）：半角 135 度，正后方（180 度）在外，135 度线在边上。
            var reflex = Shape.Cone(Vec2.Zero, 0, 1.5 * Math.PI, 10);
            Add("cone_reflex/behind_is_outside", reflex, new Vec2(-5, 0), Outside);
            Add("cone_reflex/inside_just_before_edge", reflex, FromPolar(Vec2.Zero, 0.75 * Math.PI - Eps, 5), Inside);
            Add("cone_reflex/outside_just_after_edge", reflex, FromPolar(Vec2.Zero, 0.75 * Math.PI + Eps, 5), Outside);

            // ---------------- 线带：起点 / 终点端面、四个角、侧边 ----------------
            var line = Shape.Line(Vec2.Zero, 0, 10, 4);
            Add("line/start_point", line, Vec2.Zero, Inside);
            Add("line/end_point", line, new Vec2(10, 0), Inside);
            Add("line/start_corner_left", line, new Vec2(0, 2), Inside);
            Add("line/start_corner_right", line, new Vec2(0, -2), Inside);
            Add("line/end_corner_left", line, new Vec2(10, 2), Inside);
            Add("line/end_corner_right", line, new Vec2(10, -2), Inside);
            Add("line/side_edge_mid", line, new Vec2(5, 2), Inside);
            Add("line/just_before_start", line, new Vec2(-Eps, 0), Outside);
            Add("line/just_past_end", line, new Vec2(10 + Eps, 0), Outside);
            Add("line/just_outside_side", line, new Vec2(5, 2 + Eps), Outside);
            Add("line/just_outside_end_corner", line, new Vec2(10 + Eps, 2 + Eps), Outside);

            // 起点不在世界原点的线带：端点由 origin + length × (cos,sin) 算出。
            var lo = new Vec2(-2, 3);
            var ldir = 0.9;
            var lline = Shape.Line(lo, ldir, 8, 3);
            Vec2 L(double along, double across) { var o = Rotate(new Vec2(along, across), ldir); return new Vec2(lo.X + o.X, lo.Y + o.Y); }
            Add("line_rot/start_on", lline, lo, Inside);
            Add("line_rot/end_on_axis", lline, FromPolar(lo, ldir, 8), AgreementOnly);
            Add("line_rot/end_corner_on", lline, L(8, 1.5), AgreementOnly);
            Add("line_rot/start_corner_on", lline, L(0, -1.5), AgreementOnly);
            Add("line_rot/past_end_outside", lline, L(8 + Eps, 0), Outside);
            Add("line_rot/before_start_outside", lline, L(-Eps, 0), Outside);
            Add("line_rot/side_inside", lline, L(4, 1.5 - Eps), Inside);
            Add("line_rot/side_outside", lline, L(4, 1.5 + Eps), Outside);
            Add("line_rot/end_inside", lline, L(8 - Eps, 0), Inside);

            // 旋转 90 度、原点在世界原点：舍入让角点是否被含取决于 cos(π/2)≈6e-17 的符号——只比较三份实现。
            var line90 = Shape.Line(Vec2.Zero, Math.PI / 2, 10, 4);
            Add("line90/end_on_axis", line90, new Vec2(0, 10), Inside);
            Add("line90/start", line90, Vec2.Zero, Inside);
            Add("line90/side_edge_mid", line90, new Vec2(2, 5), Inside);
            Add("line90/corner_right_end", line90, new Vec2(2, 10), AgreementOnly);
            Add("line90/corner_left_end", line90, new Vec2(-2, 10), AgreementOnly);
            Add("line90/corner_right_start", line90, new Vec2(2, 0), AgreementOnly);
            Add("line90/corner_left_start", line90, new Vec2(-2, 0), AgreementOnly);
            Add("line90/past_end_outside", line90, new Vec2(0, 10 + Eps), Outside);

            // 零宽 / 零长线带：退化形状的轴上点。
            Add("line/zero_width_on_axis", Shape.Line(Vec2.Zero, 0, 10, 0), new Vec2(5, 0), Inside);
            Add("line/zero_width_off_axis", Shape.Line(Vec2.Zero, 0, 10, 0), new Vec2(5, Eps), Outside);
            Add("line/zero_length_at_origin", Shape.Line(Vec2.Zero, 0, 0, 4), Vec2.Zero, Inside);
            Add("line/zero_length_past_origin", Shape.Line(Vec2.Zero, 0, 0, 4), new Vec2(Eps, 0), Outside);

            return cases;
        }

        // =====================================================================
        // 三份实现逐点一致 + 口径期望
        // =====================================================================

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void ThreeImplementations_AgreeOnEveryBoundaryPoint_AndMatchInclusiveRule(string name)
        {
            var tc = Cases[name];

            var foundation = ShapeGeometry.Contains(tc.Shape, tc.Point);
            var areaTrigger = AreaTriggerShapeGeometry.Contains(tc.Shape, tc.Point);
            var encounter = EncounterContains(tc.Shape, tc.Point);

            Assert.True(foundation == areaTrigger,
                $"{name}: ShapeGeometry={foundation} 与 AreaTriggerShapeGeometry={areaTrigger} 不一致（点 {tc.Point}）");
            Assert.True(foundation == encounter,
                $"{name}: ShapeGeometry={foundation} 与 EncounterShapeMath={encounter} 不一致（点 {tc.Point}）");
            Assert.True(areaTrigger == encounter,
                $"{name}: AreaTriggerShapeGeometry={areaTrigger} 与 EncounterShapeMath={encounter} 不一致（点 {tc.Point}）");

            if (tc.Expected == Inside)
            {
                Assert.True(foundation, $"{name}: 期望在形状内（边界含），实际在外（点 {tc.Point}）");
            }
            else if (tc.Expected == Outside)
            {
                Assert.False(foundation, $"{name}: 期望在形状外，实际在内（点 {tc.Point}）");
            }
        }

        [Fact]
        public void EveryShapeKind_HasBoundaryCasesOfAllThreeKinds()
        {
            // 防止用例表退化：每种形状都同时有"含 / 不含 / 仅比对一致"三类点，且三份实现都真的被调用到。
            foreach (var prefix in new[] { "circle/", "rect", "cone", "line" })
            {
                var group = Cases.Values.Where(c => c.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                Assert.Contains(group, c => c.Expected == Inside);
                Assert.Contains(group, c => c.Expected == Outside);
                Assert.Contains(group, c => c.Expected == AgreementOnly);
            }

            Assert.NotNull(EncounterContains);
        }

        [Fact]
        public void DefaultShape_TreatedAsCircleOfZeroRadius_ByAllThreeImplementations()
        {
            // default(Shape).Kind==Circle(0)、Origin==(0,0)、Radius==0：原点在内、其余点在外，三份一致。
            var shape = default(Shape);
            foreach (var p in new[] { Vec2.Zero, new Vec2(Eps, 0), new Vec2(0, -1) })
            {
                var expected = p.X == 0 && p.Y == 0;
                Assert.Equal(expected, ShapeGeometry.Contains(shape, p));
                Assert.Equal(expected, AreaTriggerShapeGeometry.Contains(shape, p));
                Assert.Equal(expected, EncounterContains(shape, p));
            }
        }

        // =====================================================================
        // StubSpatialQuery.QueryRadius 与 QueryShape(Circle) / ShapeGeometry.Contains 的口径
        // =====================================================================

        private static StubSpatialQuery Register(params (string Id, Vec2 Pos, double Radius)[] entries)
        {
            var q = new StubSpatialQuery();
            foreach (var e in entries)
            {
                q.Register(new Id(e.Id), e.Pos, e.Radius);
            }

            return q;
        }

        [Fact]
        public void QueryRadius_TangentDistanceEqualsQueryRadiusPlusEntityRadius_IsIncluded_AndEpsilonBeyondIsExcluded()
        {
            // 口径：distance(center, entity) <= queryRadius + entityRadius（闭区间，恰好相切算命中）。
            const double queryRadius = 3;
            const double entityRadius = 2;
            var center = new Vec2(1, 1);
            var tangentDistance = queryRadius + entityRadius;
            var q = Register(
                ("unit.tangent_east", new Vec2(center.X + tangentDistance, center.Y), entityRadius),
                ("unit.tangent_3_4_5", new Vec2(center.X + tangentDistance * 0.6, center.Y + tangentDistance * 0.8), entityRadius),
                ("unit.beyond_east", new Vec2(center.X + tangentDistance + Eps, center.Y), entityRadius),
                ("unit.inside_east", new Vec2(center.X + tangentDistance - Eps, center.Y), entityRadius));

            var hits = q.QueryRadius(center, queryRadius, QueryFilter.None).Select(i => i.Value).ToList();

            Assert.Contains("unit.tangent_east", hits);
            Assert.Contains("unit.tangent_3_4_5", hits);
            Assert.Contains("unit.inside_east", hits);
            Assert.DoesNotContain("unit.beyond_east", hits);
        }

        [Fact]
        public void QueryRadius_ZeroRadiusEntity_OnQueryRadius_IsIncluded_LikeShapeGeometryContainsCircle()
        {
            const double queryRadius = 5;
            var center = new Vec2(2, -1);
            var onRadius = new Vec2(center.X + 3, center.Y + 4);
            var q = Register(("unit.on_radius", onRadius, 0), ("unit.outside", new Vec2(center.X + queryRadius + Eps, center.Y), 0));

            var hits = q.QueryRadius(center, queryRadius, QueryFilter.None).Select(i => i.Value).ToList();

            Assert.Equal(new[] { "unit.on_radius" }, hits);
            Assert.True(ShapeGeometry.Contains(Shape.Circle(center, queryRadius), onRadius));
        }

        [Fact]
        public void QueryShapeCircle_EqualsQueryRadius_AndEqualsContainsOfInflatedCircle_OverAGridOfEntities()
        {
            // 不变量：对任意登记实体，QueryShape(Circle(c,r)) == QueryRadius(c,r)，
            // 且二者 == ShapeGeometry.Contains(Circle(c, r + entityRadius), entityPosition)。
            var center = new Vec2(0.5, -0.5);
            const double queryRadius = 4;
            var entries = new List<(string, Vec2, double)>();
            var index = 0;
            foreach (var entityRadius in new[] { 0.0, 0.5, 2.0 })
            {
                for (var ix = -8; ix <= 8; ix++)
                {
                    for (var iy = -8; iy <= 8; iy++)
                    {
                        entries.Add(($"unit.e{index++}", new Vec2(ix * 0.75, iy * 0.75), entityRadius));
                    }
                }
            }

            var q = Register(entries.ToArray());

            var viaRadius = q.QueryRadius(center, queryRadius, QueryFilter.None).Select(i => i.Value).ToList();
            var viaShape = q.QueryShape(Shape.Circle(center, queryRadius), QueryFilter.None).Select(i => i.Value).ToList();
            var viaContains = entries
                .Where(e => ShapeGeometry.Contains(Shape.Circle(center, queryRadius + e.Item3), e.Item2))
                .Select(e => e.Item1)
                .OrderBy(v => new Id(v))
                .ToList();

            Assert.Equal(viaRadius, viaShape);
            Assert.Equal(viaContains, viaRadius);
            Assert.NotEmpty(viaRadius);
            Assert.True(viaRadius.Count < entries.Count);
        }

        /// <summary>口径：对<b>半径为 0</b> 的实体（纯点），QueryShape(Rect/Cone) 命中集合必须等于
        /// <see cref="ShapeGeometry.Contains"/> 对其位置的判定。实体位置取自形状边界点表（含浮点敏感点以外的全部点），
        /// 所以这一条同时钉住"边上 / 角上 / 半角上 / 半径上"的含边界口径。</summary>
        [Theory]
        [InlineData("rect")]
        [InlineData("cone")]
        public void QueryShape_ForPointEntities_MatchesShapeGeometryContains_OnBoundaryCases(string kind)
        {
            // 排除：顶点容差 1e-9 内的点（ShapeGeometry 视为顶点恒为内，桩实现用 distance<=entityRadius(=0) 判顶点，
            // 只在恰为 0 距离时为内——二者在 (0,1e-9) 距离内有差异，见 cone/apex_tolerance_behind_inside）。
            var selected = Cases.Values
                .Where(c => c.Shape.Kind == (kind == "rect" ? ShapeKind.Rect : ShapeKind.Cone))
                .Where(c => !c.Name.Contains("apex_tolerance"))
                .GroupBy(c => c.Shape.ToString())
                .ToList();
            Assert.NotEmpty(selected);

            foreach (var group in selected)
            {
                var shape = group.First().Shape;
                var q = new StubSpatialQuery();
                var points = new Dictionary<string, Case>();
                var i = 0;
                foreach (var c in group)
                {
                    var id = $"unit.p{i++}";
                    points[id] = c;
                    q.Register(new Id(id), c.Point, 0);
                }

                var hits = new HashSet<string>(q.QueryShape(shape, QueryFilter.None).Select(v => v.Value));
                foreach (var kv in points)
                {
                    var contains = ShapeGeometry.Contains(shape, kv.Value.Point);
                    Assert.True(contains == hits.Contains(kv.Key),
                        $"{kv.Value.Name}: ShapeGeometry.Contains={contains}，QueryShape 命中={hits.Contains(kv.Key)}（点 {kv.Value.Point}）");
                }
            }
        }

        [Fact]
        public void QueryShape_Line_ForPointEntities_MatchesContains_WithinTheBandInteriorAndOnSideEdges()
        {
            // 线带内部与侧边（局部 0<=x<=length）两种实现一致；端面外侧的"圆头"差异另见汇报（桩把线带当胶囊）。
            var shape = Shape.Line(Vec2.Zero, 0, 10, 4);
            var q = Register(
                ("unit.mid", new Vec2(5, 0), 0),
                ("unit.side_edge", new Vec2(5, 2), 0),
                ("unit.outside_side", new Vec2(5, 2 + 1e-6), 0),
                ("unit.start_point", new Vec2(0, 0), 0),
                ("unit.end_point", new Vec2(10, 0), 0),
                ("unit.end_corner", new Vec2(10, 2), 0));

            var hits = new HashSet<string>(q.QueryShape(shape, QueryFilter.None).Select(v => v.Value));

            foreach (var (id, pos) in new[]
            {
                ("unit.mid", new Vec2(5, 0)), ("unit.side_edge", new Vec2(5, 2)), ("unit.outside_side", new Vec2(5, 2 + 1e-6)),
                ("unit.start_point", new Vec2(0, 0)), ("unit.end_point", new Vec2(10, 0)), ("unit.end_corner", new Vec2(10, 2)),
            })
            {
                Assert.Equal(ShapeGeometry.Contains(shape, pos), hits.Contains(id));
            }
        }
    }
}
