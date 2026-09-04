using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Xunit;

namespace Tests.Carriers.Item
{
    public class InventoryHostTests
    {
        private const string SlotJson =
            "[{\"id\": \"item.slot.consumable\", \"name_key\": \"l10n.item.slot.consumable\"}]";

        private const string QualityJson =
            "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\"}]";

        private static string PotionTemplateJson(string id = "item.sample_potion", int stackSize = 5) =>
            "[{"
            + "\"id\": \"" + id + "\","
            + "\"slot\": \"item.slot.consumable\","
            + "\"quality\": \"item.quality.common\","
            + "\"item_level\": 1,"
            + "\"display_ref\": \"display.item.sample_potion\","
            + "\"stack_size\": " + stackSize + ","
            + "\"name_key\": \"l10n.item.sample_potion.name\""
            + "}]";

        private static InventoryHost BuildHost(out Core.Foundation.EventBus.IEventBus bus, InventoryOptions? options = null, int stackSize = 5)
        {
            var registry = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.template", TestSupport.Table("item.template", PotionTemplateJson(stackSize: stackSize)));
            });

            bus = TestSupport.CreateBus();
            return new InventoryHost(registry, bus, options);
        }

        [Fact]
        public void AddItem_StacksSameTemplate_MergesUpToStackSize()
        {
            var host = BuildHost(out _, stackSize: 5);
            var unit = new Id("player.hero");

            Assert.True(host.AddItem(unit, new Id("item.sample_potion"), 3));
            Assert.True(host.AddItem(unit, new Id("item.sample_potion"), 2));

            var items = host.ListItems(unit);
            Assert.Single(items);
            Assert.Equal(5, items[0].Count);
        }

        [Fact]
        public void AddItem_ExceedsStackSize_CreatesNewInstance()
        {
            var host = BuildHost(out _, stackSize: 5);
            var unit = new Id("player.hero");

            Assert.True(host.AddItem(unit, new Id("item.sample_potion"), 8));

            var items = host.ListItems(unit);
            Assert.Equal(2, items.Count);
            Assert.Equal(5, items[0].Count);
            Assert.Equal(3, items[1].Count);
        }

        [Fact]
        public void AddItem_UnknownTemplate_Throws()
        {
            var host = BuildHost(out _);
            var unit = new Id("player.hero");

            Assert.Throws<System.ArgumentException>(() => host.AddItem(unit, new Id("item.does_not_exist"), 1));
        }

        [Fact]
        public void AddItem_MaxSlotsReject_FullBatchFailsAtomically()
        {
            var options = new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Reject };
            var host = BuildHost(out _, options, stackSize: 5);
            var unit = new Id("player.hero");

            // 需要 2 个格子（5 + 3），但 MaxSlots=1，整批应原子失败。
            Assert.False(host.AddItem(unit, new Id("item.sample_potion"), 8));
            Assert.Empty(host.ListItems(unit));
        }

        [Fact]
        public void AddItem_MaxSlotsPartial_AddsPartialAndReturnsTrue()
        {
            var options = new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Partial };
            var host = BuildHost(out _, options, stackSize: 5);
            var unit = new Id("player.hero");

            Assert.True(host.AddItem(unit, new Id("item.sample_potion"), 8));

            var items = host.ListItems(unit);
            Assert.Single(items);
            Assert.Equal(5, items[0].Count);
        }

        [Fact]
        public void RemoveItem_PartialReducesCount()
        {
            var host = BuildHost(out _, stackSize: 5);
            var unit = new Id("player.hero");
            host.AddItem(unit, new Id("item.sample_potion"), 5);
            var instanceId = host.ListItems(unit)[0].InstanceId;

            Assert.True(host.RemoveItem(unit, instanceId, 2));

            var items = host.ListItems(unit);
            Assert.Single(items);
            Assert.Equal(3, items[0].Count);
        }

        [Fact]
        public void RemoveItem_FullRemovesInstance()
        {
            var host = BuildHost(out _, stackSize: 5);
            var unit = new Id("player.hero");
            host.AddItem(unit, new Id("item.sample_potion"), 5);
            var instanceId = host.ListItems(unit)[0].InstanceId;

            Assert.True(host.RemoveItem(unit, instanceId, 5));
            Assert.Empty(host.ListItems(unit));
        }

        [Fact]
        public void RemoveItem_UnknownInstance_ReturnsFalse()
        {
            var host = BuildHost(out _, stackSize: 5);
            var unit = new Id("player.hero");

            Assert.False(host.RemoveItem(unit, new Id("item.inst_999"), 1));
        }

        [Fact]
        public void RemoveItem_MoreThanOwned_ReturnsFalse()
        {
            var host = BuildHost(out _, stackSize: 5);
            var unit = new Id("player.hero");
            host.AddItem(unit, new Id("item.sample_potion"), 2);
            var instanceId = host.ListItems(unit)[0].InstanceId;

            Assert.False(host.RemoveItem(unit, instanceId, 3));
        }

        [Fact]
        public void AddItem_RaisesItemAddedEvent_WithExpectedFields()
        {
            var host = BuildHost(out var bus, stackSize: 5);
            var unit = new Id("player.hero");

            ItemAddedEvent? received = null;
            bus.Subscribe<ItemAddedEvent>(CarriersEventKeys.ItemAdded, evt => received = evt);

            host.AddItem(unit, new Id("item.sample_potion"), 3);
            bus.DispatchPending();

            Assert.NotNull(received);
            Assert.Equal(unit, received!.UnitId);
            Assert.Equal(new Id("item.sample_potion"), received.ItemTemplateId);
            Assert.Equal(3, received.Count);
        }

        [Fact]
        public void RemoveItem_RaisesItemRemovedEvent_WithExpectedFields()
        {
            var host = BuildHost(out var bus, stackSize: 5);
            var unit = new Id("player.hero");
            host.AddItem(unit, new Id("item.sample_potion"), 5);
            bus.DispatchPending();
            var instanceId = host.ListItems(unit)[0].InstanceId;

            ItemRemovedEvent? received = null;
            bus.Subscribe<ItemRemovedEvent>(CarriersEventKeys.ItemRemoved, evt => received = evt);

            host.RemoveItem(unit, instanceId, 2);
            bus.DispatchPending();

            Assert.NotNull(received);
            Assert.Equal(unit, received!.UnitId);
            Assert.Equal(instanceId, received.ItemInstanceId);
            Assert.Equal(2, received.Count);
        }

        [Fact]
        public void CountOf_SumsAcrossMultipleStacks()
        {
            var host = BuildHost(out _, stackSize: 5);
            var unit = new Id("player.hero");
            host.AddItem(unit, new Id("item.sample_potion"), 5);
            host.AddItem(unit, new Id("item.sample_potion"), 3);

            Assert.Equal(8, host.CountOf(unit, new Id("item.sample_potion")));
        }

        [Fact]
        public void FindInstance_ReturnsNullWhenNotOwnedByUnit()
        {
            var host = BuildHost(out _, stackSize: 5);
            var owner = new Id("player.hero");
            var other = new Id("player.other");
            host.AddItem(owner, new Id("item.sample_potion"), 1);
            var instanceId = host.ListItems(owner)[0].InstanceId;

            Assert.Null(host.FindInstance(other, instanceId));
            Assert.NotNull(host.FindInstance(owner, instanceId));
        }
    }
}
