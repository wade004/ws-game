using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Rng;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>
    /// GP-09 复现与回归（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
    /// <see cref="CompositeFeedbackSink.HasPendingPlayback"/> 端到端串联真实 <see cref="VfxPlayer"/>/
    /// <see cref="SfxPlayer"/>（配合 <see cref="StubResourceLoader"/> 模拟真实异步加载）——
    /// 确认 <c>PlayVfx</c>/<c>PlaySfx</c> 命中冷资源时，本类型能如实反映"仍有播放请求排队等待"，
    /// 而不是像修复前那样完全不提供这个信号。
    /// </summary>
    public class CompositeFeedbackSinkTests
    {
        private static readonly Id WorldVfx = new Id("vfx.world_spark");
        private static readonly Id PlainSfx = new Id("sfx.footstep");

        private static Dictionary<Id, VfxDef> VfxCatalog() => new Dictionary<Id, VfxDef>
        {
            [WorldVfx] = new VfxDef(WorldVfx, "impact", VfxAttachMode.World, null, new Id("res.spark")),
        };

        private static Dictionary<Id, SfxDef> SfxCatalog() => new Dictionary<Id, SfxDef>
        {
            [PlainSfx] = new SfxDef(PlainSfx, "combat", 1, null, new Id("res.footstep")),
        };

        private static CompositeFeedbackSink BuildSink(IVfxPlayer vfx, ISfxPlayer sfx) => new CompositeFeedbackSink(
            vfx, sfx,
            onFloatingText: (_, __, ___) => { },
            onFreeze: _ => { },
            onShakeCamera: _ => { },
            onFlash: (_, __) => { });

        [Fact]
        public void HasPendingPlayback_FalseWhenNothingPlayedYet()
        {
            var vfx = new VfxPlayer(new StubRenderer2D(), new StubCamera(), VfxCatalog());
            var sfx = new SfxPlayer(new StubAudio(), new RngHost(1), SfxCatalog());
            var sink = BuildSink(vfx, sfx);

            Assert.False(sink.HasPendingPlayback);
        }

        [Fact]
        public void HasPendingPlayback_TrueWhileVfxFirstLoadPending_FalseAfterCompletes()
        {
            var vfxLoader = new StubResourceLoader { DeferCallbacks = true };
            var vfx = new VfxPlayer(new StubRenderer2D(), new StubCamera(), VfxCatalog(), resourceLoader: vfxLoader);
            var sfx = new SfxPlayer(new StubAudio(), new RngHost(1), SfxCatalog());
            var sink = BuildSink(vfx, sfx);

            sink.PlayVfx(WorldVfx, FeedbackAttachSpec.ForWorld(Vec2.Zero));
            Assert.True(sink.HasPendingPlayback);

            vfxLoader.CompletePending(new Id("res.spark"));
            Assert.False(sink.HasPendingPlayback);
        }

        [Fact]
        public void HasPendingPlayback_TrueWhileSfxFirstLoadPending_FalseAfterCompletes()
        {
            var vfx = new VfxPlayer(new StubRenderer2D(), new StubCamera(), VfxCatalog());
            var sfxLoader = new StubResourceLoader { DeferCallbacks = true };
            var sfx = new SfxPlayer(new StubAudio(), new RngHost(1), SfxCatalog(), resourceLoader: sfxLoader);
            var sink = BuildSink(vfx, sfx);

            sink.PlaySfx(PlainSfx, null);
            Assert.True(sink.HasPendingPlayback);

            sfxLoader.CompletePending(new Id("res.footstep"));
            Assert.False(sink.HasPendingPlayback);
        }
    }
}
