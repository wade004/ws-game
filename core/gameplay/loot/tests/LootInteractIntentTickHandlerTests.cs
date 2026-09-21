using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// ADR-0062：<see cref="LootInteractIntentTickHandler"/> 覆盖第三条 <c>interact</c> 意图原生
    /// 分流——与 <c>Core.Carriers.Gobj.InteractIntentTickHandlerTests</c>/<c>Core.Carriers.Creature.
    /// CreatureInteractIntentTickHandlerTests</c> 同一套验收惯例：提交一条 <c>Kind == "interact"</c>、
    /// <c>Args = {loot_instance_id: Id}</c> 的意图 → tick 一次 → 掉落物从 <c>ActiveLootIds</c> 消失、
    /// 物品进入背包。
    /// </summary>
    public sealed class LootInteractIntentTickHandlerTests
    {
        private const string EmptyTable = "[]";
        private static readonly Id MapId = new Id("map.sample_1");
        private static readonly Id Unit = new Id("unit.sample_1");

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public IWorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public FakeInventoryHost Inventory = null!;
            public LootHost Host = null!;
            public InMemoryLootDiagnostics Diagnostics = null!;
        }

        private static Fixture NewFixture()
        {
            var fixture = new Fixture();
            fixture.Bus = LootTestSupport.NewEventBus();
            var registry = LootTestSupport.MakeRegistry(fixture.Bus, EmptyTable);
            fixture.World = LootTestSupport.NewWorld(fixture.Bus);
            fixture.Units = new WorldUnitAccess(fixture.World);
            fixture.Inventory = new FakeInventoryHost();
            fixture.Diagnostics = new InMemoryLootDiagnostics();
            fixture.Host = new LootHost(
                registry, new RngHost(1), fixture.Bus, fixture.World, fixture.Units, fixture.Inventory,
                new FakeExprHostFactory(), () => 0.0);

            fixture.World.RegisterPhaseHandler(
                TickPhase.TriggerEvaluation, new LootInteractIntentTickHandler(fixture.Host, fixture.Diagnostics));

            return fixture;
        }

        [Fact]
        public void Tick_WithInteractIntent_PicksUpLoot_AndRemovesFromActiveLootIds()
        {
            var f = NewFixture();
            LootTestSupport.AddPlayer(f.World, Unit, MapId, new Vec2(0, 0));
            var lootId = f.Host.Drop(MapId, new Vec2(1, 0), new[] { new ItemStack(new Id("item.sample_ore"), 2) });

            var args = new JsonObjectBuilder().Add("loot_instance_id", new JsonString(lootId.Value)).Build();
            f.World.SubmitIntent(new Intent(Unit, "interact", args));
            f.World.Tick(SimStep.Continuous(0.1));

            Assert.DoesNotContain(lootId, f.Host.ActiveLootIds);
            Assert.Equal(2, f.Inventory.CountOf(Unit, new Id("item.sample_ore")));
            Assert.Empty(f.Diagnostics.Warnings);
        }

        [Fact]
        public void Tick_WithInteractIntent_UnknownLootInstanceId_DoesNotThrow_RecordsDiagnostic()
        {
            var f = NewFixture();
            LootTestSupport.AddPlayer(f.World, Unit, MapId, new Vec2(0, 0));

            var args = new JsonObjectBuilder().Add("loot_instance_id", new JsonString("loot.never_dropped")).Build();
            f.World.SubmitIntent(new Intent(Unit, "interact", args));

            var ex = Record.Exception(() => f.World.Tick(SimStep.Continuous(0.1)));

            Assert.Null(ex);
            Assert.NotEmpty(f.Diagnostics.Warnings);
        }

        [Fact]
        public void Tick_WithInteractIntent_GobjInstanceIdPresent_SkipsSilently()
        {
            var f = NewFixture();
            LootTestSupport.AddPlayer(f.World, Unit, MapId, new Vec2(0, 0));

            var args = new JsonObjectBuilder().Add("gobj_instance_id", new JsonString("gobj.inst_1")).Build();
            f.World.SubmitIntent(new Intent(Unit, "interact", args));

            var ex = Record.Exception(() => f.World.Tick(SimStep.Continuous(0.1)));

            Assert.Null(ex);
            Assert.Empty(f.Diagnostics.Warnings);
        }
    }
}
