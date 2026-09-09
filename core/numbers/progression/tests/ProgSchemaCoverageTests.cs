using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Numbers.Progression
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Numbers.Progression.ProgSchemas.LevelCurve"/> 的
    /// <c>entries</c> 子结构登记（<c>Item</c>，<c>growth</c> 为 Map 不登记），覆盖范围：子结构
    /// 命中/坏形状各一例，<c>ProgLevelCurveValidationRule</c>（连续性业务判断）不因本次登记双报。
    /// </summary>
    public sealed class ProgSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void LevelCurveEntries_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"prog.level_curve.cov_sample\",\"max_level\":2,\"entries\":[" +
                "{\"level\":1,\"xp_to_next\":100,\"growth\":{\"stat.cov_sample\":1}}," +
                "{\"level\":2,\"xp_to_next\":200}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.LevelCurve.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.LevelCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.LevelCurve);
            registry.RegisterValidationRule(new Core.Numbers.Progression.ProgLevelCurveValidationRule());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void LevelCurveEntries_MissingXpToNext_ReportsRequiredField_NotDoubleReportedByBusinessRule()
        {
            var rows = "[{\"id\":\"prog.level_curve.cov_bad\",\"max_level\":1,\"entries\":[{\"level\":1}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.LevelCurve.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.LevelCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.LevelCurve);
            registry.RegisterValidationRule(new Core.Numbers.Progression.ProgLevelCurveValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "entries[0].xp_to_next");
        }
    }
}
