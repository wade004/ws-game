using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SceneRouter;
using Xunit;

namespace Tests.Foundation.SceneRouter
{
    /// <summary>
    /// M4-W1a（ADR-0130 追加决定"地形形状"）：凸多边形区域（平面/斜面）、规则网格高度场（双线性插值）与既有矩形同表声明、
    /// 后声明覆盖先声明；数据校验覆盖新形状。期望值由公式算出（平面方程、双线性插值），不写死裸数。
    /// </summary>
    public sealed class TerrainShapesTests
    {
        private static readonly Id MapA = new Id("world.shapes_a");

        private static Vec2[] Square(double x0, double y0, double x1, double y1) =>
            new[] { new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1) };

        // ---------------------------------------------------------------- 凸多边形

        [Fact]
        public void Polygon_Plane_GroundIsGroundPlusSlopeTimesOffsetFromOrigin()
        {
            var tri = new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(0, 10) };
            var poly = new TerrainPolygon(tri, ground: 1.0, slopeX: 0.5, slopeY: -0.25);
            foreach (var p in new[] { new Vec2(0, 0), new Vec2(2, 3), new Vec2(5, 5), new Vec2(9, 0.5) })
            {
                Assert.True(poly.Contains(p));
                Assert.Equal(1.0 + 0.5 * (p.X - 0) - 0.25 * (p.Y - 0), poly.GroundAt(p), 12);
            }

