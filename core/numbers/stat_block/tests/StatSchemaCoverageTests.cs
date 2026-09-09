using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Numbers.StatBlock
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Numbers.StatBlock.StatSchemas.RatingConversion"/> 的
    /// <c>entries</c> 子结构登记（<c>Item</c>），覆盖范围：子结构命中/坏形状各一例（惯例同
    /// <c>core/gameplay/loot/tests/LootSchemaCoverageTests.cs</c>）。
    /// </summary>
    public sealed class StatSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void RatingConversionEntries_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"stat.rating.cov_sample\",\"entries\":[" +
                "{\"level\":1,\"points_per_percent\":10},{\"level\":10,\"points_per_percent\":20}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.StatBlock.StatSchemas.RatingConversion.Name,
                Envelope(Core.Numbers.StatBlock.StatSchemas.RatingConversion.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.RatingConversion);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void RatingConversionEntries_MissingPointsPerPercent_ReportsRequiredField()
        {
            var rows = "[{\"id\":\"stat.rating.cov_bad\",\"entries\":[{\"level\":1}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.StatBlock.StatSchemas.RatingConversion.Name,
                Envelope(Core.Numbers.StatBlock.StatSchemas.RatingConversion.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.RatingConversion);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "entries[0].points_per_percent");
        }
    }
}
