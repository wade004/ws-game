using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Dialog;

namespace Core.Gameplay.Dialog
{
    /// <summary>
    /// <c>dialog.gossip_menu</c>/<c>dialog.story_tree</c> 的 <see cref="TableSchema"/> 声明（见 08
    /// 第 3.1、3.2 节字段表）。
    /// <para>
    /// ADR-0019 首批登记（F1b，04 第 3.2 节"复合字段子结构登记"）：<c>options</c>（gossip 菜单选项，
    /// <see cref="GossipOptionItemSchema"/>）、<c>options[].actions</c>（按判别字段 <c>kind</c> 分派
    /// 十种动作类型的 <see cref="VariantSchema"/>，键集合与 <see cref="DialogActionKinds.EnumValues"/>
    /// 全集一致，见 <c>DialogSchemaCoverageTests</c> 用测试锁死这条一致性）、<c>nodes</c>（剧情节点，
    /// <see cref="StoryNodeItemSchema"/>）、<c>nodes[].branches</c>（剧情分支，
    /// <see cref="StoryBranchItemSchema"/>）均登记为机器可读的 <see cref="FieldSchema.Fields"/>/
    /// <see cref="FieldSchema.Item"/>/<see cref="FieldSchema.Variants"/>。子字段以运行时解析代码
    /// （<see cref="GossipMenuDefinition.FromRecord"/>/<see cref="StoryTreeDefinition.FromRecord"/>）
    /// 为唯一依据逐个对照，见 <c>schema/README.md</c>"子结构登记表（ADR-0019 / F1b）"一节的完整对照
    /// 与判断记录。登记表达不了的业务判断（<c>next_node_id</c> 树内存在性、成环、节点 id 重复、
    /// 节点数量下限）保留在 <see cref="DialogContentValidationRule"/>，不与本文件的结构性登记重复
    /// 报告同一缺陷（见该类型判断记录）。
    /// </para>
    /// </summary>
    public static class DialogSchemas
    {
        // -----------------------------------------------------------------
        // dialog.gossip_menu.options[].actions[] 元素结构（按 kind 分派，十种动作见 DialogActionKind.cs）
        // -----------------------------------------------------------------

        /// <summary>gossip 动作列表（<c>options[].actions</c>）的元素结构：按判别字段 <c>kind</c>
        /// 分派到十种动作各自的 <c>ref</c>/<c>params</c> 形状（见 08 第 3.1 节、
        /// <see cref="GossipMenuDefinition"/> 解析代码）。<c>Name</c>/<c>Required</c> 仅作占位
        /// （本字段只作为 <see cref="FieldSchema.Item"/> 使用，同 <c>SkillSchemas.EffectsItemSchema</c>
        /// 判断记录）。</summary>
        public static readonly FieldSchema GossipActionItemSchema = new FieldSchema(
            "<action>", FieldKind.Object, required: true, variants: BuildGossipActionVariants(),
            description: "{kind, ref?, params?}，十种动作见 DialogActionKind");

        private static VariantSchema BuildGossipActionVariants()
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                // vendor：NPC 与商店可以是一对一关系，ref 为空时按当前 NPC 自身推断（见
                // DialogActionKinds.RequiresRef 判断记录），两种真正不强制 ref 的动作之一。
                [DialogActionKinds.ToWireString(DialogActionKind.Vendor)] = new[]
                {
                    new FieldSchema("ref", FieldKind.Id, required: false,
                        description: "商店 id；为空时按当前 NPC 自身推断，见 DialogActionKinds.RequiresRef 判断记录"),
                },

                [DialogActionKinds.ToWireString(DialogActionKind.QuestAccept)] = new[]
                {
                    new FieldSchema("ref", FieldKind.Reference, required: true, referenceTable: "quest.def",
                        description: "待接取的任务，Reference(quest.def)"),
                },

                [DialogActionKinds.ToWireString(DialogActionKind.QuestTurnIn)] = new[]
                {
                    new FieldSchema("ref", FieldKind.Reference, required: true, referenceTable: "quest.def",
                        description: "待交付的任务，Reference(quest.def)"),
                },

