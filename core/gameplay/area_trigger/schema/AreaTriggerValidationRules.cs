using System;
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
    //
    // P2-01 ABI/API 兼容 façade 补漏（外部审计 audit-c9ff301-20260909）：审计原文只点名 FieldSchema
    // 七参数构造 + 五个 public 规则类型（GobjOnUseKindRule、GobjLockRequirementFieldGroupRule、
    // EffectKindRegisteredRule、CostEntryShapeRule、ChargesShapeRule）。按"核对 1.12→1.13 还有没有
    // 其它删除/改签的公开成员"复查 `git diff v1.12.0 v1.13.0 -- '*.cs'`，另发现本类型
    // AreaTriggerShapeKindRule 同一批（ADR-0019）整条删除、审计未点名——同一类兼容缺口，一并补齐，
    // 判断记录同 core/rules/skill/schema/SkillValidationRules.cs 顶部"P2-01 ABI/API 兼容 façade"
    // 分节。

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

    /// <summary>
    /// ABI/API 兼容 façade（P2-01 根治）：1.12 的 <c>AreaTriggerShapeKindRule</c>——
    /// <c>shape.kind</c> 必须是已登记的四种形状之一。已被 <c>AreaTriggerSchemas.ShapeSchema</c> 的
    /// <c>VariantSchema</c> 覆盖（见本文件顶部退役记录），显式注册会重复报告（check 名不同：本类用
    /// <c>area_trigger_shape_kind</c>，内建用 <c>variant_discriminator</c>），不是新缺陷。
    /// </summary>
    [Obsolete("已被 AreaTriggerSchemas.ShapeSchema 的 VariantSchema 声明式登记覆盖；仅为 1.12 源码/二进制兼容保留，显式注册会与内建结构校验对同一坏形状重复报告。")]
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
}
