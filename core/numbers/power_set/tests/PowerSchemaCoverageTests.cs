using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Numbers.PowerSet
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Numbers.PowerSet.PowerSchemas.MaxSourceSchema"/> 的
    /// <c>Variants</c> 登记，覆盖范围：变体键集合与 <see cref="Core.Numbers.PowerSet.PowerTypeDefinition"/>
    /// 运行时 switch 分支一致、子结构命中/坏形状各一例、L1 同程序集 <c>stat.definition</c>
    /// 引用完整性。
    /// </summary>
    public sealed class PowerSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void MaxSourceVariantKeys_MatchRuntimeSwitch()
        {
            var registered = Core.Numbers.PowerSet.PowerSchemas.MaxSourceSchema.Variants!.Cases.Keys.ToHashSet();
            Assert.Equal(Core.Numbers.PowerSet.PowerSchemas.MaxSourceKindValues.ToHashSet(), registered);
        }

        [Fact]
        public void MaxSource_WellFormedStatKind_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"arch.power.cov_sample\",\"name_key\":\"l10n.power.cov_sample\"," +
                "\"max_source\":{\"kind\":\"stat\",\"stat\":\"stat.cov_sample\"}}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.PowerSet.PowerSchemas.PowerType.Name,
                    Envelope(Core.Numbers.PowerSet.PowerSchemas.PowerType.Name, rows))
                .Add("stat.definition", Envelope("stat.definition",
                    "[{\"id\":\"stat.cov_sample\",\"name_key\":\"l10n.stat.cov_sample\",\"group\":\"primary\"}]"));

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.PowerSet.PowerSchemas.PowerType);
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void MaxSource_StatKindUnknownStat_ReportsReferenceIntegrity()
        {
            var rows = "[{\"id\":\"arch.power.cov_bad\",\"name_key\":\"l10n.power.cov_bad\"," +
                "\"max_source\":{\"kind\":\"stat\",\"stat\":\"stat.does_not_exist\"}}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.PowerSet.PowerSchemas.PowerType.Name,
                    Envelope(Core.Numbers.PowerSet.PowerSchemas.PowerType.Name, rows))
                .Add("stat.definition", Envelope("stat.definition", "[]"));

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.PowerSet.PowerSchemas.PowerType);
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "max_source.stat");
        }

        [Fact]
        public void MaxSource_FixedKindMissingValue_ReportsRequiredField()
        {
            var rows = "[{\"id\":\"arch.power.cov_fixed_bad\",\"name_key\":\"l10n.power.cov_fixed_bad\"," +
                "\"max_source\":{\"kind\":\"fixed\"}}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.PowerSet.PowerSchemas.PowerType.Name,
                Envelope(Core.Numbers.PowerSet.PowerSchemas.PowerType.Name, rows));

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.PowerSet.PowerSchemas.PowerType);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "max_source.value");
        }
    }
}
