using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Tests.Foundation.Feel;
using Tests.Foundation.SimLoop;
using Xunit;
using static Tests.Foundation.InputMap.BufferRig;

namespace Tests.Foundation.InputMap
{
    /// <summary>输入缓冲的接线：本地输入边沿 → 缓冲、tick 步骤 1 处理器与世界 tick 管线、宽限窗口、schema 与既有数据兼容。</summary>
    public class InputBufferIntegrationTests
    {
        private static readonly Id AttackId = new Id("input.action.attack");

        // ---------------------------------------------------------------- 本地输入边沿

        private sealed class RecordingSink : IInputEdgeSink
        {
            public readonly List<string> Edges = new List<string>();

            public void OnButtonEdge(string actionName, bool isDown) => Edges.Add(actionName + (isDown ? "+" : "-"));
        }

        private static (InputMapHost Host, IEventBus Bus) MapWith(params ActionDefinition[] actions)
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(InputMapEventKeys.ActionTriggered, "input", new[] { "actionName" }),
                new EventDefinition(InputMapEventKeys.RebindConflict, "input", new[] { "actionName", "binding" }),
            });
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
            var host = new InputMapHost(bus);
            host.DeclareActionSet(new Id("input.set.test"), actions);
            return (host, bus);
        }

        private static ActionDefinition Move() =>
            new ActionDefinition(new Id("input.action.move"), ActionKind.Axis2D, new[] { "composite2d:key:w|key:s|key:a|key:d" });

        [Fact]
        public void EdgeSink_ReceivesPressAndReleaseInRealOrder_EvenWhenBothHappenInOneBatch()
        {
            var attack = new ActionDefinition(AttackId, ActionKind.Button, new[] { "key:space" });
            var (map, bus) = MapWith(attack);
            var sink = new RecordingSink();
            map.SetEdgeSink(sink);
            var stub = new StubInput();
            var triggered = 0;
            bus.Subscribe<InputActionTriggeredEvent>(InputMapEventKeys.ActionTriggered, _ => triggered++);

            stub.Press("space");
            stub.Release("space"); // 同一批次内按下又抬起：点按不丢
            map.Update(stub);
            bus.DispatchPending();

            Assert.Equal(new[] { "input.action.attack+", "input.action.attack-" }, sink.Edges);
            Assert.False(map.IsActionActive("input.action.attack")); // 既有语义：批次末持有状态
            Assert.Equal(1, triggered); // 既有的按下沿事件不受影响，仍恰好一次
        }

        [Fact]
        public void EdgeSink_TwoBindingsOfOneAction_FollowOrSemantics_OneUpEdgeAfterTheLastIsReleased()
        {
            var attack = new ActionDefinition(AttackId, ActionKind.Button, new[] { "key:space", "pad:rb" });
            var (map, _) = MapWith(attack);
            var sink = new RecordingSink();
            map.SetEdgeSink(sink);
            var stub = new StubInput();

            stub.Press("space");
            map.Update(stub);
            stub.PressGamepadButton(0, "rb");
            map.Update(stub); // 已激活，第二个绑定按下不产生边沿
            stub.Release("space");
            map.Update(stub); // 仍有 pad:rb 按着，不产生抬起边沿
            stub.ReleaseGamepadButton(0, "rb");
            map.Update(stub);

            Assert.Equal(new[] { "input.action.attack+", "input.action.attack-" }, sink.Edges);
        }

        [Fact]
        public void WithoutAnEdgeSink_UpdateBehavesAsBefore_AndTheSinkCanBeRemoved()
        {
            var attack = new ActionDefinition(AttackId, ActionKind.Button, new[] { "key:space" });
            var (map, _) = MapWith(attack);
            var sink = new RecordingSink();
            var stub = new StubInput();
            stub.Press("space");
            map.Update(stub);
            Assert.Empty(sink.Edges);

            map.SetEdgeSink(sink);
            stub.Release("space");
            map.Update(stub);
            Assert.Equal(new[] { "input.action.attack-" }, sink.Edges);

            map.SetEdgeSink(null);
            stub.Press("space");
            map.Update(stub);
            Assert.Single(sink.Edges);
        }

        /// <summary>
        /// 消费方反馈 P2 缺口 6：地图切换让场景清空（entity.destroyed → RemoveActor），游戏随后用同一个 id 把玩家单位重新加回世界。
        /// 复现：销毁行动者后，本地绑定被置空，重新加回的玩家再按键，边沿被忽略。
        /// 不变量：本地绑定在行动者被销毁、重建之后仍有效——同一 id 的按键仍进入缓冲，<see cref="InputBufferHost.LocalActorId"/> 不变。
        /// </summary>
        [Fact]
        public void BindLocalInput_SurvivesActorDestroyAndRecreate_EdgesStillReachTheBuffer()
        {
            var attack = new ActionDefinition(AttackId, ActionKind.Button, new[] { "key:space" }, "default", null,
                ActionClass.Attack, null, null, null, InputRepeatPolicy.Refresh, null, null);
            var (map, bus) = MapWith(attack);
            var buffer = new InputBufferHost(bus, new InputBufferOptions());
            buffer.DeclareActions(new[] { attack });
            buffer.BindLocalInput(map, Actor);
            var stub = new StubInput();

            stub.Press("space");
            map.Update(stub);
            buffer.BeginTick();
            Assert.Single(buffer.Snapshot(Actor));

            buffer.RemoveActor(Actor); // 场景清空：entity.destroyed
            Assert.Equal(Actor, buffer.LocalActorId);

            stub.Release("space");
            map.Update(stub);
            stub.Press("space");
            map.Update(stub);
            buffer.BeginTick();
            Assert.Single(buffer.Snapshot(Actor));
        }

        [Fact]
        public void BindLocalInput_RoutesEdgesIntoTheActorBuffer_WithTapHoldAndDirectionSnapshot()
        {
            const double holdMs = 200;
            var attack = new ActionDefinition(AttackId, ActionKind.Button, new[] { "key:space" }, "default", null,
                ActionClass.Attack, null, null, holdMs, InputRepeatPolicy.Refresh, null, null);
            var (map, bus) = MapWith(Move(), attack);
            var buffer = new InputBufferHost(bus, new InputBufferOptions());
            buffer.DeclareActions(new[] { Move(), attack });
            buffer.BindLocalInput(map, Actor, "input.action.move");
            var stub = new StubInput();

            // 点按：同一批次内按下又抬起，同时按着 d（方向 (1,0)）。
            stub.Press("d");
            stub.Press("space");
            stub.Release("space");
            map.Update(stub);
            buffer.BeginTick();
            var tap = buffer.Snapshot(Actor).Single();
            Assert.Equal(BufferHoldState.Tap, tap.HoldState);
            Assert.Equal(new Vec2(1, 0), tap.DirectionSnapshot);
            Assert.True(tap.FaceOnAccept);
            Assert.True(buffer.TryConsume(Actor, null, out _));
            buffer.BeginTick();
            Assert.Empty(buffer.Snapshot(Actor));

            // 按住：按下后经过 >= 阈值 tick 再抬起（没有轴输入：方向快照为空）。
            stub.Release("d");
            stub.Press("space");
            map.Update(stub);
            buffer.BeginTick();
            Assert.Equal(BufferHoldState.HoldPending, buffer.Snapshot(Actor).Single().HoldState);
            Assert.Null(buffer.Snapshot(Actor).Single().DirectionSnapshot);
            var threshold = FeelCalibration.MillisecondsToTicks(holdMs, 1.0 / 60.0);
            for (var i = 0; i < threshold; i++) buffer.BeginTick();
            stub.Release("space");
            map.Update(stub);
            buffer.BeginTick();
            var hold = buffer.Snapshot(Actor).Single();
            Assert.Equal(BufferHoldState.HoldReleased, hold.HoldState);
            Assert.True(hold.HeldTicks >= threshold);
        }

        // ---------------------------------------------------------------- tick 步骤 1 的处理器与世界管线

        private sealed class FakeAttackLayer : IBufferedIntentSink
        {
            public int AcceptableFrom;
            public InputBufferHost? Host;
            public readonly List<string> Asked = new List<string>();

            public bool TryAccept(Id actorId, BufferedIntent record, out Intent intent)
            {
                Asked.Add(Host!.CurrentTick + ":" + record.ActionId.Value);
                if (Host.CurrentTick < AcceptableFrom)
                {
                    intent = default;
                    return false;
                }
                intent = new Intent(actorId, "cast", new JsonObjectBuilder().Add("skill_id", new JsonString("skill.basic_attack")).Build());
                return true;
            }
        }

        [Fact]
        public void TickHandler_FeedsTheBufferedAttackIntoStep1_OnTheFirstAcceptableTick_BeforeTheSkillPipeline()
        {
            // "在后摇结束前 X ms 按下攻击，动作在可接受的第一个 tick 被取用"：后摇在 tick 40 结束，动作层从 40 起可接受。
            const int acceptable = 40;
            var step = 1.0 / 60.0;
            var windowTicks = FeelCalibration.MillisecondsToTicks(120, step);
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var resolver = new FeelResolver(FeelTestSupport.FrameworkProfiles(), FeelTestSupport.CalA("feel.preset.arpg_responsive"), step);
            var buffer = new InputBufferHost(bus, new InputBufferOptions { StepSeconds = step, Feel = resolver });
            buffer.DeclareActions(new[] { Def("input.action.attack", ActionClass.Attack) });
            var layer = new FakeAttackLayer { AcceptableFrom = acceptable, Host = buffer };
            InputBufferTickHandler.Register(world, buffer, layer);

            var seenAtSkillPipeline = new List<string>();
            world.RegisterPhaseHandler(TickPhase.SkillPipeline, new DelegatePhaseHandler((s, w) =>
            {
                foreach (var intent in w.CurrentIntents)
                {
                    if (intent.Kind == "cast") seenAtSkillPipeline.Add(buffer.CurrentTick + ":" + intent.ActorId.Value);
                }
            }));

            var pressTick = acceptable - windowTicks; // 窗口内最早的一个 tick 按下（按下到可接受共 windowTicks 个 tick）
            for (var t = 0; t < acceptable + 5; t++)
            {
                if (t == pressTick) buffer.Submit(Actor, AttackId);
                world.Tick(SimStep.Continuous(step));
            }

            // 按下后每个 tick 动作层都被问一次，直到可接受的第一个 tick 才接受；意图在同 tick 的步骤 3 之前出现，且仅一次。
            Assert.Equal(new[] { $"{acceptable}:{Actor.Value}" }, seenAtSkillPipeline);
            Assert.Equal(acceptable - pressTick + 1, layer.Asked.Count);
            Assert.Equal($"{acceptable}:input.action.attack", layer.Asked.Last());
        }

        [Fact]
        public void TickHandler_PressBeyondTheWindow_NeverReachesTheSkillPipeline()
        {
            const int acceptable = 40;
            var step = 1.0 / 60.0;
            var windowTicks = FeelCalibration.MillisecondsToTicks(120, step);
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var resolver = new FeelResolver(FeelTestSupport.FrameworkProfiles(), FeelTestSupport.CalA("feel.preset.arpg_responsive"), step);
            var buffer = new InputBufferHost(bus, new InputBufferOptions { StepSeconds = step, Feel = resolver });
            buffer.DeclareActions(new[] { Def("input.action.attack", ActionClass.Attack) });
            var layer = new FakeAttackLayer { AcceptableFrom = acceptable, Host = buffer };
            InputBufferTickHandler.Register(world, buffer, layer);
            var casts = 0;
            world.RegisterPhaseHandler(TickPhase.SkillPipeline, new DelegatePhaseHandler((s, w) => casts += w.CurrentIntents.Count(i => i.Kind == "cast")));
            var drops = new List<string>();
            bus.Subscribe<InputBufferDroppedEvent>(InputMapEventKeys.BufferDropped, e => drops.Add(e.Reason.ToString()));

            var pressTick = acceptable - windowTicks - 1; // 比窗口早一个 tick
            for (var t = 0; t < acceptable + 5; t++)
            {
                if (t == pressTick) buffer.Submit(Actor, AttackId);
                world.Tick(SimStep.Continuous(step));
            }

            Assert.Equal(0, casts);
            Assert.Equal(new[] { "Expired" }, drops);
        }

        [Fact]
        public void TickHandler_DiscreteStepsClearTheBufferAndDoNothing()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var buffer = new InputBufferHost(bus, new InputBufferOptions());
            buffer.DeclareActions(new[] { Def("input.action.attack", ActionClass.Attack) });
            var handler = new InputBufferTickHandler(buffer);
            var drops = new List<string>();
            bus.Subscribe<InputBufferDroppedEvent>(InputMapEventKeys.BufferDropped, e => drops.Add(e.Reason.ToString()));

            buffer.Submit(Actor, AttackId);
            handler.Execute(SimStep.Continuous(1.0 / 60.0), world);
            Assert.Single(buffer.Snapshot(Actor));

            handler.Execute(SimStep.Discrete(Actor, StepPhase.Act), world);
            bus.DispatchPending();

            Assert.Empty(buffer.Snapshot(Actor));
            Assert.Equal(new[] { "Cleared" }, drops);
        }

        // ---------------------------------------------------------------- 宽限窗口

        private sealed class MutableCondition : IGraceConditionEvaluator
        {
            public readonly HashSet<(Id, Id)> True = new HashSet<(Id, Id)>();

            public bool Evaluate(Id actorId, Id conditionId) => True.Contains((actorId, conditionId));
        }

        [Theory]
        [InlineData("feel.preset.arpg_responsive")] // grace_ms = 100
        [InlineData("feel.preset.rpg_classic")] // grace_ms = 0
        public void Grace_ConditionJustLost_StaysSatisfiedForGraceTicks_ThenIsRejected(string preset)
        {
            var step = 1.0 / 60.0;
            var resolver = new FeelResolver(FeelTestSupport.FrameworkProfiles(), FeelTestSupport.CalA(preset), step);
            var inRange = new Id("input.grace.target_in_range");
            var evaluator = new MutableCondition();
            var tracker = new GraceTracker(evaluator, resolver);
            tracker.Register(Actor, new[] { inRange });
            var graceMs = resolver.ResolveJudging(Actor).GetNumber("grace_ms");
            var graceTicks = FeelCalibration.MillisecondsToTicks(graceMs, step);
            const int lostAt = 10; // 条件在 tick 10 起为假（最近一次为真的 tick 是 9）

            var satisfied = new List<int>();
            var inGrace = new List<int>();
            for (var t = 0; t < lostAt + graceTicks + 5; t++)
            {
                if (t < lostAt) evaluator.True.Add((Actor, inRange)); else evaluator.True.Remove((Actor, inRange));
                tracker.Sample(t);
                if (tracker.IsSatisfied(Actor, inRange)) satisfied.Add(t);
                if (tracker.IsInGrace(Actor, inRange)) inGrace.Add(t);
            }

            // 满足：条件为真的 0..9，加上失效后的 graceTicks 个 tick（10..9+graceTicks）；之后拒绝。
            Assert.Equal(Enumerable.Range(0, lostAt + graceTicks).ToList(), satisfied);
            Assert.Equal(Enumerable.Range(lostAt, graceTicks).ToList(), inGrace);
            Assert.Equal(lostAt - 1, tracker.LastTrueTick(Actor, inRange));
        }

        [Fact]
        public void Grace_ArpgPresetGivesAWindow_ClassicPresetDoesNot_AndNeverTrueIsNeverSatisfied()
        {
            var step = 1.0 / 60.0;
            var cond = new Id("input.grace.has_target");
            Assert.Equal(6, FeelCalibration.MillisecondsToTicks(100, step)); // 档案 grace_ms=100 在 60 Hz 下的换算

            int GraceTicksAfterLoss(string preset)
            {
                var evaluator = new MutableCondition();
                var tracker = new GraceTracker(evaluator, new FeelResolver(FeelTestSupport.FrameworkProfiles(), FeelTestSupport.CalA(preset), step));
                tracker.Register(Actor, new[] { cond });
                Assert.False(tracker.IsSatisfied(Actor, cond)); // 从未为真
                evaluator.True.Add((Actor, cond));
                tracker.Sample(0);
                evaluator.True.Clear();
                var n = 0;
                for (var t = 1; t < 30; t++)
                {
                    tracker.Sample(t);
                    if (tracker.IsSatisfied(Actor, cond)) n++;
                }
                return n;
            }

            Assert.Equal(6, GraceTicksAfterLoss("feel.preset.arpg_responsive"));
            Assert.Equal(0, GraceTicksAfterLoss("feel.preset.rpg_classic"));
        }

        [Fact]
        public void Grace_AreAllSatisfied_RequiresEveryCondition_AndEmptyMeansNoConstraint_AndUnregisterForgets()
        {
            var evaluator = new MutableCondition();
            var tracker = new GraceTracker(evaluator);
            var a = new Id("input.grace.a");
            var b = new Id("input.grace.b");
            var def = new ActionDefinition(AttackId, ActionKind.Button, new[] { "key:space" }, "default", null,
                ActionClass.Attack, null, null, null, InputRepeatPolicy.Refresh, null, new[] { a, b });
            tracker.RegisterAction(Actor, def);
            evaluator.True.Add((Actor, a));
            tracker.Sample(0);

            Assert.True(tracker.IsSatisfied(Actor, a));
            Assert.False(tracker.AreAllSatisfied(Actor, def.GraceConditions)); // b 从未为真
            Assert.True(tracker.AreAllSatisfied(Actor, Array.Empty<Id>()));

            evaluator.True.Add((Actor, b));
            tracker.Sample(1);
            Assert.True(tracker.AreAllSatisfied(Actor, def.GraceConditions));

            tracker.Unregister(Actor);
            Assert.False(tracker.IsSatisfied(Actor, a));
            Assert.Equal(-1, tracker.LastTrueTick(Actor, a));
        }

        [Fact]
        public void TickHandler_SamplesGraceEveryStep_BeforeAskingTheActionLayer()
        {
            var step = 1.0 / 60.0;
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var evaluator = new MutableCondition();
            var cond = new Id("input.grace.target_in_range");
            var tracker = new GraceTracker(evaluator);
            tracker.Register(Actor, new[] { cond });
            evaluator.True.Add((Actor, cond));
            var buffer = new InputBufferHost(bus, new InputBufferOptions { StepSeconds = step });
            InputBufferTickHandler.Register(world, buffer, null, tracker);

            world.Tick(SimStep.Continuous(step));
            world.Tick(SimStep.Continuous(step));

            Assert.Equal(1, tracker.CurrentTick);
            Assert.Equal(1, tracker.LastTrueTick(Actor, cond));
        }

        // ---------------------------------------------------------------- schema 与既有数据

        private static (IDataRegistry Registry, ValidationReport Report) LoadRows(string table, string rowsJson, TableSchema schema, bool withGraceTargets = false)
        {
            var source = new InMemoryDataSource().Add(table, "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}");
            if (withGraceTargets)
            {
                // grace_conditions 引用 found.grace_condition：把被引用的条件行一并装进去。
                source.Add(GraceConditionSchema.Table.Name, "{\"table\":\"" + GraceConditionSchema.Table.Name + "\",\"schema_version\":1,\"rows\":"
                    + "[{\"key\":\"input.grace.target_in_range\",\"expr\":\"1 == 1\"}]}");
            }
            var catalog = EventCatalog.FromDefinitions(Array.Empty<EventDefinition>());
            var registry = new DataRegistry(source, new EventBus(catalog, new EventBusOptions { StrictCatalog = false }), new DataRegistryOptions());
            registry.RegisterSchema(schema);
            if (withGraceTargets) registry.RegisterSchema(GraceConditionSchema.Table);
            return (registry, registry.LoadAll());
        }

        [Fact]
        public void InputActionRows_WithTheNewFields_LoadAndParseIntoTheDefinition()
        {
            var rows = "[{\"key\":\"input.action.heavy\",\"kind\":\"button\",\"default_bindings\":[\"key:h\"],"
                + "\"class\":\"attack\",\"buffer_ms\":80,\"priority\":55,\"hold_threshold_ms\":250,\"repeat_policy\":\"ignore\","
                + "\"face_on_accept\":false,\"grace_conditions\":[\"input.grace.target_in_range\"]}]";
            var (registry, report) = LoadRows(InputActionSchema.Table.Name, rows, InputActionSchema.Table, withGraceTargets: true);
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var def = ActionDefinition.FromRecord(registry.Get(InputActionSchema.Table.Name, new Id("input.action.heavy"))!);

            Assert.Equal(ActionClass.Attack, def.Class);
            Assert.Equal(80, def.BufferMs);
            Assert.Equal(55, def.Priority);
            Assert.Equal(55, def.EffectivePriority);
            Assert.Equal(250, def.HoldThresholdMs);
            Assert.Equal(InputRepeatPolicy.Ignore, def.RepeatPolicy);
            Assert.False(def.EffectiveFaceOnAccept); // 显式 false 覆盖 attack 类别缺省
            Assert.Equal(new[] { new Id("input.grace.target_in_range") }, def.GraceConditions);
            Assert.True(def.IsBuffered);
        }

        [Fact]
        public void InputActionRows_SkillSlot_ParsesAndDefaultsToNull_AndBadTypeIsRejected()
        {
            var rows = "[{\"key\":\"input.action.mapped\",\"kind\":\"button\",\"default_bindings\":[\"key:m\"],\"class\":\"attack\",\"skill_slot\":\"slot_0\"},"
                + "{\"key\":\"input.action.unmapped\",\"kind\":\"button\",\"default_bindings\":[\"key:n\"],\"class\":\"attack\"}]";
            var (registry, report) = LoadRows(InputActionSchema.Table.Name, rows, InputActionSchema.Table);
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            Assert.Equal("slot_0", ActionDefinition.FromRecord(registry.Get(InputActionSchema.Table.Name, new Id("input.action.mapped"))!).SkillSlot);
            Assert.Null(ActionDefinition.FromRecord(registry.Get(InputActionSchema.Table.Name, new Id("input.action.unmapped"))!).SkillSlot);

            var bad = "[{\"key\":\"input.action.bad\",\"kind\":\"button\",\"default_bindings\":[\"key:b\"],\"skill_slot\":3}]";
            var (_, badReport) = LoadRows(InputActionSchema.Table.Name, bad, InputActionSchema.Table);
            Assert.True(badReport.IsBlocking);
            Assert.Contains(badReport.Issues, i => i.Field == "skill_slot");
        }

        [Fact]
        public void InputActionRows_WithBadNewFieldValues_AreRejectedByTheSchema()
        {
            string Rows(string extra) => "[{\"key\":\"input.action.bad\",\"kind\":\"button\",\"default_bindings\":[\"key:b\"]," + extra + "}]";

            var (_, badClass) = LoadRows(InputActionSchema.Table.Name, Rows("\"class\":\"fly\""), InputActionSchema.Table);
            Assert.True(badClass.IsBlocking);
            Assert.Contains(badClass.Issues, i => i.Field == "class");

            var (_, negative) = LoadRows(InputActionSchema.Table.Name, Rows("\"buffer_ms\":-1"), InputActionSchema.Table);
            Assert.True(negative.IsBlocking);
            Assert.Contains(negative.Issues, i => i.Field == "buffer_ms");

            var (_, zeroHold) = LoadRows(InputActionSchema.Table.Name, Rows("\"hold_threshold_ms\":0"), InputActionSchema.Table);
            Assert.True(zeroHold.IsBlocking);

            var (_, badRepeat) = LoadRows(InputActionSchema.Table.Name, Rows("\"repeat_policy\":\"queue\""), InputActionSchema.Table);
            Assert.True(badRepeat.IsBlocking);
            Assert.Contains(badRepeat.Issues, i => i.Field == "repeat_policy");
        }

        [Fact]
        public void FrameworkInputActionData_IsUnchanged_AllRowsLoadWithNoClass_SoNothingIsBuffered()
        {
            var path = Path.Combine(FeelTestSupport.FindRepoRoot(), "data", "_framework", "found", "found.input_action.json");
            var json = File.ReadAllText(path);
            var source = new InMemoryDataSource().Add(InputActionSchema.Table.Name, json);
            var catalog = EventCatalog.FromDefinitions(Array.Empty<EventDefinition>());
            var registry = new DataRegistry(source, new EventBus(catalog, new EventBusOptions { StrictCatalog = false }), new DataRegistryOptions());
            registry.RegisterSchema(InputActionSchema.Table);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var defs = registry.GetAll(InputActionSchema.Table.Name).Select(ActionDefinition.FromRecord).ToList();

            Assert.NotEmpty(defs);
            Assert.All(defs, d =>
            {
                Assert.Null(d.Class);
                Assert.Null(d.BufferMs);
                Assert.Null(d.HoldThresholdMs);
                Assert.Equal(InputRepeatPolicy.Refresh, d.RepeatPolicy);
                Assert.Empty(d.GraceConditions);
                Assert.False(d.IsBuffered);
            });
        }

        [Fact]
        public void GraceConditionRows_LoadAndParse_ByTheirOwnRegistryTable()
        {
            var rows = "[{\"key\":\"input.grace.target_in_range\",\"expr\":\"1 == 1\",\"description\":\"测试条件\"}]";
            var (registry, report) = LoadRows(GraceConditionSchema.Table.Name, rows, GraceConditionSchema.Table);
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var def = GraceConditionDefinition.FromRecord(registry.Get(GraceConditionSchema.Table.Name, new Id("input.grace.target_in_range"))!);

            Assert.Equal("input.grace.target_in_range", def.ConditionId.Value);
            Assert.Equal("1 == 1", def.Expr);
            Assert.True(GraceConditionSchema.Table.IsRegistryTable);
        }

        [Fact]
        public void ActionDefinition_ExistingFiveArgumentConstructor_IsPreserved_WithDefaultsForTheNewFields()
        {
            var def = new ActionDefinition(new Id("input.action.confirm"), ActionKind.Button, new[] { "key:enter" }, "ui", "说明");

            Assert.Equal("ui", def.RebindGroup);
            Assert.Equal("说明", def.Description);
            Assert.Null(def.Class);
            Assert.Equal(0, def.EffectivePriority);
            Assert.False(def.EffectiveFaceOnAccept);
            Assert.Throws<ArgumentException>(() => new ActionDefinition(new Id("input.action.x"), ActionKind.Button, new[] { "key:x" }, "default", null,
                ActionClass.Attack, -5, null, null, InputRepeatPolicy.Refresh, null, null));
            Assert.Throws<ArgumentException>(() => new ActionDefinition(new Id("input.action.x"), ActionKind.Button, new[] { "key:x" }, "default", null,
                ActionClass.Attack, null, null, 0, InputRepeatPolicy.Refresh, null, null));
        }
    }
}
