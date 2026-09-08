using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Gobj;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>
    /// CR140-01（外部审计 audit-c86bfa9-20260908，P1）复现与根治：<see cref="GameObjectHost.Interact"/>
    /// 的 <c>chest</c> 一次性开箱路径修复前先把 <c>open_state</c> 无条件标记为已开（见
    /// <c>GameObjectHost.OpenChest</c> 修复前实现约 :296），再对每个掉落堆叠直接
    /// <see cref="IInventoryHost.AddItem"/>（约 :328），不检查是否真的放得下、不回滚——真实满包时奖励
    /// 会部分/全部丢失，且因为 <c>open_state</c> 已经永久标记，<see cref="GameObjectHost.Interact"/>
    /// 再交互一次直接因"已开过"短路返回，玩家再也拿不到丢失的那部分。
    /// <para>
    /// 本文件用真实 <see cref="InventoryHost"/>（实现 <see cref="IBatchableInventoryHost"/>；
    /// <c>GobjWorldBuilder</c>/<c>GameObjectHostTests</c> 用的 <c>FakeInventoryHost</c> 永远无限容量，
    /// 无法复现满包场景，同审计探针 <c>inventory_reject_chest.log</c> 的方法论：真实 <c>InventoryHost</c>
    /// + <see cref="GameObjectHost"/> + stub loot）+ 真实 <see cref="EventBus"/>/<see cref="StatHost"/>
    /// 验证根治后的 batch/Partial 协议（同 <c>core/gameplay/loot/core/LootHost.PickUp</c>）。
    /// </para>
    /// </summary>
    public sealed class CR140_01_ChestOpenTransactionTests
    {
        private static readonly Id MapId = new Id("map.cr140_01");
        private static readonly Id Unit = new Id("unit.cr140_01_player");
        private static readonly Id LootTableRef = new Id("loot.cr140_01_chest");
        private static readonly Id ItemA = new Id("item.cr140_01_gold");
        private static readonly Id ItemB = new Id("item.cr140_01_gem");
        private static readonly Id ItemFiller = new Id("item.cr140_01_filler");

        private sealed class Fixture
        {
            public IEventBus Bus = default!;
            public WorldSim World = default!;
            public FakeWorldFlags Flags = default!;
            public FakeUnitAccess Units = default!;
            public InventoryHost Inventory = default!;
            public StatHost Stats = default!;
            public FakeSkillHost Skills = default!;
            public FakeLootRoller Loot = default!;
            public GobjOptions Options = default!;
            public GameObjectFactory Factory = default!;
            public GameObjectHost Host = default!;
        }

        /// <summary>装配一套真实 <see cref="InventoryHost"/>（<paramref name="maxSlots"/>/<paramref
        /// name="fullPolicy"/> 可控，用于精确构造"某一堆放得下、另一堆放不下"的满包场景）+ 真实
        /// <see cref="GameObjectHost"/> + stub <see cref="FakeLootRoller"/>；<paramref
        /// name="chestPolicy"/> 对应 <see cref="GobjOptions.ChestLootPolicy"/>。</summary>
        private static Fixture Build(int maxSlots, InventoryFullPolicy fullPolicy, GobjLootDeliveryPolicy chestPolicy)
        {
            var source = new InMemoryDataSource()
                .Add("item.slot_definition", Envelope("item.slot_definition",
                    "[{\"id\":\"item.slot.cr140_01\",\"name_key\":\"l10n.slot.cr140_01\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition",
                    "[{\"id\":\"item.quality.cr140_01\",\"name_key\":\"l10n.quality.cr140_01\"}]"))
                .Add("item.template", Envelope("item.template", "["
                    + ItemTemplateJson(ItemA, 10) + ","
                    + ItemTemplateJson(ItemB, 10) + ","
                    + ItemTemplateJson(ItemFiller, 10)
                    + "]"))
                .Add("gobj.template", Envelope("gobj.template", "["
                    + "{\"id\":\"gobj.cr140_01_chest\",\"name_key\":\"l10n.gobj.cr140_01_chest\","
                    + "\"kind\":\"chest\",\"type_data\":{\"loot_table_ref\":\"" + LootTableRef.Value + "\"},"
                    + "\"display_ref\":\"display.gobj.cr140_01_chest\"}]"))
                .Add("gobj.lock", Envelope("gobj.lock", "[]"))
                .Add("stat.definition", Envelope("stat.definition", "[]"));

            var bus = GobjWorldBuilder.CreateBus();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(GobjSchemas.Template);
            registry.RegisterSchema(GobjSchemas.Lock);
            registry.RegisterSchema(StatSchemas.Definition);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues.Select(i => i.ToString())));

            var world = new WorldSim(bus);
            var flags = new FakeWorldFlags();
            var units = new FakeUnitAccess();
            var inventory = new InventoryHost(registry, bus, new InventoryOptions { MaxSlots = maxSlots, FullPolicy = fullPolicy });
            var stats = new StatHost(registry, bus);
            var skills = new FakeSkillHost();
            var loot = new FakeLootRoller();
            var diagnostics = new InMemoryGobjDiagnostics();
            var factory = new GameObjectFactory(world);
            var options = new GobjOptions { ChestLootPolicy = chestPolicy };

            units.Add(Unit, new Vec2(0, 0));
            stats.RegisterUnit(Unit);
            inventory.RegisterUnit(Unit);

            var host = new GameObjectHost(registry, world, bus, flags, units, inventory, stats, skills, loot, options, diagnostics);

            return new Fixture
            {
                Bus = bus,
                World = world,
                Flags = flags,
                Units = units,
                Inventory = inventory,
                Stats = stats,
                Skills = skills,
                Loot = loot,
                Options = options,
                Factory = factory,
                Host = host,
            };
        }

        private static Id SpawnChest(Fixture fixture) =>
            fixture.Factory.Spawn(new Id("gobj.cr140_01_chest"), MapId, new Vec2(0, 0), 0);

        private static bool? OpenState(Fixture fixture, Id gobjId)
        {
            var value = fixture.Host.GetState(gobjId, "open_state");
            return value.HasValue ? value.Value.AsBool : (bool?)null;
        }

        private static string ItemTemplateJson(Id id, int stackSize) =>
            "{\"id\":\"" + id.Value + "\",\"slot\":\"item.slot.cr140_01\",\"quality\":\"item.quality.cr140_01\","
            + "\"item_level\":1,\"display_ref\":\"display." + id.Value + "\","
            + "\"stack_size\":" + stackSize + ",\"name_key\":\"l10n." + id.Value.Replace('.', '_') + "\"}";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        // -----------------------------------------------------------------
        // Reject 策略：整批交付成功才提交 open_state=true；有一堆放不下就整体回滚（含已经成功的
        // 前一堆），不标记，允许重试。
        // -----------------------------------------------------------------

        [Fact]
        public void OpenChestReject_MixedStackFailure_RollsBackAlreadyAddedStack_DoesNotMarkOpen()
        {
            // MaxSlots=2：1 号格已被 filler 占满（占用 1 格），A 已有 9/10 堆叠占用另 1 格（有 1 点
            // 续填余量）——B 是第三个模板，需要开新格，但两格都已被占用，放不下。
            var fixture = Build(maxSlots: 2, fullPolicy: InventoryFullPolicy.Partial, chestPolicy: GobjLootDeliveryPolicy.Reject);
            fixture.Inventory.AddItem(Unit, ItemFiller, 1);
            fixture.Inventory.AddItem(Unit, ItemA, 9);
            fixture.Bus.DispatchPending();

            fixture.Loot.Table(LootTableRef, new ItemStack(ItemA, 1), new ItemStack(ItemB, 1));
            var gobjId = SpawnChest(fixture);

            var result = fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();

            Assert.True(result.Success);
            // CR140-01 核心断言：A 的这次续填（9→10）必须随事务整体回滚，不能停在"A 已经拿到、B 没拿到"
            // 的半途状态——否则下次重试重新 roll 一遍会在这 1 点之上再叠加一份全新的 A。
            Assert.Equal(9, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(0, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Null(OpenState(fixture, gobjId));
            Assert.Single(fixture.Loot.Calls);

            // 腾出空间（移走 filler，释放一个格子）后重试：整批（A+B）应当恰好发放一次。
            var fillerInstance = fixture.Inventory.ListItems(Unit)[0];
            Assert.Equal(ItemFiller, fillerInstance.TemplateId);
            fixture.Inventory.RemoveItem(Unit, fillerInstance.InstanceId, 1);
            fixture.Bus.DispatchPending();

            var retryResult = fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();

            Assert.True(retryResult.Success);
            Assert.Equal(10, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(1, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Equal(true, OpenState(fixture, gobjId));
            Assert.Equal(2, fixture.Loot.Calls.Count);

            // 再次交互不应重复发放（已标记 open_state，且没有 pending 剩余）。
            fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();
            Assert.Equal(10, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(1, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Equal(2, fixture.Loot.Calls.Count);
        }

        [Theory]
        [InlineData(InventoryFullPolicy.Reject)]
        [InlineData(InventoryFullPolicy.Partial)]
        public void OpenChestReject_CompletelyFull_DoesNotMarkOpen_RetryAfterFreeingSpaceDeliversOnce(InventoryFullPolicy hostPolicy)
        {
            // MaxSlots=1 且唯一的格子被一个不同模板的 filler 占满：无论宿主自身是 Reject 还是 Partial
            // 策略，chest 掉落的 A 都完全放不下（0 交付）。
            var fixture = Build(maxSlots: 1, fullPolicy: hostPolicy, chestPolicy: GobjLootDeliveryPolicy.Reject);
            fixture.Inventory.AddItem(Unit, ItemFiller, 1);
            fixture.Bus.DispatchPending();

            fixture.Loot.Table(LootTableRef, new ItemStack(ItemA, 3));
            var gobjId = SpawnChest(fixture);

            fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();

            Assert.Equal(0, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Null(OpenState(fixture, gobjId));
            Assert.Single(fixture.Loot.Calls);

            var fillerInstance = fixture.Inventory.ListItems(Unit)[0];
            fixture.Inventory.RemoveItem(Unit, fillerInstance.InstanceId, 1);
            fixture.Bus.DispatchPending();

            fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();

            Assert.Equal(3, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(true, OpenState(fixture, gobjId));
            Assert.Equal(2, fixture.Loot.Calls.Count);
        }

        // -----------------------------------------------------------------
        // Partial 策略：能拿多少拿多少，未交付部分留在箱子自身的待补发记录里，标记 open_state=true，
        // 下次交互只补发剩余（不重新 roll）。
        // -----------------------------------------------------------------

        [Fact]
        public void OpenChestPartial_KeepsDeliveredPortion_PendingRemainderDeliveredOnRetry_WithoutReRolling()
        {
            // MaxSlots=2：1 号格被 filler 占满，只剩 1 个空格——A、B 是两个不同模板，谁先处理谁能拿到
            // 那唯一的空格；LootTable 按登记顺序 A 在前，Partial 按顺序尽量交付：A 成功、B 失败。
            var fixture = Build(maxSlots: 2, fullPolicy: InventoryFullPolicy.Partial, chestPolicy: GobjLootDeliveryPolicy.Partial);
            fixture.Inventory.AddItem(Unit, ItemFiller, 1);
            fixture.Bus.DispatchPending();

            fixture.Loot.Table(LootTableRef, new ItemStack(ItemA, 4), new ItemStack(ItemB, 5));
            var gobjId = SpawnChest(fixture);

            fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();

            Assert.Equal(4, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(0, fixture.Inventory.CountOf(Unit, ItemB));
            // 已经交付了一部分：必须标记 open_state，否则下次交互会重新 roll 一遍在已交付的 A 之上
            // 又叠加一份全新掉落。
            Assert.Equal(true, OpenState(fixture, gobjId));
            Assert.Single(fixture.Loot.Calls);

            // 腾出空间：移走 filler，释放出 B 需要的那个格子。
            var fillerInstance = fixture.Inventory.ListItems(Unit).First(i => i.TemplateId.Equals(ItemFiller));
            fixture.Inventory.RemoveItem(Unit, fillerInstance.InstanceId, 1);
            fixture.Bus.DispatchPending();

            // 再次交互：open_state 已是 true，走"补发 pending"分支，不重新 roll。
            fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();

            Assert.Equal(4, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(5, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Single(fixture.Loot.Calls); // 仍然只 roll 过一次——补发不重新抽取掉落表。

            // pending 已发完，再交互一次应是彻底 no-op。
            fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();
            Assert.Equal(4, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(5, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Single(fixture.Loot.Calls);
        }

        [Fact]
        public void OpenChestPartial_CompletelyFull_DoesNotMarkOpen_AllowsRetry()
        {
            var fixture = Build(maxSlots: 1, fullPolicy: InventoryFullPolicy.Partial, chestPolicy: GobjLootDeliveryPolicy.Partial);
            fixture.Inventory.AddItem(Unit, ItemFiller, 1);
            fixture.Bus.DispatchPending();

            fixture.Loot.Table(LootTableRef, new ItemStack(ItemA, 3));
            var gobjId = SpawnChest(fixture);

            fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();

            Assert.Equal(0, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Null(OpenState(fixture, gobjId));
            Assert.Single(fixture.Loot.Calls);

            var fillerInstance = fixture.Inventory.ListItems(Unit)[0];
            fixture.Inventory.RemoveItem(Unit, fillerInstance.InstanceId, 1);
            fixture.Bus.DispatchPending();

            fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();

            Assert.Equal(3, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(true, OpenState(fixture, gobjId));
            Assert.Equal(2, fixture.Loot.Calls.Count);
        }
    }
}