                // teleport：ref 经 GameplayAssembly 注入的 TeleportTargetResolver 解析成
                // (MapId, Position)，不是任何一张 DataRegistry 表的主键（同 gobj.teleporter 的
                // teleport_target_ref 判断记录），退回 Id。
                [DialogActionKinds.ToWireString(DialogActionKind.Teleport)] = new[]
                {
                    new FieldSchema("ref", FieldKind.Id, required: true,
                        description: "传送目标；经 TeleportTargetResolver 解析（非 DataRegistry 表，退回 Id，见判断记录）"),
                },

                // save：发起存档不指向任何具体 id，两种真正不强制 ref 的动作之一，也不使用 params。
                [DialogActionKinds.ToWireString(DialogActionKind.Save)] = Array.Empty<FieldSchema>(),

                [DialogActionKinds.ToWireString(DialogActionKind.SetFlag)] = new[]
                {
                    new FieldSchema("ref", FieldKind.Id, required: true,
                        description: "world state flag key，见 world.flag_schema；判断记录同 QuestSchemas.RewardsFields.world_flags.flagKey，未登记为 Reference（该表非运行态数据，不参与运行期加载）"),
                    new FieldSchema("params", FieldKind.Object, required: false,
                        description: "{value}；value 经 Core.Gameplay.Common.ExprValueJson.Parse 解析，为 Bool|Int|Number|String|{$id: Id} 联合类型，FieldKind 无法表达，未登记子结构（缺省时 DialogHost.ExecuteAction 取 Bool(true)）"),
                },

                // start_encounter：ref 语义上指向 encounter.def（DialogHost 经
                // EncounterStartRequestedCallback 委托转发，GameplayAssembly 侧实际实现是
                // Encounter.Start(ref, ...)，见 core/gameplay/assembly/README.md
                // "EncounterStartRequestedCallback" 行），encounter 与 dialog 同层（L4）且同属
                // Core.Gameplay 程序集、GameplaySchemaCatalog.RegisterAll 同一次调用内先后注册
                // encounter.def/dialog.gossip_menu 两张表，登记为 Reference 不违反分层边界。
                [DialogActionKinds.ToWireString(DialogActionKind.StartEncounter)] = new[]
                {
                    new FieldSchema("ref", FieldKind.Reference, required: true, referenceTable: "encounter.def",
                        description: "待发起的遭遇，Reference(encounter.def)"),
                },

                // cast_skill：ref 指向 skill.def（L2，低于本模块 L4，允许 Reference）。
                [DialogActionKinds.ToWireString(DialogActionKind.CastSkill)] = new[]
                {
                    new FieldSchema("ref", FieldKind.Reference, required: true, referenceTable: "skill.def",
                        description: "待施放的技能，Reference(skill.def)"),
                },

                // start_story：ref 指向本模块自己的另一张表 dialog.story_tree（DialogHost.StartStory
                // 直接按 id 索引 _storyTrees）。
                [DialogActionKinds.ToWireString(DialogActionKind.StartStory)] = new[]
                {
                    new FieldSchema("ref", FieldKind.Reference, required: true, referenceTable: "dialog.story_tree",
                        description: "待开始的剧情树，Reference(dialog.story_tree)"),
                },

                // script：ref 指向 found.hook 钩子 id。判断记录（同 SkillSchemas.script）：found.hook
                // 当前无实现级 schema 登记（04 变更记录 2026-09-05"仍无对应实现级 schema 登记"），
                // Reference 到未登记 schema 的表在校验期恒不存在、会把每条使用 script 动作的记录判为
                // 引用失效，先退回 Id，待 found.hook 补齐登记后再升级为 Reference。
                [DialogActionKinds.ToWireString(DialogActionKind.Script)] = new[]
                {
                    new FieldSchema("ref", FieldKind.Id, required: true,
                        description: "found.hook 钩子 id；found.hook 当前无实现级 schema 登记，退回 Id，理由同 SkillSchemas.script 判断记录"),
                },
            };

