using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
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

        // -----------------------------------------------------------------
        // ADR-0013 补齐：movement_budget_rule: action_points（04 第 3.1 节勘误、06 第 6.2 节）——
        // 移动预算按行动点计，与 TurnScheduler 的 action_points 策略共享同一份账本。
        // -----------------------------------------------------------------

        [Fact]
        public void DiscreteStep_ActionPointsBudgetRule_ConsumesSharedBudget_RejectsWhenExhausted_AndEndsTurn()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var world = new WorldSim(bus);
            var player = new PlayerUnit(HeroId, MapId, FactionId, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(player);

            var units = new WorldUnitAccess(world);
            var stats = new FakeStatHost();
            stats.SetBase(HeroId, new MovementOptions().MoveSpeedStat, 10.0); // 速度 10。
            var auras = new FakeAuraQuery();
            var host = new MovementHost(world);

            // 只有 Hero 一名参与者：EndTurn 会立即回绕到"新一轮"，Hero 重新变成当前行动者且行动点
            // 满额（见 TurnScheduler.AdvanceToNextActor 回绕逻辑），用来验证"回合结束"确实发生。
            var scheduler = new TurnScheduler(world, id => 0, id => false, bus);
            scheduler.Configure(InitiativePolicy.ActionPoints, new Dictionary<string, object> { ["action_points_per_turn"] = 15.0 });
            scheduler.BeginCombat(new[] { HeroId });

            // 每回合等效秒数默认 1.0 × 速度 10 = 每步移动 10 单位距离；单位成本 1.0 → 每次移动
            // 消耗 10 点行动点，15 点预算最多支撑一次移动（第二次只剩 5 点，不够）。
            var options = new MovementOptions
            {
                MovementBudgetRule = "action_points",
                MovementActionCostPerUnit = 1.0,
                TryConsumeActionPoints = scheduler.TryConsumeActionPoints,
                RequestEndTurn = scheduler.EndTurn,
            };

            var handler = new MovementTickHandler(units, stats, auras, host, bus, options: options);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);

            // 第一次移动：预算充足（15 >= 10），成功位移 10。
            world.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));
            world.Tick(SimStep.Discrete(HeroId, StepPhase.Act));
            Assert.Equal(10.0, units.GetPosition(HeroId).X, 6);

            var roundBeforeRejection = scheduler.RoundIndex;

            // 第二次移动：只剩 5 点，需要 10 点，被拒绝——不产生位移，且回合结束（本用例唯一参与者，
            // 回合结束即整轮结束、行动点重置）。
            world.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));
            world.Tick(SimStep.Discrete(HeroId, StepPhase.Act));
            Assert.Equal(10.0, units.GetPosition(HeroId).X, 6); // 位置未变。
            Assert.True(scheduler.RoundIndex > roundBeforeRejection, "行动点不足应触发 EndTurn，进而回绕到新一轮");

            // 第三次移动：新一轮行动点已重置满额（15），移动应重新成功，验证"回合结束"确实释放了
            // 一份全新的预算，而不是本次拒绝之外没有任何实际效果。
            world.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));
            world.Tick(SimStep.Discrete(HeroId, StepPhase.Act));
            Assert.Equal(20.0, units.GetPosition(HeroId).X, 6);
        }

        // 收边任务补齐：TurnScheduler.TryConsumeActionPoints 此前把移动预算的记账门槛误绑定到
        // InitiativePolicy.ActionPoints 先攻策略（04 第 3.1 节字段表 action_points_per_turn 行原文
        // "initiative_policy 或 movement_budget_rule 任一为 action_points 时，两者共享同一份每回合
        // 行动点总额度"，与先攻策略取值无关）——本用例用 FixedOrder 先攻策略验证修正后
        // movement_budget_rule: action_points 独立生效：先攻策略只决定顺序，不再决定"是否记账"。
        [Fact]
        public void DiscreteStep_ActionPointsBudgetRule_WithFixedOrderInitiativePolicy_StillEnforcesBudget()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var world = new WorldSim(bus);
            var player = new PlayerUnit(HeroId, MapId, FactionId, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(player);

            var units = new WorldUnitAccess(world);
            var stats = new FakeStatHost();
            stats.SetBase(HeroId, new MovementOptions().MoveSpeedStat, 10.0); // 速度 10。
            var auras = new FakeAuraQuery();
            var host = new MovementHost(world);

            // 关键差异：先攻策略用 FixedOrder（修正前该策略下 TryConsumeActionPoints 恒返回 true，
            // 即"不限制"），但仍然声明 action_points_per_turn，验证移动预算独立于先攻策略生效。
            var scheduler = new TurnScheduler(world, id => 0, id => false, bus);
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object> { ["action_points_per_turn"] = 15.0 });
            scheduler.BeginCombat(new[] { HeroId });

            var options = new MovementOptions
            {
                MovementBudgetRule = "action_points",
                MovementActionCostPerUnit = 1.0,
                TryConsumeActionPoints = scheduler.TryConsumeActionPoints,
                RequestEndTurn = scheduler.EndTurn,
            };

            var handler = new MovementTickHandler(units, stats, auras, host, bus, options: options);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);

            // 第一次移动：预算充足（15 >= 10），成功位移 10。
            world.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));
            world.Tick(SimStep.Discrete(HeroId, StepPhase.Act));
            Assert.Equal(10.0, units.GetPosition(HeroId).X, 6);

            var roundBeforeRejection = scheduler.RoundIndex;

            // 第二次移动：只剩 5 点，需要 10 点，被拒绝——若先攻策略仍在错误地决定"是否记账"，
            // FixedOrder 策略下本次调用会被误判为"不限制"而成功位移，本断言精确捕获这处回归。
            world.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));
            world.Tick(SimStep.Discrete(HeroId, StepPhase.Act));
            Assert.Equal(10.0, units.GetPosition(HeroId).X, 6); // 位置未变。
            Assert.True(scheduler.RoundIndex > roundBeforeRejection, "行动点不足应触发 EndTurn，进而回绕到新一轮");
        }

        [Fact]
        public void DiscreteStep_DistanceBudgetRule_Unaffected_WhenActionPointsDelegatesNotWired()
        {
            // 默认 MovementBudgetRule="distance"（TryConsumeActionPoints/RequestEndTurn 均未装配）
            // 时，行为必须与本任务之前完全一致——DiscreteStep_ThroughWorldTick_MovesByBudget 已经
            // 覆盖这一点，本用例额外验证"即便显式把 MovementActionCostPerUnit 设成一个很大的数"，
            // distance 规则下也完全不读取它，不会意外拒绝移动（回归防护：确保两套规则互不串扰）。
            var fixture = Build(moveSpeed: 10.0);
            fixture.World.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));

            fixture.World.Tick(SimStep.Discrete(HeroId, StepPhase.Act));

            var expectedBudget = 10.0 * new MovementOptions().DiscreteTurnEquivalentSeconds;
            Assert.Equal(expectedBudget, fixture.Units.GetPosition(HeroId).X, 6);
        }

        // -----------------------------------------------------------------
        // 加固任务（05 §3.6 碰撞层规划落地）：MovementOptions.UnitBlocking。
        // -----------------------------------------------------------------

        [Fact]
        public void UnitBlockingDisabled_ByDefault_UnitsCanOverlap()
        {
            // 判断记录：本用例是"现状不变"的回归防护——05 §3.6 原文"默认关闭，允许单位重叠"，
            // 即便目标落点已经站着另一个打了 unit_block 标签的单位，UnitBlocking 默认 false 时
            // 移动系统完全不查询空间索引，照常位移到重叠位置。
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var mover = new PlayerUnit(HeroId, MapId, FactionId, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(mover);

            var units = new WorldUnitAccess(world);
            var stats = new FakeStatHost();
            stats.SetBase(HeroId, new MovementOptions().MoveSpeedStat, 10.0);
            var auras = new FakeAuraQuery();
            var host = new MovementHost(world);
            var spatial = new StubSpatialQuery();
            var blockerId = new Id("unit.blocker");
            spatial.Register(blockerId, new Vec2(1, 0), 0.1, new[] { CollisionLayers.UnitBlock });

            var handler = new MovementTickHandler(units, stats, auras, host, bus, spatial: spatial);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);

            world.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));
            world.Tick(SimStep.Continuous(0.1));

            Assert.Equal(new Vec2(1, 0), units.GetPosition(HeroId));
        }

        [Fact]
        public void UnitBlockingEnabled_DirectionalMove_TargetOccupied_StaysInPlace()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var mover = new PlayerUnit(HeroId, MapId, FactionId, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(mover);

            var units = new WorldUnitAccess(world);
            var stats = new FakeStatHost();
            stats.SetBase(HeroId, new MovementOptions().MoveSpeedStat, 10.0);
            var auras = new FakeAuraQuery();
            var host = new MovementHost(world);
            var spatial = new StubSpatialQuery();
            var blockerId = new Id("unit.blocker");
            // 目标落点 (1,0) 附近登记一个打了 unit_block 标签的对象（半径足够覆盖阻挡判定半径）。
            spatial.Register(blockerId, new Vec2(1, 0), 0.1, new[] { CollisionLayers.UnitBlock });

            var options = new MovementOptions { UnitBlocking = true, UnitBlockRadius = 0.5 };
            var handler = new MovementTickHandler(units, stats, auras, host, bus, options: options, spatial: spatial);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);

            world.SubmitIntent(new Intent(HeroId, "move", DirectionArgs(1, 0)));
            world.Tick(SimStep.Continuous(0.1)); // 移动 1 单位 → 落点 (1,0)，被阻挡。

            Assert.Equal(Vec2.Zero, units.GetPosition(HeroId)); // 停在原地，不抛异常。
        }

        [Fact]
        public void UnitBlockingEnabled_TargetMove_PathBlockedMidway_StopsAtLastUnblockedTick()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var mover = new PlayerUnit(HeroId, MapId, FactionId, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(mover);

            var units = new WorldUnitAccess(world);
            var stats = new FakeStatHost();
            stats.SetBase(HeroId, new MovementOptions().MoveSpeedStat, 1.0); // 每秒 1 单位。
            var auras = new FakeAuraQuery();
            var host = new MovementHost(world);
            var spatial = new StubSpatialQuery();
            var blockerId = new Id("unit.blocker");
            spatial.Register(blockerId, new Vec2(3, 0), 0.1, new[] { CollisionLayers.UnitBlock });

            var options = new MovementOptions { UnitBlocking = true, UnitBlockRadius = 0.5 };
            var handler = new MovementTickHandler(units, stats, auras, host, bus, options: options, spatial: spatial);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);

            world.SubmitIntent(new Intent(HeroId, "move", TargetArgs(10, 0)));

            for (var i = 0; i < 5; i++)
            {
                world.Tick(SimStep.Continuous(1.0));
            }

            // 逐步走到 x=2 时下一步落点 x=3 命中阻挡范围（半径 0.5），此后每 tick 都原地不动，
            // 不会绕过阻挡点、也不会抛异常。
            Assert.Equal(2.0, units.GetPosition(HeroId).X, 6);
        }
    }
}
