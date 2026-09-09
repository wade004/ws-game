using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Gobj
{
    /// <summary>
    /// "<c>type_data</c> 字段组完整"检查项（见 07 第 3.1 节类型数据表、schema/README.md"校验规则"
    /// 一节）：<c>gobj.template.kind</c> 决定 <c>type_data</c> 内哪些字段必填——这是"按字段值决定
    /// 另一个（嵌套）字段是否必填"的条件必填规则，<see cref="TableSchema"/> 的
    /// <see cref="FieldSchema.Required"/> 只能表达顶层字段的无条件必填，既不能表达条件必填、也
    /// 不能探进 <see cref="FieldKind.Object"/> 字段内部，因此本规则必须作为独立的
    /// <see cref="IValidationRule"/> 扩展点实现（惯例同 <c>core/foundation/display_info</c> 的
    /// <c>DisplayKindFieldGroupRule</c>，本规则把它的"两种 kind 各一组必填字段"扩展成十种）。
    /// </summary>
    public sealed class GobjTypeDataFieldGroupRule : IValidationRule
    {
        private const string CheckName = "gobj_type_data_field_group";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(GobjSchemas.Template.Name))
            {
                // kind 缺失/取值不在枚举合法集合内属于 required_field/field_type 检查项职责（由
                // DataRegistry 内建校验负责），本规则只处理 kind 合法时的 type_data 字段组完整性，
                // 避免与内建校验重复报错。
                if (!record.TryGetString("kind", out var kind) || !GobjKindNames.TryParse(kind, out var gobjKind))
                {
                    continue;
                }

                if (!record.TryGetObject("type_data", out var typeData))
                {
                    // type_data 本身缺失/类型不符属于 required_field/field_type 职责，本规则不重复报错。
                    continue;
                }

                foreach (var field in RequiredFields(gobjKind))
                {
                    if (!typeData.TryGetValue(field, out var value) || value.Kind == JsonKind.Null)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error,
                            GobjSchemas.Template.Name,
                            CheckName,
                            $"kind=\"{kind}\" 要求 type_data.\"{field}\" 必填，但缺失",
                            recordKey: record.Key,
                            field: "type_data");
                    }
                }
            }
        }

        /// <summary>各 <see cref="GobjKind"/> 的 <c>type_data</c> 必填字段集合（见 07 第 3.1 节表格
        /// "类型数据要点"列、<c>GameObjectTemplate.ParseTypeData</c> 对应分支）。<c>door</c>/
        /// <c>save_point</c>/<c>spell_focus</c> 以外各类型均至少一个必填字段；<c>door</c> 的
        /// <c>lock_id</c>、<c>chest</c> 的 <c>lock_id</c> 均为可选（见 <see cref="DoorTypeData"/>/
        /// <see cref="ChestTypeData"/> 判断记录），不在此列。</summary>
        private static IReadOnlyList<string> RequiredFields(GobjKind kind) => kind switch
        {
            GobjKind.Door => System.Array.Empty<string>(),
            GobjKind.Chest => new[] { "loot_table_ref" },
            GobjKind.QuestObject => new[] { "quest_action_ref" },
            GobjKind.Trap => new[] { "skill_id", "trigger_shape" },
            GobjKind.SpellFocus => new[] { "required_skill_tag" },
            GobjKind.GatherNode => new[] { "loot_table_ref", "respawn_after_use" },
            GobjKind.Teleporter => new[] { "teleport_target_ref" },
            GobjKind.SavePoint => System.Array.Empty<string>(),
            GobjKind.Lever => new[] { "linked_object_ids" },
            GobjKind.Sign => new[] { "text_key" },
            _ => System.Array.Empty<string>(),
        };
    }

    // ADR-0019 F1c 退役（见 gobj/schema/README.md"退役规则"一节）：GobjOnUseKindRule/
    // GobjLockRequirementFieldGroupRule 两条纯结构检查（判别字段合法性 + 按变体专属字段必填）已被
    // GobjSchemas.OnUseSchema/RequirementSchema 的 Variants 登记（variant_discriminator/
    // required_field/field_type 内建校验）完全覆盖，整条删除；测试迁移至
    // GobjSchemaCoverageTests。退役时遗留一个真实缺口：GobjLockRequirementFieldGroupRule 原本同时
    // 校验 world_flag 变体的 flag_key 与 expected 两个必填字段，Variants 登记只覆盖了
    // flag_key（expected 是 Bool｜Number 联合类型，FieldKind 枚举表达不了），expected 缺失/错型因此
    // 在 report 阶段 0 error，直到 LockDef.FromRecord 才抛异常（外部审计 P2-03）。下面
    // GobjLockWorldFlagExpectedRule 补齐这一条，不影响已退役规则的其余职责。

    /// <summary>
    /// "<c>world_flag.expected</c> 必填且形状合法"检查项（P2-03 根治）：
    /// <see cref="GobjSchemas.RequirementSchema"/> 的 <c>world_flag</c> 变体只登记了
    /// <c>flag_key</c>（<c>expected</c> 是 Bool｜Number 联合类型，<see cref="FieldKind"/> 无法表达，
    /// 见该字段判断记录），因此 <c>variant_discriminator</c>/<c>required_field</c>/<c>field_type</c>
    /// 内建校验都不会检查 <c>expected</c>——缺失或类型错误时 report 阶段 0 error，只有运行期
    /// <see cref="LockDef.FromRecord"/>（经 <c>RequireExprValue</c>）才抛
    /// <c>Core.Carriers.Common.DataFieldException</c>。本规则在 report 阶段补齐这条检查，接受集合
    /// 与 <see cref="LockDef"/> 实际解析逻辑保持一致：<b>Bool 或 Number</b>（含整数与非整数）；
    /// String 与 <c>{"$id":...}</c> 不是合法的 <c>expected</c> 形式（<c>LockDef.RequireExprValue</c>
    /// 只接受 <c>JsonBool</c>/<c>JsonNumber</c>，比 <c>Core.Gameplay.Common.ExprValueJson.Parse</c>
    /// 的通用联合更窄，二者不是同一份取值范围，不能直接复用该处的判定逻辑）。只在
    /// <c>requirement.kind == "world_flag"</c> 时生效，<c>requirement</c> 本身缺失/类型不符/
    /// <c>kind</c> 非法留给 <c>required_field</c>/<c>field_type</c>/<c>variant_discriminator</c>
    /// 职责，不重复报告同一缺陷。
    /// </summary>
    public sealed class GobjLockWorldFlagExpectedRule : IValidationRule
    {
        private const string CheckName = "gobj_lock_world_flag_expected";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(GobjSchemas.Lock.Name))
            {
                if (!record.TryGetObject("requirement", out var requirement))
                {
                    // requirement 缺失/类型不符属于 required_field/field_type 职责，本规则不重复报错。
                    continue;
                }

                if (!requirement.TryGetValue("kind", out var kindVal) ||
                    !(kindVal is JsonString kindStr) ||
                    kindStr.Value != "world_flag")
                {
                    // kind 缺失/非字符串/取值非法：留给 variant_discriminator 内建校验；
                    // kind 合法但不是 world_flag：该变体没有 expected 字段，不是本规则职责。
                    continue;
                }

                if (!requirement.TryGetValue("expected", out var expectedVal) || expectedVal.Kind == JsonKind.Null)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, GobjSchemas.Lock.Name, CheckName,
                        "requirement.kind=\"world_flag\" 要求字段 \"expected\" 必填，但缺失",
                        recordKey: record.Key, field: "requirement");
                    continue;
                }

                if (!(expectedVal is JsonBool) && !(expectedVal is JsonNumber))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, GobjSchemas.Lock.Name, CheckName,
                        $"requirement.expected 期望 Bool 或 Number（实际类型：{expectedVal.Kind}）",
                        recordKey: record.Key, field: "requirement");
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // P2-01 ABI/API 兼容 façade（外部审计 audit-c9ff301-20260909，同一分节判断记录见
    // core/rules/skill/schema/SkillValidationRules.cs 顶部）：ADR-0019/F1c 把
    // GobjOnUseKindRule/GobjLockRequirementFieldGroupRule 两个 public 类型整条删除，1.13.0
    // CHANGELOG 宣称兼容，旧源码/旧编译产物显式注册这两个类型会编译失败/MissingMethodException（见
    // architecture/落地计划/audit-c9ff301-20260909/docs-project/project-findings.md P2-01）。下面
    // 两个类型原样恢复删除前的完整逻辑，标 [Obsolete]。GobjLockRequirementFieldGroupRule 额外覆盖了
    // world_flag.expected 的必填检查（P2-03 修复前的旧行为，只检查"存在"不检查类型）——显式注册本
    // façade 与新增的 GobjLockWorldFlagExpectedRule 会对同一处缺失重复报告，行为已知、不是新缺陷。
    // -----------------------------------------------------------------------

    /// <summary>
    /// ABI/API 兼容 façade（P2-01 根治）：1.12 的 <c>GobjOnUseKindRule</c>——<c>on_use</c> 字段存在
    /// 时校验其 <c>kind</c>/<c>ref</c> 内部结构。见本文件"P2-01 ABI/API 兼容 façade"分节判断记录：
    /// 已被 <see cref="GobjSchemas.OnUseSchema"/> 的 <c>VariantSchema</c> 覆盖，显式注册会重复报告
    /// （check 名不同：本类用 <c>gobj_on_use_kind</c>，内建用 <c>variant_discriminator</c>/
    /// <c>required_field</c>/<c>field_type</c>），不是新缺陷。
    /// </summary>
    [Obsolete("已被 GobjSchemas.OnUseSchema 的 VariantSchema 声明式登记覆盖；仅为 1.12 源码/二进制兼容保留，显式注册会与内建结构校验对同一坏形状重复报告。")]
    public sealed class GobjOnUseKindRule : IValidationRule
    {
        private const string CheckName = "gobj_on_use_kind";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(GobjSchemas.Template.Name))
            {
                if (!record.TryGetObject("on_use", out var onUse))
                {
                    continue;
                }

                if (!onUse.TryGetValue("kind", out var kindVal) || !(kindVal is JsonString kindStr))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, GobjSchemas.Template.Name, CheckName,
                        "on_use.kind 缺失或不是字符串", recordKey: record.Key, field: "on_use");
                    continue;
                }

                if (kindStr.Value != "skill" && kindStr.Value != "dialog")
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, GobjSchemas.Template.Name, CheckName,
                        $"on_use.kind 取值非法：\"{kindStr.Value}\"（只能是 skill|dialog）", recordKey: record.Key, field: "on_use");
                    continue;
                }

                if (!onUse.TryGetValue("ref", out var refVal) || !(refVal is JsonString refStr) || !Core.Foundation.Common.Id.IsValidFormat(refStr.Value))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, GobjSchemas.Template.Name, CheckName,
                        "on_use.ref 缺失或不是合法 Id", recordKey: record.Key, field: "on_use");
                }
            }
        }
    }

    /// <summary>
    /// ABI/API 兼容 façade（P2-01 根治）：1.12 的 <c>GobjLockRequirementFieldGroupRule</c>——
    /// <c>gobj.lock.requirement.kind</c> 合法且三种变体各自的专属字段齐全（含 <c>world_flag</c> 的
    /// <c>expected</c> 必填——退役时遗漏这一条，见 P2-03/<see cref="GobjLockWorldFlagExpectedRule"/>
    /// 判断记录）。见 <see cref="GobjOnUseKindRule"/> 同一分节判断记录：结构性部分已被
    /// <see cref="GobjSchemas.RequirementSchema"/> 覆盖，显式注册本 façade 会与内建结构校验、以及
    /// <see cref="GobjLockWorldFlagExpectedRule"/> 对同一批缺陷重复报告，不是新缺陷。
    /// </summary>
    [Obsolete("已被 GobjSchemas.RequirementSchema 的 VariantSchema 声明式登记 + GobjLockWorldFlagExpectedRule 覆盖；仅为 1.12 源码/二进制兼容保留，显式注册会重复报告。")]
    public sealed class GobjLockRequirementFieldGroupRule : IValidationRule
    {
        private const string CheckName = "gobj_lock_requirement_field_group";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(GobjSchemas.Lock.Name))
            {
                if (!record.TryGetObject("requirement", out var requirement))
                {
                    continue;
                }

                if (!requirement.TryGetValue("kind", out var kindVal) || !(kindVal is JsonString kindStr))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, GobjSchemas.Lock.Name, CheckName,
                        "requirement.kind 缺失或不是字符串", recordKey: record.Key, field: "requirement");
                    continue;
                }

                IReadOnlyList<string> requiredFields;
                switch (kindStr.Value)
                {
                    case "item_key":
                        requiredFields = new[] { "item_id" };
                        break;
                    case "world_flag":
                        requiredFields = new[] { "flag_key", "expected" };
                        break;
                    case "skill_check":
                        requiredFields = new[] { "skill_tag", "min_value" };
                        break;
                    default:
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, GobjSchemas.Lock.Name, CheckName,
                            $"requirement.kind 取值非法：\"{kindStr.Value}\"（只能是 item_key|world_flag|skill_check）",
                            recordKey: record.Key, field: "requirement");
                        continue;
                }

                foreach (var field in requiredFields)
                {
                    if (!requirement.TryGetValue(field, out var value) || value.Kind == JsonKind.Null)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, GobjSchemas.Lock.Name, CheckName,
                            $"requirement.kind=\"{kindStr.Value}\" 要求字段 \"{field}\" 必填，但缺失",
                            recordKey: record.Key, field: "requirement");
                    }
                }
            }
        }
    }
}
