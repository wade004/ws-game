using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Foundation.DisplayInfo.DisplaySchemas.Map"/> 的
    /// <c>mirror_pairs</c>/<c>paperdoll_layers</c> 子结构登记（<c>Item</c>；<c>anchor_points</c>/
    /// <c>default_slot_meshes</c>/<c>material_params</c> 为 Map 不登记），覆盖范围：子结构命中/
    /// 坏形状各一例。
    /// </summary>
    public sealed class DisplaySchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void MirrorPairsAndPaperdollLayers_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"display.cov_sample\",\"category\":\"creature\",\"logical_id\":\"creature.cov_sample\"," +
                "\"kind\":\"sprite\"," +
                "\"mirror_pairs\":[{\"direction_slot\":\"dir.west\",\"mirror_of\":\"dir.east\",\"flip_x\":true}]," +
                "\"paperdoll_layers\":[\"base\",\"armor\"]}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.Map);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void MirrorPairs_MissingMirrorOf_ReportsRequiredField()
        {
            var rows = "[{\"id\":\"display.cov_bad\",\"category\":\"creature\",\"logical_id\":\"creature.cov_bad\"," +
                "\"kind\":\"sprite\",\"mirror_pairs\":[{\"direction_slot\":\"dir.west\"}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.Map);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "mirror_pairs[0].mirror_of");
        }

        [Fact]
        public void PaperdollLayers_NonStringElement_ReportsFieldType()
        {
            var rows = "[{\"id\":\"display.cov_bad_layer\",\"category\":\"creature\",\"logical_id\":\"creature.cov_bad_layer\"," +
                "\"kind\":\"sprite\",\"paperdoll_layers\":[123]}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.Map);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "paperdoll_layers[0]");
        }
    }
}
