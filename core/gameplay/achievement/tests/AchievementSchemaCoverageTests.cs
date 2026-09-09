using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Gameplay.Achievement;
using Xunit;

namespace Tests.Gameplay.Achievement
{
    /// <summary>
    /// ADR-0019 / F1b（见 <c>AchievementSchemas.cs</c> 类型顶部判断记录、
    /// <c>schema/README.md</c>"子结构登记表"一节）：
    /// <list type="bullet">
    /// <item><c>AchievementSchemas.CriterionItemSchema</c> 的 <c>VariantSchema.Cases</c> 键集合必须
    /// 与 <see cref="CriterionTypeIds.AllValues"/> 全集一致（做法同
    /// <c>Tests.Rules.Skill.SkillSchemaCoverageTests</c>）。</item>
    /// <item>子结构命中/坏形状各至少一例（<c>criteria</c>/<c>rewards</c> 各若干），以及
    /// <c>AchievementContentValidationRule</c> 收窄/新增后的业务判断（<c>criteria</c> 最小长度、
    /// <c>observe_event</c> 目录成员资格、<c>count</c> 数值范围、<c>rewards</c> 数值范围）各至少一例。</item>
    /// </list>
    /// </summary>
    public sealed class AchievementSchemaCoverageTests
    {
        // -----------------------------------------------------------------
        // 变体键集合一致性
        // -----------------------------------------------------------------

        [Fact]
        public void CriterionVariantKeys_MatchCriterionTypeIdsFullSet()
        {
            var criteriaField = AchievementSchemas.Def.GetField("criteria");
            Assert.NotNull(criteriaField);
            var variants = criteriaField!.Item!.Variants;
            Assert.NotNull(variants);

            var registered = new HashSet<string>(variants!.Cases.Keys, StringComparer.Ordinal);
            var expected = new HashSet<string>(CriterionTypeIds.AllValues, StringComparer.Ordinal);

            Assert.Equal(expected, registered);
            Assert.Equal("type", variants.Discriminator);
        }

        [Fact]
        public void RewardsFields_ReusesQuestSchemasRewardsFieldsInstance()
        {
            var rewardsField = AchievementSchemas.Def.GetField("rewards");
            Assert.NotNull(rewardsField);

            // 直接复用同一个静态只读实例（而不是结构相同的另一份拷贝），见 AchievementSchemas.Def 判断记录。
            Assert.Same(Core.Gameplay.Quest.QuestSchemas.RewardsFields, rewardsField!.Fields);
        }

        // -----------------------------------------------------------------
        // DataRegistry 装配：achv.def + 最小 item.template 三表（供 rewards.items Reference 用例）
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

