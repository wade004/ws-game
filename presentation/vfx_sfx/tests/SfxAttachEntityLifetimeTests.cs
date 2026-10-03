using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Rng;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    /// <summary>NF2：经 <c>PlayAttached</c> 登记的循环音效随实体销毁而停（不再要求内容侧必须显式
    /// <c>stop_sfx</c>）。复现 = 实体消失后 <c>Update</c> 把句柄从 StubAudio 的在播集合里摘掉；
    /// 不变量 = 存活实体的循环不受影响、未装配探测的行为与改动前一致、冷加载排队态同样取消、
    /// 之后重新出现的同 id 实体可以重新挂接。</summary>
    public class SfxAttachEntityLifetimeTests
    {
        private static readonly Id LoopSfx = new Id("sfx.buff_hum");
        private static readonly Id PlainSfx = new Id("sfx.footstep");

        private static Dictionary<Id, SfxDef> Catalog() => new Dictionary<Id, SfxDef>
        {
            [LoopSfx] = new SfxDef(LoopSfx, "combat", null, null, new Id("res.buff_hum"), loop: true),
            [PlainSfx] = new SfxDef(PlainSfx, "combat", 1, null, new Id("res.footstep")),
        };

        private sealed class World
        {
            public readonly HashSet<Id> Alive = new HashSet<Id>();
            public EntityPositionResolver Resolver => id => Alive.Contains(id) ? (Vec2?)new Vec2(1, 2) : null;
        }

        private sealed class ManualLoader : IResourceLoader
        {
            private readonly List<(Id Id, LoadCallback Cb)> _queued = new List<(Id, LoadCallback)>();
            private readonly HashSet<Id> _loaded = new HashSet<Id>();
            public void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback) => _queued.Add((resourceId, callback));
            public bool IsLoaded(Id resourceId) => _loaded.Contains(resourceId);
            public double GetLoadProgress(Id resourceId) => _loaded.Contains(resourceId) ? 1.0 : 0.0;
            public void Unload(Id resourceId) => _loaded.Remove(resourceId);
            public void CompleteAll()
            {
                var items = _queued.ToArray();
                _queued.Clear();
                foreach (var (id, cb) in items)
                {
                    _loaded.Add(id);
                    cb(id, true);
                }
            }
        }

        private static SfxPlayer NewPlayer(
            StubAudio audio, World world, IResourceLoader? loader = null, IPresentationDiagnostics? diagnostics = null) =>
            new SfxPlayer(audio, new RngHost(1), Catalog(), null, diagnostics, loader, world.Resolver);

        [Fact]
        public void Repro_LoopAttached_EntityDestroyed_UpdateStopsTheLoop()
        {
            var audio = new StubAudio();
            var world = new World();
            var entity = new Id("unit.dummy");
            world.Alive.Add(entity);
            var player = NewPlayer(audio, world);

            var handle = player.PlayAttached(LoopSfx, entity, null)!.Value;
            player.Update(0.016);
            var playingWhileAlive = audio.ActiveSfxPlaybacks.ContainsKey(handle.Value);

            world.Alive.Remove(entity);
            player.Update(0.016);

            Assert.True(playingWhileAlive);
            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(handle.Value));
        }

        [Fact]
        public void Invariant_OnlyDestroyedEntitysLoopStops_OtherEntityKeepsPlaying()
        {
            var audio = new StubAudio();
            var world = new World();
            var a = new Id("unit.a");
            var b = new Id("unit.b");
            world.Alive.Add(a);
            world.Alive.Add(b);
            var player = NewPlayer(audio, world);
            var ha = player.PlayAttached(LoopSfx, a, null)!.Value;
            var hb = player.PlayAttached(LoopSfx, b, null)!.Value;

            world.Alive.Remove(a);
            player.Update(0.016);

            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(ha.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(hb.Value));
        }

        [Fact]
        public void Invariant_AfterEntityDestroyed_KeyIsFreed_SameIdCanAttachAgain_ExplicitStopIsSilentNoOp()
        {
            var audio = new StubAudio();
            var world = new World();
            var entity = new Id("unit.dummy");
            world.Alive.Add(entity);
            var diagnostics = new PresentationDiagnosticsRecorder();
            var player = NewPlayer(audio, world, null, diagnostics);
            var first = player.PlayAttached(LoopSfx, entity, null)!.Value;

            world.Alive.Remove(entity);
            player.Update(0.016);
            var stopAfterDestroy = Record.Exception(() => player.StopAttached(LoopSfx, entity));
            world.Alive.Add(entity);
            var second = player.PlayAttached(LoopSfx, entity, null)!.Value;

            Assert.Null(stopAfterDestroy);
            Assert.NotEqual(first, second);
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(second.Value));
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void Invariant_NoResolverWired_LoopKeepsPlayingAfterAnyNumberOfUpdates_AsBefore()
        {
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, new RngHost(1), Catalog());
            var handle = player.PlayAttached(LoopSfx, new Id("unit.gone"), null)!.Value;

            for (var i = 0; i < 10; i++) player.Update(0.016);

            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(handle.Value));
        }

        [Fact]
        public void Invariant_ColdLoadPending_EntityDestroyedBeforeLoadCompletes_NeverStartsPlayback()
        {
            var audio = new StubAudio();
            var world = new World();
            var loader = new ManualLoader();
            var entity = new Id("unit.dummy");
            world.Alive.Add(entity);
            var player = NewPlayer(audio, world, loader);

            player.PlayAttached(LoopSfx, entity, null);
            Assert.Empty(audio.ActiveSfxPlaybacks);

            world.Alive.Remove(entity);
            player.Update(0.016);
            loader.CompleteAll();

            Assert.Empty(audio.ActiveSfxPlaybacks);
        }

        [Fact]
        public void Invariant_OneShotAttached_IsNeverTrackedOrStopped_ByEntityLifetime()
        {
            var audio = new StubAudio();
            var world = new World();
            var entity = new Id("unit.dummy");
            world.Alive.Add(entity);
            var player = NewPlayer(audio, world);
            var handle = player.PlayAttached(PlainSfx, entity, null)!.Value;

            world.Alive.Remove(entity);
            player.Update(0.016);

            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(handle.Value));
        }
    }
}
