using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.HookRegistry;
using Core.Gameplay.AreaTrigger;
using Core.Gameplay.WorldState;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    public sealed class AreaTriggerHostTests
    {
        private static Id Map => new Id("world.sample_map");

        private static (AreaTriggerHost Host, IEventBus Bus, FakeExprHostFactory Expr, AreaTriggerOptions Options) NewHost(
            IHookRegistry? hooks = null)
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            // 加固任务（AreaTrigger 实体化）：AreaTriggerHost 新增 IWorldSim 依赖，测试用真实
            // WorldSim（惯例同 core/gameplay/loot/tests/LootTestSupport.NewWorld），与 bus 共用
            // 同一份事件总线，才能让 entity.created/entity.destroyed 与 area.trigger_entered/left
            // 落在同一条可观测的事件流上。
            var worldSim = new Core.Foundation.SimLoop.WorldSim(bus);
            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            var expr = new FakeExprHostFactory();
            var options = new AreaTriggerOptions();
            var host = new AreaTriggerHost(worldSim, worldState, bus, expr, hooks, options);
            return (host, bus, expr, options);
        }

        [Fact]
        public void Evaluate_MapTransitionEnter_InvokesDelegateAndSceneRouter()
        {
            var (host, bus, _, options) = NewHost();
            var sceneRouter = new FakeSceneRouter();
            options.SceneRouter = sceneRouter;

            Id? calledUnit = null;
            Id calledTarget = default;
            Id? calledSpawn = null;
            options.MapTransitionRequested = (unit, target, spawn) => { calledUnit = unit; calledTarget = target; calledSpawn = spawn; };

            var def = AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.MapTransitionRow("area.sample_door", "world.sample_map", "world.other_map", "tp.other_map.entry")));
            host.Register(def);

            var events = Subscribe(bus);
            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));
            bus.DispatchPending();

            Assert.NotNull(calledUnit);
            Assert.Equal(new Id("unit.sample_player"), calledUnit!.Value);
            Assert.Equal(new Id("world.other_map"), calledTarget);
            Assert.NotNull(calledSpawn);
            Assert.Equal(new Id("tp.other_map.entry"), calledSpawn!.Value);
            Assert.Single(sceneRouter.LoadSceneCalls);
            Assert.Equal(new Id("world.other_map"), sceneRouter.LoadSceneCalls[0]);
            Assert.Single(events.OfType<AreaTriggerEnteredEvent>());
        }

        [Fact]
        public void Evaluate_SceneRouterThrows_DoesNotStopEvaluateLoop()
        {
            var (host, bus, _, options) = NewHost();
            var sceneRouter = new FakeSceneRouter { ThrowOnLoad = new InvalidOperationException("boom") };
            options.SceneRouter = sceneRouter;
            var otherEntered = false;
            options.EncounterStartRequested = (_, _) => otherEntered = true;

            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.MapTransitionRow("area.sample_door", "world.sample_map", "world.other_map"))));
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.EncounterStartRow("area.sample_boss", "world.sample_map", "encounter.sample_boss"))));

            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));

            Assert.True(otherEntered);
        }

        [Fact]
        public void Evaluate_QuestExploreEnter_OnlyEmitsEvent()
        {
            var (host, bus, _, _) = NewHost();
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"))));

            var events = Subscribe(bus);
            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));
            bus.DispatchPending();

            var entered = Assert.Single(events.OfType<AreaTriggerEnteredEvent>());
            Assert.Equal(new Id("area.sample_grove"), entered.TriggerId);
        }

        [Fact]
        public void Evaluate_EncounterStartEnter_InvokesDelegate()
        {
            var (host, _, _, options) = NewHost();
            Id? gotUnit = null;
            Id? gotEncounter = null;
            options.EncounterStartRequested = (unit, encounter) => { gotUnit = unit; gotEncounter = encounter; };

            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.EncounterStartRow("area.sample_boss", "world.sample_map", "encounter.sample_boss"))));

            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));

            Assert.Equal(new Id("unit.sample_player"), gotUnit);
            Assert.Equal(new Id("encounter.sample_boss"), gotEncounter);
        }

        [Fact]
        public void Evaluate_ScriptEnterAndLeave_InvokesHookWithPhases()
        {
            var hooks = new HookRegistry();
            hooks.DeclareHookPoint(new Id("found.hook.sample_area"), "triggerId, unitId, phase");
            var phases = new System.Collections.Generic.List<string>();
            hooks.Register(new Id("found.hook.sample_area"), args => phases.Add(args.Get<string>("phase")), 0);

            var (host, _, _, _) = NewHost(hooks);
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.ScriptRow("area.sample_trap_zone", "world.sample_map", "found.hook.sample_area"))));

            var unit = new Id("unit.sample_player");
            host.Evaluate(unit, new Vec2(0, 0));
            host.Evaluate(unit, new Vec2(100, 100));

            Assert.Equal(new[] { "enter", "leave" }, phases);
        }

        [Fact]
        public void Evaluate_ScriptWithoutHookRegistry_WarnsButDoesNotThrow()
        {
            var (host, _, _, _) = NewHost(hooks: null);
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.ScriptRow("area.sample_trap_zone", "world.sample_map", "found.hook.sample_area"))));

            var ex = Record.Exception(() => host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0)));
            Assert.Null(ex);
        }

        [Fact]
        public void Evaluate_ConditionFalse_DoesNotEnter()
        {
            var (host, bus, expr, _) = NewHost();
            expr.Host.Set("self", "is_alive", ExprValue.OfBool(false));

            var row = WithCondition(AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"), "self.is_alive");
            host.Register(AreaTriggerDef.FromRecord(RecordOf(row)));

            var events = Subscribe(bus);
            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));
            bus.DispatchPending();

            Assert.Empty(events);
        }

        [Fact]
        public void Evaluate_ConditionTrue_Enters()
        {
            var (host, bus, expr, _) = NewHost();
            expr.Host.Set("self", "is_alive", ExprValue.OfBool(true));

            var row = WithCondition(AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"), "self.is_alive");
            host.Register(AreaTriggerDef.FromRecord(RecordOf(row)));

            var events = Subscribe(bus);
            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));
            bus.DispatchPending();

            Assert.Single(events.OfType<AreaTriggerEnteredEvent>());
        }

        [Fact]
        public void Evaluate_OneShot_SecondEntryDoesNotFireAgain()
        {
            var (host, bus, _, _) = NewHost();
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map", oneShot: true))));

            var unit = new Id("unit.sample_player");
            var events = Subscribe(bus);

            host.Evaluate(unit, new Vec2(0, 0)); // 进入，触发一次
            host.Evaluate(unit, new Vec2(100, 100)); // 离开
            host.Evaluate(unit, new Vec2(0, 0)); // 再次进入：one_shot 已触发，跳过

            bus.DispatchPending();

            Assert.Single(events.OfType<AreaTriggerEnteredEvent>());
            // 第一次离开产生一次 left；第二次进入被跳过，不应再有 left。
            Assert.Single(events.OfType<AreaTriggerLeftEvent>());
        }

        [Fact]
        public void Evaluate_MultipleUnits_IndependentState()
        {
            var (host, bus, _, _) = NewHost();
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"))));

            var unitA = new Id("unit.sample_a");
            var unitB = new Id("unit.sample_b");
            var events = Subscribe(bus);

            host.Evaluate(unitA, new Vec2(0, 0));
            host.Evaluate(unitB, new Vec2(100, 100)); // 不在范围内，不触发

            bus.DispatchPending();

            var entered = events.OfType<AreaTriggerEnteredEvent>().ToList();
            Assert.Single(entered);
            Assert.Equal(unitA, entered[0].UnitId);
        }

        [Fact]
        public void Unregister_NoMoreEventsAfterUnregister()
        {
            var (host, bus, _, _) = NewHost();
            var triggerId = host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"))));

            host.Unregister(triggerId);

            var events = Subscribe(bus);
            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));
            bus.DispatchPending();

            Assert.Empty(events);
        }

        [Fact]
        public void LoadForMap_OnlyLoadsMatchingMap()
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus,
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"),
                AreaTriggerTestSupport.QuestExploreRow("area.sample_other", "world.other_map"));
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);

            var worldSim = new Core.Foundation.SimLoop.WorldSim(bus);
            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(worldSim, worldState, bus, new FakeExprHostFactory());
            host.LoadForMap(new Id("world.sample_map"), registry);

            var events = Subscribe(bus);
            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));
            bus.DispatchPending();

            Assert.Single(events.OfType<AreaTriggerEnteredEvent>());
        }

        [Fact]
        public void UnloadMap_RemovesOnlyThatMapsTriggers()
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus,
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"),
                AreaTriggerTestSupport.QuestExploreRow("area.sample_other", "world.other_map"));
            registry.LoadAll();

            var worldSim = new Core.Foundation.SimLoop.WorldSim(bus);
            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(worldSim, worldState, bus, new FakeExprHostFactory());
            host.LoadForMap(new Id("world.sample_map"), registry);
            host.LoadForMap(new Id("world.other_map"), registry);

            host.UnloadMap(new Id("world.sample_map"));

            var events = Subscribe(bus);
            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));
            bus.DispatchPending();

            // 只剩 world.other_map 的触发体，坐标 (0,0) 命中它自己的圆形范围。
            Assert.Single(events.OfType<AreaTriggerEnteredEvent>());
        }

        // -----------------------------------------------------------------
        // ADR-0090（消费方第三十六批相邻缺口）：区域整体卸载时补发 area.trigger_left。
        // -----------------------------------------------------------------

        /// <summary>复现（修复前）：单位仍在区域内时 <see cref="AreaTriggerHost.UnloadMap"/> 不会补发
        /// <see cref="AreaTriggerLeftEvent"/>——修复前本用例断言 <c>Assert.Single</c> 会因 0 个事件
        /// 而失败。修复后应收到恰好一条 <c>reason=unloaded</c> 的离开事件。</summary>
        [Fact]
        public void UnloadMap_UnitStillInsideTrigger_EmitsLeftEventWithReasonUnloaded()
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus,
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"));
            registry.LoadAll();

            var worldSim = new Core.Foundation.SimLoop.WorldSim(bus);
            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(worldSim, worldState, bus, new FakeExprHostFactory());
            host.LoadForMap(new Id("world.sample_map"), registry);

            var unit = new Id("unit.sample_player");
            host.Evaluate(unit, new Vec2(0, 0)); // 进入后一直停留在区域内，从未正常走出

            var events = Subscribe(bus);
            host.UnloadMap(new Id("world.sample_map"));
            bus.DispatchPending();

            var left = Assert.Single(events.OfType<AreaTriggerLeftEvent>());
            Assert.Equal(new Id("area.sample_grove"), left.TriggerId);
            Assert.Equal(unit, left.UnitId);
            Assert.Equal(AreaTriggerLeaveReason.Unloaded, left.Reason);
            Assert.True(left.TryGetField("reason", out var reasonValue));
            Assert.Equal("unloaded", reasonValue.AsString);
        }

        /// <summary>不变量（三个分支合一，见任务书"不变量用例只加一个分支，不另开第三条"）：
        /// ① 正常走出（<see cref="AreaTriggerHost.Evaluate"/> 检测到离开）默认 <c>reason=moved</c>；
        /// ② 卸载时区域内本就没有单位（<c>area.sample_empty</c>，从未被 <c>Evaluate</c> 命中）不产生
        /// 任何事件；③ 已经正常走出的单位（<c>leftUnit</c>）卸载时不会被重复补发一次 left。</summary>
        [Fact]
        public void UnloadMap_InvariantsForLeaveReasonAndDuplicateSuppression()
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus,
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"),
                J.O(
                    ("id", J.S("area.sample_empty")),
                    ("map_id", J.S("world.sample_map")),
                    ("shape", AreaTriggerTestSupport.CircleShape(500, 500, 5)),
                    ("trigger_type", J.S("quest_explore")),
                    ("one_shot", J.B(false)),
                    ("params", J.O())));
            registry.LoadAll();

            var worldSim = new Core.Foundation.SimLoop.WorldSim(bus);
            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(worldSim, worldState, bus, new FakeExprHostFactory());
            host.LoadForMap(new Id("world.sample_map"), registry);

            var stayingUnit = new Id("unit.sample_staying");
            var leftUnit = new Id("unit.sample_left_already");

            host.Evaluate(stayingUnit, new Vec2(0, 0)); // 进入 sample_grove，之后一直留在里面
            host.Evaluate(leftUnit, new Vec2(0, 0)); // 同样先进入 sample_grove

            var events = Subscribe(bus);
            host.Evaluate(leftUnit, new Vec2(100, 100)); // ① 正常走出：reason 默认为 moved
            bus.DispatchPending();

            var movedLeave = Assert.Single(events.OfType<AreaTriggerLeftEvent>());
            Assert.Equal(leftUnit, movedLeave.UnitId);
            Assert.Equal(AreaTriggerLeaveReason.Moved, movedLeave.Reason);
            Assert.True(movedLeave.TryGetField("reason", out var movedReasonValue));
            Assert.Equal("moved", movedReasonValue.AsString);

            events.Clear();

            // ②③ 卸载整张地图：sample_empty 从未被进入 -> 不产生事件；sample_grove 里 leftUnit 已经
            // 正常走出（不在 _inside 里）-> 不重复发；只有仍在里面的 stayingUnit 补发一条 reason=unloaded。
            host.UnloadMap(new Id("world.sample_map"));
            bus.DispatchPending();

            var unloadLeave = Assert.Single(events.OfType<AreaTriggerLeftEvent>());
            Assert.Equal(new Id("area.sample_grove"), unloadLeave.TriggerId);
            Assert.Equal(stayingUnit, unloadLeave.UnitId);
            Assert.Equal(AreaTriggerLeaveReason.Unloaded, unloadLeave.Reason);
        }

        [Fact]
        public void RegisterTrap_EnterInvokesTrapTriggerDelegate()
        {
            var (host, _, _, options) = NewHost();
            Id? gotGobj = null;
            Id? gotUnit = null;
            options.TrapTrigger = (gobjId, unitId) => { gotGobj = gobjId; gotUnit = unitId; };

            var shape = Core.Foundation.EngineAdapter.Shape.Circle(Vec2.Zero, 3);
            host.RegisterTrap(new Id("gobj.sample_trap"), shape, Map);

            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));

            Assert.Equal(new Id("gobj.sample_trap"), gotGobj);
            Assert.Equal(new Id("unit.sample_player"), gotUnit);
        }

        [Fact]
        public void RegisterTrap_WithoutDelegate_WarnsButDoesNotThrow()
        {
            var (host, _, _, _) = NewHost();
            var shape = Core.Foundation.EngineAdapter.Shape.Circle(Vec2.Zero, 3);
            host.RegisterTrap(new Id("gobj.sample_trap"), shape, Map);

            var ex = Record.Exception(() => host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0)));
            Assert.Null(ex);
        }

        // -----------------------------------------------------------------
        // ADR-0066（消费方反馈第九批，阻塞）：GetActiveTriggerIds 权威状态查询。
        // -----------------------------------------------------------------

        [Fact]
        public void GetActiveTriggerIds_NotInAnyTrigger_ReturnsEmpty()
        {
            var (host, _, _, _) = NewHost();
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"))));

            Assert.Empty(host.GetActiveTriggerIds(new Id("unit.sample_player")));
        }

        [Fact]
        public void GetActiveTriggerIds_OrderedByEntryOrder_NotByIdSortOrder()
        {
            var (host, _, _, _) = NewHost();
            // 刻意让 id 字母序（a_inner < z_outer）与真实进入先后相反：z_outer 半径 10 先被进入，
            // a_inner 半径 3 后被进入——若实现误按 SortedDictionary 遍历序（即 Id 字典序）而不是
            // 真正的进入序号排序，这条用例会得到 [a_inner, z_outer]，与预期 [z_outer, a_inner] 不同。
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.z_outer", "world.sample_map", radius: 10))));
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.a_inner", "world.sample_map", radius: 3))));

            var unit = new Id("unit.sample_player");
            host.Evaluate(unit, new Vec2(8, 0)); // 只在 z_outer 范围内（半径 10，不在半径 3 内）
            host.Evaluate(unit, new Vec2(0, 0)); // 同时落入 a_inner，z_outer 仍在范围内不重新进入

            Assert.Equal(
                new[] { new Id("area.z_outer"), new Id("area.a_inner") },
                host.GetActiveTriggerIds(unit));
        }

        [Fact]
        public void GetActiveTriggerIds_LeavingInnerTrigger_FallsBackToOuterOnly()
        {
            var (host, _, _, _) = NewHost();
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.z_outer", "world.sample_map", radius: 10))));
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.a_inner", "world.sample_map", radius: 3))));

            var unit = new Id("unit.sample_player");
            host.Evaluate(unit, new Vec2(8, 0)); // 进入 z_outer
            host.Evaluate(unit, new Vec2(0, 0)); // 进入 a_inner（仍在 z_outer 内）
            host.Evaluate(unit, new Vec2(8, 0)); // 离开 a_inner，仍在 z_outer 内

            Assert.Equal(new[] { new Id("area.z_outer") }, host.GetActiveTriggerIds(unit));
        }

        [Fact]
        public void GetActiveTriggerIds_IncludesTraps()
        {
            var (host, _, _, options) = NewHost();
            options.TrapTrigger = (_, _) => { };
            var shape = Core.Foundation.EngineAdapter.Shape.Circle(Vec2.Zero, 3);
            var trapId = host.RegisterTrap(new Id("gobj.sample_trap"), shape, Map);

            var unit = new Id("unit.sample_player");
            host.Evaluate(unit, new Vec2(0, 0));

            Assert.Equal(new[] { trapId }, host.GetActiveTriggerIds(unit));
        }

        [Fact]
        public void GetActiveTriggerIds_MultipleUnits_IndependentOrder()
        {
            var (host, _, _, _) = NewHost();
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"))));

            var unitA = new Id("unit.sample_a");
            var unitB = new Id("unit.sample_b");
            host.Evaluate(unitA, new Vec2(0, 0));

            Assert.Equal(new[] { new Id("area.sample_grove") }, host.GetActiveTriggerIds(unitA));
            Assert.Empty(host.GetActiveTriggerIds(unitB));
        }

        [Fact]
        public void GetActiveTriggerIds_Unregister_RemovesStaleEntry()
        {
            var (host, _, _, _) = NewHost();
            var triggerId = host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"))));

            var unit = new Id("unit.sample_player");
            host.Evaluate(unit, new Vec2(0, 0));
            Assert.Equal(new[] { triggerId }, host.GetActiveTriggerIds(unit));

            host.Unregister(triggerId);

            Assert.Empty(host.GetActiveTriggerIds(unit));
        }

        [Fact]
        public void GetActiveTriggerIds_UnloadMap_RemovesStaleEntry()
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus,
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"));
            registry.LoadAll();

            var worldSim = new Core.Foundation.SimLoop.WorldSim(bus);
            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(worldSim, worldState, bus, new FakeExprHostFactory());
            host.LoadForMap(new Id("world.sample_map"), registry);

            var unit = new Id("unit.sample_player");
            host.Evaluate(unit, new Vec2(0, 0));
            Assert.Equal(new[] { new Id("area.sample_grove") }, host.GetActiveTriggerIds(unit));

            host.UnloadMap(new Id("world.sample_map"));

            Assert.Empty(host.GetActiveTriggerIds(unit));
        }

        [Fact]
        public void IAreaTriggerHost_GetActiveTriggerIds_ForwardsToHost()
        {
            var (host, _, _, _) = NewHost();
            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"))));

            var unit = new Id("unit.sample_player");
            host.Evaluate(unit, new Vec2(0, 0));

            IAreaTriggerHost iface = host;
            Assert.Equal(new[] { new Id("area.sample_grove") }, iface.GetActiveTriggerIds(unit));
        }

        // -----------------------------------------------------------------
        // 加固任务：AreaTrigger 从"纯数据记录 + 宿主字典"改为真正的 Entity 子类
        // （见 core/gameplay/area_trigger/contracts/AreaTriggerEntity.cs）。
        // -----------------------------------------------------------------

        [Fact]
        public void Register_CreatesAreaTriggerEntity_WithPositionAndMapIdFromDef()
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var worldSim = new Core.Foundation.SimLoop.WorldSim(bus);
            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(worldSim, worldState, bus, new FakeExprHostFactory());

            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"))));

            var entities = worldSim.QueryEntities(new Core.Foundation.SimLoop.EntityFilter(
                kind: Core.Foundation.SimLoop.EntityKinds.AreaTrigger));
            var entity = Assert.Single(entities);
            Assert.Equal(new Id("world.sample_map"), entity.MapId);
            // QuestExploreRow 用 CircleShape(0, 0, 5)：Shape.Origin 即圆心 (0,0)。
            Assert.Equal(new Vec2(0, 0), entity.Position);
            Assert.Equal(new Id("area.sample_grove"), entity.TemplateId);
        }

        [Fact]
        public void Unregister_MarksEntityForDestruction_EmitsEntityDestroyed()
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var worldSim = new Core.Foundation.SimLoop.WorldSim(bus);
            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(worldSim, worldState, bus, new FakeExprHostFactory());

            var triggerId = host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"))));

            var destroyed = new System.Collections.Generic.List<Id>();
            bus.Subscribe<Core.Foundation.SimLoop.EntityDestroyedEvent>(
                Core.Foundation.SimLoop.SimEventKeys.EntityDestroyed, e => destroyed.Add(e.EntityId));

            host.Unregister(triggerId);
            // MarkForDestruction 只是排入下一次 Tick 的生命周期清理阶段才真正生效（见
            // IWorldSim.MarkForDestruction 注释），本测试推进一次 tick 让 entity.destroyed 真正派发。
            worldSim.Tick(Core.Foundation.SimLoop.SimStep.Continuous(0.016));

            Assert.Single(destroyed);
            Assert.Empty(worldSim.QueryEntities(new Core.Foundation.SimLoop.EntityFilter(
                kind: Core.Foundation.SimLoop.EntityKinds.AreaTrigger)));
        }

        [Fact]
        public void UnloadMap_DestroysEntitiesForThatMapOnly()
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus,
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map"),
                AreaTriggerTestSupport.QuestExploreRow("area.sample_other", "world.other_map"));
            registry.LoadAll();

            var worldSim = new Core.Foundation.SimLoop.WorldSim(bus);
            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(worldSim, worldState, bus, new FakeExprHostFactory());
            host.LoadForMap(new Id("world.sample_map"), registry);
            host.LoadForMap(new Id("world.other_map"), registry);

            host.UnloadMap(new Id("world.sample_map"));
            worldSim.Tick(Core.Foundation.SimLoop.SimStep.Continuous(0.016));

            var remaining = worldSim.QueryEntities(new Core.Foundation.SimLoop.EntityFilter(
                kind: Core.Foundation.SimLoop.EntityKinds.AreaTrigger));
            var entity = Assert.Single(remaining);
            Assert.Equal(new Id("world.other_map"), entity.MapId);
        }

        [Fact]
        public void RegisterTrap_CreatesAreaTriggerEntity_WithNullTriggerType()
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var worldSim = new Core.Foundation.SimLoop.WorldSim(bus);
            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(worldSim, worldState, bus, new FakeExprHostFactory());

            var shape = Core.Foundation.EngineAdapter.Shape.Circle(new Vec2(7, 9), 3);
            host.RegisterTrap(new Id("gobj.sample_trap"), shape, Map);

            var entities = worldSim.QueryEntities(new Core.Foundation.SimLoop.EntityFilter(
                kind: Core.Foundation.SimLoop.EntityKinds.AreaTrigger));
            var entity = Assert.Single(entities);
            var areaTriggerEntity = Assert.IsType<AreaTriggerEntity>(entity);
            Assert.Null(areaTriggerEntity.TriggerType);
            Assert.False(areaTriggerEntity.OneShot);
            Assert.Equal(new Vec2(7, 9), entity.Position);
        }

        [Fact]
        public void Evaluate_OneShot_FiredStateStillRecordedInWorldState()
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var worldSim = new Core.Foundation.SimLoop.WorldSim(bus);
            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(worldSim, worldState, bus, new FakeExprHostFactory());

            host.Register(AreaTriggerDef.FromRecord(RecordOf(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map", oneShot: true))));

            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));

            // 05 第 1.5 节"oneShot 已触发的状态经 WorldState 记录"——本实体化改动不改变这一路径，
            // 已触发标志仍然落在 IWorldState，不落在 AreaTriggerEntity 本身（本类型没有暴露任何
            // "已触发"字段）。
            Assert.True(worldState.Has(new Id("world.area.sample_grove.fired")));
        }

        [Fact]
        public void AreaTriggerHostAndEntity_DoNotImplementIPersistable()
        {
            // 见 05 第 1.5 节"进存档：手工放置的固定触发体不进存档（随地图数据加载）"。
            Assert.False(typeof(Core.Foundation.SaveSystem.IPersistable).IsAssignableFrom(typeof(AreaTriggerHost)));
            Assert.False(typeof(Core.Foundation.SaveSystem.IPersistable).IsAssignableFrom(typeof(AreaTriggerEntity)));
        }

        // -----------------------------------------------------------------

        private static Core.Foundation.DataRegistry.DataRecord RecordOf(Core.Foundation.Common.Json.JsonObject row)
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus, row);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException("测试数据未通过校验：\n" + string.Join("\n", report.Issues.Select(i => i.ToString())));
            }

            var id = row["id"] is Core.Foundation.Common.Json.JsonString s ? s.Value : throw new InvalidOperationException("row 缺少 id");
            return registry.Get(AreaTriggerSchemas.TriggerDef.Name, id)!;
        }

        private static Core.Foundation.Common.Json.JsonObject WithCondition(Core.Foundation.Common.Json.JsonObject row, string condition)
        {
            var builder = new Core.Foundation.Common.Json.JsonObjectBuilder();
            foreach (var kv in row)
            {
                if (kv.Key == "condition")
                {
                    continue;
                }

                builder.Add(kv.Key, kv.Value);
            }

            builder.Add("condition", J.S(condition));
            return builder.Build();
        }

        private static System.Collections.Generic.List<IEvent> Subscribe(IEventBus bus)
        {
            var list = new System.Collections.Generic.List<IEvent>();
            bus.Subscribe(AreaTriggerEventKeys.TriggerEntered, evt => list.Add(evt));
            bus.Subscribe(AreaTriggerEventKeys.TriggerLeft, evt => list.Add(evt));
            return list;
        }
    }
}
