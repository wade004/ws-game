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
    /// 地形高度的数据来源（<c>world.map.terrain</c>，ADR-0130 追加决定"地形竖直阻挡"）：区域解析、后声明覆盖先声明、区域外平地、
    /// 斜面公式、天花板、坏形状的校验。期望值由规则算出（<c>ground + slope·(p − min)</c>）。
    /// </summary>
    public sealed class MapTerrainHeightsTests
    {
        private static readonly Id MapA = new Id("world.terrain_a");
        private static readonly Id MapB = new Id("world.terrain_b");

        private const string RowA = @"{
          ""id"": ""world.terrain_a"", ""scene_ref"": ""scene.terrain_a"", ""nav_ref"": ""nav.terrain_a"",
          ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}],
          ""terrain"": [
            { ""min"": {""x"": 0, ""y"": -5}, ""max"": {""x"": 10, ""y"": 5}, ""ground"": 1.0 },
            { ""min"": {""x"": 4, ""y"": -5}, ""max"": {""x"": 6, ""y"": 5}, ""ground"": 3.0, ""ceiling"": 6.0 },
            { ""min"": {""x"": 20, ""y"": 0}, ""max"": {""x"": 30, ""y"": 10}, ""ground"": 0.5, ""slope"": {""x"": 0.25, ""y"": 0.1} }
          ] }";

        private const string RowB = @"{ ""id"": ""world.terrain_b"", ""scene_ref"": ""scene.terrain_b"", ""nav_ref"": ""nav.terrain_b"", ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}] }";

        private static MapTerrainHeights Build() =>
            new MapTerrainHeights(SceneRouterTestSupport.BuildWorldMapRegistry("[" + RowA + "," + RowB + "]"));

        [Fact]
        public void Regions_AreReadFromTheWorldMapRow_AndFlatOutsideThem()
        {
            var t = Build();
            Assert.True(t.HasTerrain(MapA));
            Assert.Equal(1.0, t.GetGroundHeight(MapA, new Vec2(2, 0)));
            Assert.Equal(0.0, t.GetGroundHeight(MapA, new Vec2(15, 0))); // 区域外：平地 0
            Assert.Equal(double.PositiveInfinity, t.GetCeilingHeight(MapA, new Vec2(2, 0)));
        }

        [Fact]
        public void LaterRegionsCoverEarlierOnes_AndRegionBoundsAreInclusive()
        {
            var t = Build();
            Assert.Equal(3.0, t.GetGroundHeight(MapA, new Vec2(5, 0)));
            Assert.Equal(6.0, t.GetCeilingHeight(MapA, new Vec2(5, 0)));
            Assert.Equal(3.0, t.GetGroundHeight(MapA, new Vec2(4, 0)));   // 含边界
            Assert.Equal(3.0, t.GetGroundHeight(MapA, new Vec2(6, 0)));
            Assert.Equal(1.0, t.GetGroundHeight(MapA, new Vec2(6.0001, 0)));
        }

        [Fact]
        public void Slope_FollowsGroundPlusSlopeTimesOffsetFromMin()
        {
            var t = Build();
            foreach (var p in new[] { new Vec2(20, 0), new Vec2(24, 3), new Vec2(30, 10) })
            {
                var expected = 0.5 + 0.25 * (p.X - 20) + 0.1 * (p.Y - 0);
                Assert.Equal(expected, t.GetGroundHeight(MapA, p), 12);
            }
        }

        [Fact]
        public void MapWithoutTerrain_IsFlatEverywhere_LikeNoTerrainCapability()
        {
            var t = Build();
            Assert.False(t.HasTerrain(MapB));
            Assert.Equal(0.0, t.GetGroundHeight(MapB, new Vec2(2, 0)));
            Assert.Equal(double.PositiveInfinity, t.GetCeilingHeight(MapB, new Vec2(2, 0)));
            Assert.Equal(0.0, t.GetGroundHeight(new Id("world.unknown"), new Vec2(2, 0)));

            var flat = FlatTerrainHeight2D.Instance;
            Assert.Equal(0.0, flat.GetGroundHeight(MapA, new Vec2(5, 5)));
            Assert.Equal(double.PositiveInfinity, flat.GetCeilingHeight(MapA, new Vec2(5, 5)));
        }

        [Fact]
        public void ProgrammaticRegions_Work_AndRejectInvertedBounds()
        {
            var t = new MapTerrainHeights();
            t.SetRegions(MapA, new[] { new TerrainRegion(new Vec2(0, 0), new Vec2(1, 1), ground: 2.0) });
            Assert.Equal(2.0, t.GetGroundHeight(MapA, new Vec2(0.5, 0.5)));
            Assert.Throws<ArgumentException>(() => new TerrainRegion(new Vec2(1, 0), new Vec2(0, 1)));
        }

        private static ValidationReport Load(string rowJson)
        {
            var source = new InMemoryDataSource().Add("world.map",
                "{\"table\": \"world.map\", \"schema_version\": 1, \"rows\": [" + rowJson + "]}");
            var registry = new Core.Foundation.DataRegistry.DataRegistry(
                source, new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false }));
            registry.RegisterSchema(WorldMapSchema.Table);
            registry.RegisterValidationRule(new WorldMapTerrainValidationRule());
            return registry.LoadAll();
        }

        [Fact]
        public void Schema_AcceptsTheDocumentedShape_AndRejectsMissingMax()
        {
            Assert.False(Load(RowA).IsBlocking);

            var bad = @"{ ""id"": ""world.terrain_bad"", ""scene_ref"": ""s"", ""nav_ref"": ""n"", ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}],
              ""terrain"": [ { ""min"": {""x"": 0, ""y"": 0}, ""ground"": 1 } ] }";
            var report = Load(bad);
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "terrain[0].max");
        }
    }
}
