using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <c>quest.def</c> 的 <see cref="TableSchema"/> 声明（见 08 第 2.1 节字段表 + 任务书补充
    /// <c>title_key</c>/<c>description_key</c>）。
    /// <para>
    /// ADR-0019 首批登记（F1b，04 第 3.2 节"复合字段子结构登记"）：<c>objectives</c> 按判别字段
    /// <c>type</c> 登记为 <see cref="VariantSchema"/>（<see cref="ObjectiveItemSchema"/>，键集合与
    /// <see cref="QuestObjectiveTypes.EnumValues"/> 全集一致，见 <c>QuestSchemaCoverageTests</c> 用
    /// 测试锁死这条一致性，做法同 <c>SkillSchemaCoverageTests</c>）；<c>rewards</c> 登记为带
    /// <see cref="FieldSchema.Fields"/> 的 <see cref="FieldSchema"/>（<see cref="RewardsFields"/>）。
    /// 参数/子字段以运行时解析代码（<see cref="QuestDefinition.FromRecord"/>、
    /// <see cref="Core.Gameplay.Common.RewardBundle.FromRecord"/>）为唯一依据逐个对照，见
    /// <c>schema/quest.def.md</c>"子结构登记表（ADR-0019 / F1b）"一节的完整对照与判断记录。登记
    /// 表达不了的业务判断（<c>count</c> 数值范围、<c>rewards</c> 数值范围等）保留在
    /// <see cref="QuestContentValidationRule"/>，不与本文件的结构性登记重复报告同一缺陷。
    /// </para>
    /// </summary>
    public static class QuestSchemas
    {
        // -----------------------------------------------------------------
        // objectives[] 元素结构（按 type 分派，8 种目标类型见 QuestObjectiveType.cs）
        // -----------------------------------------------------------------

        /// <summary>全部取值共有的子字段：<c>count</c>（必填 Int，正数/恒为 1 等数值范围约束是
        /// <see cref="FieldSchema"/> 无法表达的业务判断，见 <see cref="QuestContentValidationRule"/>）、
        /// <c>description_key</c>（可选 Id）。判别字段 <c>type</c> 本身由 <see cref="VariantSchema"/>
        /// 处理，不在这里重复登记（见 <see cref="VariantSchema.Cases"/> 判断记录"判别字段本身不必
        /// 重复登记"）。</summary>
        private static readonly IReadOnlyList<FieldSchema> ObjectiveCommonFields = new[]
        {
            new FieldSchema("count", FieldKind.Int, required: true,
                description: "需求数量；必须为正数，explore/escort/talk 恒为 1——两条均为业务判断，FieldSchema 不表达数值范围/等值约束，见 QuestContentValidationRule"),
            new FieldSchema("description_key", FieldKind.Id, required: false, description: "该条目标自身的描述文本键"),
        };

        /// <summary>八种目标类型各自不需要 <c>param</c> 子字段时的占位登记（<c>params</c> 本身可省略，
        /// 若提供则不接受任何子字段），做法同 <c>SkillSchemas</c> 的 <c>open_lock</c>/<c>flag</c>
        /// 判断记录。</summary>
        private static FieldSchema EmptyParam() => new FieldSchema(
            "param", FieldKind.Object, required: false, fields: Array.Empty<FieldSchema>(), description: "无参数");

        /// <summary>任务目标列表（<c>quest.def.objectives</c>）的元素结构：按判别字段 <c>type</c>
        /// 分派到八种目标类型各自的 <c>target_ref</c>/<c>param</c> 形状（见 08 第 2.1 节对照表、
        /// <c>QuestDefinition.ParseObjective</c>）。<c>Name</c>/<c>Required</c> 仅作占位（本字段只作为
        /// <see cref="FieldSchema.Item"/> 使用，不会被当作某个对象的具名子字段校验，同
        /// <c>SkillSchemas.EffectsItemSchema</c> 判断记录）。</summary>
        public static readonly FieldSchema ObjectiveItemSchema = new FieldSchema(
            "<objective>", FieldKind.Object, required: true, variants: BuildObjectiveVariants(),
            description: "{type, target_ref, count, param?, description_key?}，八种目标类型见 QuestObjectiveType");

        private static VariantSchema BuildObjectiveVariants()
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                // kill / escort：target_ref 概念上指向 creature.template（域名 creature，属 L3，低于
                // 本模块 L4，分层上允许 Reference）。判断记录（偏离"能 Reference 就 Reference"的默认
                // 策略）：既有测试 Tests.Gameplay.Assembly.GameplayAssemblyOwnerDayVendorExtensionPointTests
                // （core/gameplay/assembly/tests，不在本次任务改动范围内）用一个只存在于运行期世界
                // 实体、从未登记进 creature.template 内容表的 creature id 驱动 kill 目标校验
                // report.IsBlocking 必须为 false——若这里登记为 Reference("creature.template")（存在
                // 性检查），会让该测试判为阻断错误。因此这两处退回 Id（只查格式），domain 必须是
                // "creature"这条比 Reference 弱一档的检查改由 QuestContentValidationRule 手写兜底
                // （不完全放弃，只是不做跨表存在性检查），避免与既有测试冲突的同时不牺牲这条判断。
                ["kill"] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Id, required: true,
                        description: "creature.template；退回 Id（不做存在性检查），domain 匹配见 QuestContentValidationRule 判断记录"),
                    EmptyParam(),
                },

                ["collect"] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Reference, required: true, referenceTable: "item.template",
                        description: "待收集的物品，Reference(item.template)"),
                    new FieldSchema("param", FieldKind.Object, required: false, fields: new[]
                    {
                        new FieldSchema("consume_on_progress", FieldKind.Bool, required: false,
                            description: "进度是否随拾取即时消耗物品，缺省 false"),
                    }, description: "{consume_on_progress?: Bool}"),
                },

                ["interact"] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Reference, required: true, referenceTable: "gobj.template",
                        description: "待交互的场景对象，Reference(gobj.template)"),
                    EmptyParam(),
                },

                ["explore"] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Reference, required: true, referenceTable: "area.trigger_def",
                        description: "待探索的区域触发器，Reference(area.trigger_def)"),
                    EmptyParam(),
                },

                ["escort"] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Id, required: true,
                        description: "creature.template（被护送对象）；退回 Id，理由同 kill 判断记录"),
                    new FieldSchema("param", FieldKind.Object, required: false, fields: new[]
                    {
                        new FieldSchema("escort_route_ref", FieldKind.Id, required: false,
                            description: "护送路径引用；判断记录：04 §2.2 域名清单无对应 domain/登记表，无法 Reference，退回 Id"),
                    }, description: "{escort_route_ref?: Id}"),
                },

                ["event"] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Id, required: true,
                        description: "任意事件 key（见 found.event_catalog）；不做域名/存在性强校验，判断记录同 QuestObjectiveTypes.RequiredTargetDomain（event 返回 null）"),
                    new FieldSchema("param", FieldKind.Object, required: false, fields: new[]
                    {
                        new FieldSchema("eventFilter", FieldKind.Expr, required: false, description: "触发条件表达式"),
                    }, description: "{eventFilter?: Expr}"),
                },

                ["cast"] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Reference, required: true, referenceTable: "skill.def",
                        description: "待施放的技能，Reference(skill.def)"),
                    EmptyParam(),
                },

                // talk：判断记录同 QuestObjectiveTypes.RequiredTargetDomain（talk 返回 null）——目标
                // 既可以是 dialog.gossip_menu 的 id，也可以是 dialog.story_tree 某节点的 id（节点 id
                // 由内容作者自行命名，不强制 domain 段等于 "dialog"），不做域名/存在性强校验。
                ["talk"] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Id, required: true,
                        description: "dialog.gossip_menu 或 dialog.story_tree 节点 id；不做域名/存在性强校验，见判断记录"),
                    EmptyParam(),
                },
            };

            return new VariantSchema("type", cases, commonFields: ObjectiveCommonFields);
        }

        // -----------------------------------------------------------------
        // rewards 字段结构（quest.def/encounter.def/achv.def 三表共用同一份 RewardBundle 形状）
        // -----------------------------------------------------------------

        /// <summary>
        /// <c>rewards</c> 字段子结构（ADR-0019 / F1b）：以
        /// <see cref="Core.Gameplay.Common.RewardBundle.FromRecord"/> 解析代码为唯一依据登记
        /// <c>items</c>/<c>xp</c>/<c>currency</c>/<c>skills</c>/<c>world_flags</c>/<c>talent_points</c>
        /// 六个子字段。<c>quest.def</c>/<c>encounter.def</c>/<c>achv.def</c> 三张表共用同一份
        /// <c>RewardBundle</c> 结构（见该类型注释），<see cref="Core.Gameplay.Common.RewardSchemaFields.Rewards"/>
        /// 目前仍只登记裸 <see cref="FieldKind.Object"/>（不带 <see cref="FieldSchema.Fields"/>）。
        /// 判断记录：本字段公开放在本模块（<c>Core.Gameplay.Quest</c>），供其它同属 <c>Core.Gameplay</c>
        /// 单一程序集的姊妹模块（achievement/dialog 等）未来直接引用，不需要新增
        /// <c>ProjectReference</c>；本次任务范围限定在 <c>quest</c> 模块，不改动
        /// <c>Core.Gameplay.Common.RewardSchemaFields</c> 本身——上游可以考虑后续把本字段搬进
        /// <c>RewardSchemaFields.Rewards()</c>，让三张表都直接带上 <c>Fields</c>，避免各自重复登记
        /// （见最终报告"RewardSchemaFields 完整路径与字段清单"一节）。
        /// <para>
        /// <c>world_flags[].value</c> 判断记录：值类型是 Bool｜Number｜String｜Id 联合类型（经
        /// <see cref="Core.Gameplay.Common.ExprValueJson.Parse"/> 解析），<see cref="FieldKind"/>
        /// 无法表达联合类型，故不登记该子字段（同 <c>SkillSchemas</c>
        /// <c>set_world_flag.flag_key</c> 判断记录先例）；"value 必须存在"与"value 形状必须落在
        /// <see cref="Core.Gameplay.Common.ExprValueJson.IsValid"/> 接受的集合内"两条判断改由
        /// <see cref="QuestContentValidationRule"/> 手写兜底（P2-06 根治：此前只检查存在性，
        /// <c>[]</c> 一类非法形状 0 error，直到 <c>RewardBundle</c> 解析才抛异常）。
        /// </para>
        /// </summary>
        public static readonly IReadOnlyList<FieldSchema> RewardsFields = new[]
        {
            new FieldSchema("items", FieldKind.Array, required: false,
                item: new FieldSchema("<item>", FieldKind.Object, required: true, fields: new[]
                {
                    new FieldSchema("itemId", FieldKind.Reference, required: true, referenceTable: "item.template",
                        description: "所奖励的物品，Reference(item.template)"),
                    new FieldSchema("count", FieldKind.Int, required: true,
                        description: "必须为正数（ItemStack 构造期硬约束，业务判断见 QuestContentValidationRule）"),
                }, description: "{itemId: Reference(item.template), count: Int}"),
                description: "[{itemId: Reference(item.template), count: Int}, ...]"),

            new FieldSchema("xp", FieldKind.Number, required: false,
                description: "缺省 0；不能为负数（RewardBundle 构造期硬约束，业务判断见 QuestContentValidationRule）"),

            new FieldSchema("currency", FieldKind.Array, required: false,
                item: new FieldSchema("<currency>", FieldKind.Object, required: true, fields: new[]
                {
                    new FieldSchema("currencyId", FieldKind.Reference, required: true, referenceTable: "econ.currency",
                        description: "所奖励的货币种类，Reference(econ.currency)"),
                    new FieldSchema("amount", FieldKind.Int, required: true,
                        description: "奖励货币数量；不能为负数，业务判断见 QuestContentValidationRule"),
                }, description: "{currencyId: Reference(econ.currency), amount: Int}"),
                description: "[{currencyId: Reference(econ.currency), amount: Int}, ...]"),

            new FieldSchema("skills", FieldKind.Array, required: false,
                item: new FieldSchema("<skill>", FieldKind.Reference, required: true, referenceTable: "skill.def",
                    description: "所奖励解锁的技能，Reference(skill.def)"),
                description: "[Reference(skill.def), ...]"),

            new FieldSchema("world_flags", FieldKind.Array, required: false,
                item: new FieldSchema("<world_flag>", FieldKind.Object, required: true, fields: new[]
                {
                    new FieldSchema("flagKey", FieldKind.Id, required: true,
                        description: "见 world.flag_schema；判断记录同 SkillSchemas.set_world_flag.flag_key，未登记为 Reference"),
                }, description: "{flagKey: Id, value: Bool|Number|String|Id}"),
                description: "[{flagKey: Id, value: Bool|Number|String|Id}, ...]；value 未登记子结构（联合类型，见类型顶部判断记录），必填/形状判断由 QuestContentValidationRule 手写兜底（P2-06 根治）"),

            new FieldSchema("talent_points", FieldKind.Int, required: false,
                description: "缺省 0；不能为负数（RewardBundle 构造期硬约束，业务判断见 QuestContentValidationRule）"),
        };

        // -----------------------------------------------------------------
        // quest.def
        // -----------------------------------------------------------------

        public static readonly TableSchema Def = new TableSchema(
            name: "quest.def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "quest.<name>"),
                new FieldSchema("title_key", FieldKind.TextKey, required: true, description: "任务标题文本键（08 第 2.1 节已登记，必填）"),
                new FieldSchema("description_key", FieldKind.TextKey, required: false, description: "任务描述文本键（08 第 2.1 节已登记，可选）"),
                new FieldSchema("objectives", FieldKind.Array, required: true, item: ObjectiveItemSchema,
                    description: "List<QuestObjective>，见 08 第 2.1 节；元素结构 ADR-0019/F1b 起登记为 ObjectiveItemSchema（按 type 分派的 Variants）"),
                new FieldSchema("prerequisite", FieldKind.Expr, required: false, description: "前置条件 Expr"),
                new FieldSchema("exclusive_group", FieldKind.Id, required: false, description: "互斥组 id"),
                new FieldSchema("start_method", FieldKind.Enum, required: true, enumValues: QuestEnumWireNames.StartMethodValues,
                    description: "任务可被接取的触发方式，见 QuestEnumWireNames.StartMethodValues"),
                new FieldSchema("turn_in_method", FieldKind.Enum, required: true, enumValues: QuestEnumWireNames.TurnInMethodValues,
                    description: "任务交付/完成的确认方式，见 QuestEnumWireNames.TurnInMethodValues"),
                new FieldSchema("rewards", FieldKind.Object, required: false, fields: RewardsFields,
                    description: "{items, xp, currency, skills, world_flags, talent_points}，见 Core.Gameplay.Common.RewardBundle；ADR-0019/F1b 起登记 Fields（见 QuestSchemas.RewardsFields 判断记录）"),
                new FieldSchema("repeatable", FieldKind.Enum, required: true, enumValues: QuestEnumWireNames.RepeatableValues,
                    description: "任务可重复接取的规则，见 QuestEnumWireNames.RepeatableValues"),
            });
    }
}
