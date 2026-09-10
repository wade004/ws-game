using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Quest;

namespace Core.Gameplay.Achievement
{
    /// <summary>
    /// <c>achv.def</c> 的 <see cref="TableSchema"/> 声明（见 08 第 6.1 节字段表 + 任务书拍板补录
    /// <c>name_key</c>）。
    /// <para>
    /// ADR-0019 首批登记（F1b，04 第 3.2 节"复合字段子结构登记"）：<c>criteria</c> 按判别字段
    /// <c>type</c> 登记为 <see cref="VariantSchema"/>（<see cref="CriterionItemSchema"/>，键集合与
    /// <see cref="CriterionTypeIds.AllValues"/> 全集一致，见 <c>AchievementSchemaCoverageTests</c>
    /// 用测试锁死这条一致性，做法同 <c>Tests.Rules.Skill.SkillSchemaCoverageTests</c>）；<c>rewards</c>
    /// 直接复用 <see cref="Core.Gameplay.Quest.QuestSchemas.RewardsFields"/>——<c>achv.def.rewards</c>
    /// 与 <c>quest.def.rewards</c> 由同一份 <see cref="Core.Gameplay.Common.RewardBundle.FromRecord"/>
    /// 解析（见 <c>Core.Gameplay.Common.RewardSchemaFields</c> 类型注释"三张表都有一个结构相同、语义
    /// 相同的 rewards 字段"），quest/achievement 同属 <c>Core.Gameplay</c> 单一程序集，直接引用不需要
    /// 新增 <c>ProjectReference</c>。参数/子字段以运行时解析代码（<see cref="AchievementCriterion.FromRecord"/>、
    /// <see cref="Core.Gameplay.Common.RewardBundle.FromRecord"/>）为唯一依据逐个对照，见
    /// <c>schema/README.md</c>"子结构登记表（ADR-0019 / F1b）"一节的完整对照与判断记录。登记表达不了
    /// 的业务判断（<c>criteria</c> 数组最小长度、<c>count</c> 数值范围、<c>observe_event</c> 目录成员
    /// 资格、<c>rewards</c> 数值范围等）保留在 <see cref="AchievementContentValidationRule"/>，不与
    /// 本文件的结构性登记重复报告同一缺陷。
    /// </para>
    /// </summary>
    public static class AchievementSchemas
    {
        // -----------------------------------------------------------------
        // criteria[] 元素结构（按 type 分派，六种类型见 CriterionType.cs）
        // -----------------------------------------------------------------

        /// <summary>全部六种类型共有的子字段：<c>observe_event</c>（必填 Id，<see cref="AchievementCriterion.FromRecord"/>
        /// 对全部类型统一要求存在且是合法 Id，缺失/格式非法直接抛 <see cref="Core.Foundation.DataRegistry.DataFieldException"/>）、
        /// <c>count</c>（必填 Int，累计次数下限 1 是构造期硬约束，见 <see cref="AchievementCriterion"/>
        /// 构造函数，业务判断见 <see cref="AchievementContentValidationRule"/>）、<c>filter</c>（可选
        /// Expr，六种类型均可选携带一条补充过滤表达式，见 <see cref="AchievementCriterion.FilterText"/>
        /// 类型注释）。判别字段 <c>type</c> 本身由 <see cref="VariantSchema"/> 处理，不在这里重复登记。
        /// <para>
        /// 判断记录（<c>observe_event</c> 未登记为 <see cref="FieldKind.Reference"/>）：语义上指向
        /// <c>found.event_catalog</c>（<see cref="Core.Foundation.DataRegistry.BuiltinSchemas.FoundEventCatalog"/>，
        /// 主键 <c>key</c>），但该表不由 <c>GameplaySchemaCatalog.RegisterAll</c> 注册（只在
        /// <c>Core.Rules.Assembly.RulesSchemaCatalog</c>/若干 L0～L1 测试里出现），既有测试
        /// <c>AchievementHostTests</c> 用只注册 <c>achv.def</c> 一张表的最小 <c>DataRegistry</c>
        /// （<c>TestSupport.MakeRegistry</c>）驱动全部用例；若登记为 Reference，<c>found.event_catalog</c>
        /// 未加载会让 <c>Core.Foundation.DataRegistry.DataRegistry.LoadAll</c> 对每条使用
        /// 任意 <c>observe_event</c> 的记录都报 <c>reference_integrity</c> 错误，与既有测试冲突（理由
        /// 同 <c>QuestSchemas.ObjectiveItemSchema</c> "kill"/"escort" 判断记录）。退回 Id（只查格式），
        /// "已登记事件 key"这条比 Reference 弱一档的成员资格检查改由 <see cref="AchievementContentValidationRule"/>
        /// 手写兜底（对 <see cref="Core.Foundation.EventBus.EventKeys.All"/> 编译期常量集合做成员测试，
        /// 不依赖 <c>found.event_catalog</c> 表是否加载）。
        /// </para>
        /// </summary>
        private static readonly IReadOnlyList<FieldSchema> CriterionCommonFields = new[]
        {
            new FieldSchema("observe_event", FieldKind.Id, required: true,
                description: "要观察的具体事件 key；见 found.event_catalog，已登记事件 key 的成员资格检查退回 AchievementContentValidationRule 手写兜底，理由见本类型判断记录"),
            new FieldSchema("count", FieldKind.Int, required: true,
                description: "达成所需累计次数；必须 >= 1（AchievementCriterion 构造期硬约束，FieldKind 不表达数值范围，业务判断见 AchievementContentValidationRule）"),
            new FieldSchema("filter", FieldKind.Expr, required: false,
                description: "补充匹配条件；六种类型均可选携带，基础匹配规则通过之后再叠加求值，均为真才计入一次进度"),
        };

