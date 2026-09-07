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

            // C07 根治前的历史行为（本用例仍然覆盖）：超时清理此前只能在下一次任意 Play 调用开头
            // 惰性扫一遍——FirstLoadTimeoutSeconds=0 使第一次排队请求立即视为已超时，下一次 Play
            // 调用（哪怕是另一条音效）会在处理自己的请求之前先扫掉它。C07 根治后 <see
            // cref="ISfxPlayer.Update"/> 提供了不依赖下一次 Play 的独立时钟入口（见
            // <see cref="Update_TimesOut_TriggersPendingPlayCountChanged_WithoutAnyFurtherPlayCall"/>），
            // 但 Play 开头的惰性扫描仍然保留（双保险，不冲突）。
            player.Play(VariantSfx, null);

            Assert.DoesNotContain(audio.ActiveSfxPlaybacks.Values, p => p.SoundId.Equals(new Id("res.footstep")));
            Assert.Contains(diagnostics.Warnings, w => w.Contains("res.footstep") && w.Contains("超时"));

            // 迟到的加载完成不应该在超时丢弃之后又补播放一次。
            loader.CompletePending(new Id("res.footstep"));
            Assert.DoesNotContain(audio.ActiveSfxPlaybacks.Values, p => p.SoundId.Equals(new Id("res.footstep")));
        }

        // -----------------------------------------------------------------
        // GP-09 复现与回归（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        // PendingPlayCount 供 CompositeFeedbackSink/FeedbackBinder.HasPendingPlayback 把"首次
        // 异步加载中的 sfx"计入离散步表现完成门。
        // -----------------------------------------------------------------

        [Fact]
        public void PendingPlayCount_ZeroWhenNothingQueued()
        {
            var player = new SfxPlayer(new StubAudio(), new RngHost(1), BuildCatalog());

            Assert.Equal(0, player.PendingPlayCount);
        }

        [Fact]
        public void PendingPlayCount_OneWhileLoading_ZeroAfterLoadCompletes()
        {
            var audio = new StubAudio();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog(), resourceLoader: loader);

            player.Play(PlainSfx, new Vec2(1, 1));
            Assert.Equal(1, player.PendingPlayCount);

            loader.CompletePending(new Id("res.footstep"));
            Assert.Equal(0, player.PendingPlayCount);
        }

        /// <summary>
        /// C07 复现与根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：此前
        /// <see cref="ISfxPlayer"/> 没有任何时钟驱动入口，卡死不回调的排队请求只能在"下一次任意
        /// <see cref="ISfxPlayer.Play"/> 调用"开头被惰性扫到——若节奏门已经关闭、此后没有任何新的
        /// Play 调用（典型场景：这是这一步唯一的音效，玩家没有触发任何后续动作），超时永远不会被
        /// 发现、<see cref="ISfxPlayer.PendingPlayCountChanged"/> 永远不会因超时而触发，
        /// wait_for_playback 节奏门永久卡死。根治后 <see cref="ISfxPlayer.Update"/> 提供独立于
        /// <see cref="ISfxPlayer.Play"/> 的时钟驱动入口（同 <c>IVfxPlayer.Update</c> 的既有生产
        /// 接线，由 <c>FrameworkResidentHost.OnFrameTick</c> 逐帧驱动）——本用例全程不调用一次
        /// <see cref="ISfxPlayer.Play"/>，只靠 <see cref="ISfxPlayer.Update"/> 就能发现超时并恰好
        /// 触发一次完成信号。
        /// </summary>
        [Fact]
        public void Update_TimesOut_TriggersPendingPlayCountChanged_WithoutAnyFurtherPlayCall()
        {
            var audio = new StubAudio();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var diagnostics = new PresentationDiagnosticsRecorder();
            var options = new SfxOptions { FirstLoadTimeoutSeconds = 0.0 };
            var player = new SfxPlayer(audio, new RngHost(1), BuildCatalog(), options: options, diagnostics: diagnostics, resourceLoader: loader);

            player.Play(PlainSfx, null); // 唯一一次 Play——之后再也不会有任何 Play 调用。
            Assert.Equal(1, player.PendingPlayCount);

            var changedCount = 0;
            player.PendingPlayCountChanged += () => changedCount++;

            // 不调用 Play，只靠 Update（同 IVfxPlayer.Update 的引擎侧逐帧驱动惯例）发现超时。
            player.Update(0.016);

            Assert.Equal(0, player.PendingPlayCount);
            Assert.Equal(1, changedCount);
            Assert.Contains(diagnostics.Warnings, w => w.Contains("res.footstep") && w.Contains("超时"));

            player.Update(0.016); // 已经没有 pending 项了，后续 Update 不应再触发。
            Assert.Equal(1, changedCount);
        }
    }
}
