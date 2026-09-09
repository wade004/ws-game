using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Core.Gameplay.Achievement
{
    /// <summary>
    /// <c>achv.def</c> 专属内容校验规则（见 <see cref="IValidationRule"/>"模块专属校验规则的扩展
    /// 点"，惯例同 <c>core/carriers/creature</c> 的 <c>CreatureContentValidationRule</c>）。
    /// <para>
    /// ADR-0019 / F1b 收窄判断记录：本规则此前直接读取原始 JSON 做 <c>criteria[].type</c> 合法性、
    /// <c>criteria[].observe_event</c> 已登记两项检查。<c>AchievementSchemas.CriterionItemSchema</c>
    /// （F1b 首批登记）已经把 <c>type</c> 合法性登记为 <see cref="VariantSchema"/>（<c>DataRegistry</c>
    /// 加载期独立报出 <c>variant_discriminator</c>），本规则因此不再重复检查 <c>type</c>（任务书
    /// "同一缺陷不得双报"）；<c>observe_event</c> 的"缺失/格式非法"两种子情形同理已由
    /// <c>required_field</c>/<c>field_type</c> 覆盖，本规则收窄为只做"已登记事件 key 的成员资格"这
    /// 一项登记表达不了的检查（见 <c>AchievementSchemas.CriterionCommonFields</c> 判断记录：
    /// <c>found.event_catalog</c> 未必已加载，不登记为 Reference）。同时新增两项此前完全未被任何
    /// 校验覆盖、只在 <see cref="AchievementHost"/> 构造期才会暴露为异常的业务判断（见下）：
    /// </para>
    /// <list type="bullet">
    /// <item><c>criteria</c> 数组至少一条（<see cref="AchievementDefinition.FromRecord"/> 构造期硬
    /// 约束，<see cref="FieldKind.Array"/> 不表达最小长度）——<c>achv_criteria_min_count</c>。</item>
    /// <item><c>criteria[].observe_event</c> 必须是 <c>found.event_catalog</c> 已登记的事件 key
    /// （对 <see cref="EventKeys.All"/> 编译期常量集合做成员测试）——<c>achv_observe_event_unregistered</c>。</item>
    /// <item><c>criteria[].count</c> 必须 &gt;= 1（<see cref="AchievementCriterion"/> 构造期硬约束）——
    /// <c>achv_criterion_count_positive</c>。</item>
    /// <item><c>rewards.xp</c>/<c>rewards.talent_points</c> 不能为负数（<see cref="Core.Gameplay.Common.RewardBundle"/>
    /// 构造期硬约束，与 <c>QuestContentValidationRule</c> 完全同源，因为 <c>achv.def.rewards</c> 与
    /// <c>quest.def.rewards</c> 共用同一份 <c>RewardBundle.FromRecord</c>）——
    /// <c>reward_xp_non_negative</c>/<c>reward_talent_points_non_negative</c>。</item>
    /// <item><c>rewards.items[].count</c> 必须为正整数（<c>ItemStack</c> 构造期硬约束）——
    /// <c>reward_item_count_positive</c>。</item>
    /// <item><c>rewards.world_flags[].value</c> 必须存在（<c>RewardBundle.ParseWorldFlags</c> 硬
    /// 约束；<c>value</c> 是 Bool｜Number｜Id 联合类型，<see cref="FieldKind"/> 无法表达，
    /// <c>QuestSchemas.RewardsFields</c> 未登记该子字段）——<c>reward_world_flag_value_required</c>。</item>
    /// </list>
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方（组装层或本模块测试）需要显式
    /// <c>registry.RegisterValidationRule(new AchievementContentValidationRule())</c>（惯例同
    /// <c>CreatureContentValidationRule</c>）。
    /// </para>
    /// </summary>
    public sealed class AchievementContentValidationRule : IValidationRule
    {
        private static readonly HashSet<string> RegisteredEventKeys = BuildRegisteredEventKeys();

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains(AchievementSchemas.Def.Name))
            {
                yield break;
            }

            foreach (var record in view.GetAll(AchievementSchemas.Def.Name))
            {
                foreach (var issue in ValidateCriteria(record))
                {
                    yield return issue;
                }

                foreach (var issue in ValidateRewards(record))
                {
                    yield return issue;
                }
            }
        }

        private static IEnumerable<ValidationIssue> ValidateCriteria(DataRecord record)
        {
            // 数组本身缺失/类型不对已由 required_field/field_type 结构校验报告，这里只处理"数组
            // 已经确认存在"之后登记表达不了的业务判断。
            if (!record.TryGetArray("criteria", out var criteria))
            {
                yield break;
            }

            if (criteria.Count == 0)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, AchievementSchemas.Def.Name, "achv_criteria_min_count",
                    "criteria 至少需要一条达成条件", recordKey: record.Key, field: "criteria");
            }

            for (var i = 0; i < criteria.Count; i++)
            {
                if (!(criteria[i] is JsonObject o))
                {
                    continue; // 元素不是对象已由 field_type 报告。
                }

                var fieldPrefix = $"criteria[{i}]";

                // observe_event 缺失/非字符串/格式非法已由 required_field/field_type（FieldKind.Id）
                // 报告，这里只做"已登记事件 key"这一登记表达不了的成员资格检查。
                if (o.TryGetValue("observe_event", out var observeRaw) && observeRaw is JsonString observeStr &&
                    Id.TryParse(observeStr.Value, out _) && !RegisteredEventKeys.Contains(observeStr.Value))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, AchievementSchemas.Def.Name, "achv_observe_event_unregistered",
                        $"{fieldPrefix}.observe_event 必须是 found.event_catalog 已登记的事件 key",
                        recordKey: record.Key, field: $"{fieldPrefix}.observe_event");
                }

                // count 缺失/非整数已由 required_field/field_type 报告，这里只处理数值范围。
                if (o.TryGetValue("count", out var countRaw) && countRaw is JsonNumber countNum &&
                    countNum.TryGetInt64(out var count) && count < 1)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, AchievementSchemas.Def.Name, "achv_criterion_count_positive",
                        $"{fieldPrefix}.count 必须 >= 1", recordKey: record.Key, field: $"{fieldPrefix}.count");
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
                    ValidationSeverity.Error, AchievementSchemas.Def.Name, "reward_xp_non_negative",
                    "rewards.xp 不能为负数", recordKey: record.Key, field: "rewards.xp");
            }

            if (rewards.TryGetValue("talent_points", out var tpRaw) && tpRaw is JsonNumber tpNum && tpNum.Value < 0)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, AchievementSchemas.Def.Name, "reward_talent_points_non_negative",
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
                            ValidationSeverity.Error, AchievementSchemas.Def.Name, "reward_item_count_positive",
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
                            ValidationSeverity.Error, AchievementSchemas.Def.Name, "reward_world_flag_value_required",
                            "rewards.world_flags[].value 缺失", recordKey: record.Key, field: $"rewards.world_flags[{i}].value");
                    }
                }
            }
        }

        private static HashSet<string> BuildRegisteredEventKeys()
        {
            var set = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var key in EventKeys.All)
            {
                set.Add(key.Value);
            }
            return set;
        }
    }
}
