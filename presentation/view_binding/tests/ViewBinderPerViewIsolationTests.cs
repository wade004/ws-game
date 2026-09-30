using System;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Presentation.Common;
using Presentation.ViewBinding;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.PresentationViewBinding
{
    /// <summary>
    /// ADR-0121 第 5 条（D5）：<see cref="ViewBinder"/> 的 <c>SyncAll</c>/<c>OnForwardableEvent</c>/
    /// <c>OnEntityDestroyed</c>/<c>OnSaveLoaded</c> 逐视图回调各自隔离——一个视图抛异常，其余视图照常处理，
    /// 记诊断，<c>Destroy</c> 失败仍从绑定表移除该实体。11 章 §4 表现层异常不影响 tick。
    /// 每个入口一条"复现"用例（三个视图、下标 1 的视图抛异常）+ 若干不变量用例。
    /// </summary>
    public class ViewBinderPerViewIsolationTests
    {
        private static readonly Id MapId = new Id("map.test");

        private static readonly Id Physical = new Id("school.physical");

        private sealed class Fixture
        {
            public IWorldSim World = null!;
            public IEventBus Bus = null!;
            public FakeViewFactory Factory = null!;
            public PresentationDiagnosticsRecorder Diagnostics = null!;
            public ViewBinder Binder = null!;
            public TestEntity[] Entities = null!;

            public FakeView ViewOf(int index) => Factory.CreatedByEntityId[Entities[index].EntityId];

            public Id IdOf(int index) => Entities[index].EntityId;
        }

        /// <summary>创建实体并各自绑定 View（下标 0、1、2；约定下标 1 是"会出错"的那个）。</summary>
        private static Fixture Build(int entityCount = 3)
        {
            var bus = ViewBindingTestSupport.CreateBus();
            var world = new WorldSim(bus);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, new TestMoveHandler());
            var fx = new Fixture
            {
                World = world,
                Bus = bus,
                Factory = new FakeViewFactory(),
                Diagnostics = new PresentationDiagnosticsRecorder(),
            };
            fx.Binder = new ViewBinder(
                bus, fx.Factory, new WorldSimSnapshot(world), new FakeDisplayInfoRegistry(), diagnostics: fx.Diagnostics);
            fx.Entities = new TestEntity[entityCount];
            for (var i = 0; i < entityCount; i++)
            {
                fx.Entities[i] = new TestEntity(new Id("unit.iso_" + i), MapId);
                world.AddEntity(fx.Entities[i]);
            }

            world.Tick(SimStep.Continuous(0.016));
            Assert.Equal(entityCount, fx.Binder.Count);
            return fx;
        }

        private static CombatDamageDealtEvent Damage(Id source, Id target) =>
            new CombatDamageDealtEvent(source, target, Physical, 10, false, HitResult.Hit);

        // ------------------------------------------------------------------
        // SyncAll
        // ------------------------------------------------------------------

        [Fact]
        public void SyncAll_OneViewSyncPoseThrows_OtherViewsStillSyncedAndSingleDiagnostic()
        {
            var fx = Build();
            var thrown = new InvalidOperationException("SyncPose 失败（测试注入）");
            fx.ViewOf(1).ThrowOnSyncPose = () => thrown;

            var caught = Record.Exception(() => fx.Binder.SyncAll(0.5));

            Assert.Null(caught);
            Assert.Single(fx.ViewOf(0).SyncCalls);
            Assert.Single(fx.ViewOf(1).SyncCalls); // 故障视图本身也确实被调用过。
            Assert.Single(fx.ViewOf(2).SyncCalls); // 修复前：排在故障视图之后的视图整帧被跳过。
            Assert.Equal(fx.Entities.Length, fx.Binder.Count); // SyncAll 失败不改绑定表。

            var diagnostic = Assert.Single(fx.Diagnostics.Warnings);
            Assert.Contains(fx.IdOf(1).Value, diagnostic);
            Assert.Contains(nameof(InvalidOperationException), diagnostic);
            Assert.Contains(thrown.Message, diagnostic);
        }

        [Fact]
        public void SyncAll_ViewKeepsThrowingAcrossFrames_DiagnosticNotRepeatedPerFrame()
        {
            var fx = Build();
            fx.ViewOf(1).ThrowOnSyncPose = () => new InvalidOperationException("持续失败");

            const int frames = 5;
            for (var i = 0; i < frames; i++)
            {
                fx.Binder.SyncAll(0.5);
            }

            Assert.Single(fx.Diagnostics.Warnings); // 同一视图连续失败只记一条，不逐帧刷。
            for (var v = 0; v < fx.Entities.Length; v++)
            {
                Assert.Equal(frames, fx.ViewOf(v).SyncCalls.Count); // 每帧每个视图都被调用。
            }
        }

        [Fact]
        public void SyncAll_ViewRecoversThenFailsAgain_RecordsNewDiagnosticPerFailureEpisode()
        {
            var fx = Build();
            var failing = true;
            fx.ViewOf(1).ThrowOnSyncPose = () => failing ? new InvalidOperationException("间歇失败") : null;

            fx.Binder.SyncAll(0.5); // 失败 1
            fx.Binder.SyncAll(0.5); // 仍失败，不新增
            Assert.Single(fx.Diagnostics.Warnings);

            failing = false;
            fx.Binder.SyncAll(0.5); // 恢复
            Assert.Single(fx.Diagnostics.Warnings);

            failing = true;
            fx.Binder.SyncAll(0.5); // 再次失败 = 新一轮失败，再记一条
            Assert.Equal(2, fx.Diagnostics.Warnings.Count);
        }

        // ------------------------------------------------------------------
        // OnForwardableEvent
        // ------------------------------------------------------------------

        [Fact]
        public void OnForwardableEvent_OneViewOnEventThrows_OtherViewsStillReceiveAndSingleDiagnostic()
        {
            var fx = Build();
            fx.ViewOf(1).ThrowOnEvent = () => new InvalidOperationException("OnEvent 失败（测试注入）");

            // 事件同时命中 source/target 两个视图；故障视图 1 无论排在命中集合的先后，
            // 视图 0（事件 A）与视图 2（事件 B）都必须收到。
            var evtA = Damage(fx.IdOf(0), fx.IdOf(1));
            var evtB = Damage(fx.IdOf(1), fx.IdOf(2));

            var caught = Record.Exception(() =>
            {
                fx.Bus.PublishImmediate(evtA);
                fx.Bus.PublishImmediate(evtB);
            });

            Assert.Null(caught);
            Assert.Same(evtA, Assert.Single(fx.ViewOf(0).ReceivedEvents));
            Assert.Equal(2, fx.ViewOf(1).ReceivedEvents.Count); // 故障视图每个事件都被调用过。
            Assert.Same(evtB, Assert.Single(fx.ViewOf(2).ReceivedEvents)); // 修复前：被故障视图的异常挡住。
            Assert.Equal(fx.Entities.Length, fx.Binder.Count);

            // 两个事件各让视图 1 失败一次：离散事件，每次失败一条诊断。
            Assert.Equal(2, fx.Diagnostics.Warnings.Count);
            Assert.All(fx.Diagnostics.Warnings, w => Assert.Contains(fx.IdOf(1).Value, w));
        }

        // ------------------------------------------------------------------
        // OnEntityDestroyed
        // ------------------------------------------------------------------

        [Fact]
        public void OnEntityDestroyed_ViewDestroyThrows_RemovedFromBindingTableAndSingleDiagnostic()
        {
            var fx = Build();
            var thrown = new InvalidOperationException("Destroy 失败（测试注入）");
            fx.ViewOf(1).ThrowOnDestroy = () => thrown;

            var caught = Record.Exception(() => fx.Binder.OnEntityDestroyed(fx.IdOf(1)));

            Assert.Null(caught);
            Assert.False(fx.Binder.TryGetView(fx.IdOf(1), out _)); // 绑定表不残留。
            Assert.Equal(fx.Entities.Length - 1, fx.Binder.Count);
            Assert.True(fx.Binder.TryGetView(fx.IdOf(0), out _));
            Assert.True(fx.Binder.TryGetView(fx.IdOf(2), out _));
            Assert.True(fx.ViewOf(1).Destroyed);

            var diagnostic = Assert.Single(fx.Diagnostics.Warnings);
            Assert.Contains(fx.IdOf(1).Value, diagnostic);
            Assert.Contains(thrown.Message, diagnostic);

            // 位置缓存一并清理：已移除实体的插值查询回到"未绑定"语义。
            Assert.Throws<InvalidOperationException>(() => fx.Binder.GetInterpolatedPosition(fx.IdOf(1), 0.5));
        }

        [Fact]
        public void OnEntityDestroyed_ThroughWorldSimEvents_FaultyDestroyDoesNotBlockOthers()
        {
            var fx = Build();
            fx.ViewOf(1).ThrowOnDestroy = () => new InvalidOperationException("Destroy 失败");

            foreach (var e in fx.Entities)
            {
                fx.World.MarkForDestruction(e.EntityId);
            }

            fx.World.Tick(SimStep.Continuous(0.016));

            Assert.Equal(0, fx.Binder.Count);
            for (var i = 0; i < fx.Entities.Length; i++)
            {
                Assert.True(fx.ViewOf(i).Destroyed); // 每个视图都被尝试销毁。
            }

            Assert.Single(fx.Diagnostics.Warnings);
        }

        // ------------------------------------------------------------------
        // OnSaveLoaded（第一循环：销毁陈旧 View）
        // ------------------------------------------------------------------

        [Fact]
        public void OnSaveLoaded_StaleViewDestroyThrows_OtherStaleViewsDestroyedAndNoResidueAndSingleDiagnostic()
        {
            var fx = Build();
            fx.ViewOf(1).ThrowOnDestroy = () => new InvalidOperationException("Destroy 失败（测试注入）");

            // 模拟读档：抑制作用域内清空世界，entity.destroyed 被丢弃，三个 View 都成了陈旧 View。
            using (fx.Bus.SuppressDispatch())
            {
                fx.World.ClearAll();
            }

            Assert.Equal(fx.Entities.Length, fx.Binder.Count);

            var caught = Record.Exception(() =>
                fx.Bus.PublishImmediate(new SaveLoadedEvent(new Id("slot.d5_test"))));

            Assert.Null(caught);
            Assert.Equal(0, fx.Binder.Count); // 修复前：抛异常的实体残留在绑定表里。
            for (var i = 0; i < fx.Entities.Length; i++)
            {
                Assert.False(fx.Binder.TryGetView(fx.IdOf(i), out _));
                Assert.True(fx.ViewOf(i).Destroyed);
            }

            var diagnostic = Assert.Single(fx.Diagnostics.Warnings);
            Assert.Contains(fx.IdOf(1).Value, diagnostic);
        }

        [Fact]
        public void OnSaveLoaded_SecondReconcileAfterFailedDestroy_NoRepeatedDiagnostic()
        {
            var fx = Build();
            fx.ViewOf(1).ThrowOnDestroy = () => new InvalidOperationException("Destroy 失败");
            using (fx.Bus.SuppressDispatch())
            {
                fx.World.ClearAll();
            }

            fx.Bus.PublishImmediate(new SaveLoadedEvent(new Id("slot.d5_first")));
            fx.Bus.PublishImmediate(new SaveLoadedEvent(new Id("slot.d5_second")));

            Assert.Equal(0, fx.Binder.Count);
            Assert.Single(fx.Diagnostics.Warnings); // 绑定表已无残留，第二次对账无事可做、不重复报错。
        }

        // ------------------------------------------------------------------
        // 不变量：无故障时零诊断、行为不变
        // ------------------------------------------------------------------

        [Fact]
        public void HealthyViews_AllEntryPoints_RecordNoDiagnostics()
        {
            var fx = Build();

            fx.Binder.SyncAll(0.5);
            fx.Bus.PublishImmediate(Damage(fx.IdOf(0), fx.IdOf(1)));
            fx.Bus.PublishImmediate(new SaveLoadedEvent(new Id("slot.d5_healthy"))); // 世界未变：对账无增删。
            Assert.Equal(fx.Entities.Length, fx.Binder.Count);
            fx.Binder.OnEntityDestroyed(fx.IdOf(2));

            Assert.Empty(fx.Diagnostics.Warnings);
            Assert.Equal(fx.Entities.Length - 1, fx.Binder.Count);
        }
    }
}
