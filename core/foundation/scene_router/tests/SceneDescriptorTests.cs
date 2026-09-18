using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.SceneRouter;
using Xunit;

namespace Tests.Foundation.SceneRouter
{
    public class SceneDescriptorTests
    {
        [Fact]
        public void FromRecord_TownSquareExample_ParsesIdSceneRefNavRefAndDefaultSpawn()
        {
            var registry = SceneRouterTestSupport.BuildWorldMapRegistry("[" + SceneRouterTestSupport.TownSquareRow + "]");
            var record = registry.Get("world.map", "world.town_square")!;

            var descriptor = SceneDescriptor.FromRecord(record);

            Assert.Equal(new Id("world.town_square"), descriptor.Id);
            Assert.Equal("scene.town_square", descriptor.SceneRef);
            Assert.Equal("nav.town_square", descriptor.NavRef);
            Assert.Equal(new Vec2(120, 80), descriptor.DefaultSpawnPosition);
        }

        [Fact]
        public void FromRecord_MultipleSpawnPoints_UsesFirstAsDefault()
        {
            const string row = @"
            {
              ""id"": ""world.multi_spawn"",
              ""scene_ref"": ""scene.multi_spawn"",
              ""nav_ref"": ""nav.multi_spawn"",
              ""spawn_points"": [
                {""id"": ""spawn.multi_spawn.a"", ""position"": {""x"": 5, ""y"": 6}, ""facing"": 0},
                {""id"": ""spawn.multi_spawn.b"", ""position"": {""x"": 99, ""y"": 99}, ""facing"": 0}
              ]
            }";

            var registry = SceneRouterTestSupport.BuildWorldMapRegistry("[" + row + "]");
            var descriptor = SceneDescriptor.FromRecord(registry.Get("world.map", "world.multi_spawn")!);

            Assert.Equal(new Vec2(5, 6), descriptor.DefaultSpawnPosition);
        }

        // 消费方反馈第 60 条：spawn_points 空数组现在会被新增的 field_item_count 检查挡在
        // SceneRouterTestSupport.BuildWorldMapRegistry 的阻断守卫之前（该助手在 report.IsBlocking
        // 时直接抛 InvalidOperationException），无法再借道它取到一条"已加载"的坏记录来单独验证
        // SceneDescriptor.FromRecord 自身的防御性检查。这里改为手搭一个不经过该守卫的 registry，
        // LoadAll 后即便处于阻断态，仍借 IDataRegistryView.TryGet 的阻断态直读通道（绕开
        // EnsureReadable，具体 DataRegistry 实现下 TryGet 显式覆盖为直读快照，见
        // TolerantRegistryView/DataRegistry.TryGet 判断记录）取出这条记录，从而继续验证
        // SceneDescriptor.FromRecord 的运行时最后一道防线对"绕过加载期批量校验拿到"的坏记录依然
        // 生效——不是放宽断言，只是改变了取到这条坏记录的路径。
        [Fact]
        public void FromRecord_EmptySpawnPoints_ThrowsDataFieldException()
        {
            const string row = @"
            {
              ""id"": ""world.no_spawn"",
              ""scene_ref"": ""scene.no_spawn"",
              ""nav_ref"": ""nav.no_spawn"",
              ""spawn_points"": []
            }";

            var source = new InMemoryDataSource().Add("world.map",
                "{\"table\": \"world.map\", \"schema_version\": 1, \"rows\": [" + row + "]}");
            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, SceneRouterTestSupport.CreateBus());
            registry.RegisterSchema(Core.Foundation.SceneRouter.WorldMapSchema.Table);
            registry.LoadAll(); // 预期阻断（field_item_count），本用例只关心 FromRecord 自身的防御性检查

            Assert.True(((IDataRegistryView)registry).TryGet("world.map", "world.no_spawn", out var record));

            Assert.Throws<DataFieldException>(() => SceneDescriptor.FromRecord(record!));
        }

        [Fact]
        public void FromRecord_MissingNavRef_ExposesNullNavRef()
        {
            // world.map 的 nav_ref 按 05 原文必填，DataRegistry 层面缺失会报 required_field
            // 错误；这里用一个不要求 nav_ref 的宽松 schema 绕过数据校验，专测
            // SceneDescriptor.FromRecord 对 nav_ref 缺失的防御性读取（见本模块 README 判断记录 1）。
            const string row = @"
            {
              ""id"": ""world.no_nav"",
              ""scene_ref"": ""scene.no_nav"",
              ""spawn_points"": [{""id"": ""spawn.no_nav.default"", ""position"": {""x"": 0, ""y"": 0}, ""facing"": 0}]
            }";

            var looseSchema = new TableSchema(
                "world.map", "id", 1,
                new[]
                {
                    new FieldSchema("id", FieldKind.Id, required: true),
                    new FieldSchema("scene_ref", FieldKind.String, required: true),
                    new FieldSchema("spawn_points", FieldKind.Array, required: true),
                });

            var source = new InMemoryDataSource().Add("world.map",
                "{\"table\": \"world.map\", \"schema_version\": 1, \"rows\": [" + row + "]}");
            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, SceneRouterTestSupport.CreateBus());
            registry.RegisterSchema(looseSchema);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);

            var descriptor = SceneDescriptor.FromRecord(registry.Get("world.map", "world.no_nav")!);

            Assert.Null(descriptor.NavRef);
            Assert.Equal("scene.no_nav", descriptor.SceneRef);
        }
    }
}
