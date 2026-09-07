using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>持久化往返测试（见 10 第 2.2/2.5 节）：存档后用一套全新的宿主（新
    /// <see cref="InventoryHost"/>/<see cref="StatHost"/>/<see cref="EquipmentHost"/> 实例）加载，
    /// 断言背包内容一致、装备槽一致，且属性经重新执行的装备联动再次生效（不是从快照直接搬运）。</summary>
    public class ItemPersistableTests
    {
        private const string SlotJson =
            "[{\"id\": \"item.slot.main_hand\", \"name_key\": \"l10n.item.slot.main_hand\", \"is_weapon\": true}," +
            "{\"id\": \"item.slot.consumable\", \"name_key\": \"l10n.item.slot.consumable\"}]";

        private const string QualityJson =
            "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\"}]";

        private const string StatDefJson =
            "[{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"group\": \"primary\"}]";

        private const string TemplateJson =
            "[" +
            "{\"id\": \"item.sample_weapon_a\", \"slot\": \"item.slot.main_hand\", \"quality\": \"item.quality.common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.sample_weapon_a\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.sample_weapon_a\"," +
            " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 7}]}," +
            "{\"id\": \"item.sample_potion\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.sample_potion\", \"stack_size\": 10," +
            " \"name_key\": \"l10n.item.sample_potion\"}" +
            "]";

        private static readonly Id Player = new Id("player.hero");

        private sealed class Hosts
        {
            public InventoryHost Inventory = null!;
            public StatHost StatHost = null!;
            public EquipmentHost Equipment = null!;
        }

        private static Core.Foundation.DataRegistry.DataRegistry BuildRegistry() => TestSupport.BuildRegistry(source =>
        {
            source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
            source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
            source.Add("item.template", TestSupport.Table("item.template", TemplateJson));
            source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
        });

        private static Hosts BuildHosts(Core.Foundation.DataRegistry.DataRegistry registry)
        {
            var bus = TestSupport.CreateBus();
            var inventory = new InventoryHost(registry, bus);
            var statHost = new StatHost(registry, bus);
            statHost.RegisterUnit(Player);
            var unitAccess = new FakeUnitAccess().Add(Player, level: 99);
            var equipment = new EquipmentHost(
                registry, bus, inventory, statHost, new FakeEffectSink(), new RecordingSkillGranter().Grant, unitAccess);

            return new Hosts { Inventory = inventory, StatHost = statHost, Equipment = equipment };
        }

        [Fact]
        public void RoundTrip_InventoryAndEquipment_RestoresStateAndReappliesLinkage()
        {
            var registry = BuildRegistry();

            // 原始宿主：装备一件武器，背包里再留一瓶药水。
            var original = BuildHosts(registry);
            original.Inventory.AddItem(Player, new Id("item.sample_weapon_a"), 1);
            var weaponInstanceId = original.Inventory.ListItems(Player)[0].InstanceId;
            original.Equipment.Equip(Player, weaponInstanceId, new Id("item.slot.main_hand"));
            original.Inventory.AddItem(Player, new Id("item.sample_potion"), 4);

            Assert.Equal(7, original.StatHost.GetStat(Player, new Id("stat.strength")));

            var invPersistable = new InventoryPersistable(Player, original.Inventory);
            var equipPersistable = new EquipmentPersistable(Player, original.Inventory, original.Equipment);
            var savedInventory = invPersistable.Save();
            var savedEquipment = equipPersistable.Save();

            // 全新宿主（新 InventoryHost/StatHost/EquipmentHost），只用同一份 registry 与刚保存的数据加载。
            var loaded = BuildHosts(registry);
            Assert.Equal(0, loaded.StatHost.GetStat(Player, new Id("stat.strength")));

            new InventoryPersistable(Player, loaded.Inventory).Load(savedInventory);
            new EquipmentPersistable(Player, loaded.Inventory, loaded.Equipment).Load(savedEquipment);

            // 背包内容一致：药水还在背包里（武器已被装备走，不在背包列表中）。
            var loadedItems = loaded.Inventory.ListItems(Player);
            Assert.Single(loadedItems);
            Assert.Equal(new Id("item.sample_potion"), loadedItems[0].TemplateId);
            Assert.Equal(4, loadedItems[0].Count);

            // 装备槽一致。
            var equippedRef = loaded.Equipment.GetEquipped(Player, new Id("item.slot.main_hand"));
            Assert.NotNull(equippedRef);
            Assert.Equal(weaponInstanceId, equippedRef!.Value.InstanceId);

            // 属性重新联动生效（不是从快照搬运——loaded.StatHost 是全新实例，此前恒为 0，
            // Load 之后必须重新跑一遍 Equip 才可能变成 7）。
            Assert.Equal(7, loaded.StatHost.GetStat(Player, new Id("stat.strength")));
        }

        [Fact]
        public void InventoryPersistable_Load_NullData_LeavesBagEmpty()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);

            new InventoryPersistable(Player, hosts.Inventory).Load(Core.Foundation.Common.Json.JsonNull.Instance);

            Assert.Empty(hosts.Inventory.ListItems(Player));
        }

        // -----------------------------------------------------------------
        // FND-10：EquipmentPersistable.Load 必须按快照"完整替换"当前装备状态，不是合并
        // （见 core/carriers/item/core/ItemPersistable.cs EquipmentPersistable 类型判断记录、
        // 外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md FND-10、
        // validation-repros.txt R2/R2b）。
        // -----------------------------------------------------------------

        [Fact]
        public void EquipmentPersistable_Load_EmptySnapshot_UnequipsCurrentLiveItemAndRevertsGrants()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);
            hosts.Inventory.AddItem(Player, new Id("item.sample_weapon_a"), 1);
            var weaponInstanceId = hosts.Inventory.ListItems(Player)[0].InstanceId;
            var equipped = hosts.Equipment.Equip(Player, weaponInstanceId, new Id("item.slot.main_hand"));
            Assert.True(equipped.Success);
            Assert.Equal(7, hosts.StatHost.GetStat(Player, new Id("stat.strength")));

            var persistable = new EquipmentPersistable(Player, hosts.Inventory, hosts.Equipment);

            // 修复前：Load 只 Inject/Equip 快照里的槽位，空对象快照什么都不做，武器仍然装备着——
            // 见外部审计 R2「PASS_FOR_REPRO (live equipment remains)」。
            persistable.Load(new Core.Foundation.Common.Json.JsonObjectBuilder().Build());

            Assert.False(hosts.Equipment.GetEquipped(Player, new Id("item.slot.main_hand")).HasValue);
            // 联动同样必须撤销（不遗留 grants）：属性修正应回到基础值。
            Assert.Equal(0, hosts.StatHost.GetStat(Player, new Id("stat.strength")));
            // 空快照清空装备，物品既不留在装备栏，也不应凭空出现在背包里（该实例本就不属于这份
            // 空快照代表的历史状态）。
            Assert.Null(hosts.Inventory.FindInstance(Player, weaponInstanceId));
        }

        [Fact]
        public void EquipmentPersistable_Load_OldSnapshot_DoesNotLeakLiveItemIntoBag_AndRestoresOnlySnapshotItem()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);

            hosts.Inventory.AddItem(Player, new Id("item.sample_weapon_a"), 1);
            var oldInstanceId = hosts.Inventory.ListItems(Player)[0].InstanceId;
            var equipmentPersistable = new EquipmentPersistable(Player, hosts.Inventory, hosts.Equipment);
            var inventoryPersistable = new InventoryPersistable(Player, hosts.Inventory);

            hosts.Equipment.Equip(Player, oldInstanceId, new Id("item.slot.main_hand"));
            var oldEquipmentSnapshot = equipmentPersistable.Save();
            var oldInventorySnapshot = inventoryPersistable.Save();

            // 快照之后又装备了另一件同槽位物品（B）——这件物品不在旧快照的任何一段里。
            hosts.Inventory.AddItem(Player, new Id("item.sample_weapon_a"), 1);
            var liveInstanceId = hosts.Inventory.ListItems(Player)[0].InstanceId;
            hosts.Equipment.Equip(Player, liveInstanceId, new Id("item.slot.main_hand"));

            // 回滚到旧快照（顺序同外部审计 R2b 复现：先背包段，再装备段）。
            inventoryPersistable.Load(oldInventorySnapshot);
            equipmentPersistable.Load(oldEquipmentSnapshot);

            // 修复前：Equip(old) 换装逻辑会把当前活跃的 B 物品"卸下"放回背包——B 不该在这次读档
            // 后的世界里以任何形式存在，见外部审计 R2b「PASS_FOR_REPRO (live item contaminates
            // restored bag)」。
            Assert.Null(hosts.Inventory.FindInstance(Player, liveInstanceId));

            var restored = hosts.Equipment.GetEquipped(Player, new Id("item.slot.main_hand"));
            Assert.True(restored.HasValue);
            Assert.Equal(oldInstanceId, restored!.Value.InstanceId);
        }

        [Fact]
        public void EquipmentPersistable_Load_SnapshotMissingSlot_UnequipsThatSlot()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);
            hosts.Inventory.AddItem(Player, new Id("item.sample_weapon_a"), 1);
            var weaponInstanceId = hosts.Inventory.ListItems(Player)[0].InstanceId;
            hosts.Equipment.Equip(Player, weaponInstanceId, new Id("item.slot.main_hand"));

            // 同时把消耗品也"装"进消耗品槽（该 fixture 的 item.slot.consumable 未设 is_equipment，
            // 缺省按可装备处理，见 EquipmentHost.IsEquipmentSlot 判断记录）——只为凑出一份含两个
            // 槽位的完整快照，用来验证"缺槽快照只让对应槽位归空，不动其它槽位"，与纯粹的空快照
            // （上面 EmptySnapshot 用例）区分开。
            hosts.Inventory.AddItem(Player, new Id("item.sample_potion"), 1);
            var potionInstanceId = hosts.Inventory.ListItems(Player)[0].InstanceId;
            hosts.Equipment.Equip(Player, potionInstanceId, new Id("item.slot.consumable"));

            var persistable = new EquipmentPersistable(Player, hosts.Inventory, hosts.Equipment);
            var fullSnapshot = (Core.Foundation.Common.Json.JsonObject)persistable.Save();
            Assert.True(fullSnapshot.TryGetValue("item.slot.main_hand", out _));
            Assert.True(fullSnapshot.TryGetValue("item.slot.consumable", out var consumableValue));

            // "缺槽"快照：只保留 item.slot.consumable 键，main_hand 键完全不出现——不经内部
            // ItemInstanceJson（跨程序集不可见），直接复用刚 Save() 出来的公开 JsonValue。
            var snapshotWithoutMainHand = new Core.Foundation.Common.Json.JsonObjectBuilder()
                .Add("item.slot.consumable", consumableValue)
                .Build();

            persistable.Load(snapshotWithoutMainHand);

            Assert.False(hosts.Equipment.GetEquipped(Player, new Id("item.slot.main_hand")).HasValue);
            var consumableEquipped = hosts.Equipment.GetEquipped(Player, new Id("item.slot.consumable"));
            Assert.True(consumableEquipped.HasValue);
            Assert.Equal(potionInstanceId, consumableEquipped!.Value.InstanceId);
        }

        [Fact]
        public void EquipmentPersistable_Load_SameSnapshotTwice_IsIdempotent()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);
            hosts.Inventory.AddItem(Player, new Id("item.sample_weapon_a"), 1);
            var weaponInstanceId = hosts.Inventory.ListItems(Player)[0].InstanceId;
            hosts.Equipment.Equip(Player, weaponInstanceId, new Id("item.slot.main_hand"));

            var persistable = new EquipmentPersistable(Player, hosts.Inventory, hosts.Equipment);
            var snapshot = persistable.Save();

            persistable.Load(snapshot);
            persistable.Load(snapshot);

            var restored = hosts.Equipment.GetEquipped(Player, new Id("item.slot.main_hand"));
            Assert.True(restored.HasValue);
            Assert.Equal(weaponInstanceId, restored!.Value.InstanceId);
            Assert.Equal(7, hosts.StatHost.GetStat(Player, new Id("stat.strength")));
            // 幂等：背包里不应因为两次 Load 而多出任何物品。
            Assert.Empty(hosts.Inventory.ListItems(Player));
        }
    }
}
