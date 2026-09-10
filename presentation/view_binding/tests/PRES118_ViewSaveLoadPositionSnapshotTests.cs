using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Presentation.Common;
using Presentation.ViewBinding;
using Xunit;

namespace Tests.PresentationViewBinding
{
    /// <summary>
    /// PRES-118-VIEW 回归（第十八轮审核 presentation-review.md"PRES118-03"）：同图直接
    /// <see cref="SaveSystem.Load"/>（不经 <c>SceneRouter</c> 卸载重建场景）时，继续存活的既有
    /// <see cref="ViewBinder"/> 插值快照（<c>_prevPositions</c>/
    /// <c>_currPositions</c>）此前不会被刷新——<see cref="ViewBinder.OnTickFinished"/>
    /// 只在下一次 <c>sim.tick_finished</c> 才重新采样，离散模拟等待输入/暂停时不会立即有下一个 tick
    /// （ADR-0013 §4），<see cref="ViewBinder.GetInterpolatedPosition"/> 因此
    /// 会继续插值出读档前的旧坐标，直到某次无关的下一次 tick 才"顺带"刷新。
    /// <para>
    /// 本文件用真实 <see cref="SaveSystem"/>、<see cref="UnitPersistable.CurrentPosition"/>、
    /// <see cref="WorldSim"/>、<see cref="ViewBinder"/>（<see cref="IViewFactory"/>
    /// 用记录型 <see cref="FakeViewFactory"/> stub，惯例同 <c>PRES180_SaveLoadViewReconciliationTests</c>）
    /// 复现该缺口并验证根治（<c>OnSaveLoaded</c> 对本次触发前后身份都未变的既有 View 重设
    /// prev=curr=恢复后位置，见该方法判断记录"PRES-118-VIEW 根治"）。
    /// </para>
    /// </summary>
    public sealed class PRES118_ViewSaveLoadPositionSnapshotTests
    {
        private static readonly Id MapA = new Id("map.pres118_view_a");
        private static readonly Id PlayerId = new Id("unit.pres118_view_player");
        private static readonly Id PlayerFactionId = new Id("fac.pres118_view_player");
        private static readonly Id ArchetypeId = new Id("arch.class.pres118_view_sample");
        private static readonly Vec2 Restored = new Vec2(3, 4);

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public SaveSystem SaveSystem = null!;
            public ViewBinder Binder = null!;
            public FakeViewFactory Factory = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build()
        {
            var bus = ViewBindingTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var player = new PlayerUnit(PlayerId, MapA, PlayerFactionId, ArchetypeId) { Position = Restored };
            world.AddEntity(player);

            var factory = new FakeViewFactory();
            var displayInfo = new FakeDisplayInfoRegistry();
            var binder = new ViewBinder(bus, factory, new WorldSimSnapshot(world), displayInfo);

            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.pres118_view_test")), bus);
            saveSystem.RegisterPersistable(UnitPersistable.CurrentPosition(player));

            // 首次 tick：entity.created 到达，View 创建、prev=curr=Restored（见 OnEntityCreated
            // "避免第一帧从 (0,0) 插值出跳变"判断记录）。
            world.Tick(SimStep.Continuous(0.016));

            return new Fixture
            {
                Bus = bus,
                World = world,
                SaveSystem = saveSystem,
                Binder = binder,
                Factory = factory,
                Player = player,
            };
        }

