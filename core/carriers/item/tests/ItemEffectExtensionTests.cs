using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Item
{
    public class ItemEffectExtensionTests
    {
        private const string SlotJson =
            "[{\"id\": \"item.slot.consumable\", \"name_key\": \"l10n.item.slot.consumable\"}]";

        private const string QualityJson =
            "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\"}]";

        private const string TemplateJson =
            "[{\"id\": \"item.sample_potion\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.sample_potion\", \"stack_size\": 20," +
            " \"name_key\": \"l10n.item.sample_potion\"}]";

        private static InventoryHost BuildInventory(out InMemoryItemDiagnostics diagnostics, InventoryOptions? options = null)
        {
            var registry = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.template", TestSupport.Table("item.template", TemplateJson));
            });

            diagnostics = new InMemoryItemDiagnostics();
            return new InventoryHost(registry, TestSupport.CreateBus(), options);
        }

        private static EffectContext CreateItemContext(Id target, string templateId, int? count = null)
        {
            var builder = new JsonObjectBuilder().Add("item_template", new JsonString(templateId));
            if (count.HasValue)
            {
                builder = builder.Add("count", new JsonNumber(count.Value));
            }

            return new EffectContext(
                sourceId: new Id("gobj.sample_chest"),
                targetId: target,
                skillId: new Id("skill.sample_open_chest"),
                kind: EffectKind.CreateItem,
                school: new Id("skill.school.physical"),
                baseValue: 0,
                coefficient: 0,
                @params: builder.Build());
        }

        [Fact]
        public void TryHandle_CreateItem_AddsToInventory_ReturnsTrue()
        {
            var inventory = BuildInventory(out var diagnostics);
            var extension = new ItemEffectExtension(inventory, diagnostics);
            var target = new Id("player.hero");

            var handled = extension.TryHandle(CreateItemContext(target, "item.sample_potion", 3), out var result);

            Assert.True(handled);
            Assert.Equal(3, result.FinalAmount);
            Assert.Equal(3, inventory.CountOf(target, new Id("item.sample_potion")));
        }

        [Fact]
        public void TryHandle_CreateItem_DefaultCountIsOne()
        {
            var inventory = BuildInventory(out var diagnostics);
            var extension = new ItemEffectExtension(inventory, diagnostics);
            var target = new Id("player.hero");

            extension.TryHandle(CreateItemContext(target, "item.sample_potion"), out _);

            Assert.Equal(1, inventory.CountOf(target, new Id("item.sample_potion")));
        }

        [Fact]
        public void TryHandle_NonCreateItemKind_ReturnsFalse()
        {
            var inventory = BuildInventory(out var diagnostics);
            var extension = new ItemEffectExtension(inventory, diagnostics);
            var context = new EffectContext(
                new Id("skill.sample_fireball"), new Id("player.hero"), new Id("skill.sample_fireball"),
                EffectKind.SchoolDamage, new Id("skill.school.fire"), 10, 1);

            var handled = extension.TryHandle(context, out _);

            Assert.False(handled);
        }

        [Fact]
        public void TryHandle_MissingItemTemplateParam_ReturnsFalse_AndWarns()
        {
            var inventory = BuildInventory(out var diagnostics);
            var extension = new ItemEffectExtension(inventory, diagnostics);
            var context = new EffectContext(
                new Id("gobj.sample_chest"), new Id("player.hero"), new Id("skill.sample_open_chest"),
                EffectKind.CreateItem, new Id("skill.school.physical"), 0, 0);

            var handled = extension.TryHandle(context, out _);

            Assert.False(handled);
            Assert.NotEmpty(diagnostics.Warnings);
        }

        [Fact]
        public void TryHandle_InventoryFull_ReturnsTrue_ButNotAdded_AndWarns()
        {
            var inventory = BuildInventory(out var diagnostics,
                new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Reject });
            var target = new Id("player.hero");
            inventory.AddItem(target, new Id("item.sample_potion"), 20); // 占满唯一格子

            var extension = new ItemEffectExtension(inventory, diagnostics);
            var handled = extension.TryHandle(CreateItemContext(target, "item.sample_potion", 5), out var result);

            Assert.True(handled);
            Assert.Equal(0, result.FinalAmount);
            Assert.NotEmpty(diagnostics.Warnings);
        }
    }
}
