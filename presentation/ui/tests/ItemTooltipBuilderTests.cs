using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    /// <summary>
    /// 物品提示框内容（ADR-0152）：名称/品质/属性行全部由 item.template 数据算出，只列数据里真的有的项；
    /// 拖拽合法性（物品模板的槽位 = 目标槽位）同取自数据。
    /// </summary>
    public sealed class ItemTooltipBuilderTests
    {
        private const int DamageMin = 5;
        private const int DamageMax = 8;
        private const double Speed = 1.5;
        private const int ItemLevel = 7;

        private static IDataRegistryView BuildRegistry()
        {
            var world = new UiWorldFixture();
            var source = new InMemoryDataSource()
                .Add("item.slot_definition",
                    "{\"table\":\"item.slot_definition\",\"schema_version\":1,\"rows\":[" +
                    "{\"id\":\"item.slot.main_hand\",\"name_key\":\"l10n.slot.main_hand\",\"sort_weight\":1,\"is_weapon\":true,\"is_equipment\":true}," +
                    "{\"id\":\"item.slot.chest\",\"sort_weight\":11,\"is_weapon\":false,\"is_equipment\":true}]}")
                .Add("item.quality_definition",
                    "{\"table\":\"item.quality_definition\",\"schema_version\":1,\"rows\":[" +
                    "{\"id\":\"item.quality.std_rare\",\"name_key\":\"l10n.quality.rare\",\"sort_weight\":2}]}")
                .Add("item.template",
                    "{\"table\":\"item.template\",\"schema_version\":1,\"rows\":[" +
                    "{\"id\":\"item.sword\",\"slot\":\"item.slot.main_hand\",\"quality\":\"item.quality.std_rare\",\"item_level\":" + ItemLevel +
                    ",\"name_key\":\"l10n.item.sword\",\"weapon_profile\":{\"damage_min\":" + DamageMin + ",\"damage_max\":" + DamageMax + ",\"speed\":1.5}," +
                    "\"requirements\":{\"level\":3}}," +
                    "{\"id\":\"item.plate\",\"slot\":\"item.slot.chest\",\"quality\":\"item.quality.std_rare\",\"item_level\":2," +
                    "\"stats\":[{\"stat\":\"stat.armor\",\"op\":\"flat\",\"value\":12},{\"stat\":\"stat.haste\",\"op\":\"pct\",\"value\":5},{\"stat\":\"stat.power\",\"op\":\"mult\",\"value\":1.25}]}]}");
            var registry = new DataRegistry(source, world.EventBus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.LoadAll();
            return registry;
        }

        private static string Text(Id key) => "T:" + key.Value;

        [Fact]
        public void Build_Weapon_ListsEveryDeclaredProperty_InOrder_AndLocalizesNames()
        {
            var content = ItemTooltipBuilder.Build(BuildRegistry(), new Id("item.sword"), Text)!;

            Assert.Equal("T:l10n.item.sword", content.Name);
            Assert.Equal("std_rare", content.QualityShortName);
            Assert.Equal("T:l10n.quality.rare", content.QualityText);
            Assert.Equal("T:l10n.slot.main_hand", content.SlotText);
            Assert.Equal(
                new[]
                {
                    ItemTooltipBuilder.LabelSlot + "=T:l10n.slot.main_hand",
                    ItemTooltipBuilder.LabelItemLevel + "=" + ItemLevel,
                    ItemTooltipBuilder.LabelDamage + "=" + DamageMin + " - " + DamageMax,
                    ItemTooltipBuilder.LabelSpeed + "=1.5",
                    ItemTooltipBuilder.LabelRequiredLevel + "=3",
                },
                content.Rows.Select(r => r.Label + "=" + r.Value).ToArray());
        }

        [Fact]
        public void Build_Armor_HasNoDamageRows_AndFormatsEachStatOperation()
        {
            var content = ItemTooltipBuilder.Build(BuildRegistry(), new Id("item.plate"), null)!;

            // 不变量：没有 weapon_profile 的物品不出现伤害/攻速行；没有等级需求不出现需求行。
            Assert.DoesNotContain(content.Rows, r => r.Label == ItemTooltipBuilder.LabelDamage || r.Label == ItemTooltipBuilder.LabelSpeed || r.Label == ItemTooltipBuilder.LabelRequiredLevel);
            Assert.Equal("plate", content.Name);                       // 没有 name_key 也没有 text 回调：退回模板短名
            Assert.Equal("chest", content.SlotText);                   // 槽位没有 name_key：退回槽位短名
            var stats = content.Rows.Where(r => r.Label == "armor" || r.Label == "haste" || r.Label == "power").ToDictionary(r => r.Label, r => r.Value);
            Assert.Equal("+12", stats["armor"]);
            Assert.Equal("+5%", stats["haste"]);
            Assert.Equal("x1.25", stats["power"]);
        }

        [Fact]
        public void Build_UnknownTemplate_ReturnsNull()
        {
            Assert.Null(ItemTooltipBuilder.Build(BuildRegistry(), new Id("item.nope"), Text));
        }

        [Fact]
        public void FitsSlot_IsTrueOnlyForTheTemplatesOwnSlot()
        {
            var registry = BuildRegistry();

            Assert.True(ItemTooltipBuilder.FitsSlot(registry, new Id("item.sword"), new Id("item.slot.main_hand")));
            Assert.False(ItemTooltipBuilder.FitsSlot(registry, new Id("item.sword"), new Id("item.slot.chest")));
            Assert.True(ItemTooltipBuilder.FitsSlot(registry, new Id("item.plate"), new Id("item.slot.chest")));
            Assert.False(ItemTooltipBuilder.FitsSlot(registry, new Id("item.nope"), new Id("item.slot.chest")));
        }
    }
}