        [Fact]
        public void PRES118_View_01_SameMapLoad_NoExtraTick_InterpolatedPositionMatchesRestored_AtAllAlphas()
        {
            var fx = Build();
            Assert.Equal(1, fx.Binder.Count);

            var slot = new Id("slot.pres118_view_01");
            var saved = fx.SaveSystem.Save(new SaveRequest(slot, "t1"));
            Assert.True(saved.Success, saved.Message);

            // 玩家继续移动，经过两次 tick 让 prev/curr 都变成读档前坐标 (30,40)——模拟"存档之后又走了
            // 一段距离，View 的插值快照也随之推进"。
            fx.Player.Position = new Vec2(20, 30);
            fx.World.Tick(SimStep.Continuous(0.016));
            fx.Player.Position = new Vec2(30, 40);
            fx.World.Tick(SimStep.Continuous(0.016));
            Assert.Equal(new Vec2(30, 40), fx.Binder.GetInterpolatedPosition(PlayerId, 1.0));

            var loaded = fx.SaveSystem.Load(slot);
            Assert.Equal(LoadStatus.Loaded, loaded.Status);
            Assert.Equal(Restored, fx.Player.Position); // 逻辑位置已恢复（UnitPersistable.CurrentPosition）。

            // 核心断言（PRES-118-VIEW 根治点）：没有任何额外 world.Tick，插值快照已经在
            // save.loaded 处理内被重建，alpha 取 0/0.5/1 都应落在恢复点，不再显示读档前的 (30,40)——
            // 暂停/离散模式等待输入时同样不会有下一个 tick，画面必须在 Load 完成的这一刻就同步。
            Assert.Equal(Restored, fx.Binder.GetInterpolatedPosition(PlayerId, 0.0));
            Assert.Equal(Restored, fx.Binder.GetInterpolatedPosition(PlayerId, 0.5));
            Assert.Equal(Restored, fx.Binder.GetInterpolatedPosition(PlayerId, 1.0));

            // 既有 View 身份保持（不是销毁重建出的新对象）。
            Assert.True(fx.Binder.TryGetView(PlayerId, out var view));
            Assert.Same(fx.Factory.CreatedByEntityId[PlayerId], view);
        }

        [Fact]
        public void PRES118_View_02_RepeatedSameMapLoad_PositionStaysAtRestoredEachTime()
        {
            var fx = Build();
            var slot = new Id("slot.pres118_view_02");
            Assert.True(fx.SaveSystem.Save(new SaveRequest(slot, "t1")).Success);

            fx.Player.Position = new Vec2(50, 60);
            fx.World.Tick(SimStep.Continuous(0.016));

            var firstLoad = fx.SaveSystem.Load(slot);
            Assert.Equal(LoadStatus.Loaded, firstLoad.Status);
            Assert.Equal(Restored, fx.Binder.GetInterpolatedPosition(PlayerId, 0.0));
            Assert.Equal(Restored, fx.Binder.GetInterpolatedPosition(PlayerId, 1.0));

            // 玩家又走开，再读同一个槽位第二次（同图重复读档，如玩家在菜单里反复点"读取存档"）。
            fx.Player.Position = new Vec2(70, 80);
            fx.World.Tick(SimStep.Continuous(0.016));

            var secondLoad = fx.SaveSystem.Load(slot);
            Assert.Equal(LoadStatus.Loaded, secondLoad.Status);
            Assert.Equal(Restored, fx.Binder.GetInterpolatedPosition(PlayerId, 0.0));
            Assert.Equal(Restored, fx.Binder.GetInterpolatedPosition(PlayerId, 1.0));
            Assert.Equal(1, fx.Binder.Count); // 没有产生重复 View。
        }

        [Fact]
        public void PRES118_View_03_ControlGroup_WithExtraTick_AlsoConvergesToSameRestoredPoint()
        {
            // 控制组：显式补一次 world.Tick 后（OnTickFinished 重新采样，旧行为"下一 tick 会好"的
            // 路径）alpha=1 同样收敛到恢复点——用来证明上面两个用例的核心断言不是巧合，"无需额外
            // tick 即正确"与"额外 tick 后也正确"落在同一个恢复点，只是根治前只有后者成立。
            var fx = Build();
            var slot = new Id("slot.pres118_view_03");
            Assert.True(fx.SaveSystem.Save(new SaveRequest(slot, "t1")).Success);

            fx.Player.Position = new Vec2(90, 90);
            fx.World.Tick(SimStep.Continuous(0.016));

            var loaded = fx.SaveSystem.Load(slot);
            Assert.Equal(LoadStatus.Loaded, loaded.Status);

            fx.World.Tick(SimStep.Continuous(0.016));
            Assert.Equal(Restored, fx.Binder.GetInterpolatedPosition(PlayerId, 1.0));
        }
    }
}
