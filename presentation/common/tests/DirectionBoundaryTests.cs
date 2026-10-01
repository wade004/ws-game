using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.Common;
using Xunit;

namespace Tests.PresentationCommon
{
    /// <summary>
    /// 测试覆盖剩余项 T-M43：<see cref="Direction"/> 的负弧度、2π 边界、非法档位数、相等性与哈希一致性；
    /// <see cref="DirectionSlots"/> 的索引环绕、非 <c>dir.</c> 前缀与缺少独立 <c>_l</c> 段的 <c>MirrorSourceOf</c>。
    /// 期望值全部由“档位数 n、扇区角 2π/n”规则算出。
    /// </summary>
    public class DirectionBoundaryTests
    {
        public static readonly TheoryData<int> SupportedCounts = new TheoryData<int> { 4, 8, 16 };

        [Theory]
        [MemberData(nameof(SupportedCounts))]
        public void FromQuantized_ExactSectorCentres_MapToTheirOwnIndex(int n)
        {
            var sector = 2 * Math.PI / n;
            for (var k = 0; k < n; k++)
            {
                Assert.Equal(k, Direction.FromQuantized(k * sector, n).Index);
            }
        }

        [Theory]
        [MemberData(nameof(SupportedCounts))]
        public void FromQuantized_NegativeRadians_WrapToMirroredIndex(int n)
        {
            var sector = 2 * Math.PI / n;
            for (var k = 0; k < n; k++)
            {
                // -k 个扇区 == +(n-k) 个扇区（k=0 折回 0 号桶）。
                var expected = (n - k) % n;
                var direction = Direction.FromQuantized(-k * sector, n);

                Assert.Equal(expected, direction.Index);
                Assert.Equal(-k * sector, direction.RawRadians);
            }
        }

        [Theory]
        [MemberData(nameof(SupportedCounts))]
        public void FromQuantized_FullTurnsAreEquivalent_ToTheBaseAngle(int n)
        {
            var sector = 2 * Math.PI / n;
            for (var k = 0; k < n; k++)
            {
                var baseIndex = Direction.FromQuantized(k * sector, n).Index;

                Assert.Equal(baseIndex, Direction.FromQuantized(k * sector + 2 * Math.PI, n).Index);
                Assert.Equal(baseIndex, Direction.FromQuantized(k * sector - 2 * Math.PI, n).Index);
                Assert.Equal(baseIndex, Direction.FromQuantized(k * sector + 4 * Math.PI, n).Index);
            }
        }

        [Theory]
        [MemberData(nameof(SupportedCounts))]
        public void FromQuantized_ExactlyTwoPi_AndJustBelow_BothFoldToBucketZero(int n)
        {
            Assert.Equal(0, Direction.FromQuantized(2 * Math.PI, n).Index);
            // 2π 前一丁点：最近桶是第 n 号，必须折回 0 而不是越界成 n。
            Assert.Equal(0, Direction.FromQuantized(2 * Math.PI - 1e-9, n).Index);
            Assert.Equal(0, Direction.FromQuantized(-1e-9, n).Index);
        }

        [Theory]
        [MemberData(nameof(SupportedCounts))]
        public void FromQuantized_JustInsideEitherSideOfABucketBoundary_FallsIntoTheNearerBucket(int n)
        {
            var sector = 2 * Math.PI / n;
            const double eps = 1e-6;
            for (var k = 0; k < n; k++)
            {
                var boundary = (k + 0.5) * sector;

                Assert.Equal(k, Direction.FromQuantized(boundary - eps, n).Index);
                Assert.Equal((k + 1) % n, Direction.FromQuantized(boundary + eps, n).Index);
            }
        }

        [Theory]
        [MemberData(nameof(SupportedCounts))]
        public void FromQuantized_IndexIsAlwaysWithinRange_ForArbitraryAngles(int n)
        {
            for (var i = -50; i <= 50; i++)
            {
                var radians = i * 0.7312; // 任意步长，覆盖多圈正负角度
                var index = Direction.FromQuantized(radians, n).Index;

                Assert.InRange(index, 0, n - 1);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(6)]
        [InlineData(12)]
        [InlineData(32)]
        [InlineData(-8)]
        public void FromQuantized_UnsupportedDirectionCount_Throws(int n)
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Direction.FromQuantized(0.0, n));

            Assert.Equal("directionCount", ex.ParamName);
        }

        [Fact]
        public void GetHashCode_EqualDirections_HaveEqualHashes()
        {
            var a = new Direction(1.25, 3, 8);
            var b = new Direction(1.25, 3, 8);

            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.Equal(((object)a).GetHashCode(), ((object)b).GetHashCode());
        }

        [Fact]
        public void GetHashCode_NegativeZeroAndZero_AreEqualAndHashAlike()
        {
            // double.Equals 把 -0.0 与 0.0 视为相等，哈希必须与 Equals 保持一致。
            var positive = new Direction(0.0, 0, 8);
            var negative = new Direction(-0.0, 0, 8);

            Assert.True(positive.Equals(negative));
            Assert.Equal(positive.GetHashCode(), negative.GetHashCode());
        }

