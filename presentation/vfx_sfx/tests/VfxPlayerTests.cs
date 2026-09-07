using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
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

        // -----------------------------------------------------------------
        // 缺口 13（模型挂点）：renderer3D + modelHandleResolver 均注入且解析出句柄时真挂接。
        // -----------------------------------------------------------------

        [Fact]
        public void Spawn_Socket_WithModelHandle_CallsAttachToSocket_NotEmitParticle()
        {
            var renderer2D = new StubRenderer2D();
            var renderer3D = new StubRenderer3D();
            var entity = new Id("unit.golem");
            var socketId = new Id("socket.hand_main");
            var hostHandle = renderer3D.CreateModelInstance(new Id("model.golem"));

            var player = new VfxPlayer(
                renderer2D, new StubCamera(), BuildCatalog(),
                renderer3D: renderer3D,
                modelHandleResolver: e => e.Equals(entity) ? hostHandle : (ModelHandle?)null);

            var handle = player.Spawn(SocketVfx, VfxAttach.Socket(entity, socketId), null);

            Assert.NotNull(handle);
            Assert.Empty(renderer2D.EmittedParticles);
            // StubRenderer3D.Attachments 以子模型句柄为键，值元组第二项是宿主句柄（见其源码
            // AttachToSocket 实现：`Attachments[child.Value] = (socketId, handle.Value)`，命名沿用
            // 该桩既有字段名 ChildHandle，语义上实为宿主句柄值，本测试按其真实行为断言）。
            var attachment = Assert.Single(renderer3D.Attachments, kv => kv.Value.ChildHandle == hostHandle.Value);
            Assert.Equal(socketId, attachment.Value.SocketId);
            Assert.Equal(new Id("res.flame"), renderer3D.CreatedModels[attachment.Key]);
        }

        [Fact]
        public void Spawn_Socket_ModelHandleResolverMisses_FallsBackToWorldDowngrade()
        {
            var renderer2D = new StubRenderer2D();
            var renderer3D = new StubRenderer3D();
            var entity = new Id("unit.golem");

            var player = new VfxPlayer(
                renderer2D, new StubCamera(), BuildCatalog(),
                entityPositionResolver: e => e.Equals(entity) ? new Vec2(1, 2) : null,
                renderer3D: renderer3D,
                modelHandleResolver: _ => null);

            var handle = player.Spawn(SocketVfx, VfxAttach.Socket(entity, new Id("socket.hand_main")), null);

            Assert.NotNull(handle);
            Assert.Equal(new Vec2(1, 2), renderer2D.EmittedParticles[handle!.Value.Value].Position);
            Assert.Empty(renderer3D.Attachments);
        }

        [Fact]
        public void Stop_SocketAttachedHandle_DetachesAndDestroysModelInstance_NotStopParticle()
        {
            var renderer2D = new StubRenderer2D();
            var renderer3D = new StubRenderer3D();
            var entity = new Id("unit.golem");
            var hostHandle = renderer3D.CreateModelInstance(new Id("model.golem"));

            var player = new VfxPlayer(
                renderer2D, new StubCamera(), BuildCatalog(),
                renderer3D: renderer3D,
                modelHandleResolver: e => e.Equals(entity) ? hostHandle : (ModelHandle?)null);
            var handle = player.Spawn(SocketVfx, VfxAttach.Socket(entity, new Id("socket.hand_main")), null);

            player.Stop(handle!.Value);

            // StubRenderer3D 对已销毁句柄再次调用任何方法会抛异常，据此间接验证 DestroyModelInstance
            // 确实被调用过；EmittedParticles 全程为空验证从未经 IRenderer2D 播放。
            Assert.Empty(renderer2D.EmittedParticles);
            Assert.Throws<System.InvalidOperationException>(() => renderer3D.SetPlacement(
                new ModelHandle(2), Vec2.Zero, 0, 0, 1, 0));
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

        // -----------------------------------------------------------------
        // 外部审核阻塞项 4 收口回归（architecture/落地计划/audit-20260907/followup-2026-09-07.md
        // "外部审核阻塞项处理"一节）：首次特效加载边界——此前 Spawn 只是"发起加载 + 不管成不成功
        // 都立即 EmitParticle"（fire-and-forget），首次引用一个真正异步加载的资源时会在资源就绪前
        // 就调用 EmitParticle。下面三条用例用 StubResourceLoader.DeferCallbacks=true 模拟真实的
        // 异步加载（LoadAsync 调用后不立即回调，需要显式 CompletePending/FailPending 或本类型自己
        // 推进 Update 到超时），断言 EmitParticle 确实推迟到加载完成之后才发生、且恰好一次。
        // -----------------------------------------------------------------

        [Fact]
        public void Spawn_ResourceNotYetLoaded_DoesNotEmitImmediately_EmitsExactlyOnceAfterLoadCompletes()
        {
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog(), resourceLoader: loader);

            var handle = player.Spawn(WorldVfx, VfxAttach.World(new Vec2(1, 1)), null);

            Assert.Null(handle); // 资源尚未加载完成，本次调用不能立即拿到真实句柄。
            Assert.Empty(renderer.EmittedParticles); // 首次施法命中特效不应该在资源就绪前就播放。

            loader.CompletePending(new Id("res.spark"));

            Assert.Single(renderer.EmittedParticles);
            var (effectId, pos) = renderer.EmittedParticles[1];
            Assert.Equal(new Id("res.spark"), effectId);
            Assert.Equal(new Vec2(1, 1), pos);
        }

        [Fact]
        public void Spawn_ResourceLoadFails_DoesNotEmit_RecordsDiagnostic()
        {
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var diagnostics = new PresentationDiagnosticsRecorder();
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog(), diagnostics: diagnostics, resourceLoader: loader);

            player.Spawn(WorldVfx, VfxAttach.World(new Vec2(1, 1)), null);
            loader.FailPending(new Id("res.spark"));

            Assert.Empty(renderer.EmittedParticles);
            Assert.Contains(diagnostics.Warnings, w => w.Contains("res.spark") && w.Contains("加载失败"));
        }

        [Fact]
        public void Spawn_ResourceLoadNeverCompletes_TimesOut_DoesNotEmit_RecordsDiagnostic()
        {
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var diagnostics = new PresentationDiagnosticsRecorder();
            var options = new VfxOptions { FirstLoadTimeoutSeconds = 2.0 };
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog(), options: options, diagnostics: diagnostics, resourceLoader: loader);

            player.Spawn(WorldVfx, VfxAttach.World(new Vec2(1, 1)), null);

            player.Update(1.0);
            Assert.Empty(renderer.EmittedParticles); // 还没到超时，仍在排队等待。

            player.Update(1.5); // 累计 2.5s，超过 2.0s 超时阈值。

            Assert.Empty(renderer.EmittedParticles); // 超时丢弃，不会补播放。
            Assert.Contains(diagnostics.Warnings, w => w.Contains("res.spark") && w.Contains("超时"));

            // 迟到的加载完成（真实引擎里资源终究还是加载好了）不应该在超时丢弃之后又补播放一次
            // ——这次播放请求已经被放弃，不是"延迟生效"。
            loader.CompletePending(new Id("res.spark"));
            Assert.Empty(renderer.EmittedParticles);
        }

        // -----------------------------------------------------------------
        // GP-09 复现与回归（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        // PendingSpawnCount 供 CompositeFeedbackSink/FeedbackBinder.HasPendingPlayback 把"首次
        // 异步加载中的 vfx"计入离散步表现完成门。
        // -----------------------------------------------------------------

        [Fact]
        public void PendingSpawnCount_ZeroWhenNothingQueued()
        {
            var renderer = new StubRenderer2D();
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog());

            Assert.Equal(0, player.PendingSpawnCount);
        }

        [Fact]
        public void PendingSpawnCount_OneWhileLoading_ZeroAfterLoadCompletes()
        {
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog(), resourceLoader: loader);

            player.Spawn(WorldVfx, VfxAttach.World(new Vec2(1, 1)), null);
            Assert.Equal(1, player.PendingSpawnCount);

            loader.CompletePending(new Id("res.spark"));
            Assert.Equal(0, player.PendingSpawnCount);
        }

        [Fact]
        public void PendingSpawnCount_ZeroAfterTimeout()
        {
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var options = new VfxOptions { FirstLoadTimeoutSeconds = 2.0 };
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog(), options: options, resourceLoader: loader);

            player.Spawn(WorldVfx, VfxAttach.World(new Vec2(1, 1)), null);
            Assert.Equal(1, player.PendingSpawnCount);

            player.Update(2.5);
            Assert.Equal(0, player.PendingSpawnCount);
        }

        /// <summary>
        /// C07 复现与根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：此前超时
        /// 分支只从 <c>_pendingSpawns</c> 摘除、记诊断，从不触发 <see
        /// cref="IVfxPlayer.PendingSpawnCountChanged"/>——<see cref="PendingSpawnCount_ZeroAfterTimeout"/>
        /// 只断言计数归零，没有断言事件确实发出过，覆盖不到这个问题。<c>CompositeFeedbackSink</c>
        /// 把该事件汇聚成 <c>IFeedbackSink.PendingPlaybackChanged</c>，<c>FeedbackBinder</c> 借此在
        /// 冷资源"真正完成"（含超时丢弃）那一刻补一次完成检查——不触发的话 Sequential 模式下节奏门
        /// 可能永久卡住、Immediate 模式下永远等不到解门信号（见 <c>FeedbackBinder.
        /// TryPublishFinished</c> 判断记录）。本用例断言超时恰好触发一次该事件。
        /// </summary>
        [Fact]
        public void Update_TimesOut_TriggersPendingSpawnCountChanged_Once()
        {
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var options = new VfxOptions { FirstLoadTimeoutSeconds = 2.0 };
            var player = new VfxPlayer(renderer, new StubCamera(), BuildCatalog(), options: options, resourceLoader: loader);

            player.Spawn(WorldVfx, VfxAttach.World(new Vec2(1, 1)), null);

            var changedCount = 0;
            player.PendingSpawnCountChanged += () => changedCount++;

            player.Update(1.0);
            Assert.Equal(0, changedCount); // 还没到超时，不应该有任何信号。

            player.Update(1.5); // 累计 2.5s，超过超时阈值——恰好触发一次。
            Assert.Equal(1, changedCount);
            Assert.Equal(0, player.PendingSpawnCount);

            player.Update(1.0); // 已经没有 pending 项了，后续 Update 不应再触发。
            Assert.Equal(1, changedCount);
        }
    }
}
