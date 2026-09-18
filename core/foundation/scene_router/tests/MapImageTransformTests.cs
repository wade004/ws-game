using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.SceneRouter;
using Xunit;

namespace Tests.Foundation.SceneRouter
{
    /// <summary>
    /// 消费方反馈第 59 条（ADR-0036）：<see cref="MapImageTransform"/> 往返换算、Y 翻转、包围盒、
    /// 缺省读取；<see cref="WorldMapPointOutsideImageValidationRule"/>（检查名
    /// <c>world_map_point_outside_image</c>）的告警/放行分支。
    /// </summary>
    public sealed class MapImageTransformTests
    {
        [Fact]
        public void Default_HasPixelsPerUnit32AndOriginAtImageTopLeft()
        {
            Assert.Equal(32, MapImageTransform.Default.PixelsPerUnit);
            Assert.Equal(new Vec2(0, 0), MapImageTransform.Default.OriginPx);
            Assert.Null(MapImageTransform.Default.ImageSizePx);
        }

        [Fact]
        public void PixelToWorld_ThenWorldToPixel_RoundTrips()
        {
            var transform = new MapImageTransform(32, new Vec2(100, 200));
            var px = new Vec2(164, 136);

            var world = transform.PixelToWorld(px);
            var back = transform.WorldToPixel(world);

            Assert.Equal(px.X, back.X, precision: 9);
            Assert.Equal(px.Y, back.Y, precision: 9);
        }

        [Fact]
        public void PixelToWorld_YAxisIsFlipped_PixelBelowOriginIsNegativeWorldY()
        {
            // 世界 Y 轴向上、图片像素行向下为正：原点正下方 32 像素（一个世界单位）应换算为 world.y = -1。
            var transform = new MapImageTransform(32, new Vec2(0, 0));

            var world = transform.PixelToWorld(new Vec2(0, 32));

            Assert.Equal(0, world.X, precision: 9);
            Assert.Equal(-1, world.Y, precision: 9);
        }

        [Fact]
        public void PixelToWorld_PixelRightOfOrigin_IsPositiveWorldX()
        {
            var transform = new MapImageTransform(32, new Vec2(0, 0));

            var world = transform.PixelToWorld(new Vec2(64, 0));

            Assert.Equal(2, world.X, precision: 9);
            Assert.Equal(0, world.Y, precision: 9);
        }

        [Fact]
        public void WorldBounds_Null_WhenImageSizeNotDeclared()
        {
            var transform = new MapImageTransform(32, new Vec2(0, 0));

            Assert.Null(transform.WorldBounds);
        }

        [Fact]
        public void WorldBounds_ComputedFromImageCorners_OriginAtBottomLeft()
        {
            // 常见约定：世界原点在图片左下角（origin_px.y = 图片高度），图片 1024x640 像素、ppu=32。
            var transform = new MapImageTransform(32, new Vec2(0, 640), new Vec2(1024, 640));

            var bounds = transform.WorldBounds;

            Assert.NotNull(bounds);
            Assert.Equal(new Vec2(0, 0), bounds!.Value.Min);
            Assert.Equal(new Vec2(32, 20), bounds.Value.Max);
        }

