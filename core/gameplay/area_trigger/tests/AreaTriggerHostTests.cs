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
            var world = new Core.Gameplay.WorldState.WorldState(bus);
            var expr = new FakeExprHostFactory();
            var options = new AreaTriggerOptions();
            var host = new AreaTriggerHost(world, bus, expr, hooks, options);
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

            var world = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(world, bus, new FakeExprHostFactory());
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

            var world = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new AreaTriggerHost(world, bus, new FakeExprHostFactory());
            host.LoadForMap(new Id("world.sample_map"), registry);
            host.LoadForMap(new Id("world.other_map"), registry);

            host.UnloadMap(new Id("world.sample_map"));

            var events = Subscribe(bus);
            host.Evaluate(new Id("unit.sample_player"), new Vec2(0, 0));
            bus.DispatchPending();

            // 只剩 world.other_map 的触发体，坐标 (0,0) 命中它自己的圆形范围。
            Assert.Single(events.OfType<AreaTriggerEnteredEvent>());
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
