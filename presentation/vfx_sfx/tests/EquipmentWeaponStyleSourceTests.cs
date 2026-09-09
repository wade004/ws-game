using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
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
        public void PRES110_01_SaveLoadedEvent_ClearsCache_NextQueryReResolvesRealState()
        {
            // PRES-110-01 复现与根治验证：EquipmentPersistable.Load 在 SaveSystem.Load 的
            // SuppressDispatch 抑制作用域内调用真实 EquipmentHost.Equip/Unequip，其正常派发的
            // ItemEquipped/ItemUnequipped 在作用域内被丢弃——本用例不经过抑制作用域，直接模拟"缓存已
            // 命中旧值，随后底层解析结果已经真实变化，但没有任何 item.equipped/unequipped 事件到达"
            // 这一后果，验证 save.loaded（在抑制作用域外正常派发）能让下一次查询绕过陈旧缓存。
            var bus = CreateBus();
            var displayInfoRegistry = BuildDisplayInfoRegistry(SwordDisplayRow);
            Id? currentTemplate = SwordTemplateId;
            var source = new EquipmentWeaponStyleSource(bus, _ => currentTemplate, displayInfoRegistry);

            Assert.Equal(new Id("display.weapon_style.greatsword"), source.GetWeaponStyleRef(Unit));

            // 底层真实状态已经变化（同图读档后 EquipmentHost 的装备已是另一件），但装备事件被
            // SaveSystem.Load 的抑制作用域丢弃，没有任何 item.equipped/unequipped 到达本类型。
            currentTemplate = null;

            // 抑制作用域内的事件被丢弃：不发 ItemEquippedEvent/ItemUnequippedEvent，只在抑制作用域
            // 外正常派发 save.loaded（同 SaveSystem.Load 判断记录）。
            bus.PublishImmediate(new SaveLoadedEvent(new Id("slot.pres110_01")));

            Assert.Null(source.GetWeaponStyleRef(Unit));
        }

        [Fact]
        public void PRES110_01_RepeatedSaveLoadedEvents_NeverReuseStaleValueAcrossReloads()
        {
            // 验收口径"重复读档不复用 A"：连续两次 save.loaded（对应连续两次同图读档）之间，缓存都必须
            // 让下一次查询重新解析真实状态，不允许第二次读档复用第一次读档后缓存的值。
            var bus = CreateBus();
            var displayInfoRegistry = BuildDisplayInfoRegistry(SwordDisplayRow);
            Id? currentTemplate = SwordTemplateId; // A
            var source = new EquipmentWeaponStyleSource(bus, _ => currentTemplate, displayInfoRegistry);

            Assert.Equal(new Id("display.weapon_style.greatsword"), source.GetWeaponStyleRef(Unit)); // A 缓存命中

            currentTemplate = null; // B（此处用"无风格"模板代表另一件武器，B 状态）
            bus.PublishImmediate(new SaveLoadedEvent(new Id("slot.pres110_01_b")));
            Assert.Null(source.GetWeaponStyleRef(Unit)); // 读档 1 后应为 B，不应是缓存的 A

            currentTemplate = SwordTemplateId; // 读档 2 恢复回 A
            bus.PublishImmediate(new SaveLoadedEvent(new Id("slot.pres110_01_a_again")));
            Assert.Equal(new Id("display.weapon_style.greatsword"), source.GetWeaponStyleRef(Unit)); // 读档 2 后应为 A，不应复用读档 1 缓存的 B
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
