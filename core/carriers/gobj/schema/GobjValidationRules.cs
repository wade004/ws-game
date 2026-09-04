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

    /// <summary>
    /// "<c>on_use</c> 二选一"检查项（见 07 第 3.3 节"指向一个 skill.def……或一个
    /// dialog.gossip_menu 动作项，二者二选一，不并存"）：<c>on_use.kind</c> 必须是
    /// <c>skill</c>/<c>dialog</c> 之一，且携带合法的 <c>ref</c>。<c>on_use</c> 字段本身是否存在
    /// 是可选的（<see cref="GobjSchemas.Template"/> 声明 <c>required: false</c>）——本规则只在字段
    /// 存在时校验其内部结构。
    /// </summary>
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
    /// "<c>gobj.lock.requirement.kind</c> 合法且专属字段齐全"检查项（见 07 第 3.2 节三种
    /// <c>requirement</c> 变体）。
    /// </summary>
    public sealed class GobjLockRequirementFieldGroupRule : IValidationRule
    {
        private const string CheckName = "gobj_lock_requirement_field_group";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(GobjSchemas.Lock.Name))
            {
                if (!record.TryGetObject("requirement", out var requirement))
                {
                    // requirement 缺失/类型不符属于 required_field/field_type 职责。
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
