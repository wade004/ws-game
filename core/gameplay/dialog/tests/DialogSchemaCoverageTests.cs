using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Gameplay.Dialog;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Dialog
{
    /// <summary>
    /// ADR-0019 / F1b（见 <c>DialogSchemas.cs</c> 类型顶部判断记录、
    /// <c>schema/README.md</c>"子结构登记表"一节）：
    /// <list type="bullet">
    /// <item><c>DialogSchemas.GossipActionItemSchema</c> 的 <c>VariantSchema.Cases</c> 键集合必须与
    /// <see cref="DialogActionKinds.EnumValues"/> 全集一致（做法同
    /// <c>Tests.Rules.Skill.SkillSchemaCoverageTests</c>）。</item>
    /// <item>子结构命中/坏形状各至少一例（<c>options</c>/<c>actions</c>/<c>nodes</c>/<c>branches</c>
    /// 各若干），以及 <c>DialogContentValidationRule</c> 收窄后仍保留的图结构判断（节点 id 重复、
    /// 悬空 <c>next_node_id</c>、成环、节点数量下限）各至少一例。</item>
    /// </list>
    /// </summary>
    public sealed class DialogSchemaCoverageTests
    {
        // -----------------------------------------------------------------
        // 变体键集合一致性
        // -----------------------------------------------------------------

        [Fact]
        public void GossipActionVariantKeys_MatchDialogActionKindsFullSet()
        {
            var actionsField = DialogSchemas.GossipMenu.GetField("options");
            Assert.NotNull(actionsField);
            var optionFields = actionsField!.Item!.Fields;
            Assert.NotNull(optionFields);
            var actionsSubfield = optionFields!.Single(f => f.Name == "actions");
            var variants = actionsSubfield.Item!.Variants;
            Assert.NotNull(variants);

            var registered = new HashSet<string>(variants!.Cases.Keys, StringComparer.Ordinal);
            var expected = new HashSet<string>(DialogActionKinds.EnumValues, StringComparer.Ordinal);

            Assert.Equal(expected, registered);
            Assert.Equal("kind", variants.Discriminator);
        }

        // -----------------------------------------------------------------
        // DataRegistry 装配：两张 dialog 表 + 最小 quest.def/encounter.def/skill.def 三表（供
        // Reference 用例）
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

        private const string MinimalQuestRow =
            "{\"id\": \"quest.cov_a\", \"title_key\": \"l10n.quest.cov_a.title\", " +
            "\"objectives\": [{\"type\": \"talk\", \"target_ref\": \"dialog.cov_menu\", \"count\": 1}], " +
            "\"start_method\": \"npc_gossip\", \"turn_in_method\": \"npc_gossip\", \"repeatable\": \"none\"}";

        private const string MinimalEncounterRow =
            "{\"id\": \"encounter.cov_a\", \"units\": [], \"victory_condition\": \"true\", \"defeat_condition\": \"false\"}";

        private const string MinimalSkillRow =
            "{\"id\": \"skill.cov_a\", \"school\": \"school.fire\", \"kind\": \"active\", \"range\": 0, " +
            "\"cast_time\": 0, \"respects_gcd\": true, \"target_shape_ref\": \"target.chain.self\", \"effects\": []}";

        /// <summary>装配一个已注册两张 dialog 表 + 最小 quest.def/encounter.def/skill.def 三表 +
        /// <see cref="DialogContentValidationRule"/> 的 <see cref="DataRegistry"/>。
        /// <paramref name="gossipRowsJson"/>/<paramref name="storyRowsJson"/> 缺省时对应表登记但不
        /// 加载任何记录。</summary>
        private static ValidationReport Load(string? gossipRowsJson = null, string? storyRowsJson = null)
        {
            var source = new InMemoryDataSource()
                .Add(DialogSchemas.GossipMenu.Name, Envelope(DialogSchemas.GossipMenu.Name, gossipRowsJson ?? "[]"))
                .Add(DialogSchemas.StoryTree.Name, Envelope(DialogSchemas.StoryTree.Name, storyRowsJson ?? "[]"))
                .Add("quest.def", Envelope("quest.def", "[" + MinimalQuestRow + "]"))
                .Add("encounter.def", Envelope("encounter.def", "[" + MinimalEncounterRow + "]"))
                .Add("skill.def", Envelope("skill.def", "[" + MinimalSkillRow + "]"));

            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions
            {
                FailOnUnknownTable = false,
                ExprSchema = QuestExprSchemaEntries.BuildParsingSchema(),
            });
            registry.RegisterSchema(DialogSchemas.GossipMenu);
            registry.RegisterSchema(DialogSchemas.StoryTree);
            registry.RegisterSchema(QuestSchemas.Def);
            registry.RegisterSchema(Core.Gameplay.Encounter.EncounterSchemas.Def);
            registry.RegisterSchema(Core.Rules.Skill.SkillSchemas.Def);
            registry.RegisterValidationRule(new DialogContentValidationRule());
            return registry.LoadAll();
        }

        // -----------------------------------------------------------------
        // gossip_menu.options：命中 / 坏形状
        // -----------------------------------------------------------------

        [Fact]
        public void GossipMenu_AllTenActionKinds_WellFormed_Passes()
        {
            var rows = "[{\"id\": \"dialog.cov_menu\", \"options\": [{" +
                "\"text_key\": \"l10n.cov.opt\", \"visible_if\": \"true\", \"actions\": [" +
                "{\"kind\": \"vendor\"}," +
                "{\"kind\": \"quest_accept\", \"ref\": \"quest.cov_a\"}," +
                "{\"kind\": \"quest_turn_in\", \"ref\": \"quest.cov_a\"}," +
                "{\"kind\": \"teleport\", \"ref\": \"map.cov_target\"}," +
                "{\"kind\": \"save\"}," +
                "{\"kind\": \"set_flag\", \"ref\": \"world.cov_flag\", \"params\": {\"value\": true}}," +
                "{\"kind\": \"start_encounter\", \"ref\": \"encounter.cov_a\"}," +
                "{\"kind\": \"cast_skill\", \"ref\": \"skill.cov_a\"}," +
                "{\"kind\": \"start_story\", \"ref\": \"dialog.cov_story\"}," +
                "{\"kind\": \"script\", \"ref\": \"found.cov_hook\"}" +
                "]}]}]";

            var report = Load(gossipRowsJson: rows, storyRowsJson: "[{\"id\": \"dialog.cov_story\", \"nodes\": [{\"id\": \"dialog.cov_story.n1\", \"text_key\": \"l10n.cov.n1\"}]}]");

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void GossipMenu_OptionMissingTextKey_ReportsRequiredField()
        {
            var rows = "[{\"id\": \"dialog.cov_menu\", \"options\": [{\"actions\": [{\"kind\": \"save\"}]}]}]";

            var report = Load(gossipRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "options[0].text_key");
        }

        [Fact]
        public void GossipMenu_ActionUnknownKind_ReportsVariantDiscriminator()
        {
            var rows = "[{\"id\": \"dialog.cov_menu\", \"options\": [{\"text_key\": \"l10n.cov.opt\", " +
                "\"actions\": [{\"kind\": \"not_a_kind\"}]}]}]";

            var report = Load(gossipRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator" && i.Field == "options[0].actions[0].kind");
        }

        [Fact]
        public void GossipMenu_QuestAcceptMissingRef_ReportsRequiredField()
        {
            var rows = "[{\"id\": \"dialog.cov_menu\", \"options\": [{\"text_key\": \"l10n.cov.opt\", " +
                "\"actions\": [{\"kind\": \"quest_accept\"}]}]}]";

            var report = Load(gossipRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "options[0].actions[0].ref");
        }

        [Fact]
        public void GossipMenu_SaveAndVendorWithoutRef_Passes()
        {
            var rows = "[{\"id\": \"dialog.cov_menu\", \"options\": [{\"text_key\": \"l10n.cov.opt\", " +
                "\"actions\": [{\"kind\": \"save\"}, {\"kind\": \"vendor\"}]}]}]";

            var report = Load(gossipRowsJson: rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void GossipMenu_QuestAcceptRefNotBackedByQuestDef_ReportsReferenceIntegrity()
        {
            var rows = "[{\"id\": \"dialog.cov_menu\", \"options\": [{\"text_key\": \"l10n.cov.opt\", " +
                "\"actions\": [{\"kind\": \"quest_accept\", \"ref\": \"quest.cov_missing\"}]}]}]";

            var report = Load(gossipRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "options[0].actions[0].ref");
        }

        [Fact]
        public void GossipMenu_VisibleIfUnparsable_ReportsExprParsable()
        {
            var rows = "[{\"id\": \"dialog.cov_menu\", \"options\": [{\"text_key\": \"l10n.cov.opt\", " +
                "\"visible_if\": \"(( bad\", \"actions\": [{\"kind\": \"save\"}]}]}]";

            var report = Load(gossipRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "expr_parsable" && i.Field == "options[0].visible_if");
        }

        [Fact]
        public void GossipMenu_ActionsAbsent_Passes()
        {
            // ParseOption 未提供 actions 字段时视为空数组，不报错，见 DialogSchemas.GossipOptionItemSchema 判断记录。
            var rows = "[{\"id\": \"dialog.cov_menu\", \"options\": [{\"text_key\": \"l10n.cov.opt\"}]}]";

            var report = Load(gossipRowsJson: rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // story_tree.nodes：命中 / 坏形状 / 图结构判断
        // -----------------------------------------------------------------

        [Fact]
        public void StoryTree_WellFormedChain_Passes()
        {
            var rows = "[{\"id\": \"dialog.cov_story\", \"nodes\": [" +
                "{\"id\": \"dialog.cov_story.n1\", \"text_key\": \"l10n.cov.n1\", \"branches\": [" +
                "{\"text_key\": \"l10n.cov.b1\", \"condition\": \"true\", \"next_node_id\": \"dialog.cov_story.n2\"}]}," +
                "{\"id\": \"dialog.cov_story.n2\", \"text_key\": \"l10n.cov.n2\"}" +
                "]}]";

            var report = Load(storyRowsJson: rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void StoryTree_NodeMissingTextKey_ReportsRequiredField()
        {
            var rows = "[{\"id\": \"dialog.cov_story\", \"nodes\": [{\"id\": \"dialog.cov_story.n1\"}]}]";

            var report = Load(storyRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "nodes[0].text_key");
        }

        [Fact]
        public void StoryTree_ConditionUnparsable_ReportsExprParsable()
        {
            var rows = "[{\"id\": \"dialog.cov_story\", \"nodes\": [" +
                "{\"id\": \"dialog.cov_story.n1\", \"text_key\": \"l10n.cov.n1\", \"branches\": [" +
                "{\"text_key\": \"l10n.cov.b1\", \"condition\": \"(( bad\"}]}" +
                "]}]";

            var report = Load(storyRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "expr_parsable" && i.Field == "nodes[0].branches[0].condition");
        }

        [Fact]
        public void StoryTree_EmptyNodes_ReportsStoryTreeMinNodes()
        {
            var rows = "[{\"id\": \"dialog.cov_story\", \"nodes\": []}]";

            var report = Load(storyRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "story_tree_min_nodes" && i.Field == "nodes");
        }

        [Fact]
        public void StoryTree_DuplicateNodeId_ReportsStoryTreeDuplicateNodeId()
        {
            var rows = "[{\"id\": \"dialog.cov_story\", \"nodes\": [" +
                "{\"id\": \"dialog.cov_story.n1\", \"text_key\": \"l10n.cov.n1\"}," +
                "{\"id\": \"dialog.cov_story.n1\", \"text_key\": \"l10n.cov.n1b\"}" +
                "]}]";

            var report = Load(storyRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "story_tree_duplicate_node_id");
        }

        [Fact]
        public void StoryTree_DanglingNextNodeId_ReportsStoryTreeDanglingNextNode()
        {
            var rows = "[{\"id\": \"dialog.cov_story\", \"nodes\": [" +
                "{\"id\": \"dialog.cov_story.n1\", \"text_key\": \"l10n.cov.n1\", \"branches\": [" +
                "{\"text_key\": \"l10n.cov.b1\", \"next_node_id\": \"dialog.cov_story.missing\"}]}" +
                "]}]";

            var report = Load(storyRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "story_tree_dangling_next_node");
        }

        [Fact]
        public void StoryTree_Cycle_ReportsStoryTreeCycle()
        {
            var rows = "[{\"id\": \"dialog.cov_story\", \"nodes\": [" +
                "{\"id\": \"dialog.cov_story.n1\", \"text_key\": \"l10n.cov.n1\", \"branches\": [" +
                "{\"text_key\": \"l10n.cov.b1\", \"next_node_id\": \"dialog.cov_story.n2\"}]}," +
                "{\"id\": \"dialog.cov_story.n2\", \"text_key\": \"l10n.cov.n2\", \"branches\": [" +
                "{\"text_key\": \"l10n.cov.b2\", \"next_node_id\": \"dialog.cov_story.n1\"}]}" +
                "]}]";

            var report = Load(storyRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "story_tree_cycle");
        }

        [Fact]
        public void StoryTree_TerminalBranchWithoutNextNodeId_Passes()
        {
            var rows = "[{\"id\": \"dialog.cov_story\", \"nodes\": [" +
                "{\"id\": \"dialog.cov_story.n1\", \"text_key\": \"l10n.cov.n1\", \"branches\": [" +
                "{\"text_key\": \"l10n.cov.b1\"}]}" +
                "]}]";

            var report = Load(storyRowsJson: rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // P2-06 关联根治：set_flag.params.value 是 ExprValueJson.Parse 联合类型之外的形状时，此前
        // 完全未登记/未校验，非法形状只在 DialogHost.ExecuteAction 才抛 FormatException（同一类缺口，
        // 见 QuestContentValidationRule.reward_world_flag_value_shape 判断记录）。
        // -----------------------------------------------------------------

        [Fact]
        public void GossipMenu_SetFlagValueIsArray_ReportsGossipActionSetFlagValueShape()
        {
            var rows = "[{\"id\": \"dialog.cov_menu\", \"options\": [{\"text_key\": \"l10n.cov.opt\", " +
                "\"actions\": [{\"kind\": \"set_flag\", \"ref\": \"world.cov_flag\", \"params\": {\"value\": []}}]}]}]";

            var report = Load(gossipRowsJson: rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "gossip_action_set_flag_value_shape" && i.Field == "options[0].actions[0].params.value");
        }

        [Fact]
        public void GossipMenu_SetFlagValueMissing_Passes()
        {
            // params.value 可选，缺省时 DialogHost.ExecuteAction 取 Bool(true)，不要求必填。
            var rows = "[{\"id\": \"dialog.cov_menu\", \"options\": [{\"text_key\": \"l10n.cov.opt\", " +
                "\"actions\": [{\"kind\": \"set_flag\", \"ref\": \"world.cov_flag\"}]}]}]";

            var report = Load(gossipRowsJson: rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void GossipMenu_SetFlagValue_Bool_Number_String_Id_AllPass()
        {
            foreach (var value in new[] { "true", "1", "1.5", "\"some_text\"", "{\"$id\": \"world.cov_other\"}" })
            {
                var rows = "[{\"id\": \"dialog.cov_menu\", \"options\": [{\"text_key\": \"l10n.cov.opt\", " +
                    "\"actions\": [{\"kind\": \"set_flag\", \"ref\": \"world.cov_flag\", \"params\": {\"value\": " + value + "}}]}]}]";

                var report = Load(gossipRowsJson: rows);
                Assert.False(report.IsBlocking, value + " => " + string.Join("; ", report.Issues));
            }
        }
    }
}
