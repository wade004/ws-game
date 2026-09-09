using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <c>quest.def</c> 专属内容校验规则（见 <see cref="IValidationRule"/>"模块专属校验规则的
    /// 扩展点"）。
    /// <para>
    /// ADR-0019 / F1b 收窄判断记录：本规则此前整条委托给 <see cref="QuestDefinition.FromRecord"/>
    /// 解析、把任意解析异常（含纯结构性问题：<c>type</c> 缺失/非法、<c>target_ref</c> 格式错、
    /// <c>count</c> 非整数、<c>start_method</c>/<c>turn_in_method</c>/<c>repeatable</c> 取值非法、
    /// <c>prerequisite</c>/<c>eventFilter</c> Expr 语法错等）打包成一条泛化的"quest.def 解析失败"
    /// 消息报告。<c>QuestSchemas.ObjectiveItemSchema</c>/<c>RewardsFields</c>（F1b 首批登记）已经把
    /// 这些纯结构检查登记为机器可读的 <c>Fields</c>/<c>Variants</c>/<c>Reference</c>/<c>Expr</c>，
    /// <c>DataRegistry</c> 加载期已经独立、逐字段地报出
    /// <c>required_field</c>/<c>field_type</c>/<c>variant_discriminator</c>/<c>reference_integrity</c>/
    /// <c>expr_parsable</c>——若本规则继续整条委托 <c>FromRecord</c>，会对同一处缺陷重复报告一次
    /// （任务书"同一缺陷不得双报"）。本规则因此收窄为只做登记表达不了的纯业务判断（数值范围/等值
    /// 约束一类），全部直接读取原始 JSON，不再调用 <c>FromRecord</c>/<c>ExprParser</c>：
    /// </para>
    /// <list type="bullet">
    /// <item><c>objectives</c> 数组至少一条（<c>QuestDefinition</c> 构造期硬约束，
    /// <see cref="FieldKind.Array"/> 不表达最小长度）——<c>objectives_min_count</c>。</item>
    /// <item><c>objectives[].count</c> 必须为正整数（<c>QuestObjective</c> 构造期硬约束，
    /// <see cref="FieldKind.Int"/> 不区分正负）——<c>objective_count_positive</c>。</item>
    /// <item><c>objectives[].type</c> 为 <c>explore</c>/<c>escort</c>/<c>talk</c> 时 <c>count</c> 必须
    /// 恒为 1（业务判断，见 <see cref="QuestObjectiveTypes.RequiresCountOne"/>；<c>Fields</c> 无法
    /// 表达"某取值下数值必须等于常量"）——<c>objective_count_must_be_one</c>。</item>
    /// <item><c>objectives[].type</c> 为 <c>kill</c>/<c>escort</c> 时 <c>target_ref</c> 的 domain 段
    /// 必须是 <c>"creature"</c>（<c>QuestObjectiveTypes.RequiredTargetDomain</c> 判断记录；
    /// <c>QuestSchemas.ObjectiveItemSchema</c> 这两处退回 <see cref="FieldKind.Id"/> 而不是更强的
    /// <see cref="FieldKind.Reference"/>，理由见该类型判断记录：避免与既有测试
    /// <c>Tests.Gameplay.Assembly.GameplayAssemblyOwnerDayVendorExtensionPointTests</c> 冲突）——
    /// <c>objective_target_domain_mismatch</c>。</item>
    /// <item><c>rewards.xp</c>/<c>rewards.talent_points</c> 不能为负数（<c>RewardBundle</c> 构造期
    /// 硬约束）——<c>reward_xp_non_negative</c>/<c>reward_talent_points_non_negative</c>。</item>
    /// <item><c>rewards.items[].count</c> 必须为正整数（<c>ItemStack</c> 构造期硬约束）——
    /// <c>reward_item_count_positive</c>。</item>
    /// <item><c>rewards.world_flags[].value</c> 必须存在（<c>RewardBundle.ParseWorldFlags</c> 硬
    /// 约束；<c>value</c> 是 Bool｜Number｜Id 联合类型，<see cref="FieldKind"/> 无法表达，
    /// <c>QuestSchemas.RewardsFields</c> 未登记该子字段）——<c>reward_world_flag_value_required</c>。</item>
    /// </list>
    /// <para>
    /// <paramref name="exprSchema"/> 构造参数保留只是为了不改动
    /// <c>GameplaySchemaCatalog.RegisterQuestSchemas</c> 现有调用点的签名
    /// （<c>new QuestContentValidationRule(exprSchema)</c>）——本规则收窄后不再需要用它做任何 Expr
    /// 解析：<c>prerequisite</c>（顶层）/<c>eventFilter</c>（<c>event</c> 变体分支）的
    /// <c>expr_parsable</c> 检查已经由 <c>DataRegistry</c> 的顶层/子结构字段登记覆盖，且用的是同一份
    /// <c>exprSchema</c> 实例（<c>GameplaySchemaCatalog.RegisterQuestSchemas</c> 把
    /// <c>FullExprSchema</c> 同时传给 <c>DataRegistryOptions.ExprSchema</c> 与本规则构造函数），不会
    /// 产生解析行为分歧。
    /// </para>
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方（组装层或本模块测试）需要显式
    /// <c>registry.RegisterValidationRule(new QuestContentValidationRule())</c>（惯例同
    /// <c>core/carriers/creature</c> 的 <c>CreatureContentValidationRule</c>）。
    /// </para>
    /// </summary>
    public sealed class QuestContentValidationRule : IValidationRule
    {
        public QuestContentValidationRule(IExprSchema? exprSchema = null)
        {
            // 见类型顶部判断记录：参数只保留用于构造签名兼容，本规则收窄后不再使用它。
            _ = exprSchema;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains(QuestSchemas.Def.Name))
            {
                yield break;
            }

            foreach (var record in view.GetAll(QuestSchemas.Def.Name))
            {
                foreach (var issue in ValidateObjectives(record))
                {
                    yield return issue;
                }

                foreach (var issue in ValidateRewards(record))
                {
                    yield return issue;
                }
            }
        }

        private static IEnumerable<ValidationIssue> ValidateObjectives(DataRecord record)
        {
            // 数组本身缺失/类型不对已由 required_field/field_type 结构校验报告，这里只处理"数组
            // 已经确认存在"之后登记表达不了的业务判断。
            if (!record.TryGetArray("objectives", out var objectives))
            {
                yield break;
            }

            if (objectives.Count == 0)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, QuestSchemas.Def.Name, "objectives_min_count",
                    "objectives 至少需要一条目标", recordKey: record.Key, field: "objectives");
            }

            for (var i = 0; i < objectives.Count; i++)
            {
                if (!(objectives[i] is JsonObject o))
                {
                    continue; // 元素不是对象已由 field_type 报告。
                }

                var fieldPrefix = $"objectives[{i}]";

                var hasType = o.TryGetValue("type", out var typeRaw) && typeRaw is JsonString typeStr;
                var type = hasType ? ((JsonString)typeRaw!).Value : null;

                if (o.TryGetValue("count", out var countRaw) && countRaw is JsonNumber countNum &&
                    countNum.TryGetInt64(out var count))
                {
                    if (count <= 0)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, QuestSchemas.Def.Name, "objective_count_positive",
                            "objectives[].count 必须为正数", recordKey: record.Key, field: $"{fieldPrefix}.count");
                    }
                    else if (type == "explore" || type == "escort" || type == "talk")
                    {
                        if (count != 1)
                        {
                            yield return new ValidationIssue(
                                ValidationSeverity.Error, QuestSchemas.Def.Name, "objective_count_must_be_one",
                                $"目标类型 \"{type}\" 的 count 必须恒为 1", recordKey: record.Key, field: $"{fieldPrefix}.count");
                        }
                    }
                }
                // count 缺失/非数值已由 required_field/field_type 报告，这里不重复处理。

                if (type == "kill" || type == "escort")
                {
                    if (o.TryGetValue("target_ref", out var targetRaw) && targetRaw is JsonString targetStr &&
                        Id.TryParse(targetStr.Value, out var targetId) &&
                        !string.Equals(targetId.Domain, "creature", System.StringComparison.Ordinal))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, QuestSchemas.Def.Name, "objective_target_domain_mismatch",
                            $"目标类型 \"{type}\" 的 target_ref 域名必须是 \"creature\"，实际为 \"{targetId.Domain}\"",
                            recordKey: record.Key, field: $"{fieldPrefix}.target_ref");
                    }
                    // target_ref 缺失/格式非法已由 required_field/field_type（FieldKind.Id）报告。
                }
            }
        }

        private static IEnumerable<ValidationIssue> ValidateRewards(DataRecord record)
        {
            if (!record.TryGetObject("rewards", out var rewards))
            {
                yield break;
            }

            if (rewards.TryGetValue("xp", out var xpRaw) && xpRaw is JsonNumber xpNum && xpNum.Value < 0)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, QuestSchemas.Def.Name, "reward_xp_non_negative",
                    "rewards.xp 不能为负数", recordKey: record.Key, field: "rewards.xp");
            }

            if (rewards.TryGetValue("talent_points", out var tpRaw) && tpRaw is JsonNumber tpNum && tpNum.Value < 0)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, QuestSchemas.Def.Name, "reward_talent_points_non_negative",
                    "rewards.talent_points 不能为负数", recordKey: record.Key, field: "rewards.talent_points");
            }

            if (rewards.TryGetValue("items", out var itemsRaw) && itemsRaw is JsonArray items)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    if (!(items[i] is JsonObject item)) continue;
                    if (item.TryGetValue("count", out var countRaw) && countRaw is JsonNumber countNum &&
                        countNum.TryGetInt64(out var count) && count <= 0)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, QuestSchemas.Def.Name, "reward_item_count_positive",
                            "rewards.items[].count 必须为正数", recordKey: record.Key, field: $"rewards.items[{i}].count");
                    }
                }
            }

            if (rewards.TryGetValue("world_flags", out var flagsRaw) && flagsRaw is JsonArray flags)
            {
                for (var i = 0; i < flags.Count; i++)
                {
                    if (!(flags[i] is JsonObject flag)) continue;
                    if (!flag.TryGetValue("value", out var valueRaw) || valueRaw.Kind == JsonKind.Null)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, QuestSchemas.Def.Name, "reward_world_flag_value_required",
                            "rewards.world_flags[].value 缺失", recordKey: record.Key, field: $"rewards.world_flags[{i}].value");
                        continue;
                    }

                    // P2-06 根治：此前只检查"存在"，value=[] 一类不属于 ExprValueJson.Parse 联合
                    // 类型（Bool|Number|String|{"$id":...}）的形状在这里 0 error，直到
                    // RewardBundle.ParseWorldFlags → ExprValueJson.Parse 才抛 FormatException（外部
                    // 审计）。ExprValueJson.IsValid 直接复用 Parse 本身的判别逻辑，不重复维护第二份，
                    // 保证本处校验接受的形状集合与运行期解析永远一致。
                    if (!Core.Gameplay.Common.ExprValueJson.IsValid(valueRaw))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, QuestSchemas.Def.Name, "reward_world_flag_value_shape",
                            $"rewards.world_flags[].value 不是合法的 ExprValue 形状（Bool|Number|String|{{\"$id\":...}}），实际类型：{valueRaw.Kind}",
                            recordKey: record.Key, field: $"rewards.world_flags[{i}].value");
                    }
                }
            }
        }
    }
}
