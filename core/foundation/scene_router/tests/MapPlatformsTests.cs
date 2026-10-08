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
    /// 单向平台与移动平台的数据来源（<c>world.map.platforms</c>，ADR-0170）：解析、单向着陆判定、位姿是平台时钟的解析函数、
    /// 坏数据的加载期校验。期望值由运动声明与着陆规则在用例里算出。
    /// </summary>
    public sealed class MapPlatformsTests
    {
        private static readonly Id MapA = new Id("world.plat_a");
        private static readonly Id MapB = new Id("world.plat_b");

        private const string RowA = @"{
          ""id"": ""world.plat_a"", ""scene_ref"": ""scene.plat_a"", ""nav_ref"": ""nav.plat_a"",
          ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}],
          ""platforms"": [
            { ""id"": ""ledge"", ""min"": {""x"": 0, ""y"": -1}, ""max"": {""x"": 4, ""y"": 1}, ""height"": 2.0 },
            { ""id"": ""high"", ""min"": {""x"": 1, ""y"": -1}, ""max"": {""x"": 3, ""y"": 1}, ""height"": 5.0 },
            { ""id"": ""mover"", ""min"": {""x"": 10, ""y"": -1}, ""max"": {""x"": 12, ""y"": 1}, ""height"": 1.0,
              ""motion"": { ""offset"": {""x"": 6, ""y"": 0}, ""lift"": 3.0, ""travel"": 2.0, ""pause"": 1.0, ""phase"": 0.5 } }
          ] }";

        private const string RowB = @"{ ""id"": ""world.plat_b"", ""scene_ref"": ""scene.plat_b"", ""nav_ref"": ""nav.plat_b"", ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}] }";

        private static MapPlatforms Build() =>
            new MapPlatforms(SceneRouterTestSupport.BuildWorldMapRegistry("[" + RowA + "," + RowB + "]"));

        [Fact]
        public void Platforms_AreReadFromTheWorldMapRow_AndMapsWithoutTheFieldHaveNone()
        {
            var p = Build();
            Assert.True(p.HasPlatforms(MapA));
            Assert.False(p.HasPlatforms(MapB));
            Assert.False(p.TryLand(MapB, new Vec2(2, 0), 9, 0, null, out _));
            Assert.True(p.TryGetPose(MapA, "ledge", out var min, out var max, out var top));
            Assert.Equal(new Vec2(0, -1), min);
            Assert.Equal(new Vec2(4, 1), max);
            Assert.Equal(2.0, top);
        }

        [Fact]
        public void Landing_IsOneWay_OnlyAFallThatCrossesTheTopLands()
        {
            var p = Build();
            var at = new Vec2(0.5, 0); // 只在 "ledge" 的范围内
            // 从上方穿到顶面或以下：着陆。
            Assert.True(p.TryLand(MapA, at, 2.4, 1.9, null, out var contact));
            Assert.Equal("ledge", contact.PlatformId);
            Assert.Equal(2.0, contact.Height);
            Assert.True(p.TryLand(MapA, at, 2.0, 2.0, null, out _)); // 起点恰在顶面也算
            // 下方的单位（脚下低于顶面）：不接。
            Assert.False(p.TryLand(MapA, at, 1.5, 0.5, null, out _));
            Assert.False(p.TryLand(MapA, at, 1.99, 1.0, null, out _));
            // 这一步没降到顶面：还没落。
            Assert.False(p.TryLand(MapA, at, 3.0, 2.2, null, out _));
            // 平台范围之外：不接。
            Assert.False(p.TryLand(MapA, new Vec2(4.5, 0), 2.4, 1.9, null, out _));
        }

        [Fact]
        public void Landing_PicksTheHighestPlatformCrossed_AndHonoursTheIgnoredOne()
        {
            var p = Build();
            var at = new Vec2(2, 0); // 同时在 ledge(2.0) 与 high(5.0) 的范围内
            Assert.True(p.TryLand(MapA, at, 6.0, 1.0, null, out var contact));
            Assert.Equal("high", contact.PlatformId);
            Assert.True(p.TryLand(MapA, at, 6.0, 1.0, "high", out var second));
            Assert.Equal("ledge", second.PlatformId);
            Assert.False(p.TryLand(MapA, at, 6.0, 1.0, "ledge", out var third) && third.PlatformId == "ledge");
        }

        [Fact]
        public void Support_RequiresTheFootToBeFlushWithTheTop()
        {
            var p = Build();
            Assert.True(p.TryGetSupport(MapA, new Vec2(0.5, 0), 2.0, out var c));
            Assert.Equal("ledge", c.PlatformId);
            Assert.False(p.TryGetSupport(MapA, new Vec2(0.5, 0), 2.1, out _));
            Assert.False(p.TryGetSupport(MapA, new Vec2(0.5, 0), 1.9, out _));
            Assert.False(p.TryGetSupport(MapA, new Vec2(9, 0), 2.0, out _));
        }

        [Fact]
        public void MovingPlatform_PoseIsTheClosedFormOfThePlatformClock_AndMotionsReportTheDelta()
        {
            var p = Build();
            var motion = new PlatformMotionDef(new Vec2(6, 0), 3.0, travel: 2.0, pause: 1.0, phase: 0.5);
            double lastX = 10;
            double lastTop = 1.0;
            Assert.True(p.TryGetPose(MapA, "mover", out var min0, out _, out var top0));
            Assert.Equal(10 + 6 * motion.Fraction(0.0), min0.X, 12);
            Assert.Equal(1.0 + 3.0 * motion.Fraction(0.0), top0, 12);
            lastX = min0.X;
            lastTop = top0;

            const double dt = 1.0 / 30.0;
            for (var i = 1; i <= 240; i++)
            {
                p.Advance(dt);
                var fraction = motion.Fraction(p.TimeSeconds);
                Assert.True(p.TryGetPose(MapA, "mover", out var min, out _, out var top));
                Assert.Equal(10 + 6 * fraction, min.X, 12);
                Assert.Equal(1.0 + 3.0 * fraction, top, 12);
                var moved = Math.Abs(min.X - lastX) > 0 || Math.Abs(top - lastTop) > 0;
                if (moved)
                {
                    var m = Assert.Single(p.LastMotions);
                    Assert.Equal("mover", m.PlatformId);
                    Assert.Equal(min.X - lastX, m.Delta.X, 12);
                    Assert.Equal(top - lastTop, m.HeightDelta, 12);
                }
                else
                {
                    Assert.Empty(p.LastMotions);
                }

                lastX = min.X;
                lastTop = top;
            }

            Assert.Equal(240 * dt, p.TimeSeconds, 9);
        }

        [Fact]
        public void LandingOnAMovingPlatform_UsesTheCurrentPoseForTheTop_AndThePreviousPoseForTheApproach()
        {
            var p = new MapPlatforms();
            p.SetPlatforms(MapA, new[]
            {
                new PlatformDef("lift", new Vec2(0, -1), new Vec2(2, 1), 0.0, new PlatformMotionDef(Vec2.Zero, 4.0, travel: 4.0)),
            });
            p.Advance(1.0); // 顶面升到 1.0（上一步 0.0）
            // 脚下上一步在 0.0（恰在上一步的顶面），这一步落到 0.5：顶面已升到 1.0，穿过顶面，落在 1.0。
            Assert.True(p.TryLand(MapA, new Vec2(1, 0), 0.0, 0.5, null, out var c));
            Assert.Equal(1.0, c.Height, 12);
            // 脚下上一步低于上一步顶面（0.0）：在平台下方，不接。
            Assert.False(p.TryLand(MapA, new Vec2(1, 0), -0.5, 0.5, null, out _));
        }

        [Fact]
        public void SetTime_JumpsThePoseWithoutAMotionRecord()
        {
            var p = Build();
            p.SetTime(2.6);
            Assert.Equal(2.6, p.TimeSeconds, 12);
            Assert.Empty(p.LastMotions);
            var motion = new PlatformMotionDef(new Vec2(6, 0), 3.0, travel: 2.0, pause: 1.0, phase: 0.5);
            Assert.True(p.TryGetPose(MapA, "mover", out var min, out _, out _));
            Assert.Equal(10 + 6 * motion.Fraction(2.6), min.X, 12);
            Assert.Throws<ArgumentOutOfRangeException>(() => p.SetTime(double.NaN));
        }

        // ---------------------------------------------------------------- 加载期校验

        private static ValidationReport Load(string platformsJson)
        {
            var row = @"{ ""id"": ""world.plat_bad"", ""scene_ref"": ""s"", ""nav_ref"": ""n"", ""spawn_points"": [{""position"": {""x"": 0, ""y"": 0}}],
              ""platforms"": " + platformsJson + " }";
            var source = new InMemoryDataSource().Add("world.map",
                "{\"table\": \"world.map\", \"schema_version\": 1, \"rows\": [" + row + "]}");
            var registry = new Core.Foundation.DataRegistry.DataRegistry(
                source, new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false }));
            registry.RegisterSchema(WorldMapSchema.Table);
            registry.RegisterValidationRule(new WorldMapTerrainValidationRule());
            return registry.LoadAll();
        }

        [Fact]
        public void Validation_AcceptsTheDocumentedShape()
        {
            Assert.False(Load("[" + @"{ ""id"": ""a"", ""min"": {""x"": 0, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1}, ""height"": 1 },
              { ""id"": ""b"", ""min"": {""x"": 0, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1}, ""height"": 1, ""motion"": {""offset"": {""x"": 2, ""y"": 0}, ""travel"": 1.5} }" + "]").IsBlocking);
        }

        [Theory]
        [InlineData(@"[{ ""min"": {""x"": 0, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1}, ""height"": 1 }]", "required_field", "platforms[0].id")]
        [InlineData(@"[{ ""id"": ""a"", ""max"": {""x"": 1, ""y"": 1}, ""height"": 1 }]", "required_field", "platforms[0].min")]
        [InlineData(@"[{ ""id"": ""a"", ""min"": {""x"": 0, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1} }]", "required_field", "platforms[0].height")]
        [InlineData(@"[{ ""id"": ""a"", ""min"": {""x"": 2, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1}, ""height"": 1 }]", "world_map_platform", "platforms[0]")]
        [InlineData(@"[{ ""id"": ""a"", ""min"": {""x"": 0, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1}, ""height"": 1, ""motion"": {""offset"": {""x"": 2, ""y"": 0}} }]", "required_field", "platforms[0].motion.travel")]
        [InlineData(@"[{ ""id"": ""a"", ""min"": {""x"": 0, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1}, ""height"": 1, ""motion"": {""travel"": 0} }]", "world_map_platform", "platforms[0].motion.travel")]
        [InlineData(@"[{ ""id"": ""a"", ""min"": {""x"": 0, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1}, ""height"": 1, ""motion"": {""kind"": ""spin"", ""travel"": 1} }]", "world_map_platform", "platforms[0].motion.kind")]
        public void Validation_RejectsBrokenPlatforms_AtLoadTime_WithThePathOfTheProblem(string platformsJson, string check, string field)
        {
            var report = Load(platformsJson);
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == check && i.Field == field);
        }

        [Fact]
        public void Validation_RejectsDuplicatePlatformIdsOnOneMap()
        {
            var report = Load(@"[{ ""id"": ""a"", ""min"": {""x"": 0, ""y"": 0}, ""max"": {""x"": 1, ""y"": 1}, ""height"": 1 },
                                  { ""id"": ""a"", ""min"": {""x"": 5, ""y"": 0}, ""max"": {""x"": 6, ""y"": 1}, ""height"": 2 }]");
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "world_map_platform" && i.Field == "platforms[1].id");
            Assert.Throws<ArgumentException>(() => new MapPlatforms().SetPlatforms(MapA, new[]
            {
                new PlatformDef("a", Vec2.Zero, new Vec2(1, 1), 1), new PlatformDef("a", Vec2.Zero, new Vec2(1, 1), 2),
            }));
        }

        [Fact]
        public void ProgrammaticDefinitions_RejectNonsense()
        {
            Assert.Throws<ArgumentException>(() => new PlatformDef("", Vec2.Zero, new Vec2(1, 1), 1));
            Assert.Throws<ArgumentException>(() => new PlatformDef("a", new Vec2(2, 0), new Vec2(1, 1), 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlatformMotionDef(Vec2.Zero, 0, travel: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlatformMotionDef(Vec2.Zero, 0, travel: 1, pause: -1));
        }
    }
}
