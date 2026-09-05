using Core.Foundation.Common;
using Presentation.Common;
using Xunit;

namespace Tests.PresentationCommon
{
    /// <summary>
    /// <see cref="DirectionSlots"/> 的单元测试（P4-2）：量化索引 → 14 第 2.1 节档位命名的对应表、
    /// 默认镜像回退表。索引→档位的推导见该类型 <see cref="DirectionSlots.FromQuantized"/> 的类型
    /// 注释与模块 README"索引→档位对应表"一节。
    /// </summary>
    public class DirectionSlotsTests
    {
        [Theory]
        [InlineData(0, "dir.side_l")]
        [InlineData(1, "dir.front_side_l")]
        [InlineData(2, "dir.front")]
        [InlineData(3, "dir.front_side_r")]
        [InlineData(4, "dir.side_r")]
        [InlineData(5, "dir.back_side_r")]
        [InlineData(6, "dir.back")]
        [InlineData(7, "dir.back_side_l")]
        public void FromQuantized_EightDirections_MatchesNamingTable(int index, string expected)
        {
            Assert.Equal(new Id(expected), DirectionSlots.FromQuantized(index, 8));
        }

        [Theory]
        [InlineData(0, "dir.side_l")]
        [InlineData(1, "dir.front")]
        [InlineData(2, "dir.side_r")]
        [InlineData(3, "dir.back")]
        public void FromQuantized_FourDirections_MatchesNamingTable(int index, string expected)
        {
            Assert.Equal(new Id(expected), DirectionSlots.FromQuantized(index, 4));
        }

        [Theory]
        [InlineData(4, "dir.front")]
        [InlineData(5, "dir.front_side_r_a")]
        [InlineData(6, "dir.front_side_r")]
        [InlineData(7, "dir.front_side_r_b")]
        [InlineData(8, "dir.side_r")]
        [InlineData(9, "dir.back_side_r_b")]
        [InlineData(10, "dir.back_side_r")]
        [InlineData(11, "dir.back_side_r_a")]
        [InlineData(12, "dir.back")]
        [InlineData(13, "dir.back_side_l_a")]
        [InlineData(14, "dir.back_side_l")]
        [InlineData(15, "dir.back_side_l_b")]
        [InlineData(0, "dir.side_l")]
        [InlineData(1, "dir.front_side_l_b")]
        [InlineData(2, "dir.front_side_l")]
        [InlineData(3, "dir.front_side_l_a")]
        public void FromQuantized_SixteenDirections_MatchesNamingTable(int index, string expected)
        {
            Assert.Equal(new Id(expected), DirectionSlots.FromQuantized(index, 16));
        }

        [Fact]
        public void FromQuantized_UnsupportedDirectionCount_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => DirectionSlots.FromQuantized(0, 6));
        }

        [Theory]
        [InlineData("dir.front")]
        [InlineData("dir.back")]
        [InlineData("dir.front_side_r")]
        [InlineData("dir.side_r")]
        [InlineData("dir.back_side_r")]
        public void MirrorSourceOf_CanonicalSlots_ReturnNull(string slot)
        {
            Assert.Null(DirectionSlots.MirrorSourceOf(new Id(slot)));
        }

        [Theory]
        [InlineData("dir.front_side_l", "dir.front_side_r")]
        [InlineData("dir.side_l", "dir.side_r")]
        [InlineData("dir.back_side_l", "dir.back_side_r")]
        [InlineData("dir.front_side_l_a", "dir.front_side_r_a")]
        [InlineData("dir.back_side_l_b", "dir.back_side_r_b")]
        public void MirrorSourceOf_LSlots_ReturnCorrespondingRSlot_WithFlipX(string lSlot, string expectedRSlot)
        {
            var result = DirectionSlots.MirrorSourceOf(new Id(lSlot));

            Assert.NotNull(result);
            Assert.Equal(new Id(expectedRSlot), result!.Value.MirrorOf);
            Assert.True(result.Value.FlipX);
        }

        [Fact]
        public void StripPrefix_RemovesDirPrefix()
        {
            Assert.Equal("front_side_r", DirectionSlots.StripPrefix(DirectionSlots.FrontSideR));
        }

        [Fact]
        public void StripPrefix_ValueWithoutPrefix_ReturnsUnchanged()
        {
            Assert.Equal("no_prefix.value", DirectionSlots.StripPrefix(new Id("no_prefix.value")));
        }

        [Fact]
        public void NamedConstants_MatchFromQuantizedResults()
        {
            Assert.Equal(DirectionSlots.Front, DirectionSlots.FromQuantized(2, 8));
            Assert.Equal(DirectionSlots.FrontSideR, DirectionSlots.FromQuantized(3, 8));
            Assert.Equal(DirectionSlots.FrontSideL, DirectionSlots.FromQuantized(1, 8));
            Assert.Equal(DirectionSlots.SideR, DirectionSlots.FromQuantized(4, 8));
            Assert.Equal(DirectionSlots.SideL, DirectionSlots.FromQuantized(0, 8));
            Assert.Equal(DirectionSlots.BackSideR, DirectionSlots.FromQuantized(5, 8));
            Assert.Equal(DirectionSlots.BackSideL, DirectionSlots.FromQuantized(7, 8));
            Assert.Equal(DirectionSlots.Back, DirectionSlots.FromQuantized(6, 8));
        }
    }
}
