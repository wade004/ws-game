using System.Collections.Generic;
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

                var hasRatingRef = record.TryGetId("rating_conversion_ref", out _);
                var isRating = record.TryGetBool("is_rating", out var isRatingValue) && isRatingValue;
                if (hasRatingRef && !isRating)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "stat.definition", CheckRatingRefRequiresIsRating,
                        "声明了 rating_conversion_ref 但 is_rating 不为 true", recordKey: record.Key,
                        field: "rating_conversion_ref");
                }
            }
        }
    }
}
