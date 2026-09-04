using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Rng;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    public class SfxPlayerTests
    {
        private static readonly Id VariantSfx = new Id("sfx.sword_hit");
        private static readonly Id PlainSfx = new Id("sfx.footstep");

        private static Dictionary<Id, SfxDef> BuildCatalog() => new Dictionary<Id, SfxDef>
        {
            [VariantSfx] = new SfxDef(
                VariantSfx, "combat", 5,
                new[] { new Id("res.sword_hit_1"), new Id("res.sword_hit_2"), new Id("res.sword_hit_3") },
                new Id("res.sword_hit_1")),
            [PlainSfx] = new SfxDef(PlainSfx, "combat", 1, null, new Id("res.footstep")),
        };

        [Fact]
        public void Play_NoVariants_UsesResourceRefDirectly()
        {
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog());

            var handle = player.Play(PlainSfx, null);

            Assert.NotNull(handle);
            Assert.Equal(new Id("res.footstep"), audio.ActiveSfxPlaybacks[handle!.Value.Value].SoundId);
        }

        [Fact]
        public void Play_WithVariants_PicksReproducibleVariant_ForFixedSeed()
        {
            var audio1 = new StubAudio();
            var player1 = new SfxPlayer(audio1, new RngHost(42), BuildCatalog());
            var handle1 = player1.Play(VariantSfx, null)!.Value;
            var picked1 = audio1.ActiveSfxPlaybacks[handle1.Value].SoundId;

            var audio2 = new StubAudio();
            var player2 = new SfxPlayer(audio2, new RngHost(42), BuildCatalog());
            var handle2 = player2.Play(VariantSfx, null)!.Value;
            var picked2 = audio2.ActiveSfxPlaybacks[handle2.Value].SoundId;

            Assert.Equal(picked1, picked2);
            Assert.Contains(picked1, BuildCatalog()[VariantSfx].Variants!);
        }

        [Fact]
        public void Play_UnknownSfxId_ReturnsNullAndWarns()
        {
            var diagnostics = new PresentationDiagnosticsRecorder();
            var player = new SfxPlayer(new StubAudio(), new RngHost(1), BuildCatalog(), diagnostics: diagnostics);

            var handle = player.Play(new Id("sfx.does_not_exist"), null);

            Assert.Null(handle);
            Assert.Single(diagnostics.Warnings);
        }

        [Fact]
        public void Priority_PreemptsLowestPriority_WhenLayerFull()
        {
            var audio = new StubAudio();
            var catalog = new Dictionary<Id, SfxDef>
            {
                [new Id("sfx.low")] = new SfxDef(new Id("sfx.low"), "combat", 1, null, new Id("res.low")),
                [new Id("sfx.high")] = new SfxDef(new Id("sfx.high"), "combat", 9, null, new Id("res.high")),
            };
            var options = new SfxOptions { MaxConcurrentPerLayer = new Dictionary<string, int> { ["combat"] = 1 } };
            var player = new SfxPlayer(audio, new RngHost(1), catalog, options);

            var lowHandle = player.Play(new Id("sfx.low"), null)!.Value;
            Assert.Single(audio.ActiveSfxPlaybacks);

            var highHandle = player.Play(new Id("sfx.high"), null)!.Value;

            // 低优先级被抢占停止，高优先级仍在播放。
            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(lowHandle.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(highHandle.Value));
        }

        [Fact]
        public void SetLayerVolume_AffectsSubsequentPlayVolume()
        {
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog());

            player.SetLayerVolume("combat", 0.4);
            var handle = player.Play(PlainSfx, null)!.Value;

            Assert.Equal(0.4, audio.ActiveSfxPlaybacks[handle.Value].Volume);
        }

        [Fact]
        public void SetLayerMuted_ForcesZeroVolume()
        {
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog());

            player.SetLayerVolume("combat", 0.8);
            player.SetLayerMuted("combat", true);
            var handle = player.Play(PlainSfx, null)!.Value;

            Assert.Equal(0.0, audio.ActiveSfxPlaybacks[handle.Value].Volume);
        }

        [Fact]
        public void Stop_RemovesActivePlayback()
        {
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog());

            var handle = player.Play(PlainSfx, null)!.Value;
            player.Stop(handle);

            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(handle.Value));
        }
    }
}
