using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    public class MovementTickHandlerTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id FactionId = new Id("fac.player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id HeroId = new Id("unit.hero");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public IEventBus Bus = null!;
            public List<IEvent> Events = null!;
            public WorldUnitAccess Units = null!;
            public FakeStatHost Stats = null!;
            public FakeAuraQuery Auras = null!;
            public MovementHost Host = null!;
            public MovementTickHandler Handler = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build(double moveSpeed = 10.0, StubNavigation2D? navigation = null)
        {
            // StrictCatalog=false：本测试只关心 unit.moved/unit.state_changed 两个 key 的派发，
            // 不逐一登记 WorldSim 自身产生的 entity.created/sim.tick_started 等其它事件（同惯例
            // core/carriers/unit/tests/WorldUnitAccessTests.NewBus）。
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var events = new List<IEvent>();
            bus.Subscribe(CarriersEventKeys.UnitMoved, e => events.Add(e));
            bus.Subscribe(CarriersEventKeys.UnitStateChanged, e => events.Add(e));

            var world = new WorldSim(bus);
            var player = new PlayerUnit(HeroId, MapId, FactionId, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(player);

            var units = new WorldUnitAccess(world);
            var stats = new FakeStatHost();
            stats.SetBase(HeroId, new MovementOptions().MoveSpeedStat, moveSpeed);
            var auras = new FakeAuraQuery();
            var host = new MovementHost(world);
            var handler = new MovementTickHandler(units, stats, auras, host, bus, navigation);

            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);

            return new Fixture
            {
                World = world,
                Bus = bus,
                Events = events,
                Units = units,
                Stats = stats,
                Auras = auras,
                Host = host,
                Handler = handler,
                Player = player,
            };
        }

        private static JsonObject DirectionArgs(double dx, double dy) =>
            new JsonObjectBuilder().Add("dx", new JsonNumber(dx)).Add("dy", new JsonNumber(dy)).Build();

        private static JsonObject TargetArgs(double x, double y) =>
            new JsonObjectBuilder().Add("x", new JsonNumber(x)).Add("y", new JsonNumber(y)).Build();

        [Fact]
        public void DirectionMove_OneTick_DisplacesBySpeedTimesDt_WithNormalizedDirection()
        {
            var fixture = Build(moveSpeed: 10.0);
            fixture.World.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(3, 4))); // 长度 5，非单位向量

            fixture.World.Tick(SimStep.Continuous(0.5));

            var pos = fixture.Units.GetPosition(HeroId);
            // 归一化方向 (0.6, 0.8)，位移 = 10 * 0.5 = 5 → (3, 4)
            Assert.Equal(3.0, pos.X, 6);
            Assert.Equal(4.0, pos.Y, 6);
        }

        [Fact]
        public void DirectionMove_UpdatesFacingToMovementDirection()
        {
            var fixture = Build();
            fixture.World.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(0, 1)));

            fixture.World.Tick(SimStep.Continuous(0.1));

            Assert.Equal(Math.Atan2(1, 0), fixture.Player.Facing, 6);
        }

        [Fact]
        public void DirectionMove_RaisesUnitMovedAndUnitStateChangedEvents()
        {
            var fixture = Build();
            fixture.World.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));

            fixture.World.Tick(SimStep.Continuous(0.1));

            var moved = Assert.Single(fixture.Events, e => e is UnitMovedEvent);
            var stateChanged = Assert.Single(fixture.Events, e => e is UnitStateChangedEvent);
            Assert.Equal(HeroId, ((UnitMovedEvent)moved).UnitId);
            var stateEvt = (UnitStateChangedEvent)stateChanged;
            Assert.Equal("Idle", stateEvt.OldState);
            Assert.Equal("Walk", stateEvt.NewState);
        }

        [Fact]
        public void DirectionMove_ZeroVector_DoesNothing()
        {
            var fixture = Build();
            fixture.World.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(0, 0)));

            fixture.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(Vec2.Zero, fixture.Units.GetPosition(HeroId));
            Assert.DoesNotContain(fixture.Events, e => e is UnitMovedEvent);
        }

        [Fact]
        public void TargetMove_WithoutNavigation_StraightLineFallback_PartialMoveThenArrival()
        {
            var fixture = Build(moveSpeed: 1.0);
            fixture.World.SubmitIntent(new Intent(HeroId, "move", TargetArgs(10, 0)));

            fixture.World.Tick(SimStep.Continuous(1.0)); // 走 1 单位，未到达

            var midPos = fixture.Units.GetPosition(HeroId);
            Assert.Equal(1.0, midPos.X, 6);
            Assert.Equal(MoveMode.Run, fixture.Player.MovementState.Mode);
            Assert.NotNull(fixture.Player.MovementState.CurrentPath);

            for (var i = 0; i < 20; i++)
            {
                fixture.World.Tick(SimStep.Continuous(1.0));
            }

            var finalPos = fixture.Units.GetPosition(HeroId);
            Assert.Equal(10.0, finalPos.X, 6);
            Assert.Equal(MoveMode.Idle, fixture.Player.MovementState.Mode);
            Assert.Null(fixture.Player.MovementState.CurrentPath);
        }

        [Fact]
        public void TargetMove_ArrivesExactlyWhenSpeedCoversDistanceInOneTick()
        {
            var fixture = Build(moveSpeed: 10.0);
            fixture.World.SubmitIntent(new Intent(HeroId, "move", TargetArgs(5, 0)));

            fixture.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(new Vec2(5, 0), fixture.Units.GetPosition(HeroId));
            Assert.Equal(MoveMode.Idle, fixture.Player.MovementState.Mode);
        }

        [Fact]
        public void NoMoveControlFlag_PreventsMovement()
        {
            var fixture = Build();
            fixture.Auras.SetControlFlags(HeroId, ControlFlags.NoMove);
            fixture.World.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));

            fixture.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(Vec2.Zero, fixture.Units.GetPosition(HeroId));
        }

        [Fact]
        public void MovementLocked_PreventsMovement()
        {
            var fixture = Build();
            fixture.Player.MovementState = fixture.Player.MovementState.WithLocked(true);
            fixture.World.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));

            fixture.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(Vec2.Zero, fixture.Units.GetPosition(HeroId));
        }

        [Fact]
        public void Navigation_StraightTwoPointPath_AdvancesAcrossTicks()
        {
            var nav = new StubNavigation2D();
            var fixture = Build(moveSpeed: 2.0, navigation: nav);
            fixture.World.SubmitIntent(new Intent(HeroId, "move", TargetArgs(4, 0)));

            fixture.World.Tick(SimStep.Continuous(1.0)); // +2
            fixture.World.Tick(SimStep.Continuous(1.0)); // +2 → 到达

            Assert.Equal(new Vec2(4, 0), fixture.Units.GetPosition(HeroId));
            Assert.Equal(MoveMode.Idle, fixture.Player.MovementState.Mode);
        }

        [Fact]
        public void Navigation_BlockedPath_RaisesMoveFailedCallback_AndDoesNotMove()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(1, -1), new Vec2(2, 1)) });
            var fixture = Build(moveSpeed: 5.0, navigation: nav);

            Id? failedUnit = null;
            Vec2? failedFrom = null;
            Vec2? failedTo = null;
            fixture.Host.OnMoveFailed += (unitId, from, to) =>
            {
                failedUnit = unitId;
                failedFrom = from;
                failedTo = to;
            };

            fixture.World.SubmitIntent(new Intent(HeroId, "move", TargetArgs(5, 0)));
            fixture.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(HeroId, failedUnit);
            Assert.Equal(Vec2.Zero, failedFrom);
            Assert.Equal(new Vec2(5, 0), failedTo);
            Assert.Equal(Vec2.Zero, fixture.Units.GetPosition(HeroId));
        }

        [Fact]
        public void ResolveSpeed_FallsBackToDefaultSpeed_WhenStatMissing()
        {
            var fixture = Build(moveSpeed: 0.0); // 未设置有效速度 → 视为缺失
            fixture.World.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));

            fixture.World.Tick(SimStep.Continuous(1.0));

            var expected = new MovementOptions().DefaultSpeed;
            Assert.Equal(expected, fixture.Units.GetPosition(HeroId).X, 6);
        }

        /// <summary>
        /// 判断记录：本用例名字看起来像"离散步不推动移动"，但实际验证的是另一件事——直接调用
        /// <see cref="MovementTickHandler.Execute"/>（不经过 <see cref="WorldSim.Tick"/>）时，
        /// <see cref="IWorldSim.CurrentIntents"/> 从未被搬运过（只有 <c>Tick</c> 开头才会把
        /// <c>SubmitIntent</c> 提交的待处理队列搬进 <c>CurrentIntents</c>，见 <c>WorldSim.Tick</c>），
        /// 因此第一遍循环读到的是空列表，不会移动——这与离散/连续无关，纯粹是"绕过 Tick 直接调用
        /// Execute"这一调用方式本身的效果。ADR-0013 落地后离散步本身已经会真正处理移动（见下方
        /// <see cref="DiscreteStep_ThroughWorldTick_MovesByBudget"/>），本用例改名前的旧断言仍然
        /// 成立，保留作为"绕过 Tick 直接调 Execute 时不产生移动"这一调用约定的回归测试。
        /// </summary>
        [Fact]
        public void DirectExecuteCall_WithoutGoingThroughTick_DoesNotSeeAnyIntents()
        {
            var fixture = Build();
            fixture.World.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));

            fixture.Handler.Execute(SimStep.Discrete(HeroId, StepPhase.Act), fixture.World);

            Assert.Equal(Vec2.Zero, fixture.Units.GetPosition(HeroId));
        }

        // -----------------------------------------------------------------
        // 离散模式（ADR-0013、03 第 4.2 节步骤 4）：按每回合移动预算结算位移。
        // -----------------------------------------------------------------

        [Fact]
        public void DiscreteStep_ThroughWorldTick_MovesByBudget()
        {
            var fixture = Build(moveSpeed: 10.0);
            fixture.World.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));

            fixture.World.Tick(SimStep.Discrete(HeroId, StepPhase.Act));

            // 预算 = 速度(10) × DiscreteTurnEquivalentSeconds(默认 1.0) = 10。
            var expectedBudget = 10.0 * new MovementOptions().DiscreteTurnEquivalentSeconds;
            Assert.Equal(expectedBudget, fixture.Units.GetPosition(HeroId).X, 6);
        }

        [Fact]
        public void DiscreteStep_OtherUnitsWithExistingPath_DoNotContinueMoving_OnSomeoneElsesTurn()
        {
            var fixture = Build(moveSpeed: 10.0);

            // 第二个单位：连续模式下建立一条尚未走完的路径。
            var bystanderId = new Id("unit.bystander");
            var bystander = new PlayerUnit(bystanderId, MapId, FactionId, ArchetypeId) { Position = new Vec2(0, 0) };
            fixture.World.AddEntity(bystander);
            fixture.World.SubmitIntent(new Intent(bystanderId, "move", TargetArgs(100, 0)));
            fixture.World.Tick(SimStep.Continuous(0.1)); // 建立路径并走一小段。
            var posAfterContinuous = fixture.Units.GetPosition(bystanderId);
            Assert.True(posAfterContinuous.X > 0);

            // 轮到 Hero 的离散步：bystander 没有新意图，不应该继续沿旧路径移动。
            fixture.World.Tick(SimStep.Discrete(HeroId, StepPhase.Act));

            Assert.Equal(posAfterContinuous, fixture.Units.GetPosition(bystanderId));
        }
    }
}
