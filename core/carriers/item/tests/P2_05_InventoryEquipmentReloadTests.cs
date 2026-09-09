using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// P2-05 关联根治回归测试（外部审计 audit-c9ff301-20260909）：<see cref="InventoryHost"/> 的
    /// <c>item.template</c> 缓存与 <see cref="EquipmentHost"/> 的 <c>item.slot_definition</c>/
    /// <c>item.set</c> 缓存此前只在构造期从 registry 读取一次，从不订阅
    /// <see cref="DataLoadCompletedEvent"/>，reload 后 resident host 继续用旧模板/槽位定义——同
    /// <c>Core.Rules.Skill.SkillDefCache</c> 一类问题。本文件不复用 <see cref="TestSupport.BuildRegistry"/>
    /// （内部用不可变 <see cref="InMemoryDataSource"/>，同名表二次 Add 会被当成多根合并处理），改用
    /// 自定义可覆写 <see cref="IDataSource"/>，其余夹具（<see cref="FakeEffectSink"/>/
    /// <see cref="FakeUnitAccess"/>/<see cref="RecordingSkillGranter"/>）复用本目录既有假实现。
    /// </summary>
    public sealed class P2_05_InventoryEquipmentReloadTests
    {
        private sealed class MutableSource : IDataSource
        {
            private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);
            public MutableSource Add(string table, string text) { _texts[table] = text; return this; }
            public void Replace(string table, string text) => _texts[table] = text;
            public IReadOnlyList<DataTableSource> ListTables()
            {
                var result = new List<DataTableSource>();
                foreach (var pair in _texts)
                {
                    var table = pair.Key;
                    result.Add(new DataTableSource(table, "memory://" + table, () => _texts[table]));
                }
                return result;
            }
        }

        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                new EventDefinition(CarriersEventKeys.ItemAdded, "item",
                    new[] { "unitId", "itemInstanceId", "itemTemplateId", "count" }),
                new EventDefinition(CarriersEventKeys.ItemRemoved, "item",
                    new[] { "unitId", "itemInstanceId", "count", "reason" }),
                new EventDefinition(CarriersEventKeys.ItemEquipped, "item",
                    new[] { "unitId", "itemInstanceId", "slot" }),
                new EventDefinition(CarriersEventKeys.ItemUnequipped, "item",
                    new[] { "unitId", "slot", "itemInstanceId" }),
                new EventDefinition(StatBlockEventKeys.StatChanged, "stat",
                    new[] { "unitId", "stat", "oldValue", "newValue" }),
            });
            return new EventBus(catalog);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static string TemplateRow(int stackSize) =>
            "[{\"id\": \"item.p2_05_potion\", \"slot\": \"item.slot.p2_05\", \"quality\": \"item.quality.p2_05\", " +
            "\"item_level\": 1, \"display_ref\": \"display.item.p2_05_potion\", \"stack_size\": " + stackSize + ", " +
            "\"name_key\": \"l10n.item.p2_05_potion.name\"}]";

        private const string SlotRow = "[{\"id\": \"item.slot.p2_05\", \"name_key\": \"l10n.item.slot.p2_05\"}]";
        private const string QualityRow = "[{\"id\": \"item.quality.p2_05\", \"name_key\": \"l10n.item.quality.p2_05\"}]";

        private static DataRegistry MakeRegistry(MutableSource source, IEventBus bus)
        {
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(ItemSchemas.Set);
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(Core.Rules.Skill.SkillSchemas.Def);
            registry.RegisterSchema(Core.Rules.Skill.SkillSchemas.AuraDef);
            return registry;
        }

        [Fact]
        public void P2_05_InventoryHost_Reload_PicksUpNewStackSize_AfterDataLoadCompleted()
        {
            var bus = MakeBus();
            var source = new MutableSource()
                .Add("item.template", Envelope("item.template", TemplateRow(5)))
                .Add("item.slot_definition", Envelope("item.slot_definition", SlotRow))
                .Add("item.quality_definition", Envelope("item.quality_definition", QualityRow))
                .Add("item.set", Envelope("item.set", "[]"))
                .Add("stat.definition", Envelope("stat.definition", "[]"))
                .Add("skill.def", Envelope("skill.def", "[]"))
                .Add("skill.aura_def", Envelope("skill.aura_def", "[]"));
            var registry = MakeRegistry(source, bus);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var inventory = new InventoryHost(registry, bus);
            var unit = new Id("unit.p2_05_player");
            inventory.RegisterUnit(unit);

            Assert.True(inventory.AddItem(unit, new Id("item.p2_05_potion"), 5));

            source.Replace("item.template", Envelope("item.template", TemplateRow(1)));
            var reload = registry.Reload("item.template");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));

            // reload 后 stack_size 收窄为 1：继续加 1 个必须开新堆叠而不是合并进已有的 5 个堆叠
            // （AddItemCore 读 _templates 里的 stack_size 决定是否能继续叠加，见该方法判断记录）。
            Assert.True(inventory.AddItem(unit, new Id("item.p2_05_potion"), 1));
            var items = inventory.ListItems(unit);
            Assert.Equal(2, items.Count);
        }

        private static string EquipmentTemplateRow() =>
            "[{\"id\": \"item.p2_05_gear\", \"slot\": \"item.slot.p2_05\", \"quality\": \"item.quality.p2_05\", " +
            "\"item_level\": 1, \"display_ref\": \"display.item.p2_05_gear\", \"stack_size\": 1, " +
            "\"name_key\": \"l10n.item.p2_05_gear.name\"}]";

        private static string SlotRowWithIsEquipment(bool isEquipment) =>
            "[{\"id\": \"item.slot.p2_05\", \"name_key\": \"l10n.item.slot.p2_05\", \"is_equipment\": " +
            (isEquipment ? "true" : "false") + "}]";

        [Fact]
        public void P2_05_EquipmentHost_Reload_PicksUpNewSlotDefinition_AfterDataLoadCompleted()
        {
            var bus = MakeBus();
            var source = new MutableSource()
                .Add("item.template", Envelope("item.template", EquipmentTemplateRow()))
                .Add("item.slot_definition", Envelope("item.slot_definition", SlotRowWithIsEquipment(true)))
                .Add("item.quality_definition", Envelope("item.quality_definition", QualityRow))
                .Add("item.set", Envelope("item.set", "[]"))
                .Add("stat.definition", Envelope("stat.definition", "[]"))
                .Add("skill.def", Envelope("skill.def", "[]"))
                .Add("skill.aura_def", Envelope("skill.aura_def", "[]"));
            var registry = MakeRegistry(source, bus);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var inventory = new InventoryHost(registry, bus);
            var statHost = new StatHost(registry, bus);
            var effectSink = new FakeEffectSink();
            var skillGranter = new RecordingSkillGranter();
            var player = new Id("unit.p2_05_player");
            var unitAccess = new FakeUnitAccess().Add(player, 1);
            statHost.RegisterUnit(player);

            var equipment = new EquipmentHost(
                registry, bus, inventory, statHost, effectSink, skillGranter.Grant, unitAccess);

            // reload 前 is_equipment=true（默认）：可以正常装备。
            inventory.AddItem(player, new Id("item.p2_05_gear"), 1);
            var instanceBefore = inventory.ListItems(player)[0].InstanceId;
            var before = equipment.Equip(player, instanceBefore, new Id("item.slot.p2_05"));
            Assert.True(before.Success);
            equipment.Unequip(player, new Id("item.slot.p2_05"));

            source.Replace("item.slot_definition", Envelope("item.slot_definition", SlotRowWithIsEquipment(false)));
            var reload = registry.Reload("item.slot_definition");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));

            // reload 后 is_equipment=false：同一个槽位、同一个模板不应再能装备
            // （IsEquipmentSlot 判断记录"is_equipment=false 的槽位是分类桶……即便 slot 匹配也不可经
            // Equip 装备"，与 EquipmentHostTests.Equip_NonEquipmentBucketSlot_Fails 同一断言）。
            inventory.AddItem(player, new Id("item.p2_05_gear"), 1);
            var instanceAfter = inventory.ListItems(player)[0].InstanceId;
            var after = equipment.Equip(player, instanceAfter, new Id("item.slot.p2_05"));
            Assert.False(after.Success);
            Assert.Equal(EquipFailureReason.SlotMismatch, after.Reason);
        }
    }
}
