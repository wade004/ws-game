using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// "<c>display.anim_set.clips[*].events</c> 形状合法"检查项（ADR-0017 决策 c：关键帧数据统一解析
    /// 结果类型 <see cref="AnimClipEventSpec"/> 落地的配套校验，同 <see cref="DisplayKindFieldGroupRule"/>
    /// 判断记录——<see cref="TableSchema"/> 的 <see cref="FieldSchema.Required"/> 只能表达字段级
    /// 是否必填，无法表达"<c>clips</c> 内每个剪辑的 <c>events</c> 数组元素必须是
    /// <c>{name: 非空 String, time_pct: [0,1] 内的 Number}</c>"这种嵌套结构校验，因此作为独立的
    /// <see cref="IValidationRule"/> 扩展点实现）。
    /// <para>
    /// 判断记录（只登记警告，不阻断加载）：<see cref="AnimSetDef.FromRecord"/> 对同样的非法形状会抛
    /// <see cref="DataFieldException"/> 中止解析，本规则的价值是在"未必调用 FromRecord 的场景"
    /// （如 <c>toolchain/validator</c> 只跑校验、不构造运行期强类型视图）下也能及早发现问题；
    /// <c>time_pct</c> 超出 <c>[0,1]</c> 不影响 <see cref="AnimSetDef.FromRecord"/> 是否能成功解析
    /// （该方法只要求是数字），是纯粹的语义合理性问题，二者严重级别按此区分：结构缺失（不是对象/
    /// 缺少必填子字段/类型不对）为 <see cref="ValidationSeverity.Error"/>（与 FromRecord 抛异常的
    /// 情形对齐，本就会导致后续解析失败）；<c>time_pct</c> 越界为 <see cref="ValidationSeverity.Warning"/>
    /// （FromRecord 仍能解析成功，只是数值不合理，交由 <see cref="DataRegistryOptions.Strictness"/>
    /// 决定是否阻断）。
    /// </para>
    /// </summary>
    public sealed class AnimSetEventsShapeRule : IValidationRule
    {
        private const string CheckName = "anim_set_events_shape";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(DisplaySchemas.AnimSet.Name))
            {
                if (!record.TryGetObject("clips", out var clips))
                {
                    continue;
                }

                foreach (var clipEntry in clips)
                {
                    var clipName = clipEntry.Key;

                    if (!(clipEntry.Value is JsonObject clipObj))
                    {
                        yield return Error(record, $"剪辑 \"{clipName}\" 的值必须是对象");
                        continue;
                    }

                    if (!clipObj.TryGetValue("events", out var eventsVal) || eventsVal is JsonNull)
                    {
                        continue;
                    }

                    if (!(eventsVal is JsonArray eventsArr))
                    {
                        yield return Error(record, $"剪辑 \"{clipName}\" 的 events 必须是数组");
                        continue;
                    }

                    for (var i = 0; i < eventsArr.Count; i++)
                    {
                        foreach (var issue in ValidateEvent(record, clipName, i, eventsArr[i]))
                        {
                            yield return issue;
                        }
                    }
                }
            }
        }

        private static IEnumerable<ValidationIssue> ValidateEvent(DataRecord record, string clipName, int index, JsonValue eventValue)
        {
            if (!(eventValue is JsonObject eventObj))
            {
                yield return Error(record, $"剪辑 \"{clipName}\" 的 events 第 {index} 项必须是对象");
                yield break;
            }

            if (!eventObj.TryGetValue("name", out var nameVal) || !(nameVal is JsonString nameStr) || string.IsNullOrEmpty(nameStr.Value))
            {
                yield return Error(record, $"剪辑 \"{clipName}\" 的 events 第 {index} 项缺少非空字符串字段 \"name\"");
            }

            if (!eventObj.TryGetValue("time_pct", out var pctVal) || !(pctVal is JsonNumber pctNum))
            {
                yield return Error(record, $"剪辑 \"{clipName}\" 的 events 第 {index} 项缺少数值字段 \"time_pct\"");
            }
            else if (pctNum.Value < 0.0 || pctNum.Value > 1.0)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Warning,
                    DisplaySchemas.AnimSet.Name,
                    CheckName,
                    $"剪辑 \"{clipName}\" 的 events 第 {index} 项 time_pct={pctNum.Value} 超出建议范围 [0,1]",
                    recordKey: record.Key,
                    field: "clips");
            }
        }

        private static ValidationIssue Error(DataRecord record, string message) => new ValidationIssue(
            ValidationSeverity.Error,
            DisplaySchemas.AnimSet.Name,
            CheckName,
            message,
            recordKey: record.Key,
            field: "clips");
    }
}
