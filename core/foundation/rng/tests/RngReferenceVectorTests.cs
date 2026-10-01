using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Rng;
using Xunit;

namespace Tests.Foundation.Rng
{
    /// <summary>
    /// 对照公开参考向量的 RNG 锚点（T-L1，2026-10-01 测试覆盖第四批）。
    /// <para>
    /// 为什么这些锚点比 <c>Next_KnownVectorRegressionAnchor</c> 更强：后者的期望值是本实现首次跑出后写死的，
    /// 实现算法写错也会"自洽"。这里的期望值来自实现之外：
    /// ① xoshiro256** 官方参考实现在状态 <c>{1,2,3,4}</c> 下的前 10 个输出（Blackman/Vigna 参考 C 实现，
    /// 同一向量也出现在常见移植库的单元测试里）；② SplitMix64 在种子 0 下的前 4 个输出
    /// （<c>E220A8397B1DCDAF 6E789E6AA1B965F4 06C45D188009454F F88BB8A8724C81EC</c>，公开参考向量）；
    /// ③ FNV-1a 64 位 / 种子混合按定义手工推算（见各用例注释）。
    /// </para>
    /// 内部类型 <c>Xoshiro256StarStar</c>/<c>SeedDerivation</c> 不对测试程序集可见，
    /// 一律经 <see cref="RngHost.SetStreamState"/> / <see cref="RngHost.GetStreamState"/> / <see cref="RngHost.Next"/> 观察。
    /// </summary>
    public sealed class RngReferenceVectorTests
    {
        private static readonly Id Stream = new Id("rng_test.reference");

        // xoshiro256** 参考输出，初始状态 s = {1, 2, 3, 4}。
        private static readonly ulong[] Xoshiro1234 =
        {
            11520UL,
            0UL,
            1509978240UL,
            1215971899390074240UL,
            1216172134540287360UL,
            607988272756665600UL,
            16172922978634559625UL,
            8476171486693032832UL,
            10595114339597558777UL,
            2904607092377533576UL,
        };

        private static RngHost HostAtReferenceState()
        {
            var host = new RngHost(0UL);
            host.SetStreamState(Stream, new RngStreamState(1UL, 2UL, 3UL, 4UL));
            return host;
        }

        // Next() 把 64 位输出的高 53 位映射到 [0,1)：值 = (x >> 11) / 2^53。
        private static double ToUnit(ulong x) => (x >> 11) * (1.0 / (1UL << 53));

        [Fact]
        public void Next_FromReferenceState_MatchesXoshiro256StarStarReferenceOutputs()
        {
            var host = HostAtReferenceState();

            foreach (var raw in Xoshiro1234)
            {
                Assert.Equal(ToUnit(raw), host.Next(Stream));
            }
        }

        [Fact]
        public void Next_FromReferenceState_StateAdvancesLikeReference()
        {
            // 官方算法一步后的状态（Python 独立实现复算）：{1,2,3,4} → 前进一步。
            // 手算：t = s1<<17 = 2<<17 = 262144；s2 ^= s0 → 2；s3 ^= s1 → 6；s1 ^= s2 → 0；s0 ^= s3 → 7；
            // s2 ^= t → 262146；s3 = rotl(6, 45) = 6 << 45。
            var host = HostAtReferenceState();

            host.Next(Stream);
            var state = host.GetStreamState(Stream);

            Assert.Equal(7UL, state.S0);
            Assert.Equal(0UL, state.S1);
            Assert.Equal(262146UL, state.S2);
            Assert.Equal(6UL << 45, state.S3);
        }

        [Fact]
        public void NextInt_FromReferenceState_FollowsRejectionSamplingRuleOverReferenceOutputs()
        {
            // NextInt(0, 99)：range = 100，拒绝阈值 t = 2^64 mod 100；输出 < t 被丢弃并抽下一个，否则取 out % 100。
            const ulong range = 100UL;
            var threshold = unchecked(0UL - range) % range;
            var expected = new List<int>();
            foreach (var raw in Xoshiro1234)
            {
                if (raw >= threshold)
                {
                    expected.Add((int)(raw % range));
                }
            }

            // 参考向量里确有一个输出（0）落在拒绝区，才能证明"丢弃并重抽"这条路径被走到。
            Assert.True(expected.Count < Xoshiro1234.Length);

            var host = HostAtReferenceState();
            var actual = new List<int>();
            for (var i = 0; i < expected.Count; i++)
            {
                actual.Add(host.NextInt(Stream, 0, 99));
            }

            Assert.Equal(expected, actual);

            // 10 个原始输出已全部消耗：状态应等于另一台主机用 Next() 前进 10 步后的状态。
            var control = HostAtReferenceState();
            for (var i = 0; i < Xoshiro1234.Length; i++)
            {
                control.Next(Stream);
            }

            Assert.Equal(control.GetStreamState(Stream), host.GetStreamState(Stream));
        }

