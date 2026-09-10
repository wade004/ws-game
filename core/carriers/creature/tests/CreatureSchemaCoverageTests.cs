using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Carriers.Creature
{
    /// <summary>
    /// ADR-0024 第二批登记：<see cref="Core.Carriers.Creature.CreatureSchemas.Template"/> 的
    /// <c>base_stats</c> 改用 <c>MapSchema.ReferenceKeyTable("stat.definition", ...)</c> 登记，覆盖
    /// 范围：子结构命中/坏形状各一例（其余字段的结构性校验不在本文件重复，见
    /// <c>CreatureValidationTests.cs</c>）。
    /// </summary>
    public sealed class CreatureSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private const string StatDefinitionRows = "[{\"id\":\"stat.cov_sample\",\"name_key\":\"l10n.stat.cov_sample.name\",\"group\":\"primary\"}]";

        private const string TierRows = "[{\"id\":\"creature.tier.cov_sample\",\"name_key\":\"l10n.creature.tier.cov_sample.name\"}]";

        private static DataRegistry MakeRegistry(string templateRows)
        {
            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, StatDefinitionRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name, TierRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.Template.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.Template.Name, templateRows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Carriers.Creature.CreatureSchemas.TierDefinition);
            registry.RegisterSchema(Core.Carriers.Creature.CreatureSchemas.Template);
            return registry;
        }

        [Fact]
        public void BaseStats_KnownStatKey_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"creature.cov_sample\",\"name_key\":\"l10n.creature.cov_sample.name\",\"level\":1," +
                "\"tier\":\"creature.tier.cov_sample\",\"base_stats\":{\"stat.cov_sample\":10}," +
                "\"faction_id\":\"fac.cov_sample\",\"display_ref\":\"display.map.cov_sample\"}]";

            var report = MakeRegistry(rows).LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void BaseStats_UnknownStatKey_ReportsReferenceIntegrityWithBracketPath()
        {
            var rows = "[{\"id\":\"creature.cov_bad\",\"name_key\":\"l10n.creature.cov_bad.name\",\"level\":1," +
                "\"tier\":\"creature.tier.cov_sample\",\"base_stats\":{\"stat.no_such\":10}," +
                "\"faction_id\":\"fac.cov_sample\",\"display_ref\":\"display.map.cov_sample\"}]";

            var report = MakeRegistry(rows).LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "reference_integrity" && i.Field == "base_stats[stat.no_such]");
        }

        [Fact]
        public void BaseStats_ValueNotNumber_ReportsFieldTypeWithBracketPath()
        {
            var rows = "[{\"id\":\"creature.cov_bad_type\",\"name_key\":\"l10n.creature.cov_bad_type.name\",\"level\":1," +
                "\"tier\":\"creature.tier.cov_sample\",\"base_stats\":{\"stat.cov_sample\":\"not_a_number\"}," +
                "\"faction_id\":\"fac.cov_sample\",\"display_ref\":\"display.map.cov_sample\"}]";

            var report = MakeRegistry(rows).LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "field_type" && i.Field == "base_stats[stat.cov_sample]");
        }
    }
}
