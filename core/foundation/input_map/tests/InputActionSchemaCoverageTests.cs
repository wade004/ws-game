using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.InputMap
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Foundation.InputMap.InputActionSchema.Table"/> 的
    /// <c>default_bindings</c> 子结构登记（<c>Item</c>），覆盖范围：子结构命中/坏形状各一例。
    /// </summary>
    public sealed class InputActionSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void DefaultBindings_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"key\":\"input.action.cov_sample\",\"kind\":\"button\",\"default_bindings\":[\"keyboard:space\"]}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.InputMap.InputActionSchema.Table.Name,
                Envelope(Core.Foundation.InputMap.InputActionSchema.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.InputMap.InputActionSchema.Table);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void DefaultBindings_NonStringElement_ReportsFieldType()
        {
            var rows = "[{\"key\":\"input.action.cov_bad\",\"kind\":\"button\",\"default_bindings\":[123]}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.InputMap.InputActionSchema.Table.Name,
                Envelope(Core.Foundation.InputMap.InputActionSchema.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.InputMap.InputActionSchema.Table);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "default_bindings[0]");
        }
    }
}
