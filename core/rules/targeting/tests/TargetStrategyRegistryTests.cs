using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Targeting;
using Xunit;

namespace Tests.Rules.Targeting
{
    public class TargetStrategyRegistryTests
    {
        private sealed class FixedIdStrategy : ITargetSourceStrategy
        {
            private readonly Id _result;

            public FixedIdStrategy(string name, Id result)
            {
                Name = name;
                _result = result;
            }

            public string Name { get; }

            public IReadOnlyList<Id> Collect(TargetContext ctx) => new[] { _result };
        }

        [Fact]
        public void RegisterAll_RegistersAllSixBuiltinNames()
        {
            var registry = new TargetStrategyRegistry();
            BuiltinTargetStrategies.RegisterAll(registry);

            Assert.Equal(6, registry.Names.Count);
            Assert.Contains(BuiltinTargetStrategies.CurrentTarget, registry.Names);
            Assert.Contains(BuiltinTargetStrategies.NearestInShape, registry.Names);
            Assert.Contains(BuiltinTargetStrategies.Self, registry.Names);
            Assert.Contains(BuiltinTargetStrategies.PartyLowestHpPct, registry.Names);
            Assert.Contains(BuiltinTargetStrategies.ThreatTop, registry.Names);
            Assert.Contains(BuiltinTargetStrategies.AllInShape, registry.Names);
        }

        [Fact]
        public void Register_DuplicateName_Throws()
        {
            var registry = new TargetStrategyRegistry();
            registry.Register(new FixedIdStrategy("dup", new Id("unit.a")));

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(new FixedIdStrategy("dup", new Id("unit.b"))));
        }

        [Fact]
        public void Get_UnregisteredName_Throws()
        {
            var registry = new TargetStrategyRegistry();
            Assert.Throws<ArgumentException>(() => registry.Get("does_not_exist"));
        }

        [Fact]
        public void Register_CustomStrategy_IsRetrievableAndUsableByName()
        {
            var registry = new TargetStrategyRegistry();
            var expected = new Id("unit.custom_target");
            registry.Register(new FixedIdStrategy("always_custom_target", expected));

            var strategy = registry.Get("always_custom_target");
            var result = strategy.Collect(null!);

            Assert.Single(result);
            Assert.Equal(expected, result[0]);
        }
    }
}
