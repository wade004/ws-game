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

        /// <summary>T-N1-1：schema_version 2 写法（<c>stat.definition</c> 已含 <c>category</c> 时用
        /// 这个而不是 <see cref="Envelope"/>——后者固定 <c>schema_version:1</c>，若行内也写了
        /// <c>category</c> 会在 1→2 迁移里与迁移函数自己补算的 <c>category</c> 撞键。</summary>
        private static string EnvelopeV2(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":2,\"rows\":" + rowsJson + "}";

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
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "entries[0].y");
        }

        // -----------------------------------------------------------------
        // T-N1-1：stat.definition.derived_from / clamp 子结构覆盖（ADR-0019 惯例：子结构命中/坏
        // 形状各一例）。
        // -----------------------------------------------------------------

        [Fact]
        public void DerivedFrom_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[" +
                "{\"id\":\"stat.cov_src\",\"name_key\":\"l10n.src\",\"category\":\"primary\",\"group\":\"primary\"}," +
                "{\"id\":\"stat.cov_derived\",\"name_key\":\"l10n.derived\",\"category\":\"derived\",\"group\":\"derived\"," +
                "\"derived_from\":[{\"stat\":\"stat.cov_src\",\"coefficient\":1.5}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                EnvelopeV2(Core.Numbers.StatBlock.StatSchemas.Definition.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void DerivedFrom_MissingCoefficient_ReportsRequiredField()
        {
            var rows = "[" +
                "{\"id\":\"stat.cov_src2\",\"name_key\":\"l10n.src2\",\"category\":\"primary\",\"group\":\"primary\"}," +
                "{\"id\":\"stat.cov_bad_derived\",\"name_key\":\"l10n.bad_derived\",\"category\":\"derived\",\"group\":\"derived\"," +
                "\"derived_from\":[{\"stat\":\"stat.cov_src2\"}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                EnvelopeV2(Core.Numbers.StatBlock.StatSchemas.Definition.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "derived_from[0].coefficient");
        }

        [Fact]
        public void Clamp_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"stat.cov_clamp\",\"name_key\":\"l10n.clamp\",\"category\":\"primary\",\"group\":\"primary\"," +
                "\"clamp\":{\"min\":0,\"max\":100}}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                EnvelopeV2(Core.Numbers.StatBlock.StatSchemas.Definition.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Clamp_WrongType_ReportsFieldType()
        {
            var rows = "[{\"id\":\"stat.cov_bad_clamp\",\"name_key\":\"l10n.bad_clamp\",\"category\":\"primary\",\"group\":\"primary\"," +
                "\"clamp\":{\"min\":\"not_a_number\"}}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                EnvelopeV2(Core.Numbers.StatBlock.StatSchemas.Definition.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "clamp.min");
        }
    }
}
