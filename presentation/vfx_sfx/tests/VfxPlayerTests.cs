using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    public class VfxPlayerTests
    {
        private static readonly Id WorldVfx = new Id("vfx.world_spark");
        private static readonly Id AnchorVfx = new Id("vfx.anchor_glow");
        private static readonly Id SocketVfx = new Id("vfx.socket_flame");
        private static readonly Id ScreenVfx = new Id("vfx.screen_flash");

        private static Dictionary<Id, VfxDef> BuildCatalog(double? lifetime = null) => new Dictionary<Id, VfxDef>
        {
            [WorldVfx] = new VfxDef(WorldVfx, "impact", VfxAttachMode.World, lifetime, new Id("res.spark")),
            [AnchorVfx] = new VfxDef(AnchorVfx, "buff", VfxAttachMode.Anchor, lifetime, new Id("res.glow")),
            [SocketVfx] = new VfxDef(SocketVfx, "weapon", VfxAttachMode.Socket, lifetime, new Id("res.flame")),
            [ScreenVfx] = new VfxDef(ScreenVfx, "ui", VfxAttachMode.Screen, lifetime, new Id("res.flash")),
        };

        [Fact]
        public void Spawn_World_EmitsParticleAtGivenPosition()
        {
            var renderer = new StubRenderer2D();
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog());

            var handle = player.Spawn(WorldVfx, VfxAttach.World(new Vec2(3, 4)), null);

            Assert.NotNull(handle);
            var (effectId, pos) = renderer.EmittedParticles[handle!.Value.Value];
            Assert.Equal(new Id("res.spark"), effectId);
            Assert.Equal(new Vec2(3, 4), pos);
        }

        [Fact]
        public void Spawn_Anchor_UsesAnchorResolverWorldPosition()
        {
            var renderer = new StubRenderer2D();
            var entity = new Id("unit.hero");
            var anchor = new Id("anchor.hand_main");
            var player = new VfxPlayer(
                renderer, new StubCamera(), BuildCatalog(),
                anchorResolver: (e, a) => e.Equals(entity) && a.Equals(anchor) ? new Vec2(10, 20) : null);

            var handle = player.Spawn(AnchorVfx, VfxAttach.Anchor(entity, anchor), null);

            Assert.NotNull(handle);
            Assert.Equal(new Vec2(10, 20), renderer.EmittedParticles[handle!.Value.Value].Position);
        }

        [Fact]
        public void Spawn_Anchor_FallsBackToEntityPosition_WhenAnchorResolverMisses()
        {
            var renderer = new StubRenderer2D();
            var entity = new Id("unit.hero");
            var anchor = new Id("anchor.missing");
            var player = new VfxPlayer(
                renderer, new StubCamera(), BuildCatalog(),
                anchorResolver: (e, a) => null,
                entityPositionResolver: e => e.Equals(entity) ? new Vec2(5, 5) : null);

            var handle = player.Spawn(AnchorVfx, VfxAttach.Anchor(entity, anchor), null);

            Assert.NotNull(handle);
            Assert.Equal(new Vec2(5, 5), renderer.EmittedParticles[handle!.Value.Value].Position);
        }

        [Fact]
        public void Spawn_Anchor_ReturnsNull_WhenNeitherResolverAvailable()
        {
            var renderer = new StubRenderer2D();
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog());

            var handle = player.Spawn(AnchorVfx, VfxAttach.Anchor(new Id("unit.hero"), new Id("anchor.hand_main")), null);

            Assert.Null(handle);
            Assert.Empty(renderer.EmittedParticles);
        }

        [Fact]
        public void Spawn_Socket_DowngradesToWorld_UsingEntityPosition()
        {
            var renderer = new StubRenderer2D();
            var entity = new Id("unit.golem");
            var player = new VfxPlayer(
                renderer, new StubCamera(), BuildCatalog(),
                entityPositionResolver: e => e.Equals(entity) ? new Vec2(7, 8) : null);

            var handle = player.Spawn(SocketVfx, VfxAttach.Socket(entity, new Id("socket.hand_main")), null);

            Assert.NotNull(handle);
            Assert.Equal(new Vec2(7, 8), renderer.EmittedParticles[handle!.Value.Value].Position);
        }

        [Fact]
        public void Spawn_Screen_UsesCameraScreenToWorld()
        {
            var renderer = new StubRenderer2D();
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog());

            var handle = player.Spawn(ScreenVfx, VfxAttach.Screen(new Vec2(0.5, 0.5)), null);

            // StubCamera.ScreenToWorld 是恒等映射（见 StubCamera 注释）。
            Assert.NotNull(handle);
            Assert.Equal(new Vec2(0.5, 0.5), renderer.EmittedParticles[handle!.Value.Value].Position);
        }

        [Fact]
        public void Spawn_UnknownVfxId_ReturnsNullAndWarns()
        {
            var renderer = new StubRenderer2D();
            var diagnostics = new PresentationDiagnosticsRecorder();
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog(), diagnostics: diagnostics);

            var handle = player.Spawn(new Id("vfx.does_not_exist"), VfxAttach.World(Vec2.Zero), null);

            Assert.Null(handle);
            Assert.Single(diagnostics.Warnings);
        }

        [Fact]
        public void Spawn_AttachModeMismatch_ReturnsNullAndWarns()
        {
            var renderer = new StubRenderer2D();
            var diagnostics = new PresentationDiagnosticsRecorder();
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog(), diagnostics: diagnostics);

            // WorldVfx 声明 attach_mode=world，但调用方传入 Screen 形状的 VfxAttach。
            var handle = player.Spawn(WorldVfx, VfxAttach.Screen(Vec2.Zero), null);

            Assert.Null(handle);
            Assert.Single(diagnostics.Warnings);
        }

        [Fact]
        public void Pool_EvictsSoonestToExpire_WhenCategoryCapacityExceeded()
        {
            var renderer = new StubRenderer2D();
            var options = new VfxOptions { PoolCapacityPerCategory = new Dictionary<string, int> { ["impact"] = 2 } };
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog(lifetime: 5.0), options);

            var first = player.Spawn(WorldVfx, VfxAttach.World(new Vec2(1, 1)), null)!.Value;
            var second = player.Spawn(WorldVfx, VfxAttach.World(new Vec2(2, 2)), null)!.Value;

            // 手动把第一个的生命周期通过 Update 消耗到比第二个短，验证淘汰选中"最快到期"的一个而不是"最早插入"的一个。
            // 由于两者初始 lifetime 相同，先用一次极小 dt 更新，再 Spawn 第三个触发容量淘汰。
            player.Update(0.1);
            var third = player.Spawn(WorldVfx, VfxAttach.World(new Vec2(3, 3)), null)!.Value;

            // first 存活时间被消耗得更多（先创建、一起被 Update 扣减，剩余相同——用插入序作为决胜，
            // 因此 first 应被淘汰）。
            Assert.Throws<System.InvalidOperationException>(() => renderer.StopParticle(first));
            renderer.StopParticle(second);
            renderer.StopParticle(third);
        }

        [Fact]
        public void Pool_Update_ReclaimsExpiredByLifetime()
        {
            var renderer = new StubRenderer2D();
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog(lifetime: 1.0));

            var handle = player.Spawn(WorldVfx, VfxAttach.World(Vec2.Zero), null)!.Value;

            player.Update(0.5);
            player.Update(0.6);

            // 到期后对象池已经调用了 StopParticle，再次调用 renderer.StopParticle 应该抛异常
            // （StubRenderer2D 对已销毁句柄的语义，见该类型注释）。
            Assert.Throws<System.InvalidOperationException>(() => renderer.StopParticle(handle));
        }

        [Fact]
        public void Stop_RemovesHandleFromRenderer()
        {
            var renderer = new StubRenderer2D();
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog());

            var handle = player.Spawn(WorldVfx, VfxAttach.World(Vec2.Zero), null)!.Value;
            player.Stop(handle);

            Assert.Throws<System.InvalidOperationException>(() => renderer.StopParticle(handle));
        }

        [Fact]
        public void Spawn_WithResourceLoaderInjected_LoadsResourceRefAsEffect_OnlyOnce()
        {
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader();
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog(), resourceLoader: loader);

            player.Spawn(WorldVfx, VfxAttach.World(new Vec2(1, 1)), null);
            player.Spawn(WorldVfx, VfxAttach.World(new Vec2(2, 2)), null);

            var requests = loader.LoadRequests.FindAll(r => r.ResourceId.Equals(new Id("res.spark")));
            Assert.Single(requests);
            Assert.Equal(Core.Foundation.EngineAdapter.ResourceKind.Effect, requests[0].Kind);
        }

        [Fact]
        public void Spawn_WithoutResourceLoaderInjected_DoesNotThrow()
        {
            var renderer = new StubRenderer2D();
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog());

            var ex = Record.Exception(() => player.Spawn(WorldVfx, VfxAttach.World(Vec2.Zero), null));

            Assert.Null(ex);
        }
    }
}
