// SfxVariantSelectionTests：T-M46（测试覆盖剩余项第四批）——sfx.def.variants 的变体选择。
// 此前只有"固定种子可复现 + 结果属于变体集合"（SfxPlayerTests.Play_WithVariants_PicksReproducibleVariant_
// ForFixedSeed），不能证明"全部变体都选得到"，也不能证明变体选择只走表现层自己的随机流
// （SfxOptions.RngStream）、不扰动 sim 侧的流。这里补两类不变量：
//   1. 可达性：同一 def 反复播放，每个变体都被选中过；每次选择都等于"表现层流上第 n 次
//      NextInt(0, n-1) 的结果"（期望由对照 RngHost 按同一规则算出，不写裸数）。
//   2. 流隔离：SfxPlayer 只触碰 SfxOptions.RngStream 指向的那一条流；sim 流的抽样序列与"没有任何
//      音效播放"的对照宿主逐值一致；无变体 / 单变体不消耗任何随机数。
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Rng;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    public class SfxVariantSelectionTests
    {
        private static readonly Id VariantSfx = new Id("sfx.sword_hit");
        private static readonly Id PlainSfx = new Id("sfx.footstep");
        private static readonly Id SingleVariantSfx = new Id("sfx.single");
        private static readonly Id SimStream = new Id("sim.combat");

        private static readonly Id[] Variants =
        {
            new Id("res.sword_hit_1"), new Id("res.sword_hit_2"), new Id("res.sword_hit_3"), new Id("res.sword_hit_4"),
        };

        private static Dictionary<Id, SfxDef> BuildCatalog() => new Dictionary<Id, SfxDef>
        {
            [VariantSfx] = new SfxDef(VariantSfx, "combat", 5, Variants, Variants[0]),
            [PlainSfx] = new SfxDef(PlainSfx, "combat", 1, null, new Id("res.footstep")),
            [SingleVariantSfx] = new SfxDef(SingleVariantSfx, "combat", 1, new[] { new Id("res.only") }, new Id("res.only")),
        };

        /// <summary>层并发上限放到不会触发抢占的量级，让每次 Play 都真正播放（本测试只关心变体选择）。</summary>
        private static SfxOptions Roomy(Id? stream = null)
        {
            var options = new SfxOptions { DefaultMaxConcurrent = 100000 };
            if (stream.HasValue)
            {
                options.RngStream = stream.Value;
            }
            return options;
        }

        private static List<Id> PlayMany(SfxPlayer player, StubAudio audio, Id sfx, int times)
        {
            var picked = new List<Id>();
            for (var i = 0; i < times; i++)
            {
                var handle = player.Play(sfx, null);
                Assert.NotNull(handle);
                picked.Add(audio.ActiveSfxPlaybacks[handle!.Value.Value].SoundId);
            }
            return picked;
        }

        [Theory]
        [InlineData(1UL)]
        [InlineData(2UL)]
        [InlineData(42UL)]
        [InlineData(20260901UL)]
        public void Play_WithVariants_EveryVariantIsReachable_AndEachPickFollowsThePresentationStream(ulong seed)
        {
            // 次数按"变体数 × 64"取：每个变体在均匀抽样下漏掉的概率可忽略，且种子固定，结果确定。
            var plays = Variants.Length * 64;
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, new RngHost(seed), BuildCatalog(), Roomy());

            var picked = PlayMany(player, audio, VariantSfx, plays);

            // 可达性：每个变体至少被选中一次。
            foreach (var variant in Variants)
            {
                Assert.Contains(variant, picked);
            }

            // 逐次等于对照宿主在同一条流上 NextInt(0, n-1) 的结果。
            var control = new RngHost(seed);
            var stream = new SfxOptions().RngStream;
            for (var i = 0; i < plays; i++)
            {
                var expected = Variants[control.NextInt(stream, 0, Variants.Length - 1)];
                Assert.Equal(expected, picked[i]);
            }
        }

        [Fact]
        public void Play_WithVariants_OnlyTouchesTheConfiguredPresentationStream()
        {
            var rng = new RngHost(7);
            var audio = new StubAudio();
            var options = Roomy();
            var player = new SfxPlayer(audio, rng, BuildCatalog(), options);
            Assert.Empty(rng.Streams);

            PlayMany(player, audio, VariantSfx, 10);

            Assert.Equal(new[] { options.RngStream }, rng.Streams);
        }

        [Fact]
        public void Play_WithVariants_HonorsCustomRngStreamOption()
        {
            var rng = new RngHost(7);
            var audio = new StubAudio();
            var custom = new Id("presentation.sfx_custom");
            var options = Roomy(custom);
            var player = new SfxPlayer(audio, rng, BuildCatalog(), options);

            var picked = PlayMany(player, audio, VariantSfx, 10);

            Assert.Equal(new[] { custom }, rng.Streams);

            // 选择结果取自自定义流：与对照宿主在同名流上的抽样一致。
            var control = new RngHost(7);
            foreach (var pick in picked)
            {
                Assert.Equal(Variants[control.NextInt(custom, 0, Variants.Length - 1)], pick);
            }
        }

        [Fact]
        public void Play_WithVariants_DoesNotPerturbSimStream()
        {
            const ulong seed = 99;
            var withSfx = new RngHost(seed);
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, withSfx, BuildCatalog(), Roomy());
            var untouched = new RngHost(seed);

            // 交错：sim 流抽一次、播一次音效；sim 流的序列必须与从未播放音效的对照宿主逐值一致，
            // 且流状态完全相同。
            for (var i = 0; i < 40; i++)
            {
                var a = withSfx.NextInt(SimStream, 0, 1000);
                PlayMany(player, audio, VariantSfx, 1);
                var b = untouched.NextInt(SimStream, 0, 1000);
                Assert.Equal(b, a);
            }

            Assert.Equal(untouched.GetStreamState(SimStream), withSfx.GetStreamState(SimStream));
        }

        [Fact]
        public void Play_WithoutVariants_ConsumesNoRandomNumbers()
        {
            var rng = new RngHost(7);
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, rng, BuildCatalog(), Roomy());

            PlayMany(player, audio, PlainSfx, 5);

            Assert.Empty(rng.Streams);
        }

        [Fact]
        public void Play_SingleVariant_AlwaysThatVariant_AndConsumesNoRandomNumbers()
        {
            var rng = new RngHost(7);
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, rng, BuildCatalog(), Roomy());

            var picked = PlayMany(player, audio, SingleVariantSfx, 8);

            Assert.All(picked, p => Assert.Equal(new Id("res.only"), p));
            Assert.Empty(rng.Streams); // NextInt(min==max) 不消耗随机数，也不创建流。
        }
    }
}
