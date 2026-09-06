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

        [Fact]
        public void Play_WithPosition_PassesPositionThroughToPlaySfx()
        {
            // ADR-0016 决策 3：IAudio.PlaySfx 增加 position 参数，SfxPlayer.Play 的 at 要透传过去
            // （此前只记录不使用，见该类型历史判断记录，本次已解决）。
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog());

            var handle = player.Play(PlainSfx, new Vec2(3, 4))!.Value;

            Assert.Equal(new Vec2(3, 4), audio.ActiveSfxPlaybacks[handle.Value].Position);
        }

        [Fact]
        public void Play_WithoutPosition_PassesNullPosition()
        {
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog());

            var handle = player.Play(PlainSfx, null)!.Value;

            Assert.Null(audio.ActiveSfxPlaybacks[handle.Value].Position);
        }

        [Fact]
        public void Play_WithResourceLoaderInjected_LoadsResolvedResourceAsAudio_OnlyOnce()
        {
            var audio = new StubAudio();
            var loader = new StubResourceLoader();
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog(), resourceLoader: loader);

            player.Play(PlainSfx, null);
            player.Play(PlainSfx, null);

            var requests = loader.LoadRequests.FindAll(r => r.ResourceId.Equals(new Id("res.footstep")));
            Assert.Single(requests);
            Assert.Equal(Core.Foundation.EngineAdapter.ResourceKind.Audio, requests[0].Kind);
        }

        // -----------------------------------------------------------------
        // 外部审核阻塞项 4 收口回归（architecture/落地计划/audit-20260907/followup-2026-09-07.md
        // "外部审核阻塞项处理"一节）：首次音效加载边界——同 VfxPlayerTests 同款判断记录，此前 Play
        // 只是"发起加载 + 不管成不成功都立即 PlaySfx"，首次引用一个真正异步加载的资源会在资源就绪
        // 前就播放（实际是播放了一份引擎侧尚未就绪的音效资源）。下面三条用例用
        // StubResourceLoader.DeferCallbacks=true 模拟真实异步加载，断言 IAudio.PlaySfx 推迟到加载
        // 完成之后才发生、且恰好一次。
        // -----------------------------------------------------------------

        [Fact]
        public void Play_ResourceNotYetLoaded_DoesNotPlayImmediately_PlaysExactlyOnceAfterLoadCompletes()
        {
            var audio = new StubAudio();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog(), resourceLoader: loader);

            var handle = player.Play(PlainSfx, new Vec2(1, 1));

            Assert.Null(handle); // 资源尚未加载完成，本次调用不能立即拿到真实句柄。
            Assert.Empty(audio.ActiveSfxPlaybacks); // 首次施法命中音效不应该在资源就绪前就播放。

            loader.CompletePending(new Id("res.footstep"));

            Assert.Single(audio.ActiveSfxPlaybacks);
            var playback = audio.ActiveSfxPlaybacks[1];
            Assert.Equal(new Id("res.footstep"), playback.SoundId);
            Assert.Equal(new Vec2(1, 1), playback.Position);
        }

        [Fact]
        public void Play_ResourceLoadFails_DoesNotPlay_RecordsDiagnostic()
        {
            var audio = new StubAudio();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var diagnostics = new PresentationDiagnosticsRecorder();
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog(), diagnostics: diagnostics, resourceLoader: loader);

            player.Play(PlainSfx, null);
            loader.FailPending(new Id("res.footstep"));

            Assert.Empty(audio.ActiveSfxPlaybacks);
            Assert.Contains(diagnostics.Warnings, w => w.Contains("res.footstep") && w.Contains("加载失败"));
        }

        [Fact]
        public void Play_ResourceLoadNeverCompletes_TimesOut_DoesNotPlay_RecordsDiagnostic()
        {
            var audio = new StubAudio();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var diagnostics = new PresentationDiagnosticsRecorder();
            var options = new SfxOptions { FirstLoadTimeoutSeconds = 0.0 };
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog(), options: options, diagnostics: diagnostics, resourceLoader: loader);

            player.Play(PlainSfx, null);

            // ISfxPlayer 没有 Update(dt)（见 SfxOptions.FirstLoadTimeoutSeconds 判断记录），超时清理
            // 改在下一次任意 Play 调用开头惰性扫一遍——FirstLoadTimeoutSeconds=0 使第一次排队请求
            // 立即视为已超时，下一次 Play 调用（哪怕是另一条音效）会在处理自己的请求之前先扫掉它。
            player.Play(VariantSfx, null);

            Assert.DoesNotContain(audio.ActiveSfxPlaybacks.Values, p => p.SoundId.Equals(new Id("res.footstep")));
            Assert.Contains(diagnostics.Warnings, w => w.Contains("res.footstep") && w.Contains("超时"));

            // 迟到的加载完成不应该在超时丢弃之后又补播放一次。
            loader.CompletePending(new Id("res.footstep"));
            Assert.DoesNotContain(audio.ActiveSfxPlaybacks.Values, p => p.SoundId.Equals(new Id("res.footstep")));
        }
    }
}
