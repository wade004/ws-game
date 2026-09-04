using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Carriers.Common
{
    public class LockRequirementTests
    {
        [Fact]
        public void ItemKey_CarriesItemId()
        {
            var itemId = new Id("item.rusty_key");

            var requirement = LockRequirement.ItemKey(itemId);

            Assert.Equal(LockRequirementKind.ItemKey, requirement.Kind);
            Assert.Equal(itemId, requirement.ItemId);
            Assert.Null(requirement.FlagKey);
        }

        [Fact]
        public void WorldFlag_CarriesFlagKeyAndExpectedValue()
        {
            var flagKey = new Id("world.bridge.repaired");
            var expected = ExprValue.OfBool(true);

            var requirement = LockRequirement.WorldFlag(flagKey, expected);

            Assert.Equal(LockRequirementKind.WorldFlag, requirement.Kind);
            Assert.Equal(flagKey, requirement.FlagKey);
            Assert.Equal(expected, requirement.Expected);
        }

        [Fact]
        public void SkillCheck_CarriesSkillTagAndMinValue()
        {
            var skillTag = new Id("skill.lockpicking");

            var requirement = LockRequirement.SkillCheck(skillTag, 50);

            Assert.Equal(LockRequirementKind.SkillCheck, requirement.Kind);
            Assert.Equal(skillTag, requirement.SkillTag);
            Assert.Equal(50, requirement.MinValue);
        }
    }
}
