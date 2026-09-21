using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>
    /// <see cref="Core.Carriers.Gobj.InteractIntentTickHandler"/> 覆盖 03_运行时骨架.md 第 4.2 节
    /// 步骤 6"触发评估"消费交互意图的实现（此前只有文档约定、没有实现，见该类型判断记录）：
    /// 提交一条 <c>Kind == "interact"</c> 的意图 → tick 一次 → 物件状态变化（<c>open_state</c>
    /// 翻转）+ <c>gobj.interacted</c> 事件。
    /// </summary>
    public sealed class InteractIntentTickHandlerTests
    {
        private static readonly Id MapId = new Id("map.sample");
        private static readonly Id Unit = new Id("unit.sample_1");
        private static readonly Id DisplayRef = new Id("display.sample_gobj_intent");

        private static JsonObject DoorTemplate(string id) => J.O(
            ("id", J.S(id)),
            ("name_key", J.S("l10n." + id.Replace('.', '_') + ".name")),
            ("kind", J.S("door")),
            ("type_data", J.O()),
            ("display_ref", J.S(DisplayRef.Value)));

        [Fact]
        public void Tick_WithInteractIntent_TogglesDoorState_AndEmitsEvent()
        {
            var world = new GobjWorldBuilder()
                .Template(DoorTemplate("gobj.sample_door_intent"))
                .Build();

            world.World.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new Core.Carriers.Gobj.InteractIntentTickHandler(world.Host));

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_door_intent"), MapId, new Vec2(0, 0));

            Assert.Null(world.Host.GetState(gobjId, "open_state"));

            var args = new JsonObjectBuilder().Add("gobj_instance_id", new JsonString(gobjId.Value)).Build();
            world.World.SubmitIntent(new Intent(Unit, "interact", args));

            world.World.Tick(SimStep.Continuous(0.1));
            world.Flush();

            var openState = world.Host.GetState(gobjId, "open_state");
            Assert.NotNull(openState);
            Assert.True(openState!.Value.AsBool);

            var evt = Assert.Single(world.Of<Core.Carriers.Common.GobjInteractedEvent>());
            Assert.Equal(Unit, evt.UnitId);
            Assert.Equal(gobjId, evt.GobjInstanceId);
        }

        [Fact]
        public void Tick_WithInteractIntent_MissingGobjInstanceId_DoesNotThrow()
        {
            var world = new GobjWorldBuilder().Build();
            world.World.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new Core.Carriers.Gobj.InteractIntentTickHandler(world.Host));

            world.World.SubmitIntent(new Intent(Unit, "interact"));

            var ex = Record.Exception(() => world.World.Tick(SimStep.Continuous(0.1)));

            Assert.Null(ex);
        }

        [Fact]
        public void Tick_WithInteractIntent_UnknownGobj_DoesNotThrow()
        {
            var world = new GobjWorldBuilder().Build();
            world.World.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new Core.Carriers.Gobj.InteractIntentTickHandler(world.Host));
            world.AddUnit(Unit, new Vec2(0, 0));

            var args = new JsonObjectBuilder().Add("gobj_instance_id", new JsonString("gobj.never_spawned")).Build();
            world.World.SubmitIntent(new Intent(Unit, "interact", args));

            var ex = Record.Exception(() => world.World.Tick(SimStep.Continuous(0.1)));

            Assert.Null(ex);
        }

        /// <summary>ADR-0062：与既有的 <c>creature_instance_id</c> 分支同构——Args 携带
        /// <c>loot_instance_id</c>（而不含 <c>gobj_instance_id</c>）时说明这条 <c>interact</c> 意图是
        /// 发给 <c>Core.Gameplay.Loot.LootInteractIntentTickHandler</c> 的，本处理器应静默跳过、不记
        /// 诊断（该意图由 loot 侧处理器自己的诊断出口负责）。</summary>
        [Fact]
        public void Tick_WithInteractIntent_LootInstanceIdPresent_SkipsSilently_NoDiagnostic()
        {
            var world = new GobjWorldBuilder().Build();
            var diagnostics = new Core.Carriers.Gobj.InMemoryGobjDiagnostics();
            world.World.RegisterPhaseHandler(
                TickPhase.TriggerEvaluation,
                new Core.Carriers.Gobj.InteractIntentTickHandler(world.Host, diagnostics));
            world.AddUnit(Unit, new Vec2(0, 0));

            var args = new JsonObjectBuilder().Add("loot_instance_id", new JsonString("loot.inst_1")).Build();
            world.World.SubmitIntent(new Intent(Unit, "interact", args));

            var ex = Record.Exception(() => world.World.Tick(SimStep.Continuous(0.1)));

            Assert.Null(ex);
            Assert.Empty(diagnostics.Warnings);
        }
    }
}