            var shifted = new TerrainPolygon(tri, ground: 1.0, slopeX: 0.5, origin: new Vec2(4, 4));
            Assert.Equal(1.0 + 0.5 * (2 - 4), shifted.GroundAt(new Vec2(2, 3)), 12);
        }

        [Fact]
        public void Polygon_ContainsIsInclusiveOfEdges_AndExcludesOutside_ForEitherWinding()
        {
            var ccw = new TerrainPolygon(Square(0, 0, 4, 4));
            var cw = new TerrainPolygon(new[] { new Vec2(0, 0), new Vec2(0, 4), new Vec2(4, 4), new Vec2(4, 0) });
            foreach (var poly in new[] { ccw, cw })
            {
                Assert.True(poly.Contains(new Vec2(2, 2)));
                Assert.True(poly.Contains(new Vec2(4, 2))); // 边
                Assert.True(poly.Contains(new Vec2(0, 0))); // 顶点
                Assert.False(poly.Contains(new Vec2(4.0001, 2)));
                Assert.False(poly.Contains(new Vec2(-1, 2)));
            }

            // 菱形：包围盒内但在多边形外的点必须被排除（矩形做不到）。
            var diamond = new TerrainPolygon(new[] { new Vec2(0, 2), new Vec2(2, 0), new Vec2(4, 2), new Vec2(2, 4) });
            Assert.True(diamond.Contains(new Vec2(2, 2)));
            Assert.False(diamond.Contains(new Vec2(0.2, 0.2)));
        }

        [Fact]
        public void Polygon_RejectsConcaveDegenerateAndTooFewVertices()
        {
            var concave = new[] { new Vec2(0, 0), new Vec2(4, 0), new Vec2(4, 4), new Vec2(2, 1), new Vec2(0, 4) };
            Assert.Throws<ArgumentException>(() => new TerrainPolygon(concave));
            Assert.Throws<ArgumentException>(() => new TerrainPolygon(new[] { new Vec2(0, 0), new Vec2(1, 1), new Vec2(2, 2) }));
            Assert.Throws<ArgumentException>(() => new TerrainPolygon(new[] { new Vec2(0, 0), new Vec2(1, 0) }));
            Assert.Throws<ArgumentException>(() => new TerrainPolygon(new[] { new Vec2(0, 0), new Vec2(1, 0), new Vec2(double.NaN, 1) }));
        }

        // ---------------------------------------------------------------- 高度场

        private static TerrainHeightField Field() => new TerrainHeightField(
            new Vec2(10, 20), 2.0,
            new[]
            {
                new[] { 0.0, 1.0, 4.0 },
                new[] { 2.0, 3.0, 8.0 },
                new[] { 6.0, 5.0, 0.0 },
            });

        [Fact]
        public void HeightField_NodesAreExact_AndCellsInterpolateBilinearly()
        {
            var f = Field();
            Assert.Equal(new Vec2(14, 24), f.Max);
            for (var j = 0; j < 3; j++)
            {
                for (var i = 0; i < 3; i++)
                {
                    Assert.Equal(f.NodeHeight(i, j), f.GroundAt(new Vec2(10 + 2.0 * i, 20 + 2.0 * j)), 12);
                }
            }

            // 格 (0,0) 的中心：四个结点的平均。
            Assert.Equal((0.0 + 1.0 + 2.0 + 3.0) / 4.0, f.GroundAt(new Vec2(11, 21)), 12);
            // 沿一条格边是线性的。
            Assert.Equal(0.5, f.GroundAt(new Vec2(11, 20)), 12);
            // 任意点：与手算的双线性公式一致。
            var p = new Vec2(12.5, 21.0);
            var tx = (12.5 - 12.0) / 2.0;
            var ty = (21.0 - 20.0) / 2.0;
            var lower = 1.0 + (4.0 - 1.0) * tx;
            var upper = 3.0 + (8.0 - 3.0) * tx;
            Assert.Equal(lower + (upper - lower) * ty, f.GroundAt(p), 12);
        }

        [Fact]
        public void HeightField_AdjacentCellsAreContinuousAcrossTheirSharedEdge()
        {
            var f = Field();
            for (var k = 0; k <= 10; k++)
            {
                var y = 20 + 4.0 * k / 10.0;
                var left = f.GroundAt(new Vec2(12 - 1e-9, y));
                var right = f.GroundAt(new Vec2(12 + 1e-9, y));
                Assert.Equal(left, right, 6);
            }
        }

        [Fact]
        public void HeightField_ContainsItsNodeRectangleInclusive_AndRejectsBadGrids()
        {
            var f = Field();
            Assert.True(f.Contains(new Vec2(10, 20)));
            Assert.True(f.Contains(new Vec2(14, 24)));
            Assert.False(f.Contains(new Vec2(14.001, 22)));
            Assert.Throws<ArgumentException>(() => new TerrainHeightField(Vec2.Zero, 0.0, new[] { new[] { 0.0, 0.0 }, new[] { 0.0, 0.0 } }));
            Assert.Throws<ArgumentException>(() => new TerrainHeightField(Vec2.Zero, 1.0, new[] { new[] { 0.0, 0.0 } }));
            Assert.Throws<ArgumentException>(() => new TerrainHeightField(Vec2.Zero, 1.0, new[] { new[] { 0.0, 0.0 }, new[] { 0.0 } }));
            Assert.Throws<ArgumentException>(() => new TerrainHeightField(Vec2.Zero, 1.0, new[] { new[] { 0.0, double.NaN }, new[] { 0.0, 0.0 } }));
        }

        // ---------------------------------------------------------------- 同表组合（后声明覆盖）

        [Fact]
        public void MixedShapes_LaterDeclarationWins_AcrossRectPolygonAndHeightField()
        {
            var t = new MapTerrainHeights();
            t.SetShapes(MapA, new ITerrainShape[]
            {
                new TerrainRegion(new Vec2(0, 0), new Vec2(50, 50), ground: 1.0),
                new TerrainPolygon(Square(10, 10, 30, 30), ground: 4.0, ceiling: 9.0),
                new TerrainHeightField(new Vec2(20, 20), 5.0, new[] { new[] { 7.0, 7.0 }, new[] { 7.0, 7.0 } }),
            });
            Assert.Equal(1.0, t.GetGroundHeight(MapA, new Vec2(5, 5)));
            Assert.Equal(4.0, t.GetGroundHeight(MapA, new Vec2(15, 15)));
            Assert.Equal(9.0, t.GetCeilingHeight(MapA, new Vec2(15, 15)));
            Assert.Equal(7.0, t.GetGroundHeight(MapA, new Vec2(22, 22))); // 高度场盖住多边形
            Assert.Equal(4.0, t.GetGroundHeight(MapA, new Vec2(28, 28))); // 高度场范围（20..25）之外仍是多边形
            Assert.Equal(0.0, t.GetGroundHeight(MapA, new Vec2(60, 60))); // 区域外平地
            Assert.True(t.HasTerrain(MapA));

            // 声明顺序反过来：结果跟着变。
            var reversed = new MapTerrainHeights();
            reversed.SetShapes(MapA, new ITerrainShape[]
            {
                new TerrainHeightField(new Vec2(20, 20), 5.0, new[] { new[] { 7.0, 7.0 }, new[] { 7.0, 7.0 } }),
                new TerrainPolygon(Square(10, 10, 30, 30), ground: 4.0),
            });
            Assert.Equal(4.0, reversed.GetGroundHeight(MapA, new Vec2(22, 22)));
        }

        [Fact]
        public void SetRegions_StillWorks_AndEqualsSetShapesForRects()
        {
            var rect = new TerrainRegion(new Vec2(0, 0), new Vec2(10, 10), ground: 2.5, slopeX: 0.1);
            var a = new MapTerrainHeights();
            a.SetRegions(MapA, new[] { rect });
            var b = new MapTerrainHeights();
            b.SetShapes(MapA, new ITerrainShape[] { rect });
            foreach (var p in new[] { new Vec2(1, 1), new Vec2(9, 3), new Vec2(20, 20) })
            {
                Assert.Equal(a.GetGroundHeight(MapA, p), b.GetGroundHeight(MapA, p));
            }
        }

        // ---------------------------------------------------------------- 数据

        private const string Head = @"{ ""id"": ""world.shapes_a"", ""scene_ref"": ""scene.shapes_a"", ""nav_ref"": ""nav.shapes_a"", ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}], ""terrain"": [";

        private static string Row(string terrainItems) => Head + terrainItems + "] }";

        private static Core.Foundation.DataRegistry.DataRegistry Registry(string rowJson, bool withRule = true)
        {
            var source = new InMemoryDataSource().Add("world.map",
                "{\"table\": \"world.map\", \"schema_version\": 1, \"rows\": [" + rowJson + "]}");
            var registry = new Core.Foundation.DataRegistry.DataRegistry(
                source, new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false }));
            registry.RegisterSchema(WorldMapSchema.Table);
            if (withRule) registry.RegisterValidationRule(new WorldMapTerrainValidationRule());
            return registry;
        }

        private static ValidationReport Load(string rowJson) => Registry(rowJson).LoadAll();

        private const string PolyItem = @"{ ""shape"": ""polygon"", ""points"": [{""x"":0,""y"":0},{""x"":10,""y"":0},{""x"":10,""y"":10},{""x"":0,""y"":10}], ""ground"": 2.0, ""slope"": {""x"": 0.5, ""y"": 0} }";
        private const string FieldItem = @"{ ""shape"": ""heightfield"", ""min"": {""x"":20,""y"":0}, ""cell"": 2.0, ""heights"": [[0,1,2],[1,2,3],[2,3,4]] }";

        [Fact]
        public void Data_PolygonAndHeightfieldRows_LoadAndReadBackAsShapes()
        {
            var registry = Registry(Row(@"{ ""min"": {""x"":-5,""y"":-5}, ""max"": {""x"":40,""y"":40}, ""ground"": 1.0 }," + PolyItem + "," + FieldItem));
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var t = new MapTerrainHeights(registry);
            Assert.Equal(1.0, t.GetGroundHeight(MapA, new Vec2(-1, -1)));
            Assert.Equal(2.0 + 0.5 * 4.0, t.GetGroundHeight(MapA, new Vec2(4, 6)), 12);
            Assert.Equal(2.0, t.GetGroundHeight(MapA, new Vec2(22, 2)), 12); // 结点 (1,1) 的高度
        }

        [Fact]
        public void Data_BadShapes_AreReportedWithPaths()
        {
            var concave = Row(@"{ ""shape"": ""polygon"", ""points"": [{""x"":0,""y"":0},{""x"":4,""y"":0},{""x"":4,""y"":4},{""x"":2,""y"":1},{""x"":0,""y"":4}] }");
            var r1 = Load(concave);
            Assert.True(r1.IsBlocking);
            Assert.Contains(r1.Issues, i => i.Check == "world_map_terrain_shape" && i.Field!.StartsWith("terrain[0]"));

            var noPoints = Row(@"{ ""shape"": ""polygon"", ""ground"": 1 }");
            var r2 = Load(noPoints);
            Assert.Contains(r2.Issues, i => i.Check == "required_field" && i.Field == "terrain[0].points");

            var ragged = Row(@"{ ""shape"": ""heightfield"", ""min"": {""x"":0,""y"":0}, ""cell"": 1, ""heights"": [[0,0],[0]] }");
            Assert.Contains(Load(ragged).Issues, i => i.Check == "world_map_terrain_shape" && i.Field!.StartsWith("terrain[0]"));

            var badCell = Row(@"{ ""shape"": ""heightfield"", ""min"": {""x"":0,""y"":0}, ""cell"": 0, ""heights"": [[0,0],[0,0]] }");
            Assert.Contains(Load(badCell).Issues, i => i.Check == "world_map_terrain_shape");

            var unknown = Row(@"{ ""shape"": ""blob"" }");
            Assert.Contains(Load(unknown).Issues, i => i.Check == "world_map_terrain_shape" && i.Field == "terrain[0].shape");

            var inverted = Row(@"{ ""min"": {""x"":5,""y"":0}, ""max"": {""x"":0,""y"":5} }");
            Assert.Contains(Load(inverted).Issues, i => i.Check == "world_map_terrain_shape");
        }

        [Fact]
        public void Data_DefaultRectShape_IsUnchanged_AndOldReaderRejectsNewShapesLoudly()
        {
            var plain = Row(@"{ ""min"": {""x"":0,""y"":0}, ""max"": {""x"":4,""y"":4}, ""ground"": 3.0 }");
            var registry = Registry(plain);
            Assert.False(registry.LoadAll().IsBlocking);
            var t = new MapTerrainHeights(registry);
            Assert.Equal(3.0, t.GetGroundHeight(MapA, new Vec2(1, 1)));

            var mixed = Registry(Row(PolyItem));
            mixed.LoadAll();
            var record = mixed.Get("world.map", "world.shapes_a")!;
            Assert.Throws<DataFieldException>(() => MapTerrainHeights.RegionsFromRecord(record));
            Assert.Single(MapTerrainHeights.ShapesFromRecord(record)!);
        }
    }
}