            return new VariantSchema("kind", cases);
        }

        // -----------------------------------------------------------------
        // dialog.gossip_menu.options[] 元素结构
        // -----------------------------------------------------------------

        public static readonly FieldSchema GossipOptionItemSchema = new FieldSchema(
            "<option>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("text_key", FieldKind.TextKey, required: true, description: "选项文案文本键"),
                new FieldSchema("visible_if", FieldKind.Expr, required: false,
                    description: "显隐条件；缺省视为恒可见（GossipMenuDefinition.ParseOption 未提供或非字符串时视为 null）"),
                new FieldSchema("actions", FieldKind.Array, required: false, item: GossipActionItemSchema,
                    description: "缺省空数组（ParseOption 未提供 actions 字段时不报错，见 GossipMenuDefinition 判断记录）"),
            },
            description: "{text_key, visible_if?, actions?}，见 08 第 3.1 节 GossipOption");

        // -----------------------------------------------------------------
        // dialog.story_tree.nodes[].branches[] 元素结构
        // -----------------------------------------------------------------

        public static readonly FieldSchema StoryBranchItemSchema = new FieldSchema(
            "<branch>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("text_key", FieldKind.TextKey, required: true, description: "该条分支选项的文案文本键"),
                new FieldSchema("condition", FieldKind.Expr, required: false,
                    description: "显隐条件；缺省视为恒可见，同 options[].visible_if"),
                new FieldSchema("next_node_id", FieldKind.Id, required: false,
                    description: "为空表示该分支是终止分支；是否命中同一棵树内的已知节点属登记表达不了的跨元素一致性检查，见 DialogContentValidationRule"),
            },
            description: "{text_key, condition?, next_node_id?}，见 08 第 3.2 节 StoryBranch");

        // -----------------------------------------------------------------
        // dialog.story_tree.nodes[] 元素结构
        // -----------------------------------------------------------------

        public static readonly FieldSchema StoryNodeItemSchema = new FieldSchema(
            "<node>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "节点 id；同一棵树内必须唯一——唯一性是跨元素一致性检查，FieldSchema 不表达，见 DialogContentValidationRule"),
                new FieldSchema("text_key", FieldKind.TextKey, required: true, description: "该节点的对话文本键"),
                new FieldSchema("speaker_ref", FieldKind.Id, required: false,
                    description: "指向 creature.template 或占位角色 id；判断记录：本模块（dialog）不依赖 Core.Carriers.Creature 程序集（README 依赖清单未列出），无法 Reference，退回 Id（消费方反馈第 30 条：主用途是 creature.template，登记为软引用，占位角色 id 不解析属预期降级）")
                    .WithSoftReference(table: "creature.template"),
                new FieldSchema("branches", FieldKind.Array, required: false, item: StoryBranchItemSchema,
                    description: "缺省空数组；分支为空的节点是终止节点"),
                new FieldSchema("performance_hook_ref", FieldKind.Id, required: false,
                    description: "进入该节点时调用的 found.hook；found.hook 当前无实现级 schema 登记，退回 Id，理由同 actions[].script.ref"),
            },
            description: "{id, text_key, speaker_ref?, branches?, performance_hook_ref?}，见 08 第 3.2 节 StoryNode");

        // -----------------------------------------------------------------
        // 两张顶层表
        // -----------------------------------------------------------------

        public static readonly TableSchema GossipMenu = new TableSchema(
            name: "dialog.gossip_menu",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "dialog.<name> 或内容作者自定义命名"),
                new FieldSchema("options", FieldKind.Array, required: true, item: GossipOptionItemSchema,
                    description: "List<GossipOption>，见 08 第 3.1 节；元素结构 ADR-0019/F1b 起登记为 GossipOptionItemSchema"),
            }).WithOwnership(SchemaLayer.Gameplay, "dialog");

        public static readonly TableSchema StoryTree = new TableSchema(
            name: "dialog.story_tree",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "dialog.<name> 或内容作者自定义命名"),
                new FieldSchema("nodes", FieldKind.Array, required: true, item: StoryNodeItemSchema,
                    description: "List<StoryNode>，见 08 第 3.2 节；元素结构 ADR-0019/F1b 起登记为 StoryNodeItemSchema"),
            }).WithOwnership(SchemaLayer.Gameplay, "dialog");
    }
}
