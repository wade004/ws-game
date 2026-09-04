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
    }
}
