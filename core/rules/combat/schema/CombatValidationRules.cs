using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Rules.Combat
{
    /// <summary>
    /// <c>combat.hit_table_config</c> 校验规则（见落地方案 T2-7 行"命中/闪避/暴击三分支各有
    /// 测试""校验规则：概率 base 落在 [0,1]"）。六个分支各自的 <c>base</c> 字段必须落在 [0,1]；
    /// <c>crit_multiplier_base</c>/<c>glancing_damage_pct</c> 若提供须为非负数。
    /// </summary>
    public sealed class CombatHitTableValidationRule : IValidationRule
    {
        private const string Table = "combat.hit_table_config";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var issues = new List<ValidationIssue>();
            if (view.GetSchema(Table) == null)
            {
                return issues;
            }

            foreach (var record in view.GetAll(Table))
            {
                foreach (var field in CombatSchemas.HitTableBranchFields)
                {
                    if (!record.Has(field))
                    {
                        continue;
                    }

                    var branch = record.GetObject(field);
                    if (branch.TryGetValue("base", out var baseVal) && baseVal is JsonNumber baseNum)
                    {
                        if (baseNum.Value < 0.0 || baseNum.Value > 1.0)
                        {
                            issues.Add(new ValidationIssue(
                                ValidationSeverity.Error, Table, "hit_table_base_range",
                                $"{field}.base 必须落在 [0,1]，实际 {baseNum.Value}",
                                record.Key, $"{field}.base"));
                        }
                    }
                }

                if (record.TryGetNumber("crit_multiplier_base", out var critMul) && critMul < 0.0)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, Table, "hit_table_crit_multiplier_base_range",
                        $"crit_multiplier_base 不能为负数，实际 {critMul}", record.Key, "crit_multiplier_base"));
                }

                if (record.TryGetNumber("glancing_damage_pct", out var glancingPct) && glancingPct < 0.0)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, Table, "hit_table_glancing_damage_pct_range",
                        $"glancing_damage_pct 不能为负数，实际 {glancingPct}", record.Key, "glancing_damage_pct"));
                }
            }

            return issues;
        }
    }

    /// <summary>
    /// <c>combat.resist_curve</c> 校验规则（见落地方案 T2-7 行"校验规则：...max_reduction ≤ 1；
    /// 曲线 entries 单调"）：<c>max_reduction</c> 落在 [0,1]；<c>kind=table</c> 时 <c>entries</c>
    /// 非空且按 <c>value</c> 严格递增、<c>reduction</c> 不递减；<c>kind=saturation</c> 时 <c>k</c>
    /// 必须提供且为正数。
    /// </summary>
    public sealed class CombatResistCurveValidationRule : IValidationRule
    {
        private const string Table = "combat.resist_curve";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var issues = new List<ValidationIssue>();
            if (view.GetSchema(Table) == null)
            {
                return issues;
            }

            foreach (var record in view.GetAll(Table))
            {
                if (record.TryGetNumber("max_reduction", out var maxReduction)
                    && (maxReduction < 0.0 || maxReduction > 1.0))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, Table, "resist_curve_max_reduction_range",
                        $"max_reduction 必须落在 [0,1]，实际 {maxReduction}", record.Key, "max_reduction"));
                }

                var kind = record.TryGetString("kind", out var kindValue) ? kindValue : null;

                if (kind == "saturation")
                {
                    if (!record.TryGetNumber("k", out var k) || k <= 0.0)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error, Table, "resist_curve_k_required",
                            "kind=saturation 时 k 必须提供且为正数", record.Key, "k"));
                    }
                }
                else if (kind == "table")
                {
                    if (!record.TryGetArray("entries", out var entries) || entries.Count == 0)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error, Table, "resist_curve_entries_required",
                            "kind=table 时 entries 不能为空", record.Key, "entries"));
                        continue;
                    }

                    double? prevValue = null;
                    double? prevReduction = null;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (!(entries[i] is JsonObject entry)
                            || !entry.TryGetValue("value", out var valueVal) || !(valueVal is JsonNumber valueNum)
                            || !entry.TryGetValue("reduction", out var reductionVal) || !(reductionVal is JsonNumber reductionNum))
                        {
                            issues.Add(new ValidationIssue(
                                ValidationSeverity.Error, Table, "resist_curve_entries_shape",
                                $"entries[{i}] 必须是 {{value: Number, reduction: Number}}", record.Key, "entries"));
                            continue;
                        }

                        if (prevValue.HasValue && valueNum.Value <= prevValue.Value)
                        {
                            issues.Add(new ValidationIssue(
                                ValidationSeverity.Error, Table, "resist_curve_entries_monotonic",
                                $"entries[{i}].value 必须严格大于前一项（{prevValue.Value} -> {valueNum.Value}）",
                                record.Key, "entries"));
                        }

                        if (prevReduction.HasValue && reductionNum.Value < prevReduction.Value)
                        {
                            issues.Add(new ValidationIssue(
                                ValidationSeverity.Error, Table, "resist_curve_entries_monotonic",
                                $"entries[{i}].reduction 不能低于前一项（{prevReduction.Value} -> {reductionNum.Value}）",
                                record.Key, "entries"));
                        }

                        prevValue = valueNum.Value;
                        prevReduction = reductionNum.Value;
                    }
                }
            }

            return issues;
        }
    }
}
