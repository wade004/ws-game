using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Targeting;
using Xunit;

namespace Tests.Rules.Targeting
{
    /// <summary>
    /// 体积空间 / 横版二维能力包（手感设计/06 第 10 节勘误 9）在目标选择上的两处加法：命中形状的高度窗口（<c>shape.height</c> +
    /// <see cref="TargetingOptions.VerticalHit"/>）与三维距离（<see cref="TargetingOptions.SpatialDistance"/>）。
    /// 每处各有复现（开关打开后候选/排序从 X 变到 Y，期望值由 |Δh| 与三维距离公式算出）与不变量（开关关闭或链没声明高度时与旧行为逐位一致）。
    /// </summary>
    public class TargetHostVerticalTests
    {
        private static readonly Id HeroFaction = new Id("fac.target_test_hero");
        private static readonly Id MonsterFaction = new Id("fac.target_test_monster");
        private static readonly Id Caster = new Id("unit.caster");

        private const double Window = 1.0;

        private static readonly string AllInShapeRows = @"[
            { ""id"": ""target.chain.v_all"", ""source"": ""all_in_shape"",
              ""shape"": { ""kind"": ""circle"", ""radius"": 10, ""height"": 1 },
              ""filters"": [""relation:hostile"", ""alive""],
              ""sort_by"": { ""key"": ""distance"", ""direction"": ""asc"" }, ""max_targets"": 8 },
            { ""id"": ""target.chain.v_all_open"", ""source"": ""all_in_shape"",
              ""shape"": { ""kind"": ""circle"", ""radius"": 10 },
              ""filters"": [""relation:hostile"", ""alive""],
              ""sort_by"": { ""key"": ""distance"", ""direction"": ""asc"" }, ""max_targets"": 8 },
            { ""id"": ""target.chain.v_nearest"", ""source"": ""nearest_in_shape"",
              ""shape"": { ""kind"": ""circle"", ""radius"": 10, ""height"": 100 },
              ""filters"": [""relation:hostile"", ""alive""],
              ""sort_by"": { ""key"": ""distance"", ""direction"": ""asc"" }, ""max_targets"": 1 }
        ]";

        // 靶子（名 → 平面位置、脚下高度）：地面近、半高、刚好在窗口边缘、窗口外、悬空远。
        private static readonly (string Name, Vec2 Pos, double Height)[] Dummies =
        {
            ("ground", new Vec2(3, 0), 0.0),
            ("half", new Vec2(4, 0), 0.5),
            ("edge", new Vec2(5, 0), Window),
            ("above", new Vec2(6, 0), Window + 0.001),
            ("high", new Vec2(7, 0), 4.0),
        };

