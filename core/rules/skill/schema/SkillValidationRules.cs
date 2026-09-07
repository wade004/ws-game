using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 效果数上限校验（见 04 第 5 节"效果数上限……策略配置项，默认建议值见 06"、
    /// <see cref="SkillOptions.MaxEffectsPerSkill"/>）：<c>skill.def.effects</c> 与
    /// <c>skill.aura_def.effects</c> 两张表的效果列表长度均不得超过上限。
    /// </summary>
    public sealed class MaxEffectsPerSkillRule : IValidationRule
    {
        private readonly int _maxEffects;

        public MaxEffectsPerSkillRule(int maxEffects)
        {
            _maxEffects = maxEffects;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var issue in CheckTable(view, "skill.def"))
            {
                yield return issue;
            }

            foreach (var issue in CheckTable(view, "skill.aura_def"))
            {
                yield return issue;
            }
        }

        private IEnumerable<ValidationIssue> CheckTable(IDataRegistryView view, string table)
        {
            if (!view.Tables.Contains(table))
            {
                yield break;
            }

            foreach (var record in view.GetAll(table))
            {
                if (!record.TryGetArray("effects", out var effects))
                {
                    continue;
                }

                if (effects.Count > _maxEffects)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, table, "max_effects_per_skill",
                        $"效果列表长度 {effects.Count} 超过上限 {_maxEffects}", recordKey: record.Key, field: "effects");
                }
            }
        }
    }

    /// <summary>
    /// 叠加类别冲突校验（见 04 第 5 节"叠加类别冲突：同一叠加类别下不得有两条效果同时要求互斥的
    /// 叠加语义"）。判断记录：06 第 3.8 节未给出"互斥的叠加语义"的精确定义，本模块取最小可判定
    /// 解释——同一 <c>stack_category</c> 下，若同时存在 <c>max_stacks > 1</c>（声明"可叠加"）与
    /// <c>max_stacks == 1</c>（声明"不可叠加，仅可刷新"）两种记录，视为互斥冲突。
    /// </summary>
    public sealed class StackCategoryConflictRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.aura_def"))
            {
                yield break;
            }

            var stackableCategories = new Dictionary<string, string>();
            var nonStackableCategories = new Dictionary<string, string>();

            foreach (var record in view.GetAll("skill.aura_def"))
            {
                if (!record.TryGetId("stack_category", out var category))
                {
                    continue;
                }

                var maxStacks = 1L;
                record.TryGetInt("max_stacks", out maxStacks);
                if (maxStacks <= 0)
                {
                    maxStacks = 1;
                }

                if (maxStacks > 1)
                {
                    stackableCategories[category.Value] = record.Key;
                }
                else
                {
                    nonStackableCategories[category.Value] = record.Key;
                }
            }

            foreach (var pair in stackableCategories)
            {
                if (nonStackableCategories.TryGetValue(pair.Key, out var otherKey))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.aura_def", "stack_category_conflict",
                        $"叠加类别 \"{pair.Key}\" 同时存在 max_stacks>1（{pair.Value}）与 max_stacks==1（{otherKey}）两条互斥记录",
                        recordKey: pair.Value, field: "stack_category");
                }
            }
        }
    }

    /// <summary>
    /// 效果原语已登记校验：<c>skill.def.effects[].kind</c> 必须是 <see cref="EffectKindNames"/>
    /// 已登记的 snake_case 名字，<c>skill.aura_def.effects[].kind</c> 必须是
    /// <see cref="AuraEffectKindNames"/> 已登记的名字（见 04 第 5 节"枚举合法"、ADR-0010"新增原语
    /// 走审批"）。
    /// </summary>
    public sealed class EffectKindRegisteredRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (view.Tables.Contains("skill.def"))
            {
                foreach (var record in view.GetAll("skill.def"))
                {
                    if (!record.TryGetArray("effects", out var effects))
                    {
                        continue;
                    }

                    for (var i = 0; i < effects.Count; i++)
                    {
                        foreach (var issue in CheckEntry(
                            "skill.def", record.Key, i, effects[i],
                            s => EffectKindNames.TryParse(s, out _), "unknown_effect_kind", "EffectKind"))
                        {
                            yield return issue;
                        }
                    }
                }
            }

            if (view.Tables.Contains("skill.aura_def"))
            {
                foreach (var record in view.GetAll("skill.aura_def"))
                {
                    if (!record.TryGetArray("effects", out var effects))
                    {
                        continue;
                    }

                    for (var i = 0; i < effects.Count; i++)
                    {
                        foreach (var issue in CheckEntry(
                            "skill.aura_def", record.Key, i, effects[i],
                            s => AuraEffectKindNames.TryParse(s, out _), "unknown_aura_effect_kind", "AuraEffectKind"))
                        {
                            yield return issue;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// R09 收边补齐（外部审计 5e779c6，P2）：修复前本方法只在"整条 effects[i] 是 JsonObject 且
        /// 已经有一个字符串类型的 kind 字段"这个前提全部成立时才检查"这个字符串是否已登记"——
        /// <c>effects[i]</c> 根本不是对象（如数组里混进了一个裸数字/字符串）、<b>没有</b> <c>kind</c>
        /// 字段、或 <c>kind</c> 字段存在但不是字符串这三种更基础的坏形状，判断条件里的
        /// <c>is JsonObject</c>/<c>TryGetValue</c>/<c>is JsonString</c> 短路失败后直接跳过、不产生
        /// 任何校验问题——这些数据能完整通过 <c>DataRegistry.LoadAll()</c>（0 error 0 warning），但
        /// <c>SkillDefCache.ParseSkillDef</c>/<c>ParseAuraDef</c>（见该类型判断记录"懒解析，命中一次
        /// 后常驻内存"——只在这个技能/光环第一次真正被解析时才跑到这段代码，通常就是"这个技能第一次
        /// 被施放"那一刻）对同一个 <c>effects[i]</c> 做的是无防御的直接类型转换
        /// （<c>(JsonObject)array[i]</c>/<c>((JsonString)obj["kind"]).Value</c>），命中上述任一坏形状
        /// 都会抛 <see cref="System.InvalidCastException"/>/<see cref="System.Collections.Generic.KeyNotFoundException"/>
        /// ——外部审计 R09 复现场景本身（"技能嵌套坏数据通过校验，使用时才抛异常"）。现在改为逐层
        /// 显式检查，坏形状本身也各自产生一条校验问题，不再依赖后续检查的短路副作用。
        /// </summary>
        private static IEnumerable<ValidationIssue> CheckEntry(
            string table, string recordKey, int index, JsonValue entry,
            System.Func<string, bool> isKnownKind, string unknownKindCheck, string kindTypeName)
        {
            if (!(entry is JsonObject obj))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, table, "effect_entry_not_object",
                    $"effects[{index}] 不是 JSON 对象（{kindTypeName} 条目必须是 {{kind, params}} 形状）",
                    recordKey: recordKey, field: "effects");
                yield break;
            }

            if (!obj.TryGetValue("kind", out var kindVal))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, table, "effect_kind_missing",
                    $"effects[{index}] 缺少必填字段 \"kind\"",
                    recordKey: recordKey, field: "effects");
                yield break;
            }

            if (!(kindVal is JsonString kindStr))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, table, "effect_kind_not_string",
                    $"effects[{index}].kind 必须是字符串",
                    recordKey: recordKey, field: "effects");
                yield break;
            }

            if (!isKnownKind(kindStr.Value))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, table, unknownKindCheck,
                    $"effects[{index}].kind \"{kindStr.Value}\" 不是已登记的 {kindTypeName}",
                    recordKey: recordKey, field: "effects");
            }
        }
    }

    /// <summary>
    /// <c>cast_time</c>/<c>channel_time</c> 互斥校验（见 06 第 3.1 节
    /// "channel_time……与 cast_time 互斥语义"）：不得同时非零。
    /// </summary>
    public sealed class CastTimeChannelTimeExclusiveRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                var castTime = record.TryGetNumber("cast_time", out var ct) ? ct : 0;
                var channelTime = record.TryGetNumber("channel_time", out var cht) ? cht : 0;

                if (castTime != 0 && channelTime != 0)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "cast_time_channel_time_exclusive",
                        "cast_time 与 channel_time 不得同时非零", recordKey: record.Key);
                }
            }
        }
    }

    /// <summary>被动技能不得声明 <c>cast_time</c>（见 06 第 3.1 节 <c>kind</c> 字段说明"被动技能……
    /// 不可主动施放"）。</summary>
    public sealed class PassiveSkillNoCastTimeRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                if (!record.TryGetString("kind", out var kind) || kind != "passive")
                {
                    continue;
                }

                var castTime = record.TryGetNumber("cast_time", out var ct) ? ct : 0;
                if (castTime != 0)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "passive_skill_no_cast_time",
                        "kind: passive 的技能不得声明非零 cast_time", recordKey: record.Key, field: "cast_time");
                }
            }
        }
    }

    /// <summary>
    /// R06 收边补齐（外部审计 5e779c6，P2；见 <c>Core.Rules.Skill.CooldownTracker.StartCooldown</c>
    /// 判断记录）：<c>skill.def.charges.recharge_time</c> 显式登记为 <c>&lt;= 0</c> 时告警——引擎层
    /// 把 <c>recharge_time &lt;= 0</c> 统一解读为"即时恢复"（充能耗尽的下一刻立即原地补满，见
    /// <c>CooldownTracker</c> 判断记录），不是字面上可能被误读的"永久不恢复"；若数据作者本意确实是
    /// "用一次就永久没了"，<c>charges</c> 不是表达这个意图的正确字段（<c>charges.max</c> 恒定为槽位
    /// 上限，没有"永久耗尽"的语义），需要改用其它机制（如学会即用掉的消耗品/一次性任务奖励）。只是
    /// 提醒复核，不阻断——0 在"即时恢复"这个引擎语义下是合法值，不是数据错误，见
    /// <see cref="ValidationSeverity.Warning"/> 语义（04 第 5 节 Warning 是否阻断取决于
    /// <c>DataRegistryOptions.Strictness</c>）。
    /// </summary>
    /// <summary>
    /// R09 收边补齐（外部审计 5e779c6，P2）：<c>skill.def.charges</c> 顶层字段本身是 <c>required:
    /// false</c> 的 <see cref="FieldKind.Object"/>（见 <c>SkillSchemas.Def</c>），一旦声明就必须是
    /// <c>{max: Int, recharge_time: Number}</c> 形状（见该字段 <c>description</c>）——但 schema 层
    /// 只声明到"这是个对象"这一层，不深入校验对象内部的两个子字段是否存在/类型是否正确。
    /// <c>SkillDefCache.ParseSkillDef</c>（懒解析，只在这个技能第一次真正被解析/施放时才跑，见该
    /// 类型判断记录）对 <c>charges.max</c>/<c>charges.recharge_time</c> 做的是无防御的直接类型转换
    /// （<c>(int)((JsonNumber)chargesObj["max"]).Value</c>），任一子字段缺失或类型不对都会抛
    /// <see cref="System.Collections.Generic.KeyNotFoundException"/>/<see cref="System.InvalidCastException"/>
    /// ——本条是外部审计 R09"技能嵌套坏数据通过校验，使用时才抛异常"的具体复现样例：一条
    /// <c>{"id": "...", ..., "charges": {"max": 1}}</c>（漏填 <c>recharge_time</c>）的
    /// <c>skill.def</c> 记录能完整通过 <c>DataRegistry.LoadAll()</c>（0 error），只在这个技能第一次
    /// 被施放（或任何其它触发 <c>SkillDefCache.GetSkillDef</c>/<c>TryGetSkillDef</c> 的路径）时才
    /// 崩溃。
    /// </summary>
    public sealed class ChargesShapeRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                if (!record.TryGetObject("charges", out var charges))
                {
                    continue;
                }

                if (!charges.TryGetValue("max", out var maxVal))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "charges_max_missing",
                        "charges 已声明但缺少必填子字段 \"max\"", recordKey: record.Key, field: "charges.max");
                }
                else if (!(maxVal is JsonNumber maxNum) || maxNum.Value < 1 || maxNum.Value != System.Math.Floor(maxNum.Value))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "charges_max_invalid",
                        "charges.max 必须是 >= 1 的整数", recordKey: record.Key, field: "charges.max");
                }

                if (!charges.TryGetValue("recharge_time", out var rechargeVal))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "charges_recharge_time_missing",
                        "charges 已声明但缺少必填子字段 \"recharge_time\"", recordKey: record.Key, field: "charges.recharge_time");
                }
                else if (!(rechargeVal is JsonNumber))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "charges_recharge_time_not_number",
                        "charges.recharge_time 必须是数字", recordKey: record.Key, field: "charges.recharge_time");
                }
            }
        }
    }

    /// <summary>
    /// R09 收边补齐（外部审计 5e779c6，P2）：<c>skill.def.cost[]</c> 每一项要求
    /// <c>{power_type: Id, amount: Number}</c>（见 06 第 3.1 节 <c>cost</c> 字段），
    /// <c>SkillDefCache.ParseSkillDef</c> 对每一项同样是无防御直接类型转换
    /// （<c>((JsonString)entry["power_type"]).Value</c>/<c>((JsonNumber)entry["amount"]).Value</c>），
    /// 缺字段/类型错的 <c>cost</c> 条目同样能通过加载校验、只在这个技能第一次被解析时崩溃，与
    /// <see cref="ChargesShapeRule"/> 判断记录同一类问题、同一处修复动机。
    /// </summary>
    public sealed class CostEntryShapeRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                if (!record.TryGetArray("cost", out var cost))
                {
                    continue;
                }

                for (var i = 0; i < cost.Count; i++)
                {
                    if (!(cost[i] is JsonObject entry))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "skill.def", "cost_entry_not_object",
                            $"cost[{i}] 不是 JSON 对象（必须是 {{power_type, amount}} 形状）",
                            recordKey: record.Key, field: "cost");
                        continue;
                    }

                    if (!entry.TryGetValue("power_type", out var powerTypeVal) || !(powerTypeVal is JsonString powerTypeStr)
                        || !Id.TryParse(powerTypeStr.Value, out _))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "skill.def", "cost_power_type_invalid",
                            $"cost[{i}].power_type 缺失或不是合法 Id 字符串",
                            recordKey: record.Key, field: "cost");
                    }

                    if (!entry.TryGetValue("amount", out var amountVal) || !(amountVal is JsonNumber))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "skill.def", "cost_amount_invalid",
                            $"cost[{i}].amount 缺失或不是数字",
                            recordKey: record.Key, field: "cost");
                    }
                }
            }
        }
    }

    public sealed class ChargesRechargeTimeZeroWarningRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                if (!record.TryGetObject("charges", out var charges))
                {
                    continue;
                }

                if (!charges.TryGetValue("recharge_time", out var rechargeRaw) || !(rechargeRaw is JsonNumber rechargeNum))
                {
                    continue;
                }

                if (rechargeNum.Value <= 0)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Warning, "skill.def", "charges_recharge_time_zero",
                        "charges.recharge_time <= 0 会被引擎解读为\"即时恢复\"（充能不会真正耗尽），" +
                        "若本意是\"永久不再恢复\"请改用其它机制表达，不是把这里填 0",
                        recordKey: record.Key, field: "charges.recharge_time");
                }
            }
        }
    }
}
