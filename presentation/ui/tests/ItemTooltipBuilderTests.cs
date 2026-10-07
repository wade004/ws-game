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
                    Text(ItemTooltipBuilder.KeySlot) + "=T:l10n.slot.main_hand",
                    Text(ItemTooltipBuilder.KeyItemLevel) + "=" + ItemLevel,
                    Text(ItemTooltipBuilder.KeyDamage) + "=" + DamageMin + " - " + DamageMax,
                    Text(ItemTooltipBuilder.KeySpeed) + "=1.5",
                    Text(ItemTooltipBuilder.KeyRequiredLevel) + "=3",
                },
                content.Rows.Select(r => r.Label + "=" + r.Value).ToArray());
        }

        /// <summary>P4 备忘 1（红：此前行标签是写死的中文常量，英文界面里混出中文）：行标签经本地化键取文案；取不到（没传文本函数、文本函数返回空串）退回中文常量，
        /// 既有不带本地化表的调用方不受影响；五个键互不相同，且都不与属性名键冲突。</summary>
        [Fact]
        public void Build_RowLabels_UseL10nKeys_AndFallBackToChineseConstantsWhenMissing()
        {
            var registry = BuildRegistry();
            var english = new System.Collections.Generic.Dictionary<Id, string>
            {
                [ItemTooltipBuilder.KeySlot] = "Slot",
                [ItemTooltipBuilder.KeyItemLevel] = "Item level",
                [ItemTooltipBuilder.KeyDamage] = "Damage",
                [ItemTooltipBuilder.KeySpeed] = "Speed",
                [ItemTooltipBuilder.KeyRequiredLevel] = "Requires level",
            };

            var localized = ItemTooltipBuilder.Build(registry, new Id("item.sword"), key => english.TryGetValue(key, out var v) ? v : string.Empty)!;
            Assert.Equal(new[] { "Slot", "Item level", "Damage", "Speed", "Requires level" }, localized.Rows.Select(r => r.Label).ToArray());

            var fallback = ItemTooltipBuilder.Build(registry, new Id("item.sword"), null)!;
            Assert.Equal(
                new[] { ItemTooltipBuilder.LabelSlot, ItemTooltipBuilder.LabelItemLevel, ItemTooltipBuilder.LabelDamage, ItemTooltipBuilder.LabelSpeed, ItemTooltipBuilder.LabelRequiredLevel },
                fallback.Rows.Select(r => r.Label).ToArray());

            var keys = new[] { ItemTooltipBuilder.KeySlot, ItemTooltipBuilder.KeyItemLevel, ItemTooltipBuilder.KeyDamage, ItemTooltipBuilder.KeySpeed, ItemTooltipBuilder.KeyRequiredLevel };
            Assert.Equal(keys.Length, keys.Distinct().Count());
        }

        [Fact]
        public void ItemName_AndSlotText_AreTheSingleNamingPath_BackpackLabelTooltipTitleAndSlotLabelAgree()
        {
            var registry = BuildRegistry();

            // 复现：背包行标签与提示框标题曾各取各的名（一处显示 id 短名，一处显示本地化名）；现在都走 ItemName / SlotText，同一输入同一输出。
            foreach (var id in new[] { "item.sword", "item.plate" })
            {
                var template = new Id(id);
                Assert.Equal(ItemTooltipBuilder.ItemName(registry, template, Text), ItemTooltipBuilder.Build(registry, template, Text)!.Name);
                Assert.Equal(ItemTooltipBuilder.ItemName(registry, template, null), ItemTooltipBuilder.Build(registry, template, null)!.Name);
            }

            Assert.Equal("T:l10n.item.sword", ItemTooltipBuilder.ItemName(registry, new Id("item.sword"), Text));
            Assert.Equal("T:l10n.slot.main_hand", ItemTooltipBuilder.SlotText(registry, new Id("item.slot.main_hand"), Text));
            Assert.Equal(ItemTooltipBuilder.Build(registry, new Id("item.sword"), Text)!.SlotText, ItemTooltipBuilder.SlotText(registry, new Id("item.slot.main_hand"), Text));

            // 不变量：缺显示名才回落 id 短名——没有文本函数、没有 name_key、文本函数返回空、没有数据行，四种缺法都回落，不抛异常。
            Assert.Equal("sword", ItemTooltipBuilder.ItemName(registry, new Id("item.sword"), null));
            Assert.Equal("sword", ItemTooltipBuilder.ItemName(registry, new Id("item.sword"), _ => string.Empty));
            Assert.Equal("plate", ItemTooltipBuilder.ItemName(registry, new Id("item.plate"), Text));
            Assert.Equal("unknown", ItemTooltipBuilder.ItemName(registry, new Id("item.unknown"), Text));
            Assert.Equal("unknown", ItemTooltipBuilder.ItemName(null, new Id("item.unknown"), Text));
            Assert.Equal("main_hand", ItemTooltipBuilder.SlotText(registry, new Id("item.slot.main_hand"), null));
            Assert.Equal("main_hand", ItemTooltipBuilder.SlotText(registry, new Id("item.slot.main_hand"), _ => string.Empty));
            Assert.Equal("chest", ItemTooltipBuilder.SlotText(registry, new Id("item.slot.chest"), Text));
            Assert.Equal("ghost", ItemTooltipBuilder.SlotText(registry, new Id("item.slot.ghost"), Text));
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
