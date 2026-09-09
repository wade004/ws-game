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
    }
}
