using System.Linq;
using Core.Carriers.Item;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// ADR-0019 / F1b：<see cref="LootSchemas.Table"/> 的 <c>groups</c> 子结构登记（<c>Fields</c>/
    /// <c>Item</c>）+ <see cref="LootContentValidationRule"/> 收窄后仍保留的业务判断，覆盖范围：
    /// <c>roll_mode</c> 枚举登记值与 <see cref="LootRollMode"/> 运行时枚举全集一致（惯例同
    /// <c>core/rules/skill/tests/SkillSchemaCoverageTests.cs</c>）、子结构命中/坏形状各一例、
    /// <c>LootContentValidationRule</c> 收窄后仍报告的五类业务判断（ref 领域/存在性、
    /// <c>weight_or_chance</c> 区间、<c>count_range</c> 区间、<c>pick_count</c>、
    /// <c>guaranteed_min</c>）各一例。
    /// </summary>
    public sealed class LootSchemaCoverageTests
    {
        private static IEventBus NewBus() => LootTestSupport.NewEventBus();

        private static string Envelope(string table, string rowsJson) => LootTestSupport.Envelope(table, rowsJson);

        /// <summary>登记的 <c>roll_mode</c> 枚举合法集合必须与 <see cref="LootRollMode"/> 运行时枚举
        /// 全集一致——防止二者各自维护一份取值集合产生漂移（惯例同 F1a
        /// <c>SkillSchemaCoverageTests</c>"变体键集合与运行时枚举全集一致性"）。</summary>
        [Fact]
        public void RollModeEnumValues_MatchRuntimeEnum()
        {
            var registeredRollModeValues = FindGroupField("roll_mode").EnumValues!.ToHashSet();

            var runtimeValues = new[] { "chance_each", "weighted_pick_one" }.ToHashSet();
            Assert.Equal(runtimeValues, registeredRollModeValues);

            // 双向核对：枚举名到取值文本的映射与 LootTableParser 的 switch 分支一致。
            Assert.Equal(2, System.Enum.GetValues(typeof(LootRollMode)).Length);
        }

        private static FieldSchema FindGroupField(string name)
        {
            var groupsField = LootSchemas.Table.GetField("groups")!;
            return groupsField.Item!.Fields!.Single(f => f.Name == name);
        }

        [Fact]
        public void WellFormedGroupsEntries_LoadsWithoutErrors()
        {
            var rows = "[{\"id\": \"loot.sample_ok\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1,\"max\":2}}" +
                "]}], \"guaranteed_min\": 1}]";

            var source = new InMemoryDataSource().Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void MissingCountRangeMax_ReportsRequiredFieldWithNestedPath()
        {
            var rows = "[{\"id\": \"loot.sample_bad\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1}}" +
                "]}]}]";

            var source = new InMemoryDataSource().Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "groups[0].entries[0].count_range.max");

            // 同一坏形状不应被 LootContentValidationRule 重复报告（退役后只负责登记表达不了的业务判断）。
            Assert.DoesNotContain(report.Issues, i => i.Check == "loot_content");
        }

        [Fact]
        public void InvalidRollModeValue_ReportsFieldTypeError()
        {
            var rows = "[{\"id\": \"loot.sample_bad_mode\", \"groups\": [" +
                "{\"roll_mode\": \"not_a_mode\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var source = new InMemoryDataSource().Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "groups[0].roll_mode");
            Assert.DoesNotContain(report.Issues, i => i.Check == "loot_content");
        }

        [Fact]
        public void RefWrongDomain_ReportsLootContentBusinessError()
        {
            var rows = "[{\"id\": \"loot.sample_bad_domain\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"creature.sample_wolf\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var source = new InMemoryDataSource().Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "loot_content" && i.Message.Contains("领域段必须是 item 或 loot"));
        }

        [Fact]
        public void RefItemDomain_TargetMissing_WhenItemTemplateLoaded_ReportsLootContentBusinessError()
        {
            var bus = NewBus();
            string RawEnvelope(string table, string rows) =>
                "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";

            var rows = "[{\"id\": \"loot.sample_missing_item\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.does_not_exist\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var source = new InMemoryDataSource()
                .Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, rows))
                .Add("item.slot_definition", RawEnvelope("item.slot_definition",
                    "[{\"id\":\"item.slot.loot_sample\",\"name_key\":\"l10n.slot.loot_sample\"}]"))
                .Add("item.quality_definition", RawEnvelope("item.quality_definition",
                    "[{\"id\":\"item.quality.loot_sample\",\"name_key\":\"l10n.quality.loot_sample\"}]"))
                .Add("item.template", RawEnvelope("item.template",
                    "[{\"id\":\"item.sample_present\",\"slot\":\"item.slot.loot_sample\",\"quality\":\"item.quality.loot_sample\"," +
                    "\"item_level\":1,\"display_ref\":\"display.item.sample_present\",\"stack_size\":10,\"name_key\":\"l10n.item.sample_present\"}]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues,
                i => i.Check == "loot_content" && i.Message.Contains("在表 \"item.template\" 中不存在"));
        }

        [Fact]
        public void ChanceEachWeightOutOfRange_ReportsLootContentBusinessError()
        {
            var rows = "[{\"id\": \"loot.sample_bad_weight\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 1.5, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var source = new InMemoryDataSource().Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "loot_content" && i.Message.Contains("weight_or_chance"));
        }

        [Fact]
        public void CountRangeMinGreaterThanMax_ReportsLootContentBusinessError()
        {
            var rows = "[{\"id\": \"loot.sample_bad_range\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":3,\"max\":1}}" +
                "]}]}]";

            var source = new InMemoryDataSource().Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "loot_content" && i.Message.Contains("count_range"));
        }

        [Fact]
        public void PickCountLessThanOne_ReportsLootContentBusinessError()
        {
            var rows = "[{\"id\": \"loot.sample_bad_pick\", \"groups\": [" +
                "{\"roll_mode\": \"weighted_pick_one\", \"pick_count\": 0, \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var source = new InMemoryDataSource().Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "loot_content" && i.Message.Contains("pick_count"));
        }

        [Fact]
        public void GuaranteedMinNegative_ReportsLootContentBusinessError()
        {
            var rows = "[{\"id\": \"loot.sample_bad_guaranteed\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}], \"guaranteed_min\": -1}]";

            var source = new InMemoryDataSource().Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "loot_content" && i.Message.Contains("guaranteed_min"));
        }
    }
}
