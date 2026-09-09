using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Rules.Combat.CombatSchemas.HitTableConfig"/> 六个命中表分支
    /// （<c>Fields</c>）与 <see cref="Core.Rules.Combat.CombatSchemas.ResistCurve"/> 的 <c>entries</c>
    /// （<c>Item</c>）子结构登记，覆盖范围：子结构命中/坏形状各一例、<c>stat</c> 跨层引用完整性、
    /// <c>CombatHitTableValidationRule</c>/<c>CombatResistCurveValidationRule</c>（收窄后仍保留的
    /// 业务判断）不因本次登记双报。
    /// </summary>
    public sealed class CombatSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private static string MinimalHitTableRow(string id, string dodgeExtra) =>
            "[{\"id\":\"" + id + "\"," +
            "\"miss\":{\"enabled\":true,\"base\":0.05}," +
            "\"dodge\":{\"enabled\":true,\"base\":0.05" + dodgeExtra + "}," +
            "\"parry\":{\"enabled\":false},\"glancing_blow\":{\"enabled\":false}," +
            "\"block\":{\"enabled\":false},\"crit\":{\"enabled\":true,\"base\":0.05}}]";

        [Fact]
        public void HitTableBranch_WellFormedStatRef_LoadsWithoutErrors()
        {
            var rows = MinimalHitTableRow("combat.hit_table.cov_sample", ",\"stat\":\"stat.cov_sample\"");

            var source = new InMemoryDataSource()
                .Add(Core.Rules.Combat.CombatSchemas.HitTableConfig.Name,
                    Envelope(Core.Rules.Combat.CombatSchemas.HitTableConfig.Name, rows))
                .Add("stat.definition", Envelope("stat.definition",
                    "[{\"id\":\"stat.cov_sample\",\"name_key\":\"l10n.stat.cov_sample\",\"group\":\"primary\"}]"));

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Rules.Combat.CombatSchemas.HitTableConfig);
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void HitTableBranch_UnknownStatRef_ReportsReferenceIntegrity()
        {
            var rows = MinimalHitTableRow("combat.hit_table.cov_bad", ",\"stat\":\"stat.does_not_exist\"");

            var source = new InMemoryDataSource()
                .Add(Core.Rules.Combat.CombatSchemas.HitTableConfig.Name,
                    Envelope(Core.Rules.Combat.CombatSchemas.HitTableConfig.Name, rows))
                .Add("stat.definition", Envelope("stat.definition", "[]"));

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Rules.Combat.CombatSchemas.HitTableConfig);
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "dodge.stat");
        }

        [Fact]
        public void HitTableBranch_EnabledNotBool_ReportsFieldType_BaseRangeRuleNotDoubled()
        {
            var rows = "[{\"id\":\"combat.hit_table.cov_badtype\"," +
                "\"miss\":{\"enabled\":\"not_a_bool\"},\"dodge\":{\"enabled\":false},\"parry\":{\"enabled\":false}," +
                "\"glancing_blow\":{\"enabled\":false},\"block\":{\"enabled\":false},\"crit\":{\"enabled\":false}}]";

            var source = new InMemoryDataSource().Add(
                Core.Rules.Combat.CombatSchemas.HitTableConfig.Name,
                Envelope(Core.Rules.Combat.CombatSchemas.HitTableConfig.Name, rows));

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Rules.Combat.CombatSchemas.HitTableConfig);
            registry.RegisterValidationRule(new Core.Rules.Combat.CombatHitTableValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "miss.enabled");
            Assert.DoesNotContain(report.Issues, i => i.Check == "hit_table_base_range");
        }

        [Fact]
        public void ResistCurveEntries_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"combat.resist.cov_sample\",\"school\":\"school.cov_sample\",\"kind\":\"table\"," +
                "\"entries\":[{\"value\":0,\"reduction\":0},{\"value\":100,\"reduction\":0.5}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Rules.Combat.CombatSchemas.ResistCurve.Name,
                Envelope(Core.Rules.Combat.CombatSchemas.ResistCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Rules.Combat.CombatSchemas.ResistCurve);
            registry.RegisterValidationRule(new Core.Rules.Combat.CombatResistCurveValidationRule());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ResistCurveEntries_MissingReduction_ReportsRequiredField_NotDoubleReportedByBusinessRule()
        {
            var rows = "[{\"id\":\"combat.resist.cov_bad\",\"school\":\"school.cov_sample\",\"kind\":\"table\"," +
                "\"entries\":[{\"value\":0}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Rules.Combat.CombatSchemas.ResistCurve.Name,
                Envelope(Core.Rules.Combat.CombatSchemas.ResistCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Rules.Combat.CombatSchemas.ResistCurve);
            registry.RegisterValidationRule(new Core.Rules.Combat.CombatResistCurveValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "entries[0].reduction");
        }
    }
}
