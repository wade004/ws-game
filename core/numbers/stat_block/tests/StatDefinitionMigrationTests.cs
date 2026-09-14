using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Numbers.StatBlock
{
    /// <summary>
    /// 分阶段落地计划 T-N1-1 验收（<c>stat.definition</c> 1→2 迁移）：v1 数据（含
    /// <c>group</c>/<c>is_rating</c>/<c>rating_conversion_ref</c>/<c>min</c>/<c>max</c>）经迁移链
    /// 加载后，字段值与按 <c>StatSchemas.MigrateDefinitionV1ToV2</c> 判断记录手算的映射一致（验收
    /// 标准"迁移后记录字段值 = 按映射手算"）。覆盖 group→category 四种旧取值（含 secondary 按
    /// is_rating 分裂 percent/misc 两支）、min/max→clamp（含只有一侧的情形）、
    /// is_rating+ref→conversion_ref。
    /// </summary>
    public sealed class StatDefinitionMigrationTests
    {
        private static IEventBus MakeBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private const string V1Rows = @"
        {
            ""table"": ""stat.definition"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""stat.mig_primary"", ""name_key"": ""l10n.a"", ""group"": ""primary"" },
                { ""id"": ""stat.mig_derived"", ""name_key"": ""l10n.b"", ""group"": ""derived"" },
                { ""id"": ""stat.mig_resistance"", ""name_key"": ""l10n.c"", ""group"": ""resistance"" },
                { ""id"": ""stat.mig_secondary_rating"", ""name_key"": ""l10n.d"", ""group"": ""secondary"",
                  ""is_rating"": true, ""rating_conversion_ref"": ""stat.rating.mig_curve"" },
                { ""id"": ""stat.mig_secondary_plain"", ""name_key"": ""l10n.e"", ""group"": ""secondary"" },
                { ""id"": ""stat.mig_minmax_both"", ""name_key"": ""l10n.f"", ""group"": ""primary"", ""min"": -5, ""max"": 100 },
                { ""id"": ""stat.mig_minmax_min_only"", ""name_key"": ""l10n.g"", ""group"": ""primary"", ""min"": 0 },
                { ""id"": ""stat.mig_minmax_none"", ""name_key"": ""l10n.h"", ""group"": ""primary"" }
            ]
        }";

        private const string RatingConversionJson = @"
        {
            ""table"": ""stat.rating_conversion"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""stat.rating.mig_curve"", ""entries"": [{ ""level"": 1, ""points_per_percent"": 10 }, { ""level"": 10, ""points_per_percent"": 5 }] }
            ]
        }";

        private static IDataRegistry LoadV1()
        {
            var source = new InMemoryDataSource()
                .Add("stat.definition", V1Rows)
                .Add("stat.rating_conversion", RatingConversionJson);
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(StatSchemas.RatingConversion);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        [Fact]
        public void Migration_GroupPrimary_MapsToCategoryPrimary()
        {
            var registry = LoadV1();
            var record = registry.Get("stat.definition", "stat.mig_primary");
            Assert.NotNull(record);
            Assert.True(record!.TryGetString("category", out var category));
            Assert.Equal("primary", category);
            // 废弃字段保留、不删。
            Assert.True(record.TryGetString("group", out var group));
            Assert.Equal("primary", group);
        }

        [Fact]
        public void Migration_GroupDerived_MapsToCategoryDerived()
        {
            var registry = LoadV1();
            var record = registry.Get("stat.definition", "stat.mig_derived");
            Assert.NotNull(record);
            Assert.True(record!.TryGetString("category", out var category));
            Assert.Equal("derived", category);
        }

        [Fact]
        public void Migration_GroupResistance_MapsToCategoryDefense()
        {
            // 拍板 1 明文规定：抗性归 defense。
            var registry = LoadV1();
            var record = registry.Get("stat.definition", "stat.mig_resistance");
            Assert.NotNull(record);
            Assert.True(record!.TryGetString("category", out var category));
            Assert.Equal("defense", category);
        }

        [Fact]
        public void Migration_GroupSecondaryWithIsRating_MapsToCategoryPercent_AndConversionRef()
        {
            var registry = LoadV1();
            var record = registry.Get("stat.definition", "stat.mig_secondary_rating");
            Assert.NotNull(record);
            Assert.True(record!.TryGetString("category", out var category));
            Assert.Equal("percent", category);
            Assert.True(record.TryGetId("conversion_ref", out var conversionRef));
            Assert.Equal("stat.rating.mig_curve", conversionRef.Value);
            // 废弃字段保留、不删。
            Assert.True(record.TryGetBool("is_rating", out var isRating));
            Assert.True(isRating);
            Assert.True(record.TryGetId("rating_conversion_ref", out _));
        }

        [Fact]
        public void Migration_GroupSecondaryWithoutIsRating_MapsToCategoryMisc_NoConversionRef()
        {
            var registry = LoadV1();
            var record = registry.Get("stat.definition", "stat.mig_secondary_plain");
            Assert.NotNull(record);
            Assert.True(record!.TryGetString("category", out var category));
            Assert.Equal("misc", category);
            Assert.False(record.Has("conversion_ref"));
        }

        [Fact]
        public void Migration_MinAndMaxBothPresent_NestIntoClamp()
        {
            var registry = LoadV1();
            var record = registry.Get("stat.definition", "stat.mig_minmax_both");
            Assert.NotNull(record);
            Assert.True(record!.TryGetObject("clamp", out var clamp));
            Assert.True(clamp.TryGetValue("min", out var minVal) && minVal is Core.Foundation.Common.Json.JsonNumber minNum);
            Assert.True(clamp.TryGetValue("max", out var maxVal) && maxVal is Core.Foundation.Common.Json.JsonNumber maxNum);
            Assert.Equal(-5.0, ((Core.Foundation.Common.Json.JsonNumber)minVal).Value);
            Assert.Equal(100.0, ((Core.Foundation.Common.Json.JsonNumber)maxVal).Value);
            // 废弃字段保留、不删。
            Assert.True(record.TryGetNumber("min", out var oldMin));
            Assert.Equal(-5.0, oldMin);
        }

        [Fact]
        public void Migration_OnlyMinPresent_ClampOmitsMax()
        {
            var registry = LoadV1();
            var record = registry.Get("stat.definition", "stat.mig_minmax_min_only");
            Assert.NotNull(record);
            Assert.True(record!.TryGetObject("clamp", out var clamp));
            Assert.True(clamp.TryGetValue("min", out _));
            Assert.False(clamp.ContainsKey("max"));
        }

        [Fact]
        public void Migration_NeitherMinNorMax_NoClampField()
        {
            var registry = LoadV1();
            var record = registry.Get("stat.definition", "stat.mig_minmax_none");
            Assert.NotNull(record);
            Assert.False(record!.Has("clamp"));
        }
    }
}
