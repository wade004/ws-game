using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SceneRouter;
using Core.Gameplay.Assembly;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// 缺口 15：<see cref="TeleportTargetResolver"/> 三种解析情形的单元测试（见该类型顶部判断记录
    /// 的规则说明）。测试数据照抄 05_对象模型与世界.md 第 4.1 节示例记录的命名惯例——
    /// <c>spawn_points</c>/<c>teleport_points</c> 的 <c>id</c> 各自有独立领域前缀（<c>spawn.</c>/
    /// <c>tp.</c>），不与地图 id 共享 <c>"world.&lt;地图名&gt;"</c> 前缀。
    /// </summary>
    public sealed class TeleportTargetResolverTests
    {
        private static IDataRegistryView BuildRegistry()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var source = new InMemoryDataSource().Add("world.map",
                "{\"table\": \"world.map\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"world.town_square\", \"scene_ref\": \"scene.town_square\", \"nav_ref\": \"nav.town_square\", " +
                "\"spawn_points\": [{\"id\": \"spawn.town_square.default\", \"position\": {\"x\": 120, \"y\": 80}, \"facing\": 0}], " +
                "\"teleport_points\": [{\"id\": \"tp.town_square.fountain\", \"position\": {\"x\": 200, \"y\": 150}}]}" +
                "]}");

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(WorldMapSchema.Table);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        [Fact]
        public void Resolve_TwoSegmentMapId_ReturnsFirstSpawnPoint()
        {
            var resolver = new TeleportTargetResolver(BuildRegistry());

            var resolved = resolver.Resolve(new Id("world.town_square"));

            Assert.True(resolved.HasValue);
            Assert.Equal(new Id("world.town_square"), resolved!.Value.MapId);
            Assert.Equal(new Vec2(120, 80), resolved.Value.Position);
        }

        [Fact]
        public void Resolve_ThreeSegmentRef_MatchesTeleportPointByLastSegment_BeforeSpawnPoint()
        {
            var resolver = new TeleportTargetResolver(BuildRegistry());

            var resolved = resolver.Resolve(new Id("world.town_square.fountain"));

            Assert.True(resolved.HasValue);
            Assert.Equal(new Id("world.town_square"), resolved!.Value.MapId);
            Assert.Equal(new Vec2(200, 150), resolved.Value.Position);
        }

        [Fact]
        public void Resolve_ThreeSegmentRef_FallsBackToSpawnPoint_WhenNoTeleportPointMatches()
        {
            var resolver = new TeleportTargetResolver(BuildRegistry());

            // "default" 在 teleport_points 里没有匹配（唯一一条是 "fountain"），应回退到
            // spawn_points 里 id 最后一段是 "default" 的那条（"spawn.town_square.default"）。
            var resolved = resolver.Resolve(new Id("world.town_square.default"));

            Assert.True(resolved.HasValue);
            Assert.Equal(new Id("world.town_square"), resolved!.Value.MapId);
            Assert.Equal(new Vec2(120, 80), resolved.Value.Position);
        }

        [Fact]
        public void Resolve_UnknownMapId_ReturnsNull_AndReportsFailure()
        {
            var failures = new List<string>();
            var resolver = new TeleportTargetResolver(BuildRegistry(), failures.Add);

            var resolved = resolver.Resolve(new Id("world.nowhere"));

            Assert.False(resolved.HasValue);
            Assert.NotEmpty(failures);
        }

        [Fact]
        public void Resolve_ThreeSegmentRef_UnknownPointName_ReturnsNull_AndReportsFailure()
        {
            var failures = new List<string>();
            var resolver = new TeleportTargetResolver(BuildRegistry(), failures.Add);

            var resolved = resolver.Resolve(new Id("world.town_square.nope"));

            Assert.False(resolved.HasValue);
            Assert.NotEmpty(failures);
        }

        [Fact]
        public void Resolve_FourSegmentRef_ReturnsNull_AndReportsFailure()
        {
            var failures = new List<string>();
            var resolver = new TeleportTargetResolver(BuildRegistry(), failures.Add);

            var resolved = resolver.Resolve(new Id("world.town_square.fountain.extra"));

            Assert.False(resolved.HasValue);
            Assert.NotEmpty(failures);
        }
    }
}
