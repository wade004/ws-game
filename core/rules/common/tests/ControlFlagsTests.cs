using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    public class ControlFlagsTests
    {
        [Fact]
        public void Combination_ContainsBothFlags()
        {
            var combo = ControlFlags.NoMove | ControlFlags.NoCast;

            Assert.True((combo & ControlFlags.NoMove) == ControlFlags.NoMove);
            Assert.True((combo & ControlFlags.NoCast) == ControlFlags.NoCast);
            Assert.False((combo & ControlFlags.NoAttack) == ControlFlags.NoAttack);
        }

        [Fact]
        public void None_HasNoBitsSet()
        {
            Assert.Equal(0, (int)ControlFlags.None);
        }

        [Fact]
        public void AllFourFlags_AreDistinctBits()
        {
            var all = ControlFlags.NoMove | ControlFlags.NoCast | ControlFlags.NoAttack | ControlFlags.NoInteract;

            Assert.Equal(1 + 2 + 4 + 8, (int)all);
        }
    }
}
