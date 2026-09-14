using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// <c>stat.definition</c> 的模块专属校验规则（见 04_数据与内容管线.md 第 5 节校验器扩展点、
    /// 任务书"提供 IValidationRule：rating_conversion_ref 存在时 is_rating 必须为 true；
    /// min &lt;= max"）。调用方需要
    /// <c>registry.RegisterValidationRule(new StatDefinitionValidationRule())</c> 才会生效，
    /// 本模块不自动注册（同 <see cref="StatSchemas"/>，注册时机由宿主统一掌控）。
    /// </summary>
    public sealed class StatDefinitionValidationRule : IValidationRule
    {
        public const string CheckMinMaxOrder = "stat_min_max_order";

        public const string CheckRatingRefRequiresIsRating = "stat_rating_ref_requires_is_rating";

        /// <summary>分阶段落地计划 T-N1-1（ADR-0030 决策 1；06 第 1.3 节字段表"derived_from | 仅
        /// derived"）：检查名——04 第 5 节数值类校验项分级表未逐条列出本项（只列了"派生无环"这一条，
        /// 见该表），按任务派发提示词"契约未给检查名，用 stat_definition_* 前缀"处理。设计层裁定
        /// （2026-09-14）：采纳。</summary>
        public const string CheckDerivedFromRequiresDerivedCategory = "stat_definition_derived_from_requires_derived";

        /// <summary>分阶段落地计划 T-N1-1（ADR-0030 决策 1；06 第 1.3 节字段表"conversion_ref | 仅
        /// percent"）：检查名同 <see cref="CheckDerivedFromRequiresDerivedCategory"/> 判断记录——
        /// 04 第 5 节分级表未列出本项，按 stat_definition_* 前缀命名。设计层裁定（2026-09-14）：
        /// 采纳。</summary>
        public const string CheckConversionRefRequiresPercentCategory = "stat_definition_conversion_ref_requires_percent";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var records = view.GetAll("stat.definition");
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];

                var hasMin = record.TryGetNumber("min", out var min);
                var hasMax = record.TryGetNumber("max", out var max);
                if (hasMin && hasMax && min > max)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "stat.definition", CheckMinMaxOrder,
                        $"min ({min}) 大于 max ({max})", recordKey: record.Key, field: "min");
                }

                // T-N1-2 补充：CheckMinMaxOrder 原本只查平级（废弃）min/max——一条只填新字段
                // clamp{min,max}、完全不带平级 min/max 的纯 v2 记录会绕过上面这条检查，
                // clamp.min > clamp.max 却拿不到任何 Error（StatHost.ComputeFinal 在第二轮聚合
                // 之后夹取时，Math.Max 再 Math.Min 的顺序会把取值恒定收敛到 clamp.max，属于
                // "内容错误被静默降级"的同款模式）。检查名沿用同一个 CheckMinMaxOrder——两处校验的
                // 是同一条语义不变量（下限不得大于上限），只是承载字段从平级迁到嵌套，不新增检查名。
                if (record.TryGetObject("clamp", out var clampObj))
                {
                    var hasClampMin = clampObj.TryGetValue("min", out var clampMinRaw) && clampMinRaw is JsonNumber;
                    var hasClampMax = clampObj.TryGetValue("max", out var clampMaxRaw) && clampMaxRaw is JsonNumber;
                    if (hasClampMin && hasClampMax)
                    {
                        var clampMin = ((JsonNumber)clampMinRaw).Value;
                        var clampMax = ((JsonNumber)clampMaxRaw).Value;
                        if (clampMin > clampMax)
                        {
                            yield return new ValidationIssue(
                                ValidationSeverity.Error, "stat.definition", CheckMinMaxOrder,
                                $"clamp.min ({clampMin}) 大于 clamp.max ({clampMax})", recordKey: record.Key, field: "clamp.min");
                        }
                    }
                }

                var hasRatingRef = record.TryGetId("rating_conversion_ref", out _);
                var isRating = record.TryGetBool("is_rating", out var isRatingValue) && isRatingValue;
                if (hasRatingRef && !isRating)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "stat.definition", CheckRatingRefRequiresIsRating,
                        "声明了 rating_conversion_ref 但 is_rating 不为 true", recordKey: record.Key,
                        field: "rating_conversion_ref");
                }

                var hasCategory = record.TryGetString("category", out var category);
                var hasDerivedFrom = record.Has("derived_from");
                if (hasDerivedFrom && (!hasCategory || category != "derived"))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "stat.definition", CheckDerivedFromRequiresDerivedCategory,
                        $"声明了 derived_from 但 category 不是 derived（当前 \"{(hasCategory ? category : "（缺失）")}\"）",
                        recordKey: record.Key, field: "derived_from");
                }

                var hasConversionRef = record.Has("conversion_ref");
                if (hasConversionRef && (!hasCategory || category != "percent"))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "stat.definition", CheckConversionRefRequiresPercentCategory,
                        $"声明了 conversion_ref 但 category 不是 percent（当前 \"{(hasCategory ? category : "（缺失）")}\"）",
                        recordKey: record.Key, field: "conversion_ref");
                }
            }
        }
    }
}