        private static TargetHostTests.Fixture Build(string rows, TargetingOptions? options, double casterHeight = 0.0)
        {
            return TargetHostTests.Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(Caster, HeroFaction, new Vec2(0, 0));
                units.SetHeight(Caster, casterHeight);
                foreach (var d in Dummies)
                {
                    var id = new Id("unit." + d.Name);
                    units.Add(id, MonsterFaction, d.Pos);
                    units.SetHeight(id, d.Height);
                    spatial.Register(id, d.Pos, 0);
                }
            }, options: options);
        }

        private static string[] Names(System.Collections.Generic.IReadOnlyList<Id> ids) =>
            ids.Select(i => i.Value.Substring("unit.".Length)).ToArray();

        // ---------- 高度窗口：复现 ----------

        [Fact]
        public void VerticalHit_KeepsOnlyCandidatesWithinShapeHeightOfTheCaster()
        {
            var fx = Build(AllInShapeRows, new TargetingOptions { VerticalHit = true });
            var result = Names(fx.Host.Resolve(new Id("target.chain.v_all"), Caster));

            // 期望由规则算出：|目标高度 − 施法者高度| ≤ shape.height（边缘含），再按平面距离升序。
            var expected = Dummies.Where(d => Math.Abs(d.Height - 0.0) <= Window).OrderBy(d => d.Pos.X).Select(d => d.Name).ToArray();
            Assert.Equal(expected, result);
            Assert.Equal(new[] { "ground", "half", "edge" }, result);
        }

        [Fact]
        public void VerticalHit_WindowFollowsTheCastersHeight_AndGroundPointCastsAnchorAtZero()
        {
            // 施法者在 4 个单位高（跳起）：窗口跟着施法者走，只剩高处的靶子。
            var airborne = Build(AllInShapeRows, new TargetingOptions { VerticalHit = true }, casterHeight: 4.0);
            var expected = Dummies.Where(d => Math.Abs(d.Height - 4.0) <= Window).Select(d => d.Name).ToArray();
            Assert.Equal(expected, Names(airborne.Host.Resolve(new Id("target.chain.v_all"), Caster)));
            Assert.Equal(new[] { "high" }, expected);

            // 地面坐标施法（ResolveAtPoint）：锚点高度是地面 0，不取施法者的高度。
            var atPoint = Names(airborne.Host.ResolveAtPoint(new Id("target.chain.v_all"), Caster, new Vec2(0, 0)));
            Assert.Equal(Dummies.Where(d => Math.Abs(d.Height) <= Window).OrderBy(d => d.Pos.X).Select(d => d.Name).ToArray(), atPoint);
        }

        // ---------- 高度窗口：不变量（旧行为逐位一致） ----------

        [Fact]
        public void VerticalHit_OffByDefault_HeightIsIgnored_EvenWhenTheChainDeclaresIt()
        {
            var fx = Build(AllInShapeRows, options: null);
            Assert.Equal(Dummies.OrderBy(d => d.Pos.X).Select(d => d.Name).ToArray(), Names(fx.Host.Resolve(new Id("target.chain.v_all"), Caster)));
        }

        [Fact]
        public void VerticalHit_ChainWithoutHeight_IsUnboundedVertically()
        {
            var fx = Build(AllInShapeRows, new TargetingOptions { VerticalHit = true });
            Assert.Equal(Dummies.OrderBy(d => d.Pos.X).Select(d => d.Name).ToArray(), Names(fx.Host.Resolve(new Id("target.chain.v_all_open"), Caster)));
        }

        // ---------- 三维距离 ----------

        [Fact]
        public void SpatialDistance_PicksNearestByThreeDimensionalDistance()
        {
            // 平面最近是 ground(3)，三维最近要把高度差算进去：把 ground 抬高后换人。
            var planarFx = Build(AllInShapeRows, options: null);
            var spatialFx = Build(AllInShapeRows, new TargetingOptions { SpatialDistance = true });
            foreach (var fx in new[] { planarFx, spatialFx })
            {
                fx.Units.SetHeight(new Id("unit.ground"), 3.0);
                fx.Units.SetHeight(new Id("unit.half"), 0.0);
            }

            double Planar(string name) => Dummies.Single(d => d.Name == name).Pos.X;
            double Spatial(string name, double h) => Math.Sqrt(Planar(name) * Planar(name) + h * h);

            // 期望：ground 三维距离 sqrt(3²+3²)=4.24 > half 平面 4（高度 0）→ 体积空间改选 half；平面距离仍选 ground。
            Assert.True(Spatial("ground", 3.0) > Spatial("half", 0.0));
            Assert.Equal(new[] { "ground" }, Names(planarFx.Host.Resolve(new Id("target.chain.v_nearest"), Caster)));
            Assert.Equal(new[] { "half" }, Names(spatialFx.Host.Resolve(new Id("target.chain.v_nearest"), Caster)));
        }

        [Fact]
        public void SpatialDistance_AllInShapeSortFollowsThreeDimensionalDistance()
        {
            var fx = Build(AllInShapeRows, new TargetingOptions { SpatialDistance = true });
            var heights = Dummies.ToDictionary(d => d.Name, d => d.Height);
            var expected = Dummies
                .OrderBy(d => Math.Sqrt(d.Pos.X * d.Pos.X + heights[d.Name] * heights[d.Name]))
                .ThenBy(d => "unit." + d.Name, StringComparer.Ordinal)
                .Select(d => d.Name).ToArray();
            Assert.Equal(expected, Names(fx.Host.Resolve(new Id("target.chain.v_all_open"), Caster)));
        }

        // ---------- shape.height 解析 ----------

        private static DataRecord ChainRecord(string shapeJson)
        {
            var json = "{\"id\":\"target.chain.h_sample\",\"source\":\"all_in_shape\",\"shape\":" + shapeJson + "}";
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(TargetSchemas.ChainDef, "target.chain.h_sample", new Id("target.chain.h_sample"), raw);
        }

        [Fact]
        public void ShapeHeight_ParsedForEveryShapeKind_AndNullWhenAbsent()
        {
            foreach (var shape in new[]
            {
                "{\"kind\":\"circle\",\"radius\":2,\"height\":1.5}",
                "{\"kind\":\"cone\",\"angle\":1.0,\"radius\":2,\"height\":1.5}",
                "{\"kind\":\"line\",\"length\":2,\"width\":1,\"height\":1.5}",
                "{\"kind\":\"rect\",\"length\":2,\"width\":1,\"height\":1.5}",
            })
            {
                Assert.Equal(1.5, new TargetChainDef(ChainRecord(shape)).ShapeHeight);
            }

            Assert.Null(new TargetChainDef(ChainRecord("{\"kind\":\"circle\",\"radius\":2}")).ShapeHeight);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("\"tall\"")]
        public void ShapeHeight_NonPositiveOrNonNumeric_ThrowsNamingShapeHeight(string value)
        {
            var ex = Assert.Throws<DataFieldException>(() => new TargetChainDef(ChainRecord("{\"kind\":\"circle\",\"radius\":2,\"height\":" + value + "}")));
            Assert.Equal("shape.height", ex.Field);
        }
    }
}
