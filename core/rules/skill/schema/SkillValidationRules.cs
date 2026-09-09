using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

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
    /// ADR-0019 / F1a 判断记录（退役说明）：本文件此前的 <c>EffectKindRegisteredRule</c>
    /// （<c>effect_entry_not_object</c>/<c>effect_kind_missing</c>/<c>effect_kind_not_string</c>/
    /// <c>unknown_effect_kind</c>/<c>unknown_aura_effect_kind</c>）、<c>CostEntryShapeRule</c>
    /// （<c>cost_entry_not_object</c>/<c>cost_power_type_invalid</c>/<c>cost_amount_invalid</c>）
    /// 两条纯结构手写规则已整条删除，<c>ChargesShapeRule</c> 的结构部分
    /// （<c>charges_max_missing</c>/<c>charges_recharge_time_missing</c>/
    /// <c>charges_recharge_time_not_number</c>）同样删除，唯一的业务判断（<c>charges.max</c> 必须
    /// &gt;= 1，FieldSchema 无法表达的数值范围约束）收窄保留为下方 <see cref="ChargesMaxAtLeastOneRule"/>：
    /// <c>SkillSchemas.Def</c>/<c>AuraDef</c> 现把 <c>effects</c> 登记为按 <c>kind</c> 分派的
    /// <see cref="Core.Foundation.DataRegistry.VariantSchema"/>、<c>charges</c>/<c>cost[]</c> 登记为
    /// 带 <c>Fields</c> 的 <see cref="Core.Foundation.DataRegistry.FieldSchema"/>，退役规则要检查的
    /// 结构性坏形状（条目不是对象/缺 kind/kind 非字符串/未登记的 kind 取值、charges 子字段缺失或
    /// 类型错、cost 条目缺失或类型错）全部由 <c>DataRegistry</c> 的递归结构校验以
    /// <c>required_field</c>/<c>field_type</c>/<c>variant_discriminator</c> 三个既有/新增检查名覆盖，
    /// 不再需要平行的手写规则（避免同一缺陷双报，也避免登记与手写规则各自维护一份"合法 kind 集合"
    /// 造成的漂移）。原测试已改写为直接对 <c>SkillSchemas</c> 断言（见
    /// <c>SkillValidationNestedGapTests.cs</c>、<c>SkillSchemaCoverageTests.cs</c>）。
    /// </summary>
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
    /// ADR-0019 / F1a 判断记录（<c>ChargesShapeRule</c> 退役后的收窄版，见本文件顶部退役说明）：
    /// <c>charges.max</c> 是充能槽位上限，语义上必须 &gt;= 1（0 或负数没有"最多同时持有 N 次充能"
    /// 的意义）——这条数值范围约束不是 <see cref="Core.Foundation.DataRegistry.FieldSchema.Fields"/>
    /// 能表达的"存在/类型/枚举"结构（登记层只保证 <c>max</c> 是 <see cref="FieldKind.Int"/>，0 与
    /// 负数同样是合法 Int），保留为一条纯粹的业务判断，呼应 04 第 3.2 节"各模块原有的手写结构校验
    /// 规则只保留登记表达不了的业务判断"。<c>max</c> 缺失或类型不对时结构层已经报过
    /// <c>required_field</c>/<c>field_type</c>，本规则跳过、不重复报告。
    /// </summary>
    public sealed class ChargesMaxAtLeastOneRule : IValidationRule
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

                if (!charges.TryGetValue("max", out var maxVal) || !(maxVal is JsonNumber maxNum))
                {
                    continue;
                }

                if (maxNum.Value < 1 || maxNum.Value != System.Math.Floor(maxNum.Value))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "charges_max_invalid",
                        "charges.max 必须是 >= 1 的整数", recordKey: record.Key, field: "charges.max");
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
