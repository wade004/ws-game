using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    /// <summary><see cref="EquipmentWeaponStyleSource"/> 用例（ADR-0017 决策 e）。</summary>
    public class EquipmentWeaponStyleSourceTests
    {
        private static readonly Id Unit = new Id("unit.hero_1");
        private static readonly Id SwordTemplateId = new Id("item.sample_sword");

        private const string SwordDisplayRow = @"
        {
          ""id"": ""display.map.sample_sword"",
          ""category"": ""item"",
          ""logical_id"": ""item.sample_sword"",
          ""kind"": ""sprite"",
          ""sprite_set_id"": ""sprite.item.sample_sword"",
          ""direction_count"": 4,
          ""weapon_style_ref"": ""display.weapon_style.greatsword""
        }";

        private const string NoStyleDisplayRow = @"
        {
          ""id"": ""display.map.sample_dagger"",
          ""category"": ""item"",
          ""logical_id"": ""item.sample_dagger"",
          ""kind"": ""sprite"",
          ""sprite_set_id"": ""sprite.item.sample_dagger"",
          ""direction_count"": 4
        }";

        private static IEventBus CreateBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static IDisplayInfoRegistry BuildDisplayInfoRegistry(string row)
        {
            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + row + "]",
            });
            Assert.False(report.IsBlocking);
            return new DisplayInfoRegistry(registry, VfxSfxTestSupport.CreateBus());
        }

        [Fact]
        public void GetWeaponStyleRef_MainHandEquipped_ResolvesFromDisplayMap()
        {
            var bus = CreateBus();
            var displayInfoRegistry = BuildDisplayInfoRegistry(SwordDisplayRow);
            var source = new EquipmentWeaponStyleSource(bus, unitId => unitId == Unit ? SwordTemplateId : (Id?)null, displayInfoRegistry);

            Assert.Equal(new Id("display.weapon_style.greatsword"), source.GetWeaponStyleRef(Unit));
        }

        [Fact]
        public void GetWeaponStyleRef_NoMainHandEquipped_ReturnsNull()
        {
            var bus = CreateBus();
            var displayInfoRegistry = BuildDisplayInfoRegistry(SwordDisplayRow);
            var source = new EquipmentWeaponStyleSource(bus, _ => null, displayInfoRegistry);

            Assert.Null(source.GetWeaponStyleRef(Unit));
        }

        [Fact]
        public void GetWeaponStyleRef_TemplateWithoutWeaponStyleRef_ReturnsNull()
        {
            var bus = CreateBus();
            var displayInfoRegistry = BuildDisplayInfoRegistry(NoStyleDisplayRow);
            var source = new EquipmentWeaponStyleSource(bus, _ => new Id("item.sample_dagger"), displayInfoRegistry);

            Assert.Null(source.GetWeaponStyleRef(Unit));
        }

        [Fact]
        public void GetWeaponStyleRef_CachesResult_ResolverNotCalledAgain()
        {
            var bus = CreateBus();
            var displayInfoRegistry = BuildDisplayInfoRegistry(SwordDisplayRow);
            var callCount = 0;
            var source = new EquipmentWeaponStyleSource(bus, _ =>
            {
                callCount++;
                return SwordTemplateId;
            }, displayInfoRegistry);

            source.GetWeaponStyleRef(Unit);
            source.GetWeaponStyleRef(Unit);
            source.GetWeaponStyleRef(Unit);

            Assert.Equal(1, callCount);
        }

        [Fact]
        public void ItemEquippedEvent_InvalidatesCache_NextQueryReResolves()
        {
            var bus = CreateBus();
            var displayInfoRegistry = BuildDisplayInfoRegistry(SwordDisplayRow);
            Id? currentTemplate = SwordTemplateId;
            var source = new EquipmentWeaponStyleSource(bus, _ => currentTemplate, displayInfoRegistry);

            Assert.Equal(new Id("display.weapon_style.greatsword"), source.GetWeaponStyleRef(Unit));

            currentTemplate = null;
            bus.PublishImmediate(new ItemEquippedEvent(Unit, new Id("item_instance.sample_1"), new Id("item.slot.main_hand")));

            Assert.Null(source.GetWeaponStyleRef(Unit));
        }

        [Fact]
        public void ItemUnequippedEvent_InvalidatesCache_NextQueryReResolves()
        {
            var bus = CreateBus();
            var displayInfoRegistry = BuildDisplayInfoRegistry(SwordDisplayRow);
            Id? currentTemplate = null;
            var source = new EquipmentWeaponStyleSource(bus, _ => currentTemplate, displayInfoRegistry);

            Assert.Null(source.GetWeaponStyleRef(Unit));

            currentTemplate = SwordTemplateId;
            bus.PublishImmediate(new ItemUnequippedEvent(Unit, new Id("item.slot.main_hand"), new Id("item_instance.sample_1")));

            Assert.Equal(new Id("display.weapon_style.greatsword"), source.GetWeaponStyleRef(Unit));
        }

        [Fact]
        public void ItemEquippedEvent_ForDifferentUnit_DoesNotInvalidateOtherUnitsCache()
        {
            var bus = CreateBus();
            var displayInfoRegistry = BuildDisplayInfoRegistry(SwordDisplayRow);
            var callCount = 0;
            var source = new EquipmentWeaponStyleSource(bus, _ =>
            {
                callCount++;
                return SwordTemplateId;
            }, displayInfoRegistry);

            source.GetWeaponStyleRef(Unit);
            bus.PublishImmediate(new ItemEquippedEvent(new Id("unit.other"), new Id("item_instance.sample_2"), new Id("item.slot.main_hand")));
            source.GetWeaponStyleRef(Unit);

            Assert.Equal(1, callCount);
        }

        [Fact]
        public void Dispose_UnsubscribesFromBus_NoExceptionOnFurtherPublish()
        {
            var bus = CreateBus();
            var displayInfoRegistry = BuildDisplayInfoRegistry(SwordDisplayRow);
            var source = new EquipmentWeaponStyleSource(bus, _ => SwordTemplateId, displayInfoRegistry);

            source.Dispose();

            var ex = Record.Exception(() =>
                bus.PublishImmediate(new ItemEquippedEvent(Unit, new Id("item_instance.sample_1"), new Id("item.slot.main_hand"))));
            Assert.Null(ex);
        }
    }
}
