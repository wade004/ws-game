using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.SceneRouter
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Foundation.SceneRouter.WorldMapSchema.Table"/> 的
    /// <c>spawn_points</c>/<c>teleport_points</c> 子结构登记（共用同一份 Item：
    /// <c>{id?:String, position?:{x,y}}</c>），覆盖范围：子结构命中/坏形状各一例。
    /// </summary>
    public sealed class WorldMapSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void SpawnAndTeleportPoints_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"world.cov_sample\",\"scene_ref\":\"scene.cov_sample\",\"nav_ref\":\"nav.cov_sample\"," +
                "\"spawn_points\":[{\"id\":\"spawn.default\",\"position\":{\"x\":0,\"y\":0}}]," +
                "\"teleport_points\":[{\"id\":\"tp.fountain\",\"position\":{\"x\":10,\"y\":5}}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.SceneRouter.WorldMapSchema.Table.Name,
                Envelope(Core.Foundation.SceneRouter.WorldMapSchema.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.SceneRouter.WorldMapSchema.Table);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void SpawnPoints_PositionMissingY_ReportsRequiredField()
        {
            var rows = "[{\"id\":\"world.cov_bad\",\"scene_ref\":\"scene.cov_bad\",\"nav_ref\":\"nav.cov_bad\"," +
                "\"spawn_points\":[{\"id\":\"spawn.default\",\"position\":{\"x\":0}}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.SceneRouter.WorldMapSchema.Table.Name,
                Envelope(Core.Foundation.SceneRouter.WorldMapSchema.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.SceneRouter.WorldMapSchema.Table);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "spawn_points[0].position.y");
        }

        // -----------------------------------------------------------------
        // P3-07 根治：WorldMapSpawnPointsValidationRule（外部审计 audit-c9ff301-20260909）
        // -----------------------------------------------------------------

        private static DataRegistry BuildRegistryWithSpawnPointsRule(string rows)
        {
            var source = new InMemoryDataSource().Add(
                Core.Foundation.SceneRouter.WorldMapSchema.Table.Name,
                Envelope(Core.Foundation.SceneRouter.WorldMapSchema.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.SceneRouter.WorldMapSchema.Table);
            registry.RegisterValidationRule(new Core.Foundation.SceneRouter.WorldMapSpawnPointsValidationRule());
            return registry;
        }

        [Fact]
        public void P3_07_SpawnPoints_Empty_ReportsWorldMapSpawnPointsFirstPosition()
        {
            var rows = "[{\"id\":\"world.p3_07_empty\",\"scene_ref\":\"scene.p3_07\",\"nav_ref\":\"nav.p3_07\"," +
                "\"spawn_points\":[]}]";

            var report = BuildRegistryWithSpawnPointsRule(rows).LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "world_map_spawn_points_first_position" && i.Field == "spawn_points");
        }

        [Fact]
        public void P3_07_SpawnPoints_FirstElementMissingPosition_ReportsWorldMapSpawnPointsFirstPosition()
        {
            // 第 0 个元素只有 id（命名点，语义上合法——但作为"第 0 个默认出生点"缺少坐标就是无法
            // 确定默认出生点，见 SceneDescriptor.FromRecord 判断记录）；第 1 个元素同样只有 id 是
            // 合法的命名点，不应报错——本规则只管第 0 个元素。
            var rows = "[{\"id\":\"world.p3_07_no_pos\",\"scene_ref\":\"scene.p3_07\",\"nav_ref\":\"nav.p3_07\"," +
                "\"spawn_points\":[{\"id\":\"spawn.named_only\"},{\"id\":\"spawn.named_only_2\"}]}]";

            var report = BuildRegistryWithSpawnPointsRule(rows).LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "world_map_spawn_points_first_position" && i.Field == "spawn_points");
        }

        [Fact]
        public void P3_07_SpawnPoints_FirstElementHasPosition_Passes()
        {
            var rows = "[{\"id\":\"world.p3_07_ok\",\"scene_ref\":\"scene.p3_07\",\"nav_ref\":\"nav.p3_07\"," +
                "\"spawn_points\":[{\"position\":{\"x\":1,\"y\":2}},{\"id\":\"spawn.named_only\"}]}]";

            var registry = BuildRegistryWithSpawnPointsRule(rows);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var record = registry.Get(Core.Foundation.SceneRouter.WorldMapSchema.Table.Name, "world.p3_07_ok")
                ?? throw new System.InvalidOperationException("record not loaded");
            var descriptor = Core.Foundation.SceneRouter.SceneDescriptor.FromRecord(record);
            Assert.Equal(1, descriptor.DefaultSpawnPosition.X);
            Assert.Equal(2, descriptor.DefaultSpawnPosition.Y);
        }
    }
}