        [Fact]
        public void Constructor_NonPositivePixelsPerUnit_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MapImageTransform(0, Vec2.Zero));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MapImageTransform(-1, Vec2.Zero));
        }

        // -----------------------------------------------------------------
        // FromRecord：缺省读取
        // -----------------------------------------------------------------

        private static IDataRegistry BuildRegistry(string rowJson) =>
            SceneRouterTestSupport.BuildWorldMapRegistry("[" + rowJson + "]");

        [Fact]
        public void FromRecord_FieldMissing_ReturnsNull()
        {
            var registry = BuildRegistry(SceneRouterTestSupport.TownSquareRow);
            var record = registry.Get("world.map", "world.town_square")!;

            Assert.Null(MapImageTransform.FromRecord(record));
        }

        [Fact]
        public void FromRecord_FieldPresent_ParsesAllSubFields()
        {
            const string row = @"
            {
              ""id"": ""world.with_transform"",
              ""scene_ref"": ""scene.with_transform"",
              ""nav_ref"": ""nav.with_transform"",
              ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}],
              ""image_transform"": {
                ""pixels_per_unit"": 32,
                ""origin_px"": {""x"": 0, ""y"": 640},
                ""image_size_px"": {""x"": 1024, ""y"": 640}
              }
            }";
            var registry = BuildRegistry(row);
            var record = registry.Get("world.map", "world.with_transform")!;

            var transform = MapImageTransform.FromRecord(record);

            Assert.NotNull(transform);
            Assert.Equal(32, transform!.PixelsPerUnit);
            Assert.Equal(new Vec2(0, 640), transform.OriginPx);
            Assert.Equal(new Vec2(1024, 640), transform.ImageSizePx);
        }

        [Fact]
        public void FromRecord_FieldPresentWithoutImageSize_ImageSizeIsNull()
        {
            const string row = @"
            {
              ""id"": ""world.no_image_size"",
              ""scene_ref"": ""scene.no_image_size"",
              ""nav_ref"": ""nav.no_image_size"",
              ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}],
              ""image_transform"": {
                ""pixels_per_unit"": 32,
                ""origin_px"": {""x"": 0, ""y"": 0}
              }
            }";
            var registry = BuildRegistry(row);
            var record = registry.Get("world.map", "world.no_image_size")!;

            var transform = MapImageTransform.FromRecord(record);

            Assert.NotNull(transform);
            Assert.Null(transform!.ImageSizePx);
            Assert.Null(transform.WorldBounds);
        }

        // -----------------------------------------------------------------
        // WorldMapPointOutsideImageValidationRule
        // -----------------------------------------------------------------

        private static IDataRegistry BuildRegistryWithRule(string rowJson)
        {
            var source = new InMemoryDataSource().Add("world.map",
                "{\"table\": \"world.map\", \"schema_version\": 1, \"rows\": [" + rowJson + "]}");
            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, SceneRouterTestSupport.CreateBus());
            registry.RegisterSchema(WorldMapSchema.Table);
            registry.RegisterValidationRule(new WorldMapPointOutsideImageValidationRule());
            return registry;
        }

        [Fact]
        public void SpawnPointInsideBounds_NoWarning()
        {
            const string row = @"
            {
              ""id"": ""world.inside"",
              ""scene_ref"": ""scene.inside"",
              ""nav_ref"": ""nav.inside"",
              ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}],
              ""image_transform"": {
                ""pixels_per_unit"": 32,
                ""origin_px"": {""x"": 0, ""y"": 640},
                ""image_size_px"": {""x"": 1024, ""y"": 640}
              }
            }";

            var report = BuildRegistryWithRule(row).LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == "world_map_point_outside_image");
        }

        [Fact]
        public void SpawnPointOutsideBounds_ReportsWarning_NotBlocking()
        {
            const string row = @"
            {
              ""id"": ""world.outside"",
              ""scene_ref"": ""scene.outside"",
              ""nav_ref"": ""nav.outside"",
              ""spawn_points"": [
                {""position"": {""x"": 0, ""y"": 0}},
                {""position"": {""x"": 999, ""y"": 999}}
              ],
              ""image_transform"": {
                ""pixels_per_unit"": 32,
                ""origin_px"": {""x"": 0, ""y"": 640},
                ""image_size_px"": {""x"": 1024, ""y"": 640}
              }
            }";

            var report = BuildRegistryWithRule(row).LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Contains(report.Issues, i =>
                i.Check == "world_map_point_outside_image"
                && i.Severity == ValidationSeverity.Warning
                && i.Field == "spawn_points[1].position");
        }

        [Fact]
        public void TeleportPointOutsideBounds_ReportsWarning()
        {
            const string row = @"
            {
              ""id"": ""world.tp_outside"",
              ""scene_ref"": ""scene.tp_outside"",
              ""nav_ref"": ""nav.tp_outside"",
              ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}],
              ""teleport_points"": [{""id"": ""tp.far"", ""position"": {""x"": -50, ""y"": 5}}],
              ""image_transform"": {
                ""pixels_per_unit"": 32,
                ""origin_px"": {""x"": 0, ""y"": 640},
                ""image_size_px"": {""x"": 1024, ""y"": 640}
              }
            }";

            var report = BuildRegistryWithRule(row).LoadAll();

            Assert.Contains(report.Issues, i =>
                i.Check == "world_map_point_outside_image" && i.Field == "teleport_points[0].position");
        }

        [Fact]
        public void NoImageTransform_RuleSkips_NoIssues()
        {
            var report = BuildRegistryWithRule(SceneRouterTestSupport.TownSquareRow).LoadAll();

            Assert.DoesNotContain(report.Issues, i => i.Check == "world_map_point_outside_image");
        }

        [Fact]
        public void ImageTransformWithoutImageSize_RuleSkips_NoIssues()
        {
            const string row = @"
            {
              ""id"": ""world.no_size_rule"",
              ""scene_ref"": ""scene.no_size_rule"",
              ""nav_ref"": ""nav.no_size_rule"",
              ""spawn_points"": [{""position"": {""x"": 99999, ""y"": 99999}}],
              ""image_transform"": {
                ""pixels_per_unit"": 32,
                ""origin_px"": {""x"": 0, ""y"": 0}
              }
            }";

            var report = BuildRegistryWithRule(row).LoadAll();

            Assert.DoesNotContain(report.Issues, i => i.Check == "world_map_point_outside_image");
        }
    }
}
