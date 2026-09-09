using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// ADR-0019 / F1b（见 <c>QuestSchemas.cs</c> 类型顶部判断记录、
    /// <c>schema/quest.def.md</c>"子结构登记表"一节）：
    /// <list type="bullet">
    /// <item><c>QuestSchemas.Def.objectives</c> 的 <c>VariantSchema.Cases</c> 键集合必须与
    /// <see cref="QuestObjectiveTypes.EnumValues"/> 全集一致（做法同
    /// <c>Tests.Rules.Skill.SkillSchemaCoverageTests</c>）。</item>
    /// <item><c>QuestSchemas.RewardsFields</c> 覆盖 <c>RewardBundle</c> 的六个已知子字段。</item>
    /// <item>子结构命中/坏形状各至少一例（<c>objectives</c>/<c>rewards</c> 各若干），以及
    /// <c>QuestContentValidationRule</c> 收窄后仍保留的业务判断（数值范围/等值约束、
    /// <c>kill</c>/<c>escort</c> domain 弱校验）各至少一例。</item>
    /// </list>
    /// </summary>
    public sealed class QuestSchemaCoverageTests
    {
        // -----------------------------------------------------------------
        // 变体键集合一致性
        // -----------------------------------------------------------------

        [Fact]
        public void ObjectiveVariantKeys_MatchQuestObjectiveTypesFullSet()
        {
            var objectivesField = QuestSchemas.Def.GetField("objectives");
            Assert.NotNull(objectivesField);
            var variants = objectivesField!.Item!.Variants;
            Assert.NotNull(variants);

            var registered = new HashSet<string>(variants!.Cases.Keys, StringComparer.Ordinal);
            var expected = new HashSet<string>(QuestObjectiveTypes.EnumValues, StringComparer.Ordinal);

            Assert.Equal(expected, registered);
            Assert.Equal("type", variants.Discriminator);
        }

        [Fact]
        public void RewardsFields_ContainsAllSixKnownFieldNames()
        {
            var names = new HashSet<string>(QuestSchemas.RewardsFields.Select(f => f.Name), StringComparer.Ordinal);
            var expected = new HashSet<string> { "items", "xp", "currency", "skills", "world_flags", "talent_points" };

            Assert.Equal(expected, names);
        }

        // -----------------------------------------------------------------
        // DataRegistry 装配：quest.def + 最小 item.template 三表（供 collect Reference 用例）
        // -----------------------------------------------------------------

        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static string ItemTemplateRow(string id) =>
            "{\"id\": \"" + id + "\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\", " +
            "\"item_level\": 1, \"display_ref\": \"display.item.placeholder\", \"stack_size\": 1, " +
            "\"name_key\": \"l10n." + id + "\"}";

        /// <summary>装配一个已注册 <c>quest.def</c>（+ <c>QuestContentValidationRule</c>）与最小
        /// <c>item.*</c> 三表的 <see cref="DataRegistry"/>；<paramref name="questRowsJson"/> 是
        /// <c>quest.def</c> 的 <c>rows</c> JSON 数组文本，<paramref name="extraItemIds"/> 是额外要
        /// 登记进 <c>item.template</c> 的 id（供 <c>collect</c>/<c>rewards.items</c> 的 Reference
        /// 命中用例引用）。</summary>
        private static ValidationReport Load(string questRowsJson, params string[] extraItemIds)
        {
            var itemRows = "[" + string.Join(",", extraItemIds.Select(ItemTemplateRow)) + "]";
            var source = new InMemoryDataSource()
                .Add("quest.def", Envelope("quest.def", questRowsJson))
                .Add("item.template", Envelope("item.template", itemRows))
                .Add("item.slot_definition", Envelope("item.slot_definition",
                    "[{\"id\": \"item.slot.consumable\", \"name_key\": \"l10n.slot.consumable\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition",
                    "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.quality.common\"}]"));

            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions
            {
                FailOnUnknownTable = false,
                ExprSchema = QuestExprSchemaEntries.BuildParsingSchema(),
            });
            registry.RegisterSchema(QuestSchemas.Def);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Template);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.SlotDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.QualityDefinition);
            registry.RegisterValidationRule(new QuestContentValidationRule());
            return registry.LoadAll();
        }

        private const string MinimalQuestHeader =
            "\"id\": \"quest.cov_a\", \"title_key\": \"l10n.quest.cov_a.title\", " +
            "\"start_method\": \"npc_gossip\", \"turn_in_method\": \"npc_gossip\", \"repeatable\": \"none\"";

        // -----------------------------------------------------------------
        // objectives：命中 / 坏形状
        // -----------------------------------------------------------------

        [Fact]
        public void Objectives_CollectWithExistingItemReference_Passes()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"collect\", \"target_ref\": \"item.cov_token\", \"count\": 1}]}]";

            var report = Load(rows, "item.cov_token");

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Objectives_CollectWithMissingItemReference_ReportsReferenceIntegrity()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"collect\", \"target_ref\": \"item.cov_missing\", \"count\": 1}]}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "objectives[0].target_ref");
        }

        [Fact]
        public void Objectives_UnknownType_ReportsVariantDiscriminator()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"not_a_type\", \"target_ref\": \"item.cov_x\", \"count\": 1}]}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator" && i.Field == "objectives[0].type");
        }

        [Fact]
        public void Objectives_ExploreCountNotOne_ReportsObjectiveCountMustBeOne()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"explore\", \"target_ref\": \"area.cov_zone\", \"count\": 2}]}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "objective_count_must_be_one" && i.Field == "objectives[0].count");
        }

        [Fact]
        public void Objectives_KillCountZero_ReportsObjectiveCountPositive()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"kill\", \"target_ref\": \"creature.cov_wolf\", \"count\": 0}]}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "objective_count_positive" && i.Field == "objectives[0].count");
        }

        /// <summary><c>kill</c> 的 <c>target_ref</c> 退回 Id（不做存在性检查）——一个 domain 正确、但
        /// 从未登记进 <c>creature.template</c> 的 id 必须能通过校验（呼应
        /// <c>GameplayAssemblyOwnerDayVendorExtensionPointTests</c> 依赖的这条行为，见
        /// <c>QuestSchemas.ObjectiveItemSchema</c> 判断记录）。</summary>
        [Fact]
        public void Objectives_KillTargetRefNotBackedByCreatureTable_StillPasses()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"kill\", \"target_ref\": \"creature.cov_unregistered\", \"count\": 1}]}]";

            var report = Load(rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Objectives_KillTargetRefWrongDomain_ReportsObjectiveTargetDomainMismatch()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"kill\", \"target_ref\": \"item.cov_not_a_creature\", \"count\": 1}]}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "objective_target_domain_mismatch" && i.Field == "objectives[0].target_ref");
        }

        [Fact]
        public void Objectives_EmptyArray_ReportsObjectivesMinCount()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": []}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "objectives_min_count" && i.Field == "objectives");
        }

        [Fact]
        public void Objectives_CollectParamConsumeOnProgressWrongType_ReportsFieldType()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"collect\", \"target_ref\": \"item.cov_token\", \"count\": 1, " +
                "\"param\": {\"consume_on_progress\": \"not_a_bool\"}}]}]";

            var report = Load(rows, "item.cov_token");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "objectives[0].param.consume_on_progress");
        }

        [Fact]
        public void Objectives_EventEventFilterUnparsable_ReportsExprParsable()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"event\", \"target_ref\": \"test.cov_event\", \"count\": 1, " +
                "\"param\": {\"eventFilter\": \"(( bad\"}}]}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "expr_parsable" && i.Field == "objectives[0].param.eventFilter");
        }

        // -----------------------------------------------------------------
        // rewards：命中 / 坏形状
        // -----------------------------------------------------------------

        [Fact]
        public void Rewards_WellFormed_Passes()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"kill\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"items\": [{\"itemId\": \"item.cov_reward\", \"count\": 1}], \"xp\": 10}}]";

            var report = Load(rows, "item.cov_reward");

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Rewards_ItemIdNotBackedByItemTemplate_ReportsReferenceIntegrity()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"kill\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"items\": [{\"itemId\": \"item.cov_missing_reward\", \"count\": 1}]}}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "rewards.items[0].itemId");
        }

        [Fact]
        public void Rewards_ItemCountZero_ReportsRewardItemCountPositive()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"kill\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"items\": [{\"itemId\": \"item.cov_reward\", \"count\": 0}]}}]";

            var report = Load(rows, "item.cov_reward");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reward_item_count_positive" && i.Field == "rewards.items[0].count");
        }

        [Fact]
        public void Rewards_NegativeXp_ReportsRewardXpNonNegative()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"kill\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"xp\": -5}}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reward_xp_non_negative" && i.Field == "rewards.xp");
        }

        [Fact]
        public void Rewards_NegativeTalentPoints_ReportsRewardTalentPointsNonNegative()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"kill\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"talent_points\": -1}}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reward_talent_points_non_negative" && i.Field == "rewards.talent_points");
        }

        [Fact]
        public void Rewards_WorldFlagMissingValue_ReportsRewardWorldFlagValueRequired()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"kill\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"world_flags\": [{\"flagKey\": \"world.cov_flag\"}]}}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reward_world_flag_value_required" && i.Field == "rewards.world_flags[0].value");
        }

        [Fact]
        public void Rewards_WorldFlagWithValue_Passes()
        {
            var rows = "[{" + MinimalQuestHeader + ", \"objectives\": [" +
                "{\"type\": \"kill\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"world_flags\": [{\"flagKey\": \"world.cov_flag\", \"value\": true}]}}]";

            var report = Load(rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }
    }
}