        /// <summary>装配一个已注册 <c>achv.def</c>（+ <c>AchievementContentValidationRule</c>）与最小
        /// <c>item.*</c> 三表的 <see cref="DataRegistry"/>；<paramref name="defRowsJson"/> 是
        /// <c>achv.def</c> 的 <c>rows</c> JSON 数组文本，<paramref name="extraItemIds"/> 是额外要
        /// 登记进 <c>item.template</c> 的 id（供 <c>rewards.items</c> 的 Reference 命中用例引用）。</summary>
        private static ValidationReport Load(string defRowsJson, params string[] extraItemIds)
        {
            var itemRows = "[" + string.Join(",", extraItemIds.Select(ItemTemplateRow)) + "]";
            var source = new InMemoryDataSource()
                .Add(AchievementSchemas.Def.Name, Envelope(AchievementSchemas.Def.Name, defRowsJson))
                .Add("item.template", Envelope("item.template", itemRows))
                .Add("item.slot_definition", Envelope("item.slot_definition",
                    "[{\"id\": \"item.slot.consumable\", \"name_key\": \"l10n.slot.consumable\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition",
                    "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.quality.common\"}]"));

            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(AchievementSchemas.Def);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Template);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.SlotDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.QualityDefinition);
            registry.RegisterValidationRule(new AchievementContentValidationRule());
            return registry.LoadAll();
        }

        private const string MinimalHeader =
            "\"id\": \"achv.cov_a\", \"name_key\": \"l10n.achv.cov_a.name\"";

        // -----------------------------------------------------------------
        // criteria：命中 / 坏形状
        // -----------------------------------------------------------------

        [Fact]
        public void Criteria_AllSixTypes_WellFormed_Passes()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}," +
                "{\"type\": \"collect_count\", \"observe_event\": \"item.added\", \"target_ref\": \"item.cov_herb\", \"count\": 1}," +
                "{\"type\": \"quest_complete\", \"observe_event\": \"quest.turned_in\", \"target_ref\": \"quest.cov_a\", \"count\": 1}," +
                "{\"type\": \"reach_area\", \"observe_event\": \"area.trigger_entered\", \"target_ref\": \"area.cov_zone\", \"count\": 1}," +
                "{\"type\": \"cast_count\", \"observe_event\": \"skill.cast_success\", \"target_ref\": \"skill.cov_fireball\", \"count\": 1}," +
                "{\"type\": \"custom_event\", \"observe_event\": \"achievement.progressed\", \"count\": 1, \"filter\": \"true\"}" +
                "]}]";

            var report = Load(rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Criteria_UnknownType_ReportsVariantDiscriminator()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"not_a_type\", \"observe_event\": \"unit.died\", \"count\": 1}]}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator" && i.Field == "criteria[0].type");
        }

        [Fact]
        public void Criteria_MissingObserveEvent_ReportsRequiredField()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"kill_count\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}]}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "criteria[0].observe_event");
        }

        [Fact]
        public void Criteria_ObserveEventNotRegistered_ReportsAchvObserveEventUnregistered()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"kill_count\", \"observe_event\": \"not.a.registered.event\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}]}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "achv_observe_event_unregistered" && i.Field == "criteria[0].observe_event");
        }

        [Fact]
        public void Criteria_TargetRefNotBackedByAnyTable_StillPasses()
        {
            // kill_count 的 target_ref 退回 Id（不做存在性检查），见 AchievementSchemas.CriterionItemSchema 判断记录。
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", \"target_ref\": \"creature.cov_unregistered\", \"count\": 1}]}]";

            var report = Load(rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Criteria_CountZero_ReportsAchvCriterionCountPositive()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", \"target_ref\": \"creature.cov_wolf\", \"count\": 0}]}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "achv_criterion_count_positive" && i.Field == "criteria[0].count");
        }

        [Fact]
        public void Criteria_EmptyArray_ReportsAchvCriteriaMinCount()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": []}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "achv_criteria_min_count" && i.Field == "criteria");
        }

        [Fact]
        public void Criteria_FilterUnparsable_ReportsExprParsable()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"custom_event\", \"observe_event\": \"achievement.progressed\", \"count\": 1, \"filter\": \"(( bad\"}]}]";

            var itemRows = "[]";
            var source = new InMemoryDataSource()
                .Add(AchievementSchemas.Def.Name, Envelope(AchievementSchemas.Def.Name, rows))
                .Add("item.template", Envelope("item.template", itemRows));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions
            {
                FailOnUnknownTable = false,
                ExprSchema = Core.Rules.ExprHost.RulesExprSchema.Base,
            });
            registry.RegisterSchema(AchievementSchemas.Def);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Template);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "expr_parsable" && i.Field == "criteria[0].filter");
        }

        // -----------------------------------------------------------------
        // rewards：命中 / 坏形状
        // -----------------------------------------------------------------

        [Fact]
        public void Rewards_WellFormed_Passes()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"items\": [{\"itemId\": \"item.cov_reward\", \"count\": 1}], \"xp\": 10}}]";

            var report = Load(rows, "item.cov_reward");

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Rewards_ItemIdNotBackedByItemTemplate_ReportsReferenceIntegrity()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"items\": [{\"itemId\": \"item.cov_missing\", \"count\": 1}]}}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "rewards.items[0].itemId");
        }

        [Fact]
        public void Rewards_NegativeXp_ReportsRewardXpNonNegative()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"xp\": -5}}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reward_xp_non_negative" && i.Field == "rewards.xp");
        }

        [Fact]
        public void Rewards_ItemCountZero_ReportsRewardItemCountPositive()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"items\": [{\"itemId\": \"item.cov_reward\", \"count\": 0}]}}]";

            var report = Load(rows, "item.cov_reward");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reward_item_count_positive" && i.Field == "rewards.items[0].count");
        }

        [Fact]
        public void Rewards_WorldFlagMissingValue_ReportsRewardWorldFlagValueRequired()
        {
            var rows = "[{" + MinimalHeader + ", \"criteria\": [" +
                "{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", \"target_ref\": \"creature.cov_wolf\", \"count\": 1}], " +
                "\"rewards\": {\"world_flags\": [{\"flagKey\": \"world.cov_flag\"}]}}]";

            var report = Load(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reward_world_flag_value_required" && i.Field == "rewards.world_flags[0].value");
        }
    }
}
