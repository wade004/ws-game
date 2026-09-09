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
    // GobjSchemaCoverageTests。
}
