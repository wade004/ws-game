using System.Linq;
using Core.Rules.Targeting;
using Xunit;

namespace Tests.Rules.Targeting
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="TargetSchemas.ChainDef"/> 的 <c>shape</c>（Variants）/
    /// <c>filters</c>（Item）/<c>sort_by</c>（Fields）子结构登记，覆盖范围：<c>shape</c> 变体键集合
    /// 与 <see cref="TargetChainDef"/> 运行时 switch 分支一致、子结构命中/坏形状各一例。
    /// </summary>
    public sealed class TargetSchemaCoverageTests
    {
        [Fact]
        public void ShapeVariantKeys_MatchRuntimeSwitch()
        {
            var shapeField = TargetSchemas.ChainDef.GetField("shape")!;
            var registered = shapeField.Variants!.Cases.Keys.ToHashSet();
            Assert.Equal(TargetSchemas.ShapeKindValues.ToHashSet(), registered);
        }

        [Fact]
        public void Shape_WellFormedCone_LoadsWithoutErrors()
        {
            var strategies = new TargetStrategyRegistry();
            var bus = TargetingTestSupport.MakeBus();
            var rows = "[{\"id\":\"target.chain.cov_cone\",\"source\":\"self\"," +
                "\"shape\":{\"kind\":\"cone\",\"angle\":60,\"radius\":10}}]";

            var registry = TargetingTestSupport.BuildChainRegistry(bus, rows, strategies, withValidationRule: false);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Shape_UnknownKind_ReportsVariantDiscriminator()
        {
            var strategies = new TargetStrategyRegistry();
            var bus = TargetingTestSupport.MakeBus();
            var rows = "[{\"id\":\"target.chain.cov_bad_shape\",\"source\":\"self\"," +
                "\"shape\":{\"kind\":\"not_a_real_kind\"}}]";

            var registry = TargetingTestSupport.BuildChainRegistry(bus, rows, strategies, withValidationRule: false);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator" && i.Field == "shape.kind");
        }

        [Fact]
        public void Filters_NonStringElement_ReportsFieldType()
        {
            var strategies = new TargetStrategyRegistry();
            var bus = TargetingTestSupport.MakeBus();
            var rows = "[{\"id\":\"target.chain.cov_bad_filter\",\"source\":\"self\",\"filters\":[123]}]";

            var registry = TargetingTestSupport.BuildChainRegistry(bus, rows, strategies, withValidationRule: false);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "filters[0]");
        }

        [Fact]
        public void SortBy_UnknownKey_ReportsFieldType()
        {
            var strategies = new TargetStrategyRegistry();
            var bus = TargetingTestSupport.MakeBus();
            var rows = "[{\"id\":\"target.chain.cov_bad_sort\",\"source\":\"self\"," +
                "\"sort_by\":{\"key\":\"not_a_real_key\"}}]";

            var registry = TargetingTestSupport.BuildChainRegistry(bus, rows, strategies, withValidationRule: false);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "sort_by.key");
        }
    }
}
