using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// <c>stat.rating_conversion</c> 的模块专属校验规则（分阶段落地计划 T-N1-3；ADR-0030 决策 3；
    /// 04 第 3.6 节；数值设计 01 第 5 节"三种曲线形态"）：一条记录必须<b>恰好</b>登记 <c>entries</c>
    /// （断点表，"标准版：等级索引除数"形态）或 <c>saturation</c>（二元饱和，"变态版：饱和曲线，除数
    /// 随等级增长"形态）之一——不得两者都缺（这样一条记录没有任何求值方式，<see cref="StatHost"/>
    /// 的 <see cref="StatHost.ConvertRating"/> 找不到已知形态时会直通原值，等价于内容错误被静默
    /// 降级，提前到加载期拦下），也不得两者都填（内容意图不明确，两种形态给出不同结果）。
    /// <para>
    /// 判断记录（检查名未见于 04 第 5 节数值类校验项分级表逐条列出）：分级表只列了"曲线单调有限"
    /// 这类通用规则，本表形态二选一属于 <c>stat.rating_conversion</c> 表自身的结构性约束，按
    /// <c>StatDefinitionValidationRule</c>/<c>StatDefinitionDerivationCycleValidationRule</c> 已有的
    /// "契约未给检查名、用 <c>stat_*</c> 前缀"命名惯例处理，已在任务汇报标注"待设计层确认"。
    /// </para>
    /// <para>
    /// 调用方需要 <c>registry.RegisterValidationRule(new StatRatingConversionValidationRule())</c>
    /// 才会生效，本模块不自动注册（同 <see cref="StatDefinitionValidationRule"/>，注册时机由宿主
    /// 统一掌控）。
    /// </para>
    /// </summary>
    public sealed class StatRatingConversionValidationRule : IValidationRule
    {
        /// <summary>检查名（判断记录见类型顶部，待设计层确认）。</summary>
        public const string CheckRequiresExactlyOneShape = "stat_rating_conversion_requires_one_shape";

        public string RuleId => nameof(StatRatingConversionValidationRule);

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public bool NonEscalatable => false;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var records = view.GetAll("stat.rating_conversion");
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var hasEntries = record.Has("entries");
                var hasSaturation = record.Has("saturation");

                if (hasEntries == hasSaturation)
                {
                    var reason = hasEntries
                        ? "entries 与 saturation 不能同时登记（两种形态二选一，见 04 第 3.6 节）"
                        : "必须登记 entries 或 saturation 之一（两种形态二选一，见 04 第 3.6 节），" +
                            "两者都缺会让本记录没有任何求值方式";
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "stat.rating_conversion", CheckRequiresExactlyOneShape,
                        reason, recordKey: record.Key, field: "entries");
                }
            }
        }
    }
}
