using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Rng;
using Xunit;

namespace Tests.Foundation.Rng
{
    public class RngHostTests
    {
        private static readonly Id StreamA = new Id("rng_test.stream_a");
        private static readonly Id StreamB = new Id("rng_test.stream_b");

        [Fact]
        public void SameMasterSeedSameStream_ProducesIdenticalSequence()
        {
            var hostOne = new RngHost(12345UL);
            var hostTwo = new RngHost(12345UL);

            for (var i = 0; i < 1000; i++)
            {
                Assert.Equal(hostOne.Next(StreamA), hostTwo.Next(StreamA));
            }
        }

        [Fact]
        public void DifferentStreams_DoNotInterfereWithEachOther()
        {
            // 对照：只取流 B 的一段序列。
            var control = new RngHost(999UL);
            var expected = new double[200];
            for (var i = 0; i < expected.Length; i++)
            {
                expected[i] = control.Next(StreamB);
            }

            // 实验：交替从流 A、流 B 取值（在两次流 B 取值之间穿插大量流 A 取值），
            // 流 B 自身的序列应与对照完全一致，不受流 A 访问次数影响。
            var subject = new RngHost(999UL);
            var actual = new double[200];
            for (var i = 0; i < expected.Length; i++)
            {
                for (var j = 0; j < 7; j++)
                {
                    subject.Next(StreamA);
                }

                actual[i] = subject.Next(StreamB);
            }

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void DifferentMasterSeeds_ProduceDifferentSequences()
        {
            var hostOne = new RngHost(1UL);
            var hostTwo = new RngHost(2UL);

            var sequenceOne = new double[20];
            var sequenceTwo = new double[20];
            for (var i = 0; i < 20; i++)
            {
                sequenceOne[i] = hostOne.Next(StreamA);
                sequenceTwo[i] = hostTwo.Next(StreamA);
            }

            Assert.NotEqual(sequenceOne, sequenceTwo);
        }

        [Fact]
        public void GetAndSetStreamState_RoundTripsIdenticalContinuation()
        {
            var host = new RngHost(42UL);

            // 预热若干次，确保状态不处于"刚派生"的初始特殊位置。
            for (var i = 0; i < 37; i++)
            {
                host.Next(StreamA);
            }

            var savedState = host.GetStreamState(StreamA);

            var firstRun = new double[100];
            for (var i = 0; i < firstRun.Length; i++)
            {
                firstRun[i] = host.Next(StreamA);
            }

            host.SetStreamState(StreamA, savedState);

            var secondRun = new double[100];
            for (var i = 0; i < secondRun.Length; i++)
            {
                secondRun[i] = host.Next(StreamA);
            }

            Assert.Equal(firstRun, secondRun);
        }

        [Fact]
        public void RngStreamState_TextRoundTrips()
        {
            var host = new RngHost(7UL);
            host.Next(StreamA); // 触发懒创建并推进，避免测试恰好覆盖全零初始状态这种特例
            var state = host.GetStreamState(StreamA);

            var text = state.ToString();
            var parsed = RngStreamState.Parse(text);

            Assert.Equal(state, parsed);
            Assert.Equal(text, parsed.ToString());
        }

        [Fact]
        public void RngStreamState_TryParse_RejectsIllegalText()
        {
            Assert.False(RngStreamState.TryParse(null, out _));
            Assert.False(RngStreamState.TryParse("", out _));
            Assert.False(RngStreamState.TryParse("only-two-segments", out _));
            Assert.False(RngStreamState.TryParse("zz-0000000000000000-0000000000000000-0000000000000000", out _));
            Assert.False(RngStreamState.TryParse("0-0-0-0-0", out _));
        }

        /// <summary>P3-01 收口回归：此前 <c>TryParseSegment</c> 只用 <c>ulong.TryParse</c> 校验能否
        /// 解析为十六进制数，未检查段长度和大小写，<c>"0-0-0-0"</c> 这类非规范短文本也会被接受
        /// （见 rng/README.md 第 51～53 行的定长规范文本格式承诺）。现在应逐段拒绝短段、长段、
        /// 大写字母，只接受 16 个小写十六进制字符。</summary>
        [Fact]
        public void RngStreamState_TryParse_RejectsNonCanonicalSegmentLengthAndCase()
        {
            // 短段：数值上可能代表合法状态，但绕过了定长规范格式。
            Assert.False(RngStreamState.TryParse("0-0-0-0", out _));
            Assert.False(RngStreamState.TryParse("1-1-1-1", out _));

            // 长段：超过 16 位十六进制字符。
            Assert.False(RngStreamState.TryParse(
                "00000000000000001-0000000000000000-0000000000000000-0000000000000000", out _));

            // 大写字母：ToString 恒输出小写，Parse 应严格对称，不接受大写变体。
            Assert.False(RngStreamState.TryParse(
                "AAAAAAAAAAAAAAAA-0000000000000000-0000000000000000-0000000000000000", out _));

            // 恰好 16 位小写十六进制的规范文本应被接受（正对照，避免上面全部断言"假阳性通过"）。
            Assert.True(RngStreamState.TryParse(
                "0000000000000001-0000000000000000-0000000000000000-0000000000000000", out var parsed));
            Assert.Equal(1UL, parsed.S0);
        }

        /// <summary>P1-03/P1-04 收口新增：<see cref="IRngHost.MasterSeed"/> 应原样返回构造时传入的
        /// 主种子，<see cref="IRngHost.Reset"/> 后应更新为新种子——供
        /// <see cref="Core.Foundation.SaveSystem.RngStreamsPersistable"/> 存读该值。</summary>
        [Fact]
        public void MasterSeed_ReflectsConstructorArgument_AndUpdatesAfterReset()
        {
            var host = new RngHost(42UL);
            Assert.Equal(42UL, host.MasterSeed);

            host.Next(StreamA);
            Assert.Equal(42UL, host.MasterSeed); // 消耗随机数不改变主种子。

            host.Reset(999UL);
            Assert.Equal(999UL, host.MasterSeed);
        }

        [Fact]
        public void Next_AlwaysFallsWithinZeroToOneExclusiveUpperBound()
        {
            var host = new RngHost(2024UL);
            for (var i = 0; i < 100_000; i++)
            {
                var value = host.Next(StreamA);
                Assert.True(value >= 0.0 && value < 1.0, $"越界值：{value}");
            }
        }

        [Fact]
        public void NextInt_UniformRangeCoversAllValuesWithinTolerance()
        {
            var host = new RngHost(2024UL);
            var counts = new Dictionary<int, int>
            {
                [1] = 0, [2] = 0, [3] = 0, [4] = 0, [5] = 0, [6] = 0,
            };

            const int trials = 100_000;
            for (var i = 0; i < trials; i++)
            {
                var roll = host.NextInt(StreamA, 1, 6);
                Assert.True(roll >= 1 && roll <= 6, $"越界值：{roll}");
                counts[roll]++;
            }

            const double expectedFraction = 1.0 / 6.0;
            const double tolerance = 0.05;
            foreach (var kvp in counts)
            {
                Assert.True(kvp.Value > 0, $"点数 {kvp.Key} 从未出现");
                var fraction = (double)kvp.Value / trials;
                Assert.True(
                    Math.Abs(fraction - expectedFraction) <= tolerance,
                    $"点数 {kvp.Key} 频率 {fraction:F4} 偏离期望 {expectedFraction:F4} 超过 ±{tolerance}");
            }
        }

        [Fact]
        public void NextInt_DegenerateRange_AlwaysReturnsThatSingleValue()
        {
            var host = new RngHost(2024UL);
            for (var i = 0; i < 50; i++)
            {
                Assert.Equal(5, host.NextInt(StreamA, 5, 5));
            }
        }

        [Fact]
        public void NextInt_FullIntRange_DoesNotOverflow()
        {
            var host = new RngHost(2024UL);
            for (var i = 0; i < 10_000; i++)
            {
                var value = host.NextInt(StreamA, int.MinValue, int.MaxValue);
                Assert.True(value >= int.MinValue && value <= int.MaxValue);
            }
        }

        [Fact]
        public void NextInt_MinGreaterThanMax_ThrowsArgumentException()
        {
            var host = new RngHost(2024UL);
            Assert.Throws<ArgumentException>(() => host.NextInt(StreamA, 10, 1));
        }

        [Fact]
        public void Next_KnownVectorRegressionAnchor()
        {
            // 回归锚点：固定主种子 424242、流 "rng_test.anchor" 下前 3 个 Next() 输出值。
            // 数值由本模块实现首次跑出后写死于此，作为算法/派生方式是否发生非预期变化的回归哨兵；
            // 若确定性地有意调整了 xoshiro256** 实现或 SeedDerivation 派生方式，需要重新跑一次并
            // 有意更新下面三个常量（不允许为了让测试通过而"顺手"改掉，必须先确认变更是预期内的）。
            var host = new RngHost(424242UL);
            var anchorStream = new Id("rng_test.anchor");

            var v0 = host.Next(anchorStream);
            var v1 = host.Next(anchorStream);
            var v2 = host.Next(anchorStream);

            Assert.Equal(0.38254097611449867, v0, 15);
            Assert.Equal(0.051181279499073695, v1, 15);
            Assert.Equal(0.61542849445511649, v2, 15);
        }

        [Fact]
        public void Streams_AreOrderedByIdOrdinal_AndResetClearsThem()
        {
            var host = new RngHost(1UL);
            var idZ = new Id("rng_test.zzz");
            var idA = new Id("rng_test.aaa");
            var idM = new Id("rng_test.mmm");

            host.Next(idZ);
            host.Next(idA);
            host.Next(idM);

            Assert.Equal(new[] { idA, idM, idZ }, host.Streams);

            host.Reset(2UL);

            Assert.Empty(host.Streams);
        }
    }
}