        [Fact]
        public void NextInt_OffsetIsAddedToMin_ForNegativeAndShiftedRanges()
        {
            // 范围 [-50, 49]：range = 100；同一参考输出，结果 = min + out % 100。
            const ulong range = 100UL;
            var threshold = unchecked(0UL - range) % range;
            var host = HostAtReferenceState();

            var first = host.NextInt(Stream, -50, 49);

            Assert.Equal(-50 + (int)(Xoshiro1234[0] % range), first);
            Assert.True(Xoshiro1234[0] >= threshold);
        }

        [Fact]
        public void NextInt_DegenerateRange_DoesNotConsumeRandomness()
        {
            var host = HostAtReferenceState();
            var before = host.GetStreamState(Stream);

            Assert.Equal(7, host.NextInt(Stream, 7, 7));

            Assert.Equal(before, host.GetStreamState(Stream));
        }

        [Fact]
        public void NextInt_MinGreaterThanMax_ThrowsOnMinParam_WithoutConsumingRandomness()
        {
            var host = HostAtReferenceState();
            var before = host.GetStreamState(Stream);

            var ex = Assert.Throws<ArgumentException>(() => host.NextInt(Stream, 5, 4));

            Assert.Equal("min", ex.ParamName);
            Assert.Equal(before, host.GetStreamState(Stream));
        }

        [Fact]
        public void NextInt_TwoValueRangeAtIntExtremes_StaysWithinBounds()
        {
            var host = new RngHost(11UL);
            for (var i = 0; i < 200; i++)
            {
                var low = host.NextInt(Stream, int.MinValue, int.MinValue + 1);
                var high = host.NextInt(Stream, int.MaxValue - 1, int.MaxValue);
                Assert.InRange(low, int.MinValue, int.MinValue + 1);
                Assert.InRange(high, int.MaxValue - 1, int.MaxValue);
            }
        }

        // -----------------------------------------------------------------
        // SeedDerivation：种子 = masterSeed XOR FNV-1a64(UTF-8(stream))，再 SplitMix64 四次
        // -----------------------------------------------------------------

        [Fact]
        public void DerivedInitialState_WhenMixedSeedIsZero_EqualsSplitMix64ReferenceOutputs()
        {
            // FNV-1a64("rng_test.sm0") = 16711043889334768889（按定义逐字节手算复核，独立 Python 实现）。
            // 令 masterSeed = 该值 ⇒ 混合后种子 = 0 ⇒ 状态 = SplitMix64(seed 0) 的前 4 个公开参考输出。
            const ulong fnvOfStream = 16711043889334768889UL;
            var host = new RngHost(fnvOfStream);

            var state = host.GetStreamState(new Id("rng_test.sm0"));

            Assert.Equal(0xE220A8397B1DCDAFUL, state.S0);
            Assert.Equal(0x6E789E6AA1B965F4UL, state.S1);
            Assert.Equal(0x06C45D188009454FUL, state.S2);
            Assert.Equal(0xF88BB8A8724C81ECUL, state.S3);
        }

