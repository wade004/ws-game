using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.AreaTrigger
{
    // ADR-0019 / F1b 退役记录：原 AreaTriggerShapeKindRule（"shape.kind 合法"检查项，见 05 第 3.5
    // 节四种形状、任务书拍板"校验：shape.kind 合法"）已整条删除。AreaTriggerSchemas.ShapeSchema
    // 把 shape 登记为按判别字段 kind 分派的 VariantSchema 后，DataRegistry 内置的
    // variant_discriminator 检查（判别字段缺失/非字符串/不在合法取值集合内）已完整覆盖原规则的
    // 全部报错场景（旧检查名 area_trigger_shape_kind 变为 variant_discriminator，字段路径变为
    // shape.kind），不再重复注册、不再对同一缺陷双报，见 schema/README.md"退役规则"一节与
    // AreaTriggerSchemaCoverageTests 的迁移测试。

    /// <summary>
    /// "<c>trigger_type</c> 对应 <c>params</c> 必填字段齐全"检查项（见 05 第 7 节四种类型、任务书
    /// 拍板 <c>params</c> 字段组）。惯例同 <c>Core.Carriers.Gobj.GobjTypeDataFieldGroupRule</c>：
    /// <c>trigger_type</c> 缺失/取值非法属于 <c>required_field</c>/<c>field_type</c> 职责，本规则只
    /// 处理 <c>trigger_type</c> 合法时的 <c>params</c> 字段组完整性。
    /// <para>
    /// ADR-0019 / F1b 判断记录（本规则未退役）：<c>params</c> 的判别字段 <c>trigger_type</c> 与
    /// <c>params</c> 本身平级（不在 <c>params</c> 对象内部），<see cref="VariantSchema"/> 要求判别
    /// 字段与被判别子字段同处一个 JsonObject，机制上无法覆盖"按 trigger_type 决定 params 哪些
    /// 子字段必填"这条业务判断（见 <see cref="AreaTriggerSchemas.ParamsSchema"/> 判断记录）——
    /// <c>AreaTriggerSchemas.ParamsSchema</c> 改用 <c>Fields</c> 登记四种 trigger_type 用到的子字段
    /// 并集（全部非必填，只新增类型校验），本规则原样保留、继续是"必填性"判断的唯一来源，两者
    /// 检查不同的缺陷、不会双报。
    /// </para>
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
