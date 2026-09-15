using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// 分阶段落地计划 T-N2-7（ADR-0032 决策 8"物品实例只存身份"；10 第 2.5 节修订段）验收测试：
    /// <see cref="ItemInstance.Quality"/>/<see cref="ItemInstance.Affixes"/> 两个新字段、
    /// <c>player.inventory</c>/<c>player.equipment</c> 段 JSON 新增 <c>quality</c>/<c>affixes</c> 两个
    /// 可选 key 的缺省/兼容读取（不升 <c>save_version</c>、不登记迁移函数，见 <c>ItemInstanceJson</c>
    /// 类型顶部判断记录）、三参 <c>EquipmentHost.Equip</c> 改转发实例字段。数据夹具独立于
    /// <see cref="T_N2_5_ArmorAffixReqLevelTests"/>（同该文件"自带最小夹具"惯例），但沿用其护甲/
    /// 词缀手算口径以便复用同一组数字断言。
    /// </summary>
    public class T_N2_7_ItemInstanceIdentityPersistenceTests
    {
        private const string SlotJson =
            "[{\"id\": \"item.slot.t7_chest\", \"name_key\": \"l10n.item.slot.t7_chest\", \"has_armor\": true, \"budget_coefficient\": 2.0}]";

        private const string QualityJson =
            "[" +
            "{\"id\": \"item.quality.t7_common\", \"name_key\": \"l10n.item.quality.t7_common\", \"budget_multiplier\": 1.0}," +
            "{\"id\": \"item.quality.t7_rare\", \"name_key\": \"l10n.item.quality.t7_rare\", \"budget_multiplier\": 1.5}" +
            "]";

        private const string StatDefJson =
            "[" +
            "{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"category\": \"primary\"}," +
            "{\"id\": \"stat.armor\", \"name_key\": \"l10n.stat.armor\", \"category\": \"defense\"}" +
            "]";

        // 预算曲线在 item_level=1 处取 100（同 T_N2_5 惯例：单点曲线，越界夹取到端点等价常数）。
        private const string BudgetCurveJson =
            "[{\"id\": \"item.budget.default\", \"entries\": [{\"x\": 1, \"y\": 100}]}]";

        // 护甲曲线：item_level=1 → 5，配合槽位系数 2.0 得到 stat.armor += 10。
        private const string ArmorCurveJson =
            "[{\"id\": \"item.armor.default\", \"entries\": [{\"x\": 1, \"y\": 5}]}]";

        // 词缀：budget_share=0.5，stat_mix 单条 ratio=1.0（Σratio=1，不需要归一化）——手算同
        // T_N2_5：targetBudget = itemBudgetLimit(=100×1.0×2.0=200) × 0.5 = 100，权重缺省 1 → 反解值
        // 100；模板自身 stats（5）+ 词缀反解值（100）= 105。
        private const string AffixJson =
            "[{\"id\": \"item.affix.t7_str\", \"name_key\": \"l10n.item.affix.t7_str\"," +
            " \"budget_share\": 0.5," +
            " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 1.0}]," +
            " \"quality_pool\": \"item.quality.t7_common\", \"weight\": 1.0}]";

        private const string TemplateJson =
            "[" +
            "{\"id\": \"item.t7_chest_affix\", \"slot\": \"item.slot.t7_chest\", \"quality\": \"item.quality.t7_common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.t7_chest_affix\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.t7_chest_affix\"," +
            " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 5}]}" +
            "]";

        private static readonly Id Player = new Id("player.t7_hero");
        private static readonly Id SlotChest = new Id("item.slot.t7_chest");
        private static readonly Id TemplateChestAffix = new Id("item.t7_chest_affix");
        private static readonly Id QualityCommon = new Id("item.quality.t7_common");
        private static readonly Id QualityRare = new Id("item.quality.t7_rare");
        private static readonly Id AffixStr = new Id("item.affix.t7_str");
        private static readonly Id StatStrength = new Id("stat.strength");
        private static readonly Id StatArmor = new Id("stat.armor");

        private sealed class Fixture
        {
            public IDataRegistryView Registry = null!;
            public InventoryHost Inventory = null!;
            public StatHost StatHost = null!;
            public EquipmentHost Equipment = null!;
        }

        private static DataRegistry BuildRegistry() => TestSupport.BuildRegistry(source =>
        {
            source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
            source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
            source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
            source.Add("item.armor_curve", TestSupport.Table("item.armor_curve", ArmorCurveJson));
            source.Add("item.affix", TestSupport.Table("item.affix", AffixJson));
            source.Add("item.template", TestSupport.Table("item.template", TemplateJson));
            source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
        });

        private static Fixture BuildHosts(DataRegistry registry, int playerLevel = 99)
        {
            var bus = TestSupport.CreateBus();
            var inventory = new InventoryHost(registry, bus);
            var statHost = new StatHost(registry, bus);
            statHost.RegisterUnit(Player);
            var unitAccess = new FakeUnitAccess().Add(Player, playerLevel);
            var equipment = new EquipmentHost(
                registry, bus, inventory, statHost, new FakeEffectSink(), new RecordingSkillGranter().Grant, unitAccess);

            return new Fixture { Registry = registry, Inventory = inventory, StatHost = statHost, Equipment = equipment };
        }

        // -----------------------------------------------------------------
        // 验收标准 1：旧存档加载（player.inventory 段，无 quality/affixes key）——按模板自身品质
        // 兼容读取，词缀缺省空列表。
        // -----------------------------------------------------------------

        [Fact]
        public void InventoryPersistable_Load_OldFormatMissingQualityAndAffixesKeys_DefaultsToTemplateQualityAndEmptyAffixes()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);

            // 旧格式：字段形状是 T-N2-7 之前的（无 quality/affixes key），同 1.7.0 AUD-03/
            // achievement_state 先例的"新增可选字段、缺省兼容"场景。
            var oldFormatJson = "[{\"instance_id\": \"item.inst_old1\", \"template_id\": \"item.t7_chest_affix\", \"count\": 1}]";

            new InventoryPersistable(Player, hosts.Inventory).Load(JsonReader.Parse(oldFormatJson));

            var items = hosts.Inventory.ListItems(Player);
            Assert.Single(items);
            Assert.Equal(new Id("item.inst_old1"), items[0].InstanceId);
            Assert.Equal(TemplateChestAffix, items[0].TemplateId);
            // quality 缺省按模板自身 quality 字段解析（item.t7_chest_affix 的 quality 是 t7_common）。
            Assert.Equal(QualityCommon, items[0].Quality);
            // affixes 缺省空列表（旧物品没有词缀身份数据可恢复）。
            Assert.Empty(items[0].Affixes);
        }

        [Fact]
        public void EquipmentPersistable_Load_OldFormatMissingQualityKey_DefaultsToTemplateQuality_AndOnlyAppliesTemplateStats()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);

            var oldFormatEquipmentJson =
                "{\"item.slot.t7_chest\": {\"instance_id\": \"item.inst_old2\", \"template_id\": \"item.t7_chest_affix\", \"count\": 1}}";

            new EquipmentPersistable(Player, hosts.Inventory, hosts.Equipment).Load(JsonReader.Parse(oldFormatEquipmentJson));

            var equippedInstances = hosts.Equipment.GetAllEquippedInstances(Player);
            Assert.True(equippedInstances.TryGetValue(SlotChest, out var instance));
            Assert.Equal(QualityCommon, instance.Quality);
            Assert.Empty(instance.Affixes);

            // 旧存档没有词缀身份数据可恢复，三参 Equip 转发空 Affixes——只有模板自身 stats（5）与
            // 护甲（曲线 5 × 槽位系数 2.0 = 10）生效，没有词缀反解值。
            Assert.Equal(5, hosts.StatHost.GetStat(Player, StatStrength));
            Assert.Equal(10, hosts.StatHost.GetStat(Player, StatArmor));
        }

        // -----------------------------------------------------------------
        // 验收标准 2：新存档往返——带品质 + 词缀的实例，存→读逐字段一致（不经过模板缺省解析这条
        // 兼容路径，验证的是 quality/affixes key 存在时的正常读写）。
        // -----------------------------------------------------------------

        [Fact]
        public void InventoryPersistable_RoundTrip_NewFormatWithQualityAndAffixes_PreservesExactFields()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);

            // 显式品质（item.quality.t7_rare）不同于模板自身品质（item.quality.t7_common）——确保
            // 断言的是"存档里写的是什么"，不是"模板缺省解析出了什么"（那是上面兼容读取组的职责）。
            var newFormatJson =
                "[{\"instance_id\": \"item.inst_new1\", \"template_id\": \"item.t7_chest_affix\", \"count\": 1," +
                " \"quality\": \"item.quality.t7_rare\", \"affixes\": [\"item.affix.t7_str\"]}]";

            new InventoryPersistable(Player, hosts.Inventory).Load(JsonReader.Parse(newFormatJson));

            var loadedOnce = hosts.Inventory.ListItems(Player);
            Assert.Single(loadedOnce);
            Assert.Equal(QualityRare, loadedOnce[0].Quality);
            Assert.Equal(new[] { AffixStr }, loadedOnce[0].Affixes);

            // 存→读往返：把刚加载的状态再 Save 一次，加载进一个全新宿主，逐字段核对与第一次加载
            // 完全一致（不是"巧合读出同一个值"，是序列化/反序列化路径本身保真）。
            var resaved = new InventoryPersistable(Player, hosts.Inventory).Save();
            var freshHosts = BuildHosts(registry);
            new InventoryPersistable(Player, freshHosts.Inventory).Load(resaved);

            var loadedTwice = freshHosts.Inventory.ListItems(Player);
            Assert.Single(loadedTwice);
            Assert.Equal(loadedOnce[0].InstanceId, loadedTwice[0].InstanceId);
            Assert.Equal(loadedOnce[0].TemplateId, loadedTwice[0].TemplateId);
            Assert.Equal(loadedOnce[0].Count, loadedTwice[0].Count);
            Assert.Equal(loadedOnce[0].Quality, loadedTwice[0].Quality);
            Assert.Equal(loadedOnce[0].Affixes, loadedTwice[0].Affixes);
        }

        [Fact]
        public void InventoryPersistable_Load_BadQualityValue_ThrowsFormatException()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);

            // quality key 存在但值非法（不是字符串/不是合法 Id）——与 instance_id/template_id/count
            // 坏值同一口径：抛 FormatException，不静默吞掉、不当成缺省处理。
            var badJson =
                "[{\"instance_id\": \"item.inst_bad1\", \"template_id\": \"item.t7_chest_affix\", \"count\": 1," +
                " \"quality\": 123}]";

            Assert.Throws<System.FormatException>(
                () => new InventoryPersistable(Player, hosts.Inventory).Load(JsonReader.Parse(badJson)));
        }

        // -----------------------------------------------------------------
        // 验收标准（阶段 N2 第 8 节验收标准 4）：装备一件带词缀的实例后存档再读档，StatHost 属性与
        // 存档前一致——同时验证 EquipmentHost.Equip 把 resolvedQuality/resolvedAffixes 写回存入
        // unitSlots 的实例（不这样做的话，Save 出的 quality/affixes 会与 StatHost 上实际生效的不
        // 一致，读档后重新反解就会得到不同的值）。
        // -----------------------------------------------------------------

        [Fact]
        public void Equip_WithAffix_ThenPersistAndReloadFreshHosts_StatHostMatchesPreSaveValues()
        {
            var registry = BuildRegistry();
            var original = BuildHosts(registry);

            original.Inventory.AddItem(Player, TemplateChestAffix, 1);
            var instanceId = original.Inventory.ListItems(Player)[0].InstanceId;

            var equipResult = original.Equipment.Equip(
                Player, instanceId, SlotChest,
                qualityId: QualityCommon,
                affixIds: new List<Id> { AffixStr });
            Assert.True(equipResult.Success);

            // 手算同 T_N2_5：模板 stats(5) + 词缀反解值(100) = 105；护甲 = 5 × 2.0 = 10。
            Assert.Equal(105, original.StatHost.GetStat(Player, StatStrength));
            Assert.Equal(10, original.StatHost.GetStat(Player, StatArmor));

            // 装备实例自己携带的身份字段必须与 StatHost 上实际生效的一致（见 EquipmentHost.Equip
            // 判断记录），否则下面的存档就会写出错误的 quality/affixes。
            var equippedBeforeSave = original.Equipment.GetAllEquippedInstances(Player)[SlotChest];
            Assert.Equal(QualityCommon, equippedBeforeSave.Quality);
            Assert.Equal(new[] { AffixStr }, equippedBeforeSave.Affixes);

            var savedInventory = new InventoryPersistable(Player, original.Inventory).Save();
            var savedEquipment = new EquipmentPersistable(Player, original.Inventory, original.Equipment).Save();

            var loaded = BuildHosts(registry);
            Assert.Equal(0, loaded.StatHost.GetStat(Player, StatStrength));
            Assert.Equal(0, loaded.StatHost.GetStat(Player, StatArmor));

            new InventoryPersistable(Player, loaded.Inventory).Load(savedInventory);
            new EquipmentPersistable(Player, loaded.Inventory, loaded.Equipment).Load(savedEquipment);

            // 读档后 StatHost 与存档前完全一致（不是从快照直接搬运——loaded.StatHost 是全新实例，
            // 此前恒为 0，必须重新走一遍 Equip→ApplyAffixValues 才可能变回 105/10）。
            Assert.Equal(105, loaded.StatHost.GetStat(Player, StatStrength));
            Assert.Equal(10, loaded.StatHost.GetStat(Player, StatArmor));

            var equippedAfterLoad = loaded.Equipment.GetAllEquippedInstances(Player)[SlotChest];
            Assert.Equal(instanceId, equippedAfterLoad.InstanceId);
            Assert.Equal(QualityCommon, equippedAfterLoad.Quality);
            Assert.Equal(new[] { AffixStr }, equippedAfterLoad.Affixes);
        }
    }
}
