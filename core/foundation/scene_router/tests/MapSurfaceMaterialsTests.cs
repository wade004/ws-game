using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SceneRouter;
using Xunit;

namespace Tests.Foundation.SceneRouter
{
    /// <summary>
    /// 地面材质的数据来源（<c>world.map.surface_materials</c>，ADR-0148，手感设计/07 第 3 节"脚步材质由地图区域标签决定"）：
    /// 区域解析（rect/polygon 与 terrain 同形状）、后声明盖住先声明、区域外与未声明字段的地图返回 null（调用方按通用材质）、坏数据的校验。
    /// </summary>
    public sealed class MapSurfaceMaterialsTests
    {
        private static readonly Id MapA = new Id("world.surface_a");
        private static readonly Id MapB = new Id("world.surface_b");

        private const string RowA = @"{
          ""id"": ""world.surface_a"", ""scene_ref"": ""scene.surface_a"", ""nav_ref"": ""nav.surface_a"",
          ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}],
          ""surface_materials"": [
            { ""min"": {""x"": 0, ""y"": -5}, ""max"": {""x"": 10, ""y"": 5}, ""material"": ""stone"" },
            { ""min"": {""x"": 4, ""y"": -5}, ""max"": {""x"": 6, ""y"": 5}, ""material"": ""wood"" },
            { ""shape"": ""polygon"", ""points"": [ {""x"": 20, ""y"": 0}, {""x"": 30, ""y"": 0}, {""x"": 25, ""y"": 10} ], ""material"": ""grass"" }
          ] }";

        private const string RowB = @"{ ""id"": ""world.surface_b"", ""scene_ref"": ""scene.surface_b"", ""nav_ref"": ""nav.surface_b"", ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}] }";

        private static MapSurfaceMaterials Build() =>
            new MapSurfaceMaterials(SceneRouterTestSupport.BuildWorldMapRegistry("[" + RowA + "," + RowB + "]"));

        [Fact]
        public void Regions_AreReadFromTheWorldMapRow_AndOutsideThemIsNull()
        {
            var m = Build();
            Assert.True(m.HasRegions(MapA));
            Assert.Equal("stone", m.GetMaterial(MapA, new Vec2(2, 0)));
            Assert.Equal("grass", m.GetMaterial(MapA, new Vec2(25, 3)));   // 多边形内
            Assert.Null(m.GetMaterial(MapA, new Vec2(15, 0)));              // 区域之外 → 通用
            Assert.Null(m.GetMaterial(MapA, new Vec2(25, 11)));
        }

        [Fact]
        public void LaterRegionsCoverEarlierOnes_AndBoundsAreInclusive()
        {
            var m = Build();
            Assert.Equal("wood", m.GetMaterial(MapA, new Vec2(5, 0)));
            Assert.Equal("wood", m.GetMaterial(MapA, new Vec2(4, 0)));
            Assert.Equal("wood", m.GetMaterial(MapA, new Vec2(6, 0)));
            Assert.Equal("stone", m.GetMaterial(MapA, new Vec2(6.0001, 0)));
        }

        [Fact]
        public void MapWithoutTheField_AndUnknownMaps_AreNull_LikeNoMaterialInformation()
        {
            // 缺省不变：没声明该字段的地图与此前逐位一致（调用方走通用材质行）。
            var m = Build();
            Assert.False(m.HasRegions(MapB));
            Assert.Null(m.GetMaterial(MapB, new Vec2(2, 0)));
            Assert.Null(m.GetMaterial(new Id("world.unknown"), new Vec2(2, 0)));
        }

        [Fact]
        public void ProgrammaticRegions_Work_AndRejectEmptyMaterial()
        {
            var m = new MapSurfaceMaterials();
            var shape = new TerrainRegion(new Vec2(0, 0), new Vec2(1, 1));
            m.SetRegions(MapA, new (ITerrainShape, string)[] { (shape, "sand") });
            Assert.Equal("sand", m.GetMaterial(MapA, new Vec2(0.5, 0.5)));
            Assert.Throws<ArgumentException>(() => m.SetRegions(MapA, new (ITerrainShape, string)[] { (shape, "") }));
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

        private static string RowWith(string item) =>
            @"{ ""id"": ""world.surface_bad"", ""scene_ref"": ""s"", ""nav_ref"": ""n"", ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}],
              ""surface_materials"": [ " + item + " ] }";

        [Fact]
        public void Schema_AcceptsTheDocumentedShape()
        {
            Assert.False(Load(RowA).IsBlocking);
        }

        [Fact]
        public void Schema_RejectsAMissingOrEmptyMaterial_AndBadGeometry_WithTheFieldPath()
        {
            var noMaterial = Load(RowWith(@"{ ""min"": {""x"": 0, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1} }"));
            Assert.True(noMaterial.IsBlocking);
            Assert.Contains(noMaterial.Issues, i => i.Field == "surface_materials[0].material");

            var emptyMaterial = Load(RowWith(@"{ ""min"": {""x"": 0, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1}, ""material"": """" }"));
            Assert.True(emptyMaterial.IsBlocking);

            var inverted = Load(RowWith(@"{ ""min"": {""x"": 5, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1}, ""material"": ""stone"" }"));
            Assert.True(inverted.IsBlocking);
            Assert.Contains(inverted.Issues, i => i.Field != null && i.Field.StartsWith("surface_materials[0]", StringComparison.Ordinal));
        }
    }
}
