using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    public class UnitFilterTests
    {
        [Fact]
        public void Default_HasAliveOnlyTrueAndNoRestrictions()
        {
            var filter = default(UnitFilter);

            Assert.True(filter.AliveOnly);
            Assert.Equal(RelationFilter.Any, filter.Relation);
            Assert.Null(filter.Exclude);
            Assert.Empty(filter.RequiredTags);
            Assert.Empty(filter.ExcludedTags);
        }

        [Fact]
        public void StaticDefault_EqualsDefaultStruct()
        {
            Assert.Equal(default(UnitFilter).AliveOnly, UnitFilter.Default.AliveOnly);
            Assert.Equal(default(UnitFilter).Relation, UnitFilter.Default.Relation);
        }

        [Fact]
        public void ExplicitConstruction_OverridesAliveOnly()
        {
            var filter = new UnitFilter(RelationFilter.Hostile, aliveOnly: false);

            Assert.False(filter.AliveOnly);
            Assert.Equal(RelationFilter.Hostile, filter.Relation);
        }
    }
}
