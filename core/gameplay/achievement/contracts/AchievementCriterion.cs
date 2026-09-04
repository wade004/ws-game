using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Achievement
{
    /// <summary>
    /// 一条达成条件的强类型视图（见 08 第 6.1 节 <c>Criterion</c> 结构 + 任务书拍板补录
    /// <c>filter?(Expr)</c>）。只做字段抽取，<c>filter</c> 字段的 Expr 文本在此不解析——解析需要
    /// 一份 <see cref="Core.Foundation.Expr.IExprSchema"/>，属于 <see cref="AchievementHost"/>
    /// 构造期的职责（惯例同 <c>core/rules/ai</c> 的 <c>AiHost</c>："本任务模块自有的 FromRecord 只做
    /// 结构抽取，Expr 解析放到持有具体 schema 的 Host 构造函数里"）。
    /// </summary>
    public sealed class AchievementCriterion
    {
        public CriterionType Type { get; }

        /// <summary>要观察的具体事件 key（06/08 事件词汇表中的登记事件，见
        /// <see cref="AchievementContentValidationRule"/> 对 <c>Core.Foundation.EventBus.EventKeys.All</c>
        /// 的校验）。</summary>
        public Id ObserveEvent { get; }

        /// <summary>匹配目标（生物模板/物品模板/任务/触发器/技能 id，视 <see cref="Type"/> 而定）；
        /// <see cref="CriterionType.CustomEvent"/> 不使用该字段，可为空。</summary>
        public Id? TargetRef { get; }

        /// <summary>达成所需累计次数/数量。</summary>
        public int Count { get; }

        /// <summary>补充匹配条件的 Expr 文本（任务书拍板补录，见 08 第 6.1 节
        /// <c>custom_event</c> 类型"由内容作者指定要观察的具体事件类型与匹配条件（Expr）"——本类
        /// 型把该约定推广为全部六种类型均可选携带一条补充过滤表达式，<see cref="CriterionType.CustomEvent"/>
        /// 之外的五种类型在各自的基础匹配规则通过之后再叠加求值本表达式，均为真才计入一次进度）。
        /// 未提供时为 null。</summary>
        public string? FilterText { get; }

        public AchievementCriterion(CriterionType type, Id observeEvent, Id? targetRef, int count, string? filterText)
        {
            Type = type;
            ObserveEvent = observeEvent;
            TargetRef = targetRef;
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "count 必须 >= 1");
            }
            Count = count;
            FilterText = filterText;
        }

        public static AchievementCriterion FromRecord(DataRecord record, JsonObject criterionJson, int index)
        {
            if (!criterionJson.TryGetValue("type", out var typeRaw) || !(typeRaw is JsonString typeStr)
                || !CriterionTypeIds.TryParse(typeStr.Value, out var type))
            {
                throw new DataFieldException(record.Table.Name, record.Key, $"criteria[{index}].type",
                    $"必须是 {string.Join("|", CriterionTypeIds.AllValues)} 之一");
            }

            if (!criterionJson.TryGetValue("observe_event", out var observeRaw) || !(observeRaw is JsonString observeStr)
                || !Id.TryParse(observeStr.Value, out var observeEvent))
            {
                throw new DataFieldException(record.Table.Name, record.Key, $"criteria[{index}].observe_event",
                    "必须是合法 Id 字符串");
            }

            Id? targetRef = null;
            if (criterionJson.TryGetValue("target_ref", out var targetRaw) && targetRaw is JsonString targetStr)
            {
                if (!Id.TryParse(targetStr.Value, out var parsedTarget))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, $"criteria[{index}].target_ref",
                        "必须是合法 Id 字符串");
                }
                targetRef = parsedTarget;
            }

            if (!criterionJson.TryGetValue("count", out var countRaw) || !(countRaw is JsonNumber countNum)
                || !countNum.TryGetInt64(out var countValue))
            {
                throw new DataFieldException(record.Table.Name, record.Key, $"criteria[{index}].count", "必须是整数");
            }

            string? filterText = null;
            if (criterionJson.TryGetValue("filter", out var filterRaw) && filterRaw is JsonString filterStr)
            {
                filterText = filterStr.Value;
            }

            return new AchievementCriterion(type, observeEvent, targetRef, (int)countValue, filterText);
        }
    }
}