        /// <summary>达成条件列表（<c>achv.def.criteria</c>）的元素结构：按判别字段 <c>type</c> 分派到
        /// 六种类型各自的 <c>target_ref</c> 语义（<see cref="AchievementHost.TryMatch"/>）。
        /// <c>Name</c>/<c>Required</c> 仅作占位（本字段只作为 <see cref="FieldSchema.Item"/> 使用，
        /// 同 <c>SkillSchemas.EffectsItemSchema</c> 判断记录）。
        /// <para>
        /// 判断记录（<c>target_ref</c> 五种类型均登记为可选 <see cref="FieldKind.Id"/>，不区分
        /// required、不登记为 Reference）：<see cref="AchievementCriterion.FromRecord"/> 对全部六种
        /// 类型统一按"存在则必须是合法 Id 字符串，不存在则为 null"解析，不因 <c>type</c> 强制必填——
        /// 是否命中匹配完全交给 <see cref="AchievementHost.TryMatch"/> 运行期判断（<c>kill_count</c>/
        /// <c>collect_count</c>/<c>quest_complete</c>/<c>reach_area</c>/<c>cast_count</c> 五种类型在
        /// <c>target_ref</c> 缺失时该条 criterion 静默永不命中，不是解析期报错），因此 FieldSchema 层面
        /// 忠实保持"可选"，不额外收紧为 required（收紧属于新增业务规则的范畴，超出"以运行时解析代码
        /// 为唯一依据"的登记口径，也不属于本次任务"退役掉能被登记覆盖的检查"）。域名上
        /// <c>target_ref</c> 依次指向 <c>creature.template</c>（kill_count）、<c>item.template</c>
        /// （collect_count）、<c>quest.def</c>（quest_complete）、<c>area.trigger_def</c>（reach_area）、
        /// <c>skill.def</c>（cast_count），本可登记为 Reference（除 quest.def/area.trigger_def 需要
        /// achievement 与 quest/area_trigger 两模块同批注册这一条件恰好满足外，creature.template/
        /// item.template 均属更低层 L3，登记 Reference 不违反分层）；但既有测试
        /// <c>AchievementHostTests</c>（<c>TestSupport.MakeRegistry</c> 只注册 <c>achv.def</c> 一张表）
        /// 大量用例的 <c>target_ref</c> 指向从未注册进对应表的 id（如 <c>creature.sample_monster</c>），
        /// 登记为 Reference 会让这些既有用例改判为 <c>reference_integrity</c> 错误，理由同
        /// <c>observe_event</c> 判断记录，五处统一退回 Id。<c>custom_event</c> 不使用该字段（见
        /// <see cref="AchievementCriterion.TargetRef"/> 类型注释），本 case 不额外登记子字段。
        /// </para>
        /// </summary>
        public static readonly FieldSchema CriterionItemSchema = new FieldSchema(
            "<criterion>", FieldKind.Object, required: true, variants: BuildCriterionVariants(),
            description: "{type, observe_event, target_ref?, count, filter?}，六种类型见 CriterionType");

        private static VariantSchema BuildCriterionVariants()
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                [CriterionTypeIds.KillCountValue] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Id, required: false,
                        description: "creature.template；退回 Id（不做存在性检查），理由见 CriterionItemSchema 判断记录（消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                        .WithSoftReference(table: "creature.template"),
                },
                [CriterionTypeIds.CollectCountValue] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Id, required: false,
                        description: "item.template；退回 Id，理由同 kill_count（消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                        .WithSoftReference(table: "item.template"),
                },
                [CriterionTypeIds.QuestCompleteValue] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Id, required: false,
                        description: "quest.def；退回 Id，理由同 kill_count（消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                        .WithSoftReference(table: "quest.def"),
                },
                [CriterionTypeIds.ReachAreaValue] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Id, required: false,
                        description: "area.trigger_def；退回 Id，理由同 kill_count（消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                        .WithSoftReference(table: "area.trigger_def"),
                },
                [CriterionTypeIds.CastCountValue] = new[]
                {
                    new FieldSchema("target_ref", FieldKind.Id, required: false,
                        description: "skill.def；退回 Id，理由同 kill_count（消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                        .WithSoftReference(table: "skill.def"),
                },
                // custom_event：不使用 target_ref（AchievementCriterion.TargetRef 类型注释），
                // 本 case 不额外登记子字段——若内容作者仍然提供该字段，未登记子结构默认允许扩展
                // （unknown_subfield 警告，不阻断加载）。
                [CriterionTypeIds.CustomEventValue] = Array.Empty<FieldSchema>(),
            };

            return new VariantSchema("type", cases, commonFields: CriterionCommonFields);
        }

        // -----------------------------------------------------------------
        // achv.def
        // -----------------------------------------------------------------

        public static readonly TableSchema Def = new TableSchema(
            name: "achv.def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "achv.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "显示名文本键（08 第 6.1 节未列出，任务书拍板补录）"),
                new FieldSchema("criteria", FieldKind.Array, required: true, item: CriterionItemSchema,
                    description: "List<{type, observe_event, target_ref?, count, filter?}>，见本类型判断记录；元素结构 ADR-0019/F1b 起登记为 CriterionItemSchema（按 type 分派的 Variants）"),
                new FieldSchema("rewards", FieldKind.Object, required: false, fields: QuestSchemas.RewardsFields,
                    description: "{items, xp, currency, skills, world_flags, talent_points}，见 Core.Gameplay.Common.RewardBundle；ADR-0019/F1b 起直接复用 QuestSchemas.RewardsFields（见本类型判断记录）"),
            }).WithOwnership(SchemaLayer.Gameplay, "achievement");
    }
}
