// VfxPlayerSocketColdLoadTests：收口遗留修复 A1——真 3D socket 路径（renderer3D + modelHandleResolver
// 均注入、宿主有 ModelHandle）此前在资源未就绪时仍同步 CreateModelInstance + AttachToSocket
// （引擎侧资源尚未加载完成），与 world/anchor/降级 socket 路径"冷 = 热"不一致（AGENTS.md 第 0 节）。
// 修复后：资源未就绪时排队（返回占位句柄、Stop 可取消、加载完成后才创建并挂到 socket），
// 超时/加载失败与其它路径同口径。期望值由规则算出：挂接发生的时刻 = 加载完成回调。
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    public class VfxPlayerSocketColdLoadTests
    {
        private static readonly Id SocketVfx = new Id("vfx.cold3d_socket");
        private static readonly Id SocketRes = new Id("res.cold3d_socket");
        private static readonly Id Hero = new Id("unit.hero3d");
        private static readonly Id HandSocket = new Id("socket.main_hand3d");

        private sealed class Rig
        {
            public readonly StubRenderer2D Renderer2D = new StubRenderer2D();
            public readonly StubRenderer3D Renderer3D = new StubRenderer3D();
            public readonly StubResourceLoader Loader = new StubResourceLoader { DeferCallbacks = true };
            public readonly ModelHandle Host;
            public readonly VfxPlayer Player;
            public readonly PresentationDiagnosticsRecorder Diagnostics = new PresentationDiagnosticsRecorder();
            public bool HostPresent = true;

            public Rig(VfxOptions? options = null, double? lifetime = null)
            {
                Host = Renderer3D.CreateModelInstance(new Id("model.hero3d"));
                var catalog = new Dictionary<Id, VfxDef>
                {
                    [SocketVfx] = new VfxDef(SocketVfx, "weapon", VfxAttachMode.Socket, lifetime, SocketRes),
                };
                Player = new VfxPlayer(
                    Renderer2D, new StubCamera(), catalog, options,
                    diagnostics: Diagnostics,
                    resourceLoader: Loader,
                    renderer3D: Renderer3D,
                    modelHandleResolver: e => HostPresent && e.Equals(Hero) ? Host : (ModelHandle?)null);
            }

            public ParticleHandle Spawn()
            {
                var handle = Player.Spawn(SocketVfx, VfxAttach.Socket(Hero, HandSocket), null);
                Assert.NotNull(handle);
                return handle!.Value;
            }

            /// <summary>已挂到宿主上的子模型数（Attachments 的值元组第二项是宿主句柄值，见 StubRenderer3D）。</summary>
            public int AttachedChildren() => Renderer3D.Attachments.Count;
        }

        [Fact]
        public void ColdLoad_Socket3D_QueuesWithUsableHandle_NoAttachUntilLoadCompletes()
        {
            var rig = new Rig();

            var handle = rig.Spawn();

            // 修前：同步 CreateModelInstance + AttachToSocket，PendingSpawnCount 恒为 0。
            Assert.Equal(1, rig.Player.PendingSpawnCount);
            Assert.Empty(rig.Renderer3D.Attachments);
            Assert.Single(rig.Renderer3D.CreatedModels); // 只有宿主自己，没有为特效创建子模型。
            Assert.Empty(rig.Renderer2D.EmittedParticles);

            rig.Loader.CompletePending(SocketRes);

            Assert.Equal(0, rig.Player.PendingSpawnCount);
            var attachment = Assert.Single(rig.Renderer3D.Attachments);
            Assert.Equal(HandSocket, attachment.Value.SocketId);
            Assert.Equal(rig.Host.Value, attachment.Value.ChildHandle);
            Assert.Equal(SocketRes, rig.Renderer3D.CreatedModels[attachment.Key]);
            Assert.Empty(rig.Renderer2D.EmittedParticles);

            // 占位句柄在补挂之后仍能停到那个子模型（拆除挂接并销毁实例）。
            rig.Player.Stop(handle);
            Assert.Throws<System.InvalidOperationException>(() => rig.Renderer3D.SetPlacement(
                new ModelHandle(attachment.Key), Vec2.Zero, 0, 0, 1, 0));
        }

        [Fact]
        public void ColdLoad_Socket3D_StopBeforeLoadCompletes_NeverAttaches()
        {
            var rig = new Rig();
            var handle = rig.Spawn();

            rig.Player.Stop(handle);
            Assert.Equal(0, rig.Player.PendingSpawnCount);

            rig.Loader.CompletePending(SocketRes);

            Assert.Empty(rig.Renderer3D.Attachments);
            Assert.Single(rig.Renderer3D.CreatedModels);
        }

        [Fact]
        public void ColdLoad_Socket3D_LoadFails_DiscardsRequest_WithWarning()
        {
            var rig = new Rig();
            rig.Spawn();

            rig.Loader.FailPending(SocketRes);

            Assert.Equal(0, rig.Player.PendingSpawnCount);
            Assert.Empty(rig.Renderer3D.Attachments);
            Assert.Single(rig.Diagnostics.Warnings);
        }

        [Fact]
        public void ColdLoad_Socket3D_LoadTimesOut_DiscardsRequest()
        {
            var rig = new Rig(new VfxOptions { FirstLoadTimeoutSeconds = 1.0 });
            rig.Spawn();

            rig.Player.Update(1.5);

            Assert.Equal(0, rig.Player.PendingSpawnCount);
            Assert.Empty(rig.Renderer3D.Attachments);
        }

        [Fact]
        public void ColdLoad_Socket3D_HostGoneWhenLoadCompletes_DiscardsWithWarning_NoThrow()
        {
            var rig = new Rig();
            rig.Spawn();
            rig.HostPresent = false;

            Assert.Null(Record.Exception(() => rig.Loader.CompletePending(SocketRes)));

            Assert.Equal(0, rig.Player.PendingSpawnCount);
            Assert.Empty(rig.Renderer3D.Attachments);
            Assert.NotEmpty(rig.Diagnostics.Warnings);
        }

        [Fact]
        public void HotPath_Socket3D_ResourceAlreadyLoaded_AttachesSynchronously_SameOutcomeAsColdAfterLoad()
        {
            // 冷 = 热：同一份数据，热路径同步挂接得到的挂接记录与冷路径加载完成后一致。
            var hot = new Rig();
            hot.Loader.DeferCallbacks = false;
            hot.Loader.Register(SocketRes);
            hot.Loader.LoadAsync(SocketRes, ResourceKind.Effect, (_, __) => { });
            Assert.True(hot.Loader.IsLoaded(SocketRes));
            hot.Spawn();
            Assert.Equal(0, hot.Player.PendingSpawnCount);
            var hotAttachment = Assert.Single(hot.Renderer3D.Attachments);

            var cold = new Rig();
            cold.Spawn();
            cold.Loader.CompletePending(SocketRes);
            var coldAttachment = Assert.Single(cold.Renderer3D.Attachments);

            Assert.Equal(hotAttachment.Value, coldAttachment.Value);
            Assert.Equal(hot.Renderer3D.CreatedModels[hotAttachment.Key], cold.Renderer3D.CreatedModels[coldAttachment.Key]);
        }

        [Fact]
        public void ColdLoad_Socket3D_LifetimeExpiry_AfterLateAttach_DestroysModel()
        {
            // 补挂之后与热路径同一套生命周期：到期由 VfxPool 回收（经 StopInternal 拆挂接并销毁实例）。
            var rig = new Rig(lifetime: 2.0);
            rig.Spawn();
            rig.Loader.CompletePending(SocketRes);
            var child = Assert.Single(rig.Renderer3D.Attachments).Key;

            rig.Player.Update(2.5);

            Assert.Throws<System.InvalidOperationException>(() => rig.Renderer3D.SetPlacement(
                new ModelHandle(child), Vec2.Zero, 0, 0, 1, 0));
        }
    }
}
