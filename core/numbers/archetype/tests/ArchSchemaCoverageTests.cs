using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Numbers.Archetype
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Numbers.Archetype.ArchSchemas.TalentTree"/> 的 <c>nodes</c>
    /// 子结构登记（<c>Item</c>，<c>grants</c> 未被进一步解析不登记子结构），覆盖范围：子结构
    /// 命中/坏形状各一例。
    /// </summary>
    public sealed class ArchSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void TalentTreeNodes_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"arch.talent_tree.cov_sample\",\"nodes\":[" +
                "{\"id\":\"n1\",\"cost\":1},{\"id\":\"n2\",\"prerequisites\":[\"n1\"],\"cost\":2,\"grants\":{}}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Archetype.ArchSchemas.TalentTree.Name,
                Envelope(Core.Numbers.Archetype.ArchSchemas.TalentTree.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.TalentTree);
            registry.RegisterValidationRule(new Core.Numbers.Archetype.ArchTalentTreeCycleValidationRule());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void TalentTreeNodes_MissingId_ReportsRequiredField()
        {
            var rows = "[{\"id\":\"arch.talent_tree.cov_bad\",\"nodes\":[{\"cost\":1}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Archetype.ArchSchemas.TalentTree.Name,
                Envelope(Core.Numbers.Archetype.ArchSchemas.TalentTree.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.TalentTree);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "nodes[0].id");
        }
    }
}