        [Fact]
        public void DerivedInitialState_FirstSplitMixOutputZero_IsNotMistakenForAllZeroState()
        {
            // 构造 masterSeed 使 SplitMix64 第一个输出恰为 0：混合后种子取 0x61C8864680B583EB（SplitMix64 输出变换的
            // 逆运算得出，独立 Python 复算），masterSeed = 该种子 XOR FNV-1a64("rng_test.zero_first")
            // （FNV 值 12832555816643473944）：S0 = 0，其余三个字依次为 SplitMix64 的后续输出，均非 0。
            // 全零兜底（S0 := 黄金比例常量）只在四个字全为 0 时触发，这里不得被误触发。
            // 注：四个输出同时为 0 在数学上不可达（SplitMix64 的输出变换对状态是双射，
            // 同一变换在四个不同状态上不可能同时为 0），兜底是防御性死代码，没有合法输入能触发它。
            const ulong master = 0x61C8864680B583EBUL ^ 12832555816643473944UL;
            var host = new RngHost(master);

            var state = host.GetStreamState(new Id("rng_test.zero_first"));

            Assert.Equal(0UL, state.S0);
            Assert.Equal(16294208416658607535UL, state.S1);
            Assert.Equal(7960286522194355700UL, state.S2);
            Assert.Equal(487617019471545679UL, state.S3);
        }

        [Fact]
        public void DerivedInitialState_DependsOnBothMasterSeedAndStreamName()
        {
            var a = new RngHost(1UL).GetStreamState(new Id("rng_test.a"));
            var sameAgain = new RngHost(1UL).GetStreamState(new Id("rng_test.a"));
            var otherSeed = new RngHost(2UL).GetStreamState(new Id("rng_test.a"));
            var otherStream = new RngHost(1UL).GetStreamState(new Id("rng_test.b"));

            Assert.Equal(a, sameAgain);
            Assert.NotEqual(a, otherSeed);
            Assert.NotEqual(a, otherStream);
        }

        // -----------------------------------------------------------------
        // SetStreamState
        // -----------------------------------------------------------------

        [Fact]
        public void SetStreamState_OnUnknownStream_CreatesIt_AndGetReturnsExactlyThatState()
        {
            var host = new RngHost(3UL);
            Assert.Empty(host.Streams);

            var state = new RngStreamState(11UL, 22UL, 33UL, 44UL);
            host.SetStreamState(Stream, state);

            Assert.Equal(new[] { Stream }, host.Streams);
            Assert.Equal(state, host.GetStreamState(Stream));
        }

        [Fact]
        public void SetStreamState_OnExistingStream_ReplacesStateInPlace_WithoutDuplicatingStream()
        {
            var host = new RngHost(3UL);
            host.Next(Stream);
            var replacement = new RngStreamState(1UL, 2UL, 3UL, 4UL);

            host.SetStreamState(Stream, replacement);

            Assert.Single(host.Streams);
            Assert.Equal(ToUnit(Xoshiro1234[0]), host.Next(Stream));
        }

        [Fact]
        public void SetStreamState_DoesNotDisturbOtherStreams()
        {
            var host = new RngHost(9UL);
            var other = new Id("rng_test.other");
            host.Next(other);
            var otherBefore = host.GetStreamState(other);

            host.SetStreamState(Stream, new RngStreamState(1UL, 2UL, 3UL, 4UL));

            Assert.Equal(otherBefore, host.GetStreamState(other));
        }

        [Fact]
        public void SetStreamState_ThenReset_ForgetsStream_AndRederivesFromNewMasterSeed()
        {
            var host = new RngHost(3UL);
            host.SetStreamState(Stream, new RngStreamState(1UL, 2UL, 3UL, 4UL));

            host.Reset(3UL);

            Assert.Empty(host.Streams);
            Assert.Equal(new RngHost(3UL).GetStreamState(Stream), host.GetStreamState(Stream));
        }

        [Fact]
        public void SetStreamState_AllZeroState_Characterization_StreamStaysAtZeroForever()
        {
            // 特征化（待设计层确认）：全零状态是 xoshiro256** 的不动点，Next() 恒为 0.0 且状态永不离开全零。
            // SeedDerivation 会在派生时避开它，但 SetStreamState / RngStreamState.Parse 不拒绝全零状态，
            // 一份被写坏成全零的存档会让该流之后的所有抽样静默恒为 0。是否应在 Set/Load 路径拒绝全零，属设计决定；
            // 此用例只固定当前可观测事实。
            var host = new RngHost(1UL);
            host.SetStreamState(Stream, new RngStreamState(0UL, 0UL, 0UL, 0UL));

            for (var i = 0; i < 20; i++)
            {
                Assert.Equal(0.0, host.Next(Stream));
            }

            Assert.Equal(new RngStreamState(0UL, 0UL, 0UL, 0UL), host.GetStreamState(Stream));
        }
    }
}