        [Fact]
        public void GetHashCode_NaNRadians_IsSelfEqualAndHashStable()
        {
            var a = new Direction(double.NaN, 1, 8);
            var b = new Direction(double.NaN, 1, 8);

            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Equals_Object_RejectsNullAndOtherTypes_AcceptsBoxedEqualValue()
        {
            var a = new Direction(1.0, 2, 8);

            Assert.False(a.Equals(null));
            Assert.False(a.Equals("Direction(raw=1, index=2/8)"));
            Assert.True(a.Equals((object)new Direction(1.0, 2, 8)));
        }

        [Fact]
        public void Equality_DifferingOnlyInRawRadiansOrCount_AreNotEqual()
        {
            var baseline = new Direction(1.0, 2, 8);

            Assert.NotEqual(baseline, new Direction(1.0 + 1e-12, 2, 8));
            Assert.NotEqual(baseline, new Direction(1.0, 2, 16));
            Assert.False(baseline == new Direction(2.0, 2, 8));
        }

        [Fact]
        public void Continuous_HasZeroIndexAndCount_AndIsDistinctFromQuantizedWithSameRadians()
        {
            var continuous = Direction.Continuous(1.0);
            var quantized = Direction.FromQuantized(1.0, 8);

            Assert.NotEqual(continuous, quantized);
            Assert.Equal(0, continuous.DirectionCount);
        }

        // ---------------------------------------------------------------- DirectionSlots

        [Theory]
        [MemberData(nameof(SupportedCounts))]
        public void SlotsFromQuantized_IndexWrapsModuloDirectionCount(int n)
        {
            for (var index = 0; index < n; index++)
            {
                var expected = DirectionSlots.FromQuantized(index, n);

                Assert.Equal(expected, DirectionSlots.FromQuantized(index + n, n));
                Assert.Equal(expected, DirectionSlots.FromQuantized(index + 3 * n, n));
                Assert.Equal(expected, DirectionSlots.FromQuantized(index - n, n));
            }
        }

        [Theory]
        [MemberData(nameof(SupportedCounts))]
        public void SlotsFromQuantized_NegativeIndex_EqualsTheCongruentPositiveIndex(int n)
        {
            for (var k = 1; k <= n; k++)
            {
                Assert.Equal(DirectionSlots.FromQuantized(n - k, n), DirectionSlots.FromQuantized(-k, n));
            }
        }

        [Theory]
        [MemberData(nameof(SupportedCounts))]
        public void SlotsFromQuantized_EveryIndexYieldsADistinctDirPrefixedSlot(int n)
        {
            var seen = new HashSet<Id>();
            for (var index = 0; index < n; index++)
            {
                var slot = DirectionSlots.FromQuantized(index, n);

                Assert.StartsWith(DirectionSlots.IdPrefix, slot.Value, StringComparison.Ordinal);
                Assert.True(seen.Add(slot), $"索引 {index} 产出了与其它索引重复的档位 {slot}");
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        [InlineData(7)]
        [InlineData(12)]
        public void SlotsFromQuantized_UnsupportedDirectionCount_ThrowsWithParamName(int n)
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => DirectionSlots.FromQuantized(0, n));

            Assert.Equal("directionCount", ex.ParamName);
        }

        [Theory]
        [InlineData("dir.left")]
        [InlineData("dir.front_side_left")]
        [InlineData("dir.side_lx")]
        [InlineData("dir.sidel")]
        [InlineData("dir.left_side_r")]
        public void MirrorSourceOf_NoStandaloneLSegment_ReturnsNull(string slot)
        {
            Assert.Null(DirectionSlots.MirrorSourceOf(new Id(slot)));
        }

        [Fact]
        public void MirrorSourceOf_ReplacesOnlyTheLastStandaloneLSegment()
        {
            // 命中规则：按 '_' 切分后“最后一个恰好等于 l”的分段换成 r。
            var result = DirectionSlots.MirrorSourceOf(new Id("dir.l_side_l"));

            Assert.NotNull(result);
            Assert.Equal(new Id("dir.l_side_r"), result!.Value.MirrorOf);
            Assert.True(result.Value.FlipX);
        }

        [Fact]
        public void MirrorSourceOf_SlotWithoutDirPrefix_WithoutLSegment_ReturnsNull()
        {
            Assert.Null(DirectionSlots.MirrorSourceOf(new Id("custom.side_r")));
            Assert.Null(DirectionSlots.MirrorSourceOf(new Id("custom.front")));
        }

        [Fact]
        public void MirrorSourceOf_SlotWithoutDirPrefix_WithLSegment_IsReprefixedWithDirOnTheUnstrippedValue()
        {
            // 现行为（特征化）：StripPrefix 对非 dir. 前缀原样返回，MirrorSourceOf 结果再统一补上 dir. 前缀，
            // 因此 "custom.side_l" -> "dir.custom.side_r"。非 DirectionSlots 产出的 Id 不是本方法的合法输入，
            // 此例只钉住“不抛异常、结果确定”，不把它宣称为受支持用法。
            var result = DirectionSlots.MirrorSourceOf(new Id("custom.side_l"));

            Assert.NotNull(result);
            Assert.Equal(new Id(DirectionSlots.IdPrefix + "custom.side_r"), result!.Value.MirrorOf);
            Assert.True(result.Value.FlipX);
        }

        [Theory]
        [MemberData(nameof(SupportedCounts))]
        public void MirrorSourceOf_OfEveryQuantizedSlot_IsNullExactlyForTheDrawnHalf(int n)
        {
            // 规则：n 个索引里恰有 n/2 - 1 个镜像档位（_l），其余（front/back/_r 族）是原创绘制，没有镜像来源。
            var mirrored = 0;
            for (var index = 0; index < n; index++)
            {
                var slot = DirectionSlots.FromQuantized(index, n);
                var source = DirectionSlots.MirrorSourceOf(slot);
                if (source == null)
                {
                    continue;
                }

                mirrored++;
                // 镜像来源本身必须是原创档位，不再有镜像来源。
                Assert.Null(DirectionSlots.MirrorSourceOf(source.Value.MirrorOf));
            }

            Assert.Equal(n / 2 - 1, mirrored);
        }
    }
}
