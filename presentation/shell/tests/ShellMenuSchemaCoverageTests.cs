using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Presentation.Shell;
using Presentation.Ui;
using Xunit;

namespace Tests.Presentation.Shell
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="ShellSchemas.ShellMenuDefinitionTable"/> 的 <c>entries</c> 子结构
    /// 登记（<c>Item</c>），覆盖范围：子结构命中/坏形状各一例、<c>target_panel</c> 同层引用完整性。
    /// </summary>
    public sealed class ShellMenuSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void Entries_WellFormedWithTargetPanel_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"shell_menu_definition.cov_sample\",\"entries\":[" +
                "{\"id\":\"shell.entry.settings\",\"text_key\":\"l10n.shell.settings\",\"action\":\"settings\"," +
                "\"target_panel\":\"ui_layout_definition.cov_sample\"}]}]";

            var source = new InMemoryDataSource()
                .Add(ShellSchemas.ShellMenuDefinitionTable.Name, Envelope(ShellSchemas.ShellMenuDefinitionTable.Name, rows))
                .Add(UiSchemas.UiLayoutDefinition.Name, Envelope(UiSchemas.UiLayoutDefinition.Name,
                    "[{\"id\":\"ui_layout_definition.cov_sample\",\"panel\":\"settings\",\"fields\":{}}]"));

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(ShellSchemas.ShellMenuDefinitionTable);
            registry.RegisterSchema(UiSchemas.UiLayoutDefinition);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Entries_UnknownTargetPanel_ReportsReferenceIntegrity()
        {
            var rows = "[{\"id\":\"shell_menu_definition.cov_bad\",\"entries\":[" +
                "{\"id\":\"shell.entry.settings\",\"text_key\":\"l10n.shell.settings\",\"action\":\"settings\"," +
                "\"target_panel\":\"ui_layout_definition.does_not_exist\"}]}]";

            var source = new InMemoryDataSource()
                .Add(ShellSchemas.ShellMenuDefinitionTable.Name, Envelope(ShellSchemas.ShellMenuDefinitionTable.Name, rows))
                .Add(UiSchemas.UiLayoutDefinition.Name, Envelope(UiSchemas.UiLayoutDefinition.Name, "[]"));

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(ShellSchemas.ShellMenuDefinitionTable);
            registry.RegisterSchema(UiSchemas.UiLayoutDefinition);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "entries[0].target_panel");
        }

        [Fact]
        public void Entries_UnknownAction_ReportsFieldType()
        {
            var rows = "[{\"id\":\"shell_menu_definition.cov_bad_action\",\"entries\":[" +
                "{\"id\":\"shell.entry.x\",\"text_key\":\"l10n.shell.x\",\"action\":\"not_a_real_action\"}]}]";

            var source = new InMemoryDataSource().Add(
                ShellSchemas.ShellMenuDefinitionTable.Name, Envelope(ShellSchemas.ShellMenuDefinitionTable.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(ShellSchemas.ShellMenuDefinitionTable);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "entries[0].action");
        }
    }
}
