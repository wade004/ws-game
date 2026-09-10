using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Numbers.Archetype
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Numbers.Archetype.ArchSchemas.TalentTree"/> 的 <c>nodes</c>
    /// 子结构登记（<c>Item</c>，<c>grants</c> 未被进一步解析不登记子结构）；ADR-0024 第二批登记起
    /// <see cref="Core.Numbers.Archetype.ArchSchemas.Class"/>/<see cref="Core.Numbers.Archetype.ArchSchemas.Race"/>
    /// 的 <c>base_stats</c>/<c>stat_mods</c> 改用 <c>MapSchema.ReferenceKeyTable("stat.definition", ...)</c>
    /// 登记，覆盖范围：子结构命中/坏形状各一例。
    /// </summary>
    public sealed class ArchSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private const string StatDefinitionRows = "[{\"id\":\"stat.cov_sample\",\"name_key\":\"l10n.stat.cov_sample.name\",\"group\":\"primary\"}]";

        // -----------------------------------------------------------------
        // ADR-0024 第二批登记：base_stats（arch.class） / stat_mods（arch.race）
        // -----------------------------------------------------------------

        [Fact]
        public void ClassBaseStats_KnownStatKey_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"arch.class.cov_sample\",\"name_key\":\"l10n.arch.class.cov_sample.name\"," +
                "\"primary_stat\":\"stat.cov_sample\",\"base_stats\":{\"stat.cov_sample\":10},\"power_types\":[]}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, StatDefinitionRows))
                .Add(Core.Numbers.Archetype.ArchSchemas.Class.Name,
                    Envelope(Core.Numbers.Archetype.ArchSchemas.Class.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.Class);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ClassBaseStats_UnknownStatKey_ReportsReferenceIntegrityWithBracketPath()
        {
            var rows = "[{\"id\":\"arch.class.cov_bad\",\"name_key\":\"l10n.arch.class.cov_bad.name\"," +
                "\"primary_stat\":\"stat.cov_sample\",\"base_stats\":{\"stat.no_such\":10},\"power_types\":[]}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, StatDefinitionRows))
                .Add(Core.Numbers.Archetype.ArchSchemas.Class.Name,
                    Envelope(Core.Numbers.Archetype.ArchSchemas.Class.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.Class);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "reference_integrity" && i.Field == "base_stats[stat.no_such]");
        }

        [Fact]
        public void RaceStatMods_KnownStatKey_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"arch.race.cov_sample\",\"name_key\":\"l10n.arch.race.cov_sample.name\"," +
                "\"stat_mods\":{\"stat.cov_sample\":1}}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, StatDefinitionRows))
                .Add(Core.Numbers.Archetype.ArchSchemas.Race.Name,
                    Envelope(Core.Numbers.Archetype.ArchSchemas.Race.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.Race);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void RaceStatMods_UnknownStatKey_ReportsReferenceIntegrityWithBracketPath()
        {
            var rows = "[{\"id\":\"arch.race.cov_bad\",\"name_key\":\"l10n.arch.race.cov_bad.name\"," +
                "\"stat_mods\":{\"stat.no_such\":1}}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, StatDefinitionRows))
                .Add(Core.Numbers.Archetype.ArchSchemas.Race.Name,
                    Envelope(Core.Numbers.Archetype.ArchSchemas.Race.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.Race);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "reference_integrity" && i.Field == "stat_mods[stat.no_such]");
        }

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
