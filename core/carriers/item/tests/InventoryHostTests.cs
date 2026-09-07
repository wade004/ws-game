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

        /// <summary>C05 根治：<see cref="IInventoryHost.TryAddItem"/> 在 <see
        /// cref="InventoryFullPolicy.Partial"/> 下必须如实返回实际落地量（这里是 5——已有 5、
        /// stackSize=5、MaxSlots=1，请求 8 只能续填满当前这一个堆叠），不能把请求的 8 原样当作
        /// 实际量返回给调用方（见 <see cref="Core.Gameplay.Common.RewardDispatcher.GrantItems"/>
        /// 判断记录——这正是 C05 的根因）。</summary>
        [Fact]
        public void TryAddItem_MaxSlotsPartial_ReturnsActualAddedCount_NotRequestedCount()
        {
            var options = new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Partial };
            var host = BuildHost(out _, options, stackSize: 5);
            var unit = new Id("player.hero");

            Assert.True(host.AddItem(unit, new Id("item.sample_potion"), 3));

            Assert.True(host.TryAddItem(unit, new Id("item.sample_potion"), 8, out var actualCount));
            Assert.Equal(2, actualCount); // 3 -> 5（stackSize），只有 2 个真正落地，不是请求的 8。

            var items = host.ListItems(unit);
            Assert.Single(items);
            Assert.Equal(5, items[0].Count);
        }

        /// <summary>C05 根治：<see cref="InventoryFullPolicy.Reject"/> 下 <see
        /// cref="IInventoryHost.TryAddItem"/> 的实际量语义与既有 <see cref="IInventoryHost.AddItem"/>
        /// 保持一致——失败时 actualCount=0（不产生任何变化），成功时 actualCount 恰好等于请求量。</summary>
        [Fact]
        public void TryAddItem_MaxSlotsReject_FailureReportsZeroActualCount()
        {
            var options = new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Reject };
            var host = BuildHost(out _, options, stackSize: 5);
            var unit = new Id("player.hero");

            Assert.False(host.TryAddItem(unit, new Id("item.sample_potion"), 8, out var actualCount));
            Assert.Equal(0, actualCount);
            Assert.Empty(host.ListItems(unit));

            Assert.True(host.TryAddItem(unit, new Id("item.sample_potion"), 5, out var actualCount2));
            Assert.Equal(5, actualCount2);
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

            // N12 收边补齐：未跨堆叠（一次 AddItem 只命中一个既有实例）时，Removals 应恰好一项，
            // 与旧的 ItemInstanceId/Count 语义等价。
            Assert.Single(received.Removals);
            Assert.Equal(received.ItemInstanceId, received.Removals[0].InstanceId);
            Assert.Equal(received.Count, received.Removals[0].Count);
        }

        /// <summary>N12（外部审计 68c9bed，P2）：一次 <c>AddItem</c> 跨堆叠（先续填一个已有堆叠的
        /// 剩余空间，剩下的部分新开一个堆叠）时，<c>ItemAddedEvent.Removals</c> 应按实际落地顺序
        /// 逐项列出每个实例分到的数量，而不是只报告"最后触碰的那一个实例"+"全部数量"——修复前
        /// <c>QuestHost.HandleItemAdded</c> 的 <c>ConsumeOnProgress</c> 分支会用旧的单一
        /// <c>ItemInstanceId</c>+<c>Count</c> 去调用 <c>RemoveItem</c>，当 <c>Count</c> 超过"最后
        /// 那个实例"实际持有的数量时（比如新开的堆叠只装了 2 个，但 Count 是总数 5）整取失败、
        /// 进度不推进（外部审计 N12"stack1 一次加 2 时 consume2 进度为 0"）。</summary>
        [Fact]
        public void AddItem_CrossStack_RaisesItemAddedEvent_WithPerInstanceRemovalsBreakdown()
        {
            var host = BuildHost(out var bus, stackSize: 5);
            var unit = new Id("player.hero");
            host.AddItem(unit, new Id("item.sample_potion"), 3); // 先攒一个还差 2 就满的堆叠。
            var existingInstanceId = host.ListItems(unit)[0].InstanceId;

            ItemAddedEvent? received = null;
            bus.Subscribe<ItemAddedEvent>(CarriersEventKeys.ItemAdded, evt => received = evt);

            // 再加 4 个：2 个续满已有堆叠（3→5），剩下 2 个新开一个堆叠——跨越两个实例。
            Assert.True(host.AddItem(unit, new Id("item.sample_potion"), 4));
            bus.DispatchPending();

            var items = host.ListItems(unit);
            Assert.Equal(2, items.Count);
            Assert.Equal(5, items[0].Count);
            Assert.Equal(2, items[1].Count);
            var newInstanceId = items[1].InstanceId;

            Assert.NotNull(received);
            Assert.Equal(4, received!.Count); // 旧字段：总量不变。
            Assert.Equal(newInstanceId, received.ItemInstanceId); // 旧字段：仍是"最后触碰的实例"。

            // 新字段：逐项分摊明细，覆盖两个实例，且每一项都精确对应各自实际持有量的一部分。
            Assert.Equal(2, received.Removals.Count);
            Assert.Equal(existingInstanceId, received.Removals[0].InstanceId);
            Assert.Equal(2, received.Removals[0].Count); // 续满已有堆叠：3→5，只加了 2。
            Assert.Equal(newInstanceId, received.Removals[1].InstanceId);
            Assert.Equal(2, received.Removals[1].Count); // 新堆叠：装了 2。
            Assert.Equal(received.Count, received.Removals[0].Count + received.Removals[1].Count);

            // 消费方按 Removals 逐项调用 RemoveItem 才能精确取回本次加入的数量（不依赖单一
            // ItemInstanceId+Count 猜测）；旧组合 RemoveItem(unit, received.ItemInstanceId,
            // received.Count) 在这个跨堆叠场景下会失败（newInstanceId 那个实例只有 2 个，Count 是 4）。
            Assert.False(host.RemoveItem(unit, received.ItemInstanceId, received.Count));
            foreach (var (instanceId, count) in received.Removals)
            {
                Assert.True(host.RemoveItem(unit, instanceId, count));
            }

            Assert.Equal(3, host.CountOf(unit, new Id("item.sample_potion"))); // 原有 3 个（未被本次加入触碰的部分）保留。
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
