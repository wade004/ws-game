using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    public class SkillFilterTests
    {
        private static readonly Id Fireball = new Id("skill.fireball");
        private static readonly Id Iceball = new Id("skill.iceball");
        private static readonly Id FireSchool = new Id("school.fire");
        private static readonly Id FrostSchool = new Id("school.frost");
        private static readonly Id DotTag = new Id("tag.dot");
        private static readonly Id BurstTag = new Id("tag.burst");

        [Fact]
        public void EmptyFilter_MatchesEverything()
        {
            var filter = SkillFilter.MatchAll;

            Assert.True(filter.Matches(Iceball, FrostSchool, new[] { BurstTag }));
        }

        [Fact]
        public void SkillIdDimension_MatchesOnlyListedId()
        {
            var filter = new SkillFilter(skillIds: new[] { Fireball });

            Assert.True(filter.Matches(Fireball, FrostSchool, System.Array.Empty<Id>()));
            Assert.False(filter.Matches(Iceball, FrostSchool, System.Array.Empty<Id>()));
        }

        [Fact]
        public void SchoolDimension_MatchesBySchoolRegardlessOfId()
        {
            var filter = new SkillFilter(schools: new[] { FireSchool });

            Assert.True(filter.Matches(Iceball, FireSchool, System.Array.Empty<Id>()));
            Assert.False(filter.Matches(Iceball, FrostSchool, System.Array.Empty<Id>()));
        }

        [Fact]
        public void TagDimension_MatchesWhenAnyTagIntersects()
        {
            var filter = new SkillFilter(tags: new[] { DotTag });

            Assert.True(filter.Matches(Iceball, FrostSchool, new[] { BurstTag, DotTag }));
            Assert.False(filter.Matches(Iceball, FrostSchool, new[] { BurstTag }));
        }

        [Fact]
        public void MultipleDimensions_TakeOr()
        {
            // school=fire 或 skillId=iceball 任一命中即整体命中（判断记录：维度间取 OR）。
            var filter = new SkillFilter(schools: new[] { FireSchool }, skillIds: new[] { Iceball });

            Assert.True(filter.Matches(Iceball, FrostSchool, System.Array.Empty<Id>()));
            Assert.True(filter.Matches(Fireball, FireSchool, System.Array.Empty<Id>()));
            Assert.False(filter.Matches(Fireball, FrostSchool, System.Array.Empty<Id>()));
        }
    }
}
