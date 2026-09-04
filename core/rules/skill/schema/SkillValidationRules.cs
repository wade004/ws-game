using System.Collections.Generic;
using System.Linq;
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
                        if (effects[i] is JsonObject obj && obj.TryGetValue("kind", out var kindVal)
                            && kindVal is JsonString kindStr && !EffectKindNames.TryParse(kindStr.Value, out _))
                        {
                            yield return new ValidationIssue(
                                ValidationSeverity.Error, "skill.def", "unknown_effect_kind",
                                $"effects[{i}].kind \"{kindStr.Value}\" 不是已登记的 EffectKind",
                                recordKey: record.Key, field: "effects");
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
                        if (effects[i] is JsonObject obj && obj.TryGetValue("kind", out var kindVal)
                            && kindVal is JsonString kindStr && !AuraEffectKindNames.TryParse(kindStr.Value, out _))
                        {
                            yield return new ValidationIssue(
                                ValidationSeverity.Error, "skill.aura_def", "unknown_aura_effect_kind",
                                $"effects[{i}].kind \"{kindStr.Value}\" 不是已登记的 AuraEffectKind",
                                recordKey: record.Key, field: "effects");
                        }
                    }
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
}
