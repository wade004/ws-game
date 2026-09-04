using Core.Foundation.DataRegistry;
using Core.Rules.Targeting;
using Xunit;

namespace Tests.Rules.Targeting
{
    public class ChainDefValidationRuleTests
    {
        [Fact]
        public void Validate_UnknownSource_ReportsError()
        {
            var strategies = new TargetStrategyRegistry();
            BuiltinTargetStrategies.RegisterAll(strategies);
            var bus = TargetingTestSupport.MakeBus();

            const string rows = @"[
                { ""id"": ""target.chain.bad"", ""source"": ""does_not_exist"" }
            ]";

            var registry = TargetingTestSupport.BuildChainRegistry(bus, rows, strategies);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "target_source_unknown" && i.RecordKey == "target.chain.bad");
        }

        [Fact]
        public void Validate_KnownSource_DoesNotReportSourceError()
        {
            var strategies = new TargetStrategyRegistry();
            BuiltinTargetStrategies.RegisterAll(strategies);
            var bus = TargetingTestSupport.MakeBus();

            const string rows = @"[
                { ""id"": ""target.chain.ok"", ""source"": ""self"" }
            ]";

            var registry = TargetingTestSupport.BuildChainRegistry(bus, rows, strategies);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking);
        }

        [Fact]
        public void Validate_FallbackCycle_ReportsError()
        {
            var strategies = new TargetStrategyRegistry();
            BuiltinTargetStrategies.RegisterAll(strategies);
            var bus = TargetingTestSupport.MakeBus();

            const string rows = @"[
                { ""id"": ""target.chain.cycle_a"", ""source"": ""self"", ""fallback"": ""target.chain.cycle_b"" },
                { ""id"": ""target.chain.cycle_b"", ""source"": ""self"", ""fallback"": ""target.chain.cycle_a"" }
            ]";

            var registry = TargetingTestSupport.BuildChainRegistry(bus, rows, strategies);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "target_chain_fallback_cycle");
        }

        [Fact]
        public void Validate_UnparsableFilterExpr_ReportsError()
        {
            var strategies = new TargetStrategyRegistry();
            BuiltinTargetStrategies.RegisterAll(strategies);
            var bus = TargetingTestSupport.MakeBus();

            const string rows = @"[
                { ""id"": ""target.chain.bad_filter"", ""source"": ""self"", ""filters"": [""target.hp_pct <"" ] }
            ]";

            var registry = TargetingTestSupport.BuildChainRegistry(bus, rows, strategies);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "target_filter_expr_parsable");
        }

        [Fact]
        public void Validate_BuiltinFilterShorthands_AreNotTreatedAsExprErrors()
        {
            var strategies = new TargetStrategyRegistry();
            BuiltinTargetStrategies.RegisterAll(strategies);
            var bus = TargetingTestSupport.MakeBus();

            const string rows = @"[
                { ""id"": ""target.chain.shorthands"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 10 },
                  ""filters"": [""relation:hostile"", ""alive"", ""tag:item.marked""] }
            ]";

            var registry = TargetingTestSupport.BuildChainRegistry(bus, rows, strategies);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking);
        }
    }
}
