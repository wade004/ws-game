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
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
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
    /// 状态——<c>flags</c>/<c>inventory</c>——但 pending 台账各自独立，只能通过存档段的 Save/Load
    /// 传递）。
    /// <para>
    /// CR150-02/03/04 根治（architecture/落地计划/audit-3224ca1-20260908，P2）追加覆盖：pending
    /// 台账改按 <see cref="GameObjectEntity.OriginKey"/> 稳定身份记账，实体因 <c>World.ClearAll</c>
    /// 重建后仍应正确关联（<see cref="PartialChest_CrossMapReentry_NewEntityReattachesOldPending"/>）；
    /// <see cref="SaveSystem"/> 真实 Save/Load 场景下旧档缺该段应清空台账，不残留（<see
    /// cref="SaveSystemLoad_OldSaveMissingPendingLootSection_ClearsExistingResidual"/>）；
    /// <c>gather_node</c> 满包时不应先提交冷却再忽略入包失败（<see
    /// cref="GatherNode_FullInventory_RejectDoesNotCommitCooldown_PartialCommitsAndPersistsRemainder"/>）。
    /// </para>
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
            /// 但是一个全新的 <see cref="GameObjectHost"/> 实例——pending 台账从零开始，只能靠
            /// <see cref="GobjPendingLootPersistable.Load"/> 恢复。</summary>
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

            // --- 存档：把 Host 的 pending 台账序列化出来。---
            var saved = new GobjPendingLootPersistable(fixture.Host).Save();

            // --- 读档：一个全新的 GameObjectHost 实例（模拟进程重启后重新装配），先确认它自己的
            // pending 表天然是空的，再用 Load 把存档内容灌回去。---
            var newHost = fixture.NewHostAfterReload();
            Assert.Empty(newHost.PendingLootSnapshot());
            new GobjPendingLootPersistable(newHost).Load(saved);

            // CR150-02 根治：台账键是 GameObjectEntity.OriginKey（稳定身份），不再是 gobjId（瞬态
            // 运行期实体 id），因此这里不再断言 ContainsKey(gobjId)——只断言唯一一条记录的内容正确
            // （下面用同一个仍然存活的实体再次 Interact 能正确关联上，才是本测试真正要验证的行为）。
            var restored = newHost.PendingLootSnapshot();
            var restoredEntry = Assert.Single(restored);
            var restoredStack = Assert.Single(restoredEntry.Value);
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
            Assert.Empty(newHost.PendingLootSnapshot());

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

            Assert.Empty(fixture.Host.PendingLootSnapshot());
        }

        [Fact]
        public void SaveThenLoad_NoPendingLoot_RoundTripsToEmpty()
        {
            var fixture = Build(maxSlots: 4, fullPolicy: InventoryFullPolicy.Partial);
            var persistable = new GobjPendingLootPersistable(fixture.Host);

            var saved = persistable.Save();
            var newHost = fixture.NewHostAfterReload();
            new GobjPendingLootPersistable(newHost).Load(saved);

            Assert.Empty(newHost.PendingLootSnapshot());
        }

        /// <summary>CR150-02 核心复现与根治：满包 Partial 开箱留下未交付余量后，真实
        /// <see cref="WorldSim.ClearAll"/> 销毁全部实体（同跨图/离图重进），同一地图同一位置同一
        /// 模板经真实 <see cref="GameObjectFactory.Spawn"/> 重新生成——生产链对应
        /// <c>SpawnHost.UnloadMap</c>（清空 EntityId）→ <c>OnMapEnter</c> 重新
        /// <c>SpawnEntity</c>，本测试直接调用同一个真实 <see cref="GameObjectFactory"/> 复现同样的
        /// "同刷新点、新运行期 id"效果。新实体的运行期 id 与旧实体不同，但腾出背包空间后与新实体
        /// 交互应当能关联上旧实体遗留的余量，恰好补发一次，不重新 roll 掉落表。</summary>
        [Fact]
        public void PartialChest_CrossMapReentry_NewEntityReattachesOldPending_DeliversRemainderExactlyOnce()
        {
            var fixture = Build(maxSlots: 2, fullPolicy: InventoryFullPolicy.Partial);
            fixture.Inventory.AddItem(Unit, ItemFiller, 1);
            fixture.Bus.DispatchPending();
            fixture.Loot.Table(LootTableRef, new ItemStack(ItemA, 4), new ItemStack(ItemB, 5));

            var oldId = SpawnChest(fixture);
            fixture.Host.Interact(Unit, oldId);
            fixture.Bus.DispatchPending();

            Assert.Equal(4, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(0, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.NotEmpty(fixture.Host.PendingLootSnapshot());

            fixture.World.ClearAll();
            fixture.Bus.DispatchPending();

            var newId = SpawnChest(fixture);
            Assert.NotEqual(oldId, newId);

            var fillerInstance = fixture.Inventory.ListItems(Unit).First(i => i.TemplateId.Equals(ItemFiller));
            fixture.Inventory.RemoveItem(Unit, fillerInstance.InstanceId, 1);
            fixture.Bus.DispatchPending();

            fixture.Host.Interact(Unit, newId);
            fixture.Bus.DispatchPending();

            Assert.Equal(4, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(5, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Single(fixture.Loot.Calls); // 全程只 roll 过一次——不是靠新实体重新 roll 拿到的 B。
            Assert.Empty(fixture.Host.PendingLootSnapshot());

            // 紧接着再交互一次：不应该因为新实体自己的 open_state 从未被设置过而又重新 roll 一遍
            // （同一份奖励变相拿两次）。
            fixture.Host.Interact(Unit, newId);
            fixture.Bus.DispatchPending();
            Assert.Equal(4, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(5, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Single(fixture.Loot.Calls);
        }

        /// <summary>CR150-03 核心复现与根治：真实 <see cref="Core.Foundation.SaveSystem.SaveSystem.Save"/>
        /// 生成的合法存档，只人为移除 <see cref="GobjPendingLootPersistable.SectionKey"/> 这一个新段
        /// （保留其它段与信封字段不动，模拟"引入本段之前产生的旧存档"），同一宿主已有 pending 残留
        /// 时，真实 <see cref="Core.Foundation.SaveSystem.SaveSystem.Load"/> 必须仍然清空它——不能
        /// 因为文档里压根没有这一段就整段跳过 <see cref="GobjPendingLootPersistable.Load"/> 调用。</summary>
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
            Assert.NotEmpty(fixture.Host.PendingLootSnapshot());

            var fs = new StubFileSystem();
            var slotId = new Id("slot.gobj_pending_loot_old_save");
            var save = new Core.Foundation.SaveSystem.SaveSystem(
                fs, new SaveSystemOptions(new Id("game.gobj_pending_loot")), fixture.Bus);
            var persistable = new GobjPendingLootPersistable(fixture.Host);
            save.RegisterPersistable(persistable);
            Assert.True(save.Save(new SaveRequest(slotId, "before-old-save")).Success);

            // 只人为移除本段，保留信封其它字段与其它段（此刻并无其它已注册段，sections 只剩
            // meta），模拟"这份存档产生于本段引入之前"。
            var parsed = (JsonObject)JsonReader.Parse(fs.ReadText(SaveSlotPath(slotId))!);
            var sections = (JsonObject)parsed["sections"];
            var oldSectionsBuilder = new JsonObjectBuilder();
            foreach (var section in sections)
            {
                if (section.Key != persistable.SectionKey)
                {
                    oldSectionsBuilder.Add(section.Key, section.Value);
                }
            }

            var oldDocument = new JsonObjectBuilder()
                .Add("save_version", parsed["save_version"])
                .Add("sections", oldSectionsBuilder.Build())
                .Build();
            Assert.True(fs.WriteTextAtomic(SaveSlotPath(slotId), JsonWriter.Write(oldDocument)));

            var loadResult = save.Load(slotId);

            Assert.Equal(LoadStatus.Loaded, loadResult.Status);
            Assert.Empty(fixture.Host.PendingLootSnapshot());
        }

        /// <summary>
        /// AUD-01 根治（architecture/落地计划/audit-85f1f4f-20260908，P1，真实复现）：1.5.0 旧格式
        /// 非空 <c>world.gobj_pending_loot</c>（条目键是瞬态运行期 <c>gobjInstanceId</c>，见历史
        /// serializer <c>git show 3224ca1:core/carriers/gobj/core/GobjPendingLootPersistable.cs</c>）
        /// 用真实 <see cref="Core.Foundation.SaveSystem.SaveSystem.Load"/> 读取，此前直接索引
        /// <c>entryObj["originKey"]</c> 抛 <see cref="FormatException"/>（旧字段是
        /// <c>gobjInstanceId</c>，不是 <c>originKey</c>，见 AUDIT_REPORT），<c>SaveSystem.Load</c>
        /// 因此返回 <c>PersistableThrew</c>；本用例复现该真实旧信封形状（含一段排在
        /// <c>gobj_pending_loot</c> 之前的 <c>player.inventory</c> 段，验证前段确实成功提交，同时
        /// 验证根治后不再回滚它——因为本段现在改为安全丢弃整条、不再抛异常，压根不会触发
        /// <see cref="Core.Foundation.SaveSystem.SaveSystem"/> 的回滚路径），断言修复后
        /// <c>load_status=Loaded</c>、旧段被安全丢弃（pending 仍是空表）、且诊断记了一条警告
        /// （不是静默吞掉）。
        /// </summary>
        [Fact]
        public void SaveSystemLoad_Legacy15GobjInstanceIdField_SafelyDropped_NotThrown()
        {
            var fixture = Build(maxSlots: 2, fullPolicy: InventoryFullPolicy.Partial);
            fixture.Inventory.AddItem(Unit, ItemFiller, 2);
            fixture.Bus.DispatchPending();

            var fs = new StubFileSystem();
            var slotId = new Id("slot.gobj_pending_loot_legacy_15");
            var diagnostics = new InMemorySaveDiagnostics();
            var save = new Core.Foundation.SaveSystem.SaveSystem(
                fs, new SaveSystemOptions(new Id("game.gobj_pending_loot")), fixture.Bus, diagnostics);
            var inventoryPersistable = new InventoryPersistable(Unit, fixture.Inventory);
            var pendingLootPersistable = new GobjPendingLootPersistable(fixture.Host, diagnostics);
            save.RegisterPersistable(inventoryPersistable);
            save.RegisterPersistable(pendingLootPersistable);

            // 手写 1.5.0 真实 on-disk 形状：player.inventory 段排在 world.gobj_pending_loot 之前
            // （同 10 第 3 节固定顺序"步骤 4 在 7a 之前"），pending_loot 条目键是旧字段
            // gobjInstanceId，不是 originKey。
            var legacyInstanceId = new Id("gobj.inst_1");
            var legacyDocument = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add("meta", new JsonObjectBuilder()
                        .Add("save_version", new JsonNumber(1))
                        .Add("slot_id", new JsonString(slotId.Value))
                        .Add("created_at", new JsonString("legacy"))
                        .Add("updated_at", new JsonString("legacy"))
                        .Add("game_id", new JsonString("game.gobj_pending_loot"))
                        .Build())
                    .Add(inventoryPersistable.SectionKey, new JsonArray(new JsonValue[]
                    {
                        new JsonObjectBuilder()
                            .Add("instance_id", new JsonString("item.inst_legacy_1"))
                            .Add("template_id", new JsonString(ItemA.Value))
                            .Add("count", new JsonNumber(2))
                            .Add("extra", new JsonObjectBuilder().Build())
                            .Build(),
                    }))
                    .Add(pendingLootPersistable.SectionKey, new JsonObjectBuilder()
                        .Add("pending_loot", new JsonArray(new JsonValue[]
                        {
                            new JsonObjectBuilder()
                                .Add("gobjInstanceId", new JsonString(legacyInstanceId.Value))
                                .Add("items", new JsonArray(new JsonValue[]
                                {
                                    new JsonObjectBuilder()
                                        .Add("templateId", new JsonString(ItemB.Value))
                                        .Add("count", new JsonNumber(3))
                                        .Build(),
                                }))
                                .Build(),
                        }))
                        .Build())
                    .Build())
                .Build();
            Assert.True(fs.WriteTextAtomic(SaveSlotPath(slotId), JsonWriter.Write(legacyDocument)));

            var loadResult = save.Load(slotId);

            Assert.Equal(LoadStatus.Loaded, loadResult.Status);
            Assert.Equal(2, fixture.Inventory.CountOf(Unit, ItemA)); // 前段（inventory）正常恢复。
            Assert.Empty(fixture.Host.PendingLootSnapshot()); // 旧段被安全丢弃，不是遗留原运行期状态。
            Assert.Contains(diagnostics.Warnings, w => w.Contains("gobjInstanceId"));
        }

        /// <summary>
        /// AUD-04 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：1.6.0 把
        /// <see cref="GameObjectHost.PendingChestLootSnapshot"/>/<see
        /// cref="GameObjectHost.RestorePendingChestLoot"/> 改名为 <see
        /// cref="GameObjectHost.PendingLootSnapshot"/>/<see cref="GameObjectHost.RestorePendingLoot"/>，
        /// 没有提供旧名转发，任何仍用旧名的 1.5 风格调用点在 1.6 上编译即 CS1061。本用例本身就是
        /// 一段"1.5 风格调用"，锁定旧名重新可编译、行为与新名完全一致（不是另一套语义）——
        /// <c>#pragma warning disable CS0618</c> 只是压制"调用了 Obsolete 成员"这一预期内的警告，
        /// 不代表本用例本身有问题。
        /// </summary>
        [Fact]
        public void ObsoletePendingChestLootAliases_StillCompileAndForwardToNewNames()
        {
            var fixture = Build(maxSlots: 2, fullPolicy: InventoryFullPolicy.Partial);
            fixture.Inventory.AddItem(Unit, ItemFiller, 1);
            fixture.Bus.DispatchPending();
            fixture.Loot.Table(LootTableRef, new ItemStack(ItemA, 4), new ItemStack(ItemB, 5));
            var gobjId = SpawnChest(fixture);
            fixture.Host.Interact(Unit, gobjId);
            fixture.Bus.DispatchPending();

#pragma warning disable CS0618 // 故意调用旧名，验证 1.5 风格调用点仍可编译、行为等价于新名。
            var snapshotViaOldName = fixture.Host.PendingChestLootSnapshot();
            Assert.NotEmpty(snapshotViaOldName);
            Assert.Equal(fixture.Host.PendingLootSnapshot(), snapshotViaOldName);

            var newHost = fixture.NewHostAfterReload();
            newHost.RestorePendingChestLoot(snapshotViaOldName);
#pragma warning restore CS0618

            Assert.Equal(snapshotViaOldName.Count, newHost.PendingLootSnapshot().Count);
        }

        /// <summary>CR150-04 核心复现与根治：满包采集不应该先提交冷却再忽略入包失败。完全失败（背包
        /// 一件都放不下）不提交冷却，允许立即重试；部分成功提交冷却并把未交付部分记入 pending，腾出
        /// 空间后在同一冷却窗口内重试应当补发剩余，不需要等冷却结束、也不重新 roll 掉落表。</summary>
        [Fact]
        public void GatherNode_FullInventory_RejectDoesNotCommitCooldown_PartialCommitsAndPersistsRemainder()
        {
            // maxSlots=2：1 号格被 filler 占满，只剩 1 个空格；A（count 1）能拿到唯一空格，B
            // （count 1）完全放不下——两件不同模板各自要求一个独立格子，同 CR140-01
            // OpenChestPartial_KeepsDeliveredPortion 场景，才能让 Reject/Partial 两种策略产生真正
            // 不同的结果（单一物品堆叠内的"部分数量"对 count=1 无意义）。
            var fixture = Build(maxSlots: 2, fullPolicy: InventoryFullPolicy.Partial);
            fixture.Inventory.AddItem(Unit, ItemFiller, 1);
            fixture.Bus.DispatchPending();
            fixture.Loot.Table(GatherLootTableRef, new ItemStack(ItemA, 1), new ItemStack(ItemB, 1));
            var gatherId = SpawnGather(fixture);

            // --- 完全失败：Reject 策略下 B 放不下，整批回滚（含已经放进去的 A），不提交冷却。---
            fixture.Options.GatherNodeLootPolicy = GobjLootDeliveryPolicy.Reject;
            fixture.Options.SimTime = () => 100;
            fixture.Host.Interact(Unit, gatherId);
            fixture.Bus.DispatchPending();
            Assert.Null(fixture.Host.GetState(gatherId, "used_at"));
            Assert.Equal(0, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(0, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Single(fixture.Loot.Calls);

            // 不提交冷却：立即重试（同一时刻）应当仍然允许再 roll 一次（不是被冷却挡住）。
            fixture.Host.Interact(Unit, gatherId);
            fixture.Bus.DispatchPending();
            Assert.Null(fixture.Host.GetState(gatherId, "used_at"));
            Assert.Equal(2, fixture.Loot.Calls.Count);

            // --- 切到 Partial 策略：部分成功——A 放进唯一空格，B 放不下，记入 pending，提交冷却。---
            fixture.Options.GatherNodeLootPolicy = GobjLootDeliveryPolicy.Partial;
            fixture.Host.Interact(Unit, gatherId);
            fixture.Bus.DispatchPending();
            Assert.Equal(ExprValue.OfNumber(100), fixture.Host.GetState(gatherId, "used_at"));
            Assert.Equal(1, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(0, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.NotEmpty(fixture.Host.PendingLootSnapshot());
            Assert.Equal(3, fixture.Loot.Calls.Count);

            // 腾出空间，仍在同一冷却窗口内（t=101 < 100+60）重试：应当补发剩余的 B，不重新 roll。
            var fillerInstance = fixture.Inventory.ListItems(Unit).Single(i => i.TemplateId.Equals(ItemFiller));
            fixture.Inventory.RemoveItem(Unit, fillerInstance.InstanceId, 1);
            fixture.Bus.DispatchPending();

            fixture.Options.SimTime = () => 101;
            fixture.Host.Interact(Unit, gatherId);
            fixture.Bus.DispatchPending();

            Assert.Equal(1, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(1, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Empty(fixture.Host.PendingLootSnapshot());
            Assert.Equal(3, fixture.Loot.Calls.Count); // 补发不重新 roll。
            // 冷却时钟没有被这次补发推迟：respawn_after_use 仍从最初真正采集的 t=100 算起。
            Assert.Equal(ExprValue.OfNumber(100), fixture.Host.GetState(gatherId, "used_at"));

            // 仍在原冷却窗口内（t=101，100+60=160 尚未到）再交互：pending 已空，冷却未到，不应再有
            // 任何变化。
            fixture.Host.Interact(Unit, gatherId);
            fixture.Bus.DispatchPending();
            Assert.Equal(1, fixture.Inventory.CountOf(Unit, ItemA));
            Assert.Equal(1, fixture.Inventory.CountOf(Unit, ItemB));
            Assert.Equal(3, fixture.Loot.Calls.Count);
        }
    }
}
