using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
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

        // ------------------------------------------------------------------
        // ADR-0075：stop_vfx 按 (vfx_id, 附着实体) 定位在播实例并停止。
        // ------------------------------------------------------------------

        [Fact]
        public void StopVfx_StopsInstancePlayedForSameVfxAndEntity()
        {
            var renderer = new StubRenderer2D();
            var vfx = new VfxPlayer(renderer, new StubCamera(), VfxCatalog());
            var sfx = new SfxPlayer(new StubAudio(), new RngHost(1), SfxCatalog());
            var entityId = new Id("unit.dummy");
            var sink = new CompositeFeedbackSink(
                vfx, sfx,
                onFloatingText: (_, __, ___) => { },
                onFreeze: _ => { },
                onShakeCamera: _ => { },
                onFlash: (_, __) => { },
                entityPositionResolver: id => id.Equals(entityId) ? Vec2.Zero : (Vec2?)null);

            var spec = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, entityId, null);
            sink.PlayVfx(WorldVfx, spec);
            Assert.True(renderer.IsParticleAlive(new ParticleHandle(1)));

            sink.StopVfx(WorldVfx, spec);
            Assert.False(renderer.IsParticleAlive(new ParticleHandle(1)));
        }

        [Fact]
        public void StopVfx_NoInstanceEverPlayed_IsSilentNoOp()
        {
            var vfx = new VfxPlayer(new StubRenderer2D(), new StubCamera(), VfxCatalog());
            var sfx = new SfxPlayer(new StubAudio(), new RngHost(1), SfxCatalog());
            var sink = BuildSink(vfx, sfx);

            var spec = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, new Id("unit.never_played"), null);
            var ex = Record.Exception(() => sink.StopVfx(WorldVfx, spec));
            Assert.Null(ex);
        }

        [Fact]
        public void StopVfx_AfterNaturalLifetimeExpiry_DoesNotThrow_AndIsANoOp()
        {
            var shortLivedVfx = new Id("vfx.short_lived");
            var catalog = new Dictionary<Id, VfxDef>
            {
                [shortLivedVfx] = new VfxDef(shortLivedVfx, "buff", VfxAttachMode.World, lifetime: 0.1, new Id("res.spark")),
            };
            var renderer = new StubRenderer2D();
            var vfx = new VfxPlayer(renderer, new StubCamera(), catalog);
            var sfx = new SfxPlayer(new StubAudio(), new RngHost(1), SfxCatalog());
            var entityId = new Id("unit.dummy2");
            var sink = new CompositeFeedbackSink(
                vfx, sfx,
                onFloatingText: (_, __, ___) => { },
                onFreeze: _ => { },
                onShakeCamera: _ => { },
                onFlash: (_, __) => { },
                entityPositionResolver: id => Vec2.Zero);

            var spec = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Target, entityId, null);
            sink.PlayVfx(shortLivedVfx, spec);
            Assert.True(renderer.IsParticleAlive(new ParticleHandle(1)));

            vfx.Update(1.0); // 超过 lifetime=0.1，VfxPool 自然回收。
            Assert.False(renderer.IsParticleAlive(new ParticleHandle(1)));

            var ex = Record.Exception(() => sink.StopVfx(shortLivedVfx, spec));
            Assert.Null(ex);
        }

        [Fact]
        public void StopVfx_WorldAttach_HasNoEntityToKeyBy_IsSilentNoOp()
        {
            // FeedbackAttachSpec 本身允许构造 world 变体（IFeedbackSink.StopVfx 签名层面不禁止），
            // 但数据驱动路径（StopVfxAction 构造函数）已经拒绝 world；这里直接对 CompositeFeedbackSink
            // 这一层断言"没有实体可键时静默忽略"，不依赖上层是否拦截。
            var vfx = new VfxPlayer(new StubRenderer2D(), new StubCamera(), VfxCatalog());
            var sfx = new SfxPlayer(new StubAudio(), new RngHost(1), SfxCatalog());
            var sink = BuildSink(vfx, sfx);

            var ex = Record.Exception(() => sink.StopVfx(WorldVfx, FeedbackAttachSpec.ForWorld(Vec2.Zero)));
            Assert.Null(ex);
        }

        [Fact]
        public void Diagnostics_ExposesInjectedInstance_ForExternalPolling()
        {
            // 诊断转发到引擎控制台第三批（presentation/assembly/README.md 判断记录 10b）：新增公开
            // 属性 Diagnostics 必须是构造期注入的同一个实例，adapters/unity 侧才能拿到正确的引用
            // 轮询转发，而不是转发一份"看起来一样但其实是另一份"的诊断记录——同 VfxPlayer/SfxPlayer/
            // FeedbackBinder/ViewBinder 四个既有类型的同名用例惯例。
            var vfx = new VfxPlayer(new StubRenderer2D(), new StubCamera(), VfxCatalog());
            var sfx = new SfxPlayer(new StubAudio(), new RngHost(1), SfxCatalog());
            var diagnostics = new PresentationDiagnosticsRecorder();
            var sink = new CompositeFeedbackSink(
                vfx, sfx,
                onFloatingText: (_, __, ___) => { },
                onFreeze: _ => { },
                onShakeCamera: _ => { },
                onFlash: (_, __) => { },
                diagnostics: diagnostics);

            Assert.Same(diagnostics, sink.Diagnostics);
        }

        [Fact]
        public void Diagnostics_NotInjected_DefaultsToRecorder_AndReflectsWarnings()
        {
            // 未显式注入 diagnostics 时（PresentationAssembly 当前的用法，见判断记录 10b"不改变既有
            // 构造行为，只转发已默认自建的实例"），Diagnostics 属性仍必须可用且能反映真实产生的
            // 警告——PlayVfx(attach=world) 缺世界坐标时会记一条警告（见 ResolveVfxAttach）。
            var vfx = new VfxPlayer(new StubRenderer2D(), new StubCamera(), VfxCatalog());
            var sfx = new SfxPlayer(new StubAudio(), new RngHost(1), SfxCatalog());
            var sink = BuildSink(vfx, sfx);

            sink.PlayVfx(WorldVfx, new FeedbackAttachSpec(FeedbackAttachTarget.World, null, null, null));

            var recorder = Assert.IsType<PresentationDiagnosticsRecorder>(sink.Diagnostics);
            Assert.Single(recorder.Warnings);
        }
    }
}
