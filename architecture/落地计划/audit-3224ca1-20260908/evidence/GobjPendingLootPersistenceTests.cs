using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Gobj;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;
using Core.Foundation.SaveSystem;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>
    /// 第九方审核任务书"第 0 步"遗留补齐：CR140-01 的 <see cref="GobjLootDeliveryPolicy.Partial"/>
    /// 未交付余量此前只记在 <see cref="GameObjectHost"/> 进程内字段，存读档会丢——满包 Partial 开箱
    /// 后存档、读档到新宿主，腾出空间再交互不会补发剩余部分。本文件验证新增的 <see
    /// cref="GobjPendingLootPersistable"/> 段：真实 <see cref="InventoryHost"/> + 两个各自独立的
    /// <see cref="GameObjectHost"/> 实例（模拟"存档时的宿主"与"读档后新建的宿主"，共享同一份世界
    /// 状态——<c>flags</c>/<c>inventory</c>——但 <c>_pendingChestLoot</c> 各自独立，只能通过存档段
    /// 的 Save/Load 传递）。
    /// </summary>
    public sealed class GobjPendingLootPersistenceTests
    {
        private static readonly Id MapId = new Id("map.gobj_pending_loot");
        private static readonly Id Unit = new Id("unit.gobj_pending_loot_player");
        private static readonly Id LootTableRef = new Id("loot.gobj_pending_loot_chest");
        private static readonly Id GatherLootTableRef = new Id("loot.gobj_pending_loot_gather");
        private static readonly Id ItemA = new Id("item.gobj_pending_loot_gold");
        private static readonly Id ItemB = new Id("item.gobj_pending_loot_gem");
        private static readonly Id ItemFiller = new Id("item.gobj_pending_loot_filler");

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
            public IDataRegistryView Registry = default!;
            public GobjOptions Options = default!;
            public GameObjectFactory Factory = default!;
            public GameObjectHost Host = default!;

            /// <summary>模拟"进程重启、读档后重新装配出来的宿主"：与 <see cref="Host"/> 共享同一份
            /// <see cref="Flags"/>/<see cref="Inventory"/>（这两者各自有自己的存档段，本测试不复测），
            /// 但是一个全新的 <see cref="GameObjectHost"/> 实例——<c>_pendingChestLoot</c> 从零开始，
            /// 只能靠 <see cref="GobjPendingLootPersistable.Load"/> 恢复。</summary>
            public GameObjectHost NewHostAfterReload() =>
                new GameObjectHost(Registry, World, Bus, Flags, Units, Inventory, Stats, Skills, Loot, Options,
                    new InMemoryGobjDiagnostics());
        }

        private static Fixture Build(int maxSlots, InventoryFullPolicy fullPolicy)
        {
            var source = new InMemoryDataSource()
                .Add("item.slot_definition", Envelope("item.slot_definition",
                    "[{\"id\":\"item.slot.gobj_pending_loot\",\"name_key\":\"l10n.slot.gobj_pending_loot\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition",
                    "[{\"id\":\"item.quality.gobj_pending_loot\",\"name_key\":\"l10n.quality.gobj_pending_loot\"}]"))
                .Add("item.template", Envelope("item.template", "["
                    + ItemTemplateJson(ItemA, 10) + ","
                    + ItemTemplateJson(ItemB, 10) + ","
                    + ItemTemplateJson(ItemFiller, 10)
                    + "]"))
                .Add("gobj.template", Envelope("gobj.template", "["
                    + "{\"id\":\"gobj.gobj_pending_loot_chest\",\"name_key\":\"l10n.gobj.gobj_pending_loot_chest\","
                    + "\"kind\":\"chest\",\"type_data\":{\"loot_table_ref\":\"" + LootTableRef.Value + "\"},"
                    + "\"display_ref\":\"display.gobj.gobj_pending_loot_chest\"},"
                    + "{\"id\":\"gobj.gobj_pending_loot_gather\",\"name_key\":\"l10n.gobj.gobj_pending_loot_gather\","
                    + "\"kind\":\"gather_node\",\"type_data\":{\"loot_table_ref\":\"" + GatherLootTableRef.Value + "\",\"respawn_after_use\":60},"
                    + "\"display_ref\":\"display.gobj.gobj_pending_loot_gather\"}]"))
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
            var factory = new GameObjectFactory(world);
            var options = new GobjOptions { ChestLootPolicy = GobjLootDeliveryPolicy.Partial };

            units.Add(Unit, new Vec2(0, 0));
            stats.RegisterUnit(Unit);
            inventory.RegisterUnit(Unit);

            var host = new GameObjectHost(registry, world, bus, flags, units, inventory, stats, skills, loot, options,
                new InMemoryGobjDiagnostics());

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
                Registry = registry,
                Options = options,
                Factory = factory,
                Host = host,
            };
        }

        private static Id SpawnChest(Fixture fixture) => SpawnChestAt(fixture, MapId);

        private static Id SpawnChestAt(Fixture fixture, Id mapId) =>
            fixture.Factory.Spawn(new Id("gobj.gobj_pending_loot_chest"), mapId, new Vec2(0, 0), 0);

        private static Id SpawnGather(Fixture fixture) =>
            fixture.Factory.Spawn(new Id("gobj.gobj_pending_loot_gather"), MapId, new Vec2(0, 0), 0);

        private static string ItemTemplateJson(Id id, int stackSize) =>
            "{\"id\":\"" + id.Value + "\",\"slot\":\"item.slot.gobj_pending_loot\",\"quality\":\"item.quality.gobj_pending_loot\","
            + "\"item_level\":1,\"display_ref\":\"display." + id.Value + "\","
            + "\"stack_size\":" + stackSize + ",\"name_key\":\"l10n." + id.Value.Replace('.', '_') + "\"}";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static string SaveSlotPath(Id slotId) => "user://saves/" + slotId.Value + ".json";

        [Fact]
        public void PartialOpenChest_SaveThenLoadOnNewHost_FreeingSpaceDeliversRemainderExactlyOnce()
        {
            // MaxSlots=2：1 号格被 filler 占满，只剩 1 个空格；A 在前先拿到唯一空格，B 完全放不下，
            // 留下 5 个 B 记入 pending（同 CR140-01 OpenChestPartial_KeepsDeliveredPortion 场景）。
            var fixture = Build(maxSlots: 2, fullPolicy: InventoryFullPolicy.Partial);
            fixture.Inventory.AddItem(Unit, ItemFiller, 1);
            fixture.Bus.DispatchPending();

            fixture.Loot.Table(LootTableRef, new ItemStack(ItemA, 4), new ItemStack(ItemB, 5));
            var gobjId = SpawnChest(fixture);

            fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();

            Assert.Equal(4, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(0, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Single(fixture.Loot.Calls);

            // --- 存档：把 Host 的 _pendingChestLoot 序列化出来。---
            var saved = new GobjPendingLootPersistable(fixture.Host).Save();

            // --- 读档：一个全新的 GameObjectHost 实例（模拟进程重启后重新装配），先确认它自己的
            // pending 表天然是空的，再用 Load 把存档内容灌回去。---
            var newHost = fixture.NewHostAfterReload();
            Assert.Empty(newHost.PendingChestLootSnapshot());
            new GobjPendingLootPersistable(newHost).Load(saved);

            var restored = newHost.PendingChestLootSnapshot();
            Assert.True(restored.ContainsKey(gobjId));
            var restoredStack = Assert.Single(restored[gobjId]);
            Assert.Equal(ItemB, restoredStack.TemplateId);
            Assert.Equal(5, restoredStack.Count);

            // 腾出空间：移走 filler，释放出 B 需要的那个格子。
            var fillerInstance = fixture.Inventory.ListItems(Unit).First(i => i.TemplateId.Equals(ItemFiller));
            fixture.Inventory.RemoveItem(Unit, fillerInstance.InstanceId, 1);
            fixture.Bus.DispatchPending();

            // 用新宿主再次交互：open_state 已经是 true（Flags 是两个宿主共享的同一份世界状态），
            // 应当直接走"补发 pending"分支，且只补发一次，不重新 roll 掉落表。
            newHost.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();

            Assert.Equal(4, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(5, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Single(fixture.Loot.Calls); // 仍然只 roll 过一次。
            Assert.Empty(newHost.PendingChestLootSnapshot());

            // pending 已发完：再交互一次是彻底 no-op，不会重复补发。
            newHost.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();
            Assert.Equal(4, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(5, fixture.Inventory.CountOf(Unit, ItemB));
        }

        [Fact]
        public void Load_JsonNull_OldSaveWithoutSection_RestoresToEmpty_DoesNotThrow()
        {
            var fixture = Build(maxSlots: 4, fullPolicy: InventoryFullPolicy.Partial);
            var persistable = new GobjPendingLootPersistable(fixture.Host);

            persistable.Load(JsonNull.Instance);

            Assert.Empty(fixture.Host.PendingChestLootSnapshot());
        }

        [Fact]
        public void SaveThenLoad_NoPendingLoot_RoundTripsToEmpty()
        {
            var fixture = Build(maxSlots: 4, fullPolicy: InventoryFullPolicy.Partial);
            var persistable = new GobjPendingLootPersistable(fixture.Host);

            var saved = persistable.Save();
            var newHost = fixture.NewHostAfterReload();
            new GobjPendingLootPersistable(newHost).Load(saved);

            Assert.Empty(newHost.PendingChestLootSnapshot());
        }

        /// <summary>审计 probe：通过真实 <see cref="SaveSystem.Load"/> 读取一个不含新段的旧存档，
        /// 在同一宿主已有 pending 残留时，缺失段必须仍调用 persistable.Load(JsonNull) 清空状态。</summary>
        [Fact]
        public void SaveSystemLoad_OldSaveMissingPendingLootSection_ClearsExistingResidual()
        {
            var fixture = Build(maxSlots: 2, fullPolicy: InventoryFullPolicy.Partial);
            fixture.Inventory.AddItem(Unit, ItemFiller, 1);
            fixture.Bus.DispatchPending();
            fixture.Loot.Table(LootTableRef, new ItemStack(ItemA, 4), new ItemStack(ItemB, 5));
            var gobjId = SpawnChest(fixture);
            fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();
            Assert.True(fixture.Host.PendingChestLootSnapshot().ContainsKey(gobjId));

            var fs = new StubFileSystem();
            var slotId = new Id("slot.gobj_pending_loot_old_save");
            var save = new Core.Foundation.SaveSystem.SaveSystem(fs, new SaveSystemOptions(new Id("game.gobj_pending_loot")), fixture.Bus);
            var persistable = new GobjPendingLootPersistable(fixture.Host);
            save.RegisterPersistable(persistable);
            Assert.True(save.Save(new SaveRequest(slotId, "before-old-save")).Success);

            // Manufacture an old save from the real SaveSystem output by removing only the newly
            // introduced section; preserve the valid envelope and meta fields.
            var parsed = (JsonObject)JsonReader.Parse(fs.ReadText(SaveSlotPath(slotId))!);
            var sections = (JsonObject)parsed["sections"];
            var oldSections = new JsonObjectBuilder();
            foreach (var section in sections)
            {
                if (section.Key != persistable.SectionKey)
                {
                    oldSections.Add(section.Key, section.Value);
                }
            }

            var oldDocument = new JsonObjectBuilder()
                .Add("save_version", parsed["save_version"])
                .Add("sections", oldSections.Build())
                .Build();
            Assert.True(fs.WriteTextAtomic(SaveSlotPath(slotId), JsonWriter.Write(oldDocument)));

            var beforeLoad = fixture.Host.PendingChestLootSnapshot().Count;
            var loadResult = save.Load(slotId);
            var afterLoad = fixture.Host.PendingChestLootSnapshot().Count;
            Assert.Equal(LoadStatus.Loaded, loadResult.Status);
            Assert.True(afterLoad == 0,
                $"expected missing world.gobj_pending_loot section to clear residual pending=0, actual={afterLoad}; beforeLoad={beforeLoad}");
        }

        /// <summary>审计 probe：真实 GameObjectFactory 生成的 Partial 箱子在离图清场后，
        /// 原地图同刷新点再次生成同模板实体会拿到新 instance id。探针只记录身份与 pending 台账关系；
        /// EnterMap 本来允许新实体生成/重 roll，因此不把新实体缺少旧 pending 解释为重复奖励。</summary>
        [Fact]
        public void PartialChest_CrossMapReentry_NewInstanceIdDoesNotReattachOldPending()
        {
            var oldMap = new Id("map.gobj_pending_loot_old");
            var fixture = Build(maxSlots: 2, fullPolicy: InventoryFullPolicy.Partial);
            fixture.Inventory.AddItem(Unit, ItemFiller, 1);
            fixture.Bus.DispatchPending();
            fixture.Loot.Table(LootTableRef, new ItemStack(ItemA, 4), new ItemStack(ItemB, 5));

            var oldId = SpawnChestAt(fixture, oldMap);
            fixture.Host.Interact(Unit, oldId);
            fixture.Bus.DispatchPending();
            var beforeClear = fixture.Host.PendingChestLootSnapshot();
            Assert.True(beforeClear.ContainsKey(oldId));

            fixture.World.ClearAll();
            fixture.Bus.DispatchPending();
            // Re-enter the same map and refresh point; only the runtime instance is replaced.
            var newId = SpawnChestAt(fixture, oldMap);
            fixture.Bus.DispatchPending();
            var afterReentry = fixture.Host.PendingChestLootSnapshot();
            var oldEntryStillPresent = afterReentry.ContainsKey(oldId);
            var newEntryPresent = afterReentry.ContainsKey(newId);
            Console.WriteLine(
                $"PROBE_C expected=old pending remains ledger-keyed while new spawn has no reattached entry; " +
                $"actual=oldId={oldId.Value},newId={newId.Value},beforeCount={beforeClear.Count}," +
                $"afterCount={afterReentry.Count},oldEntryStillPresent={oldEntryStillPresent},newEntryPresent={newEntryPresent}");

            Assert.NotEqual(oldId, newId);
            Assert.True(oldEntryStillPresent,
                $"expected old pending entry to remain after cross-map reentry; oldId={oldId}, newId={newId}, afterCount={afterReentry.Count}");
            Assert.False(newEntryPresent,
                $"expected new spawn not to inherit old pending; oldId={oldId}, newId={newId}, afterCount={afterReentry.Count}");
        }

        /// <summary>审计 probe：断言当前缺陷现状——满包采集的未交付掉落仍提交冷却，腾位后
        /// 不能补领。显式时间为 100、冷却 60，腾出背包后在 101 再交互仍不可获得奖励；PASS
        /// 表示复现成功，不表示期望的产品语义。</summary>
        [Fact]
        public void GatherNode_FullInventory_RecordsUsedAtAndHonorsCooldownAfterSpaceFreed()
        {
            var fixture = Build(maxSlots: 1, fullPolicy: InventoryFullPolicy.Partial);
            fixture.Inventory.AddItem(Unit, ItemFiller, 1);
            fixture.Bus.DispatchPending();
            fixture.Loot.Table(GatherLootTableRef, new ItemStack(ItemA, 1));
            var gatherId = SpawnGather(fixture);

            fixture.Options.SimTime = () => 100;
            fixture.Host.Interact(Unit, gatherId);
            fixture.Bus.DispatchPending();
            var usedAtAfterFull = fixture.Host.GetState(gatherId, "used_at");
            var countAfterFull = fixture.Inventory.CountOf(Unit, ItemA);

            var filler = fixture.Inventory.ListItems(Unit).Single(i => i.TemplateId.Equals(ItemFiller));
            fixture.Inventory.RemoveItem(Unit, filler.InstanceId, 1);
            fixture.Bus.DispatchPending();

            fixture.Options.SimTime = () => 101;
            fixture.Host.Interact(Unit, gatherId);
            fixture.Bus.DispatchPending();
            var usedAtAfterRetry = fixture.Host.GetState(gatherId, "used_at");
            var countAfterRetry = fixture.Inventory.CountOf(Unit, ItemA);
            Console.WriteLine(
                $"PROBE_GATHER expected=used_at=100 and no reward at t=101 while cooldown=60; " +
                $"actual=usedAtAfterFull={usedAtAfterFull},usedAtAfterRetry={usedAtAfterRetry}," +
                $"countAfterFull={countAfterFull},countAfterRetry={countAfterRetry},lootRolls={fixture.Loot.Calls.Count}");

            Assert.Equal(ExprValue.OfNumber(100), usedAtAfterFull);
            Assert.Equal(0, countAfterFull);
            Assert.Equal(ExprValue.OfNumber(100), usedAtAfterRetry);
            Assert.Equal(0, countAfterRetry);
            Assert.Single(fixture.Loot.Calls);
        }
    }
}
