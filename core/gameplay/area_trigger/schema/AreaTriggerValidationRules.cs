using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// "<c>shape.kind</c> 合法"检查项（见 05 第 3.5 节四种形状、任务书拍板"校验：shape.kind
    /// 合法"）。惯例同 <c>Core.Carriers.Gobj.GobjOnUseKindRule</c>：<see cref="AreaTriggerDef.FromRecord"/>
    /// 在真正解析时对非法 <c>kind</c> 抛异常（防御性检查），本规则则在数据加载期以不阻断其它记录
    /// 校验的方式报告问题。
    /// </summary>
    public sealed class AreaTriggerShapeKindRule : IValidationRule
    {
        private const string CheckName = "area_trigger_shape_kind";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(AreaTriggerSchemas.TriggerDef.Name))
            {
                if (!record.TryGetObject("shape", out var shape))
                {
                    // shape 缺失/类型不符属于 required_field/field_type 职责，本规则不重复报错。
                    continue;
                }

                if (!shape.TryGetValue("kind", out var kindValue) || !(kindValue is JsonString kindStr))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, AreaTriggerSchemas.TriggerDef.Name, CheckName,
                        "shape.kind 缺失或不是字符串", recordKey: record.Key, field: "shape");
                    continue;
                }

                var legal = false;
                foreach (var value in AreaTriggerSchemas.ShapeKindValues)
                {
                    if (value == kindStr.Value)
                    {
                        legal = true;
                        break;
                    }
                }

                if (!legal)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, AreaTriggerSchemas.TriggerDef.Name, CheckName,
                        $"shape.kind 取值非法：\"{kindStr.Value}\"（只能是 circle|cone|line|rect）",
                        recordKey: record.Key, field: "shape");
                }
            }
        }
    }

    /// <summary>
    /// "<c>trigger_type</c> 对应 <c>params</c> 必填字段齐全"检查项（见 05 第 7 节四种类型、任务书
    /// 拍板 <c>params</c> 字段组）。惯例同 <c>Core.Carriers.Gobj.GobjTypeDataFieldGroupRule</c>：
    /// <c>trigger_type</c> 缺失/取值非法属于 <c>required_field</c>/<c>field_type</c> 职责，本规则只
    /// 处理 <c>trigger_type</c> 合法时的 <c>params</c> 字段组完整性。
    /// </summary>
    public sealed class AreaTriggerParamsFieldGroupRule : IValidationRule
    {
        private const string CheckName = "area_trigger_params_field_group";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(AreaTriggerSchemas.TriggerDef.Name))
            {
                if (!record.TryGetString("trigger_type", out var typeText)
                    || !AreaTriggerTypeNames.TryParse(typeText, out var triggerType))
                {
                    continue;
                }

                if (!record.TryGetObject("params", out var paramsObj))
                {
                    // params 本身缺失/类型不符属于 required_field/field_type 职责。
                    continue;
                }

                foreach (var field in RequiredFields(triggerType))
                {
                    if (!paramsObj.TryGetValue(field, out var value) || value.Kind == JsonKind.Null)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, AreaTriggerSchemas.TriggerDef.Name, CheckName,
                            $"trigger_type=\"{typeText}\" 要求 params.\"{field}\" 必填，但缺失",
                            recordKey: record.Key, field: "params");
                    }
                }
            }
        }

        /// <summary>见 05 第 7 节表格：<c>quest_explore</c> 无必填字段；<c>spawn_point</c>（map_transition
        /// 的第二个字段）可选，见 <see cref="MapTransitionParams.SpawnPoint"/> 判断记录，不在此列。</summary>
        private static IReadOnlyList<string> RequiredFields(AreaTriggerType type) => type switch
        {
            AreaTriggerType.MapTransition => new[] { "target_map" },
            AreaTriggerType.EncounterStart => new[] { "encounter_ref" },
            AreaTriggerType.Script => new[] { "hook_id" },
            AreaTriggerType.QuestExplore => System.Array.Empty<string>(),
            _ => System.Array.Empty<string>(),
        };
    }
}
