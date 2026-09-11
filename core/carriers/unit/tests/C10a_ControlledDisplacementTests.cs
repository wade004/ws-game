using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    /// <summary>
    /// ADR-0026《技能位移的连续模式》：<see cref="MovementHost.BeginControlledDisplacement"/>/
    /// <see cref="MovementTickHandler"/> 的逐 tick 位移/裁决/终止条件测试——直接驱动
    /// <see cref="MovementHost"/> 的新入口，不经过 <c>core/rules/skill</c>（那一侧的调度/参数组装
    /// 由 <c>core/rules/skill/tests/C10a_ContinuousMoveDispatchTests.cs</c> 覆盖；端到端的真实技能
    /// 施法见 <c>core/carriers/assembly/tests/C10a_ContinuousMoveEndToEndTests.cs</c>）。
    /// <para>
    /// 消费方反馈最小场景复现（<c>architecture/落地计划/消费方反馈-2026-09-11-技能位移连续模式.md</c>、
    /// M-C10 反馈第 1 节）：目标在墙前 <c>(1,0)</c>，static leap 到 <c>(3,0)</c>，墙 <c>x=1.5..2</c>——
    /// 瞬移模式下终点直接跳变；<see cref="Blocking_Stop_StopsBeforeWall_MatchesFeedbackMinimalScenario"/>/
    /// <see cref="Blocking_Revert_ReturnsToOrigin_SameFeedbackScenario"/> 两例用同一坐标验证连续模式。
    /// </para>
    /// </summary>
    public class C10a_ControlledDisplacementTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id FactionId = new Id("fac.player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id HeroId = new Id("unit.hero");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public List<IEvent> Events = null!;
            public WorldUnitAccess Units = null!;
            public FakeAuraQuery Auras = null!;
            public MovementHost Host = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build(StubNavigation2D? navigation = null, MovementOptions? options = null)
        {
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
            var auras = new FakeAuraQuery();
            var host = new MovementHost(world);
            var handler = new MovementTickHandler(units, stats, auras, host, bus, navigation, options);

            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);

            return new Fixture
            {
                World = world,
                Events = events,
                Units = units,
                Auras = auras,
                Host = host,
                Player = player,
            };
        }

        private static ControlledDisplacementRequest Request(
            Vec2 origin, Vec2 target, double speed,
            DisplacementBlockingPolicy blocking = DisplacementBlockingPolicy.Stop, double sampleStep = 0) =>
            new ControlledDisplacementRequest(HeroId, origin, target, speed, blocking, sampleStep);

        // -----------------------------------------------------------------
        // 兼容性：无阻挡时连续模式与瞬移模式终点一致（ADR-0026 兼容性 4）。
        // -----------------------------------------------------------------

        [Fact]
        public void NoObstruction_ArrivesAtExactTarget_SameAsInstantSetPosition()
        {
            var fixture = Build();
            fixture.Host.BeginControlledDisplacement(Request(Vec2.Zero, new Vec2(4, 0), speed: 2.0));

            fixture.World.Tick(SimStep.Continuous(1.0)); // 0 -> 2
            Assert.Equal(new Vec2(2, 0), fixture.Units.GetPosition(HeroId));
            Assert.True(fixture.Player.MovementState.IsControlledDisplacementActive);
            Assert.Equal(MoveMode.Forced, fixture.Player.MovementState.Mode);

            fixture.World.Tick(SimStep.Continuous(1.0)); // 2 -> 4，到达

            Assert.Equal(new Vec2(4, 0), fixture.Units.GetPosition(HeroId));
            Assert.False(fixture.Player.MovementState.IsControlledDisplacementActive);
            Assert.Equal(MoveMode.Idle, fixture.Player.MovementState.Mode);
        }

        [Fact]
        public void NoObstruction_TickByTickPositionSequence_MatchesSpeedTimesDt()
        {
            var fixture = Build();
            fixture.Host.BeginControlledDisplacement(Request(Vec2.Zero, new Vec2(9, 0), speed: 3.0));

            var expected = new[] { 3.0, 6.0, 9.0 };
            foreach (var x in expected)
            {
                fixture.World.Tick(SimStep.Continuous(1.0));
                Assert.Equal(x, fixture.Units.GetPosition(HeroId).X, 6);
            }

            Assert.False(fixture.Player.MovementState.IsControlledDisplacementActive);
        }

        [Fact]
        public void Arrival_RaisesOnMoveStopped_WithDisplacementArrivedReason()
        {
            var fixture = Build();
            MoveStopReason? reason = null;
            fixture.Host.OnMoveStopped += (_, _, r) => reason = r;

            fixture.Host.BeginControlledDisplacement(Request(Vec2.Zero, new Vec2(2, 0), speed: 2.0));
            fixture.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(MoveStopReason.DisplacementArrived, reason);
        }

        [Fact]
        public void Arrival_RaisesUnitMovedEvents_ObservableTickByTick()
        {
            var fixture = Build();
            fixture.Host.BeginControlledDisplacement(Request(Vec2.Zero, new Vec2(4, 0), speed: 2.0));

            fixture.World.Tick(SimStep.Continuous(1.0));
            fixture.World.Tick(SimStep.Continuous(1.0));

            var movedEvents = fixture.Events.FindAll(e => e is UnitMovedEvent);
            Assert.Equal(2, movedEvents.Count);
        }

        // -----------------------------------------------------------------
        // 阻挡裁决：消费方反馈最小场景（(1,0) -> (3,0)，墙 x=1.5..2）。
        // -----------------------------------------------------------------

        [Fact]
        public void Blocking_Stop_StopsBeforeWall_MatchesFeedbackMinimalScenario()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(1.5, -1), new Vec2(2, 1)) });
            var fixture = Build(navigation: nav);
            fixture.Player.Position = new Vec2(1, 0);

            MoveStopReason? reason = null;
            fixture.Host.OnMoveStopped += (_, _, r) => reason = r;

            // speed 足够大，一个 tick 内就会撞到墙——验证的是"逐采样步阻挡裁决"，不是"多 tick 推进"。
            fixture.Host.BeginControlledDisplacement(
                Request(new Vec2(1, 0), new Vec2(3, 0), speed: 5.0, blocking: DisplacementBlockingPolicy.Stop));
            fixture.World.Tick(SimStep.Continuous(1.0));

            var pos = fixture.Units.GetPosition(HeroId);
            Assert.True(pos.X < 1.5, $"应停在墙前，实际 x={pos.X}");
            Assert.True(pos.X > 1.0, $"应确有推进，实际 x={pos.X}");
            Assert.Equal(MoveStopReason.DisplacementBlocked, reason);
            Assert.False(fixture.Player.MovementState.IsControlledDisplacementActive);
        }

        [Fact]
        public void Blocking_Revert_ReturnsToOrigin_SameFeedbackScenario()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(1.5, -1), new Vec2(2, 1)) });
            var fixture = Build(navigation: nav);
            fixture.Player.Position = new Vec2(1, 0);

            MoveStopReason? reason = null;
            fixture.Host.OnMoveStopped += (_, _, r) => reason = r;

            fixture.Host.BeginControlledDisplacement(
                Request(new Vec2(1, 0), new Vec2(3, 0), speed: 5.0, blocking: DisplacementBlockingPolicy.Revert));
            fixture.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(new Vec2(1, 0), fixture.Units.GetPosition(HeroId));
            Assert.Equal(MoveStopReason.DisplacementBlocked, reason);
            Assert.False(fixture.Player.MovementState.IsControlledDisplacementActive);
        }

        [Fact]
        public void Blocking_SampleStep_FinerGranularity_StillStopsBeforeWall()
        {
            // sample_step 更小时逐采样步更细——最终仍应停在墙前（同一结论，粒度不同），验证"路径
            // 采样"参数确实参与推进而不是被忽略。
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(1.5, -1), new Vec2(2, 1)) });
            var fixture = Build(navigation: nav);
            fixture.Player.Position = new Vec2(1, 0);

            fixture.Host.BeginControlledDisplacement(
                Request(new Vec2(1, 0), new Vec2(3, 0), speed: 5.0, sampleStep: 0.05));
            fixture.World.Tick(SimStep.Continuous(1.0));

            var pos = fixture.Units.GetPosition(HeroId);
            Assert.True(pos.X < 1.5 && pos.X > 1.0, $"应停在墙前，实际 x={pos.X}");
        }

        [Fact]
        public void Blocking_UnspecifiedSampleStep_FallsBackToMovementOptionsDefault()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(1.5, -1), new Vec2(2, 1)) });
            var options = new MovementOptions { DefaultDisplacementSampleStep = 0.2 };
            var fixture = Build(navigation: nav, options: options);
            fixture.Player.Position = new Vec2(1, 0);

            // sampleStep: 0（未声明的哨兵值）——应回退到 MovementOptions.DefaultDisplacementSampleStep，
            // 而不是变成"每次不推进"或抛异常。
            fixture.Host.BeginControlledDisplacement(
                Request(new Vec2(1, 0), new Vec2(3, 0), speed: 5.0, sampleStep: 0));
            fixture.World.Tick(SimStep.Continuous(1.0));

            var pos = fixture.Units.GetPosition(HeroId);
            Assert.True(pos.X < 1.5 && pos.X > 1.0, $"应停在墙前，实际 x={pos.X}");
        }

        // -----------------------------------------------------------------
        // 中断条件：控制/施法者死亡/显式 Stop。
        // -----------------------------------------------------------------

        [Fact]
        public void ControlInterrupt_EndsDisplacement_InPlace_WithDisplacementControlledReason()
        {
            var fixture = Build();
            fixture.Host.BeginControlledDisplacement(Request(Vec2.Zero, new Vec2(10, 0), speed: 1.0));
            fixture.World.Tick(SimStep.Continuous(1.0)); // 0 -> 1，仍在进行中
            var posBeforeControl = fixture.Units.GetPosition(HeroId);
            Assert.True(fixture.Player.MovementState.IsControlledDisplacementActive);

            fixture.Auras.SetControlFlags(HeroId, ControlFlags.NoMove);
            MoveStopReason? reason = null;
            fixture.Host.OnMoveStopped += (_, _, r) => reason = r;

            fixture.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(posBeforeControl, fixture.Units.GetPosition(HeroId)); // 就地停止，不位移
            Assert.Equal(MoveStopReason.DisplacementControlled, reason);
            Assert.False(fixture.Player.MovementState.IsControlledDisplacementActive);
        }

        [Fact]
        public void CasterDeath_EndsDisplacement_InPlace_WithDisplacementCasterDeadReason()
        {
            var fixture = Build();
            fixture.Host.BeginControlledDisplacement(Request(Vec2.Zero, new Vec2(10, 0), speed: 1.0));
            fixture.World.Tick(SimStep.Continuous(1.0));
            var posBeforeDeath = fixture.Units.GetPosition(HeroId);

            fixture.Player.Alive = false;
            MoveStopReason? reason = null;
            fixture.Host.OnMoveStopped += (_, _, r) => reason = r;

            fixture.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(posBeforeDeath, fixture.Units.GetPosition(HeroId));
            Assert.Equal(MoveStopReason.DisplacementCasterDead, reason);
            Assert.False(fixture.Player.MovementState.IsControlledDisplacementActive);
        }

        [Fact]
        public void ExplicitStop_EndsDisplacement_InPlace_WithRequestedReason()
        {
            var fixture = Build();
            fixture.Host.BeginControlledDisplacement(Request(Vec2.Zero, new Vec2(10, 0), speed: 1.0));
            fixture.World.Tick(SimStep.Continuous(1.0));
            var posBeforeStop = fixture.Units.GetPosition(HeroId);

            fixture.Host.Stop(HeroId);
            MoveStopReason? reason = null;
            fixture.Host.OnMoveStopped += (_, _, r) => reason = r;

            fixture.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(posBeforeStop, fixture.Units.GetPosition(HeroId));
            Assert.Equal(MoveStopReason.Requested, reason);
            Assert.False(fixture.Player.MovementState.IsControlledDisplacementActive);
        }

        // -----------------------------------------------------------------
        // 位移与移动互斥：受控位移期间普通移动意图被拒绝。
        // -----------------------------------------------------------------

        [Fact]
        public void OrdinaryMoveIntent_RejectedWhileDisplacementActive()
        {
            var fixture = Build();
            fixture.Host.BeginControlledDisplacement(Request(Vec2.Zero, new Vec2(10, 0), speed: 1.0));
            fixture.World.SubmitIntent(new Intent(
                HeroId, "move", new Core.Foundation.Common.Json.JsonObjectBuilder()
                    .Add("dx", new Core.Foundation.Common.Json.JsonNumber(0))
                    .Add("dy", new Core.Foundation.Common.Json.JsonNumber(1))
                    .Add("mode", new Core.Foundation.Common.Json.JsonString("Walk"))
                    .Build()));

            fixture.World.Tick(SimStep.Continuous(1.0));

            // 只应看到位移推进的水平位移（speed 1 × dt 1 = 1），完全没有 y 方向的位移（被拒绝的
            // 方向移动意图本应产生 y+=某值，若混入说明拒绝没生效）。
            var pos = fixture.Units.GetPosition(HeroId);
            Assert.Equal(1.0, pos.X, 6);
            Assert.Equal(0.0, pos.Y, 6);
        }

        // -----------------------------------------------------------------
        // 暂停：时间模型暂停（dt=0）不推进。
        // -----------------------------------------------------------------

        [Fact]
        public void PausedTick_ZeroDt_DoesNotAdvance_StaysActive()
        {
            var fixture = Build();
            fixture.Host.BeginControlledDisplacement(Request(Vec2.Zero, new Vec2(10, 0), speed: 2.0));

            fixture.World.Tick(SimStep.Continuous(0.0));

            Assert.Equal(Vec2.Zero, fixture.Units.GetPosition(HeroId));
            Assert.True(fixture.Player.MovementState.IsControlledDisplacementActive);
        }

        // -----------------------------------------------------------------
        // 离散模式：一次性完成整段位移（ADR-0026 决策 2）。
        // -----------------------------------------------------------------

        [Fact]
        public void DiscreteMode_CompletesEntireDisplacement_InOneTick_NoObstruction()
        {
            var fixture = Build();
            fixture.Host.BeginControlledDisplacement(Request(Vec2.Zero, new Vec2(10, 0), speed: 1.0));

            fixture.World.Tick(SimStep.Discrete(HeroId, StepPhase.Act));

            Assert.Equal(new Vec2(10, 0), fixture.Units.GetPosition(HeroId));
            Assert.False(fixture.Player.MovementState.IsControlledDisplacementActive);
        }

        [Fact]
        public void DiscreteMode_Blocked_StopsBeforeWall_InOneTick()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(1.5, -1), new Vec2(2, 1)) });
            var fixture = Build(navigation: nav);
            fixture.Player.Position = new Vec2(1, 0);

            fixture.Host.BeginControlledDisplacement(
                Request(new Vec2(1, 0), new Vec2(3, 0), speed: 1.0));
            fixture.World.Tick(SimStep.Discrete(HeroId, StepPhase.Act));

            var pos = fixture.Units.GetPosition(HeroId);
            Assert.True(pos.X < 1.5 && pos.X > 1.0, $"应停在墙前，实际 x={pos.X}");
            Assert.False(fixture.Player.MovementState.IsControlledDisplacementActive);
        }

        // -----------------------------------------------------------------
        // 幂等/防御性：不支持位移嵌套。
        // -----------------------------------------------------------------

        [Fact]
        public void BeginDisplacement_WhileAlreadyDisplacing_IgnoresSecondRequest()
        {
            var fixture = Build();
            fixture.Host.BeginControlledDisplacement(Request(Vec2.Zero, new Vec2(10, 0), speed: 1.0));
            fixture.World.Tick(SimStep.Continuous(1.0)); // 0 -> 1，仍在进行中

            // 同一 tick 提交第二个 move_displace（不同目标）：应被忽略，继续沿第一个请求推进。
            fixture.Host.BeginControlledDisplacement(Request(new Vec2(1, 0), new Vec2(1, 10), speed: 1.0));
            fixture.World.Tick(SimStep.Continuous(1.0)); // 沿原请求继续 x 方向推进到 2，不应偏向 y。

            var pos = fixture.Units.GetPosition(HeroId);
            Assert.Equal(2.0, pos.X, 6);
            Assert.Equal(0.0, pos.Y, 6);
        }
    }
}
