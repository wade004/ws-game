using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// "外形类型字段组完整"检查项（见 04_数据与内容管线.md 第 5 节校验器检查项清单、
    /// 第 7.1 节"校验规则：kind 决定哪组类型专属字段生效"）：<c>display.map</c> 记录的
    /// <c>kind</c> 字段决定哪组专属字段必填、哪组必须留空——这是"按字段值决定另一组字段是否
    /// 必填"的条件必填规则，<see cref="TableSchema"/> 的 <see cref="FieldSchema.Required"/>
    /// 只能表达"该字段任何情况下都必填/都可选"这种无条件规则，无法表达条件必填，因此这条
    /// 检查项必须作为独立的 <see cref="IValidationRule"/> 扩展点实现，而不是把专属字段在
    /// schema 里标记为 <c>Required: true</c>（见 <see cref="DisplaySchemas.Map"/> 类型注释）。
    /// </summary>
    public sealed class DisplayKindFieldGroupRule : IValidationRule
    {
        private const string CheckName = "display_kind_field_group";

        private static readonly string[] SpriteRequiredFields = { "sprite_set_id", "direction_count" };

        private static readonly string[] SpriteOnlyFields =
        {
            "sprite_set_id", "direction_count", "mirror_pairs", "paperdoll_layers", "anchor_points",
        };

        private static readonly string[] ModelRequiredFields = { "model_ref", "anim_set_ref" };

        private static readonly string[] ModelOnlyFields =
        {
            "model_ref", "anim_set_ref", "sockets", "slots", "default_slot_meshes", "material_params",
        };

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(DisplaySchemas.Map.Name))
            {
                // kind 缺失/取值不在枚举合法集合内属于 required_field/field_type/枚举合法
                // 检查项的职责（由 DataRegistry 内建校验负责），本规则只处理 kind 合法时的
                // 字段组完整性，避免与内建校验重复报错。
                if (!record.TryGetString("kind", out var kind))
                {
                    continue;
                }

                string[] requiredFields;
                string[] mustBeEmptyFields;

                if (kind == "sprite")
                {
                    requiredFields = SpriteRequiredFields;
                    mustBeEmptyFields = ModelOnlyFields;
                }
                else if (kind == "model")
                {
                    requiredFields = ModelRequiredFields;
                    mustBeEmptyFields = SpriteOnlyFields;
                }
                else
                {
                    continue;
                }

                foreach (var issue in CheckRequired(record, kind, requiredFields))
                {
                    yield return issue;
                }

                foreach (var issue in CheckMustBeEmpty(record, kind, mustBeEmptyFields))
                {
                    yield return issue;
                }
            }
        }

        private static IEnumerable<ValidationIssue> CheckRequired(DataRecord record, string kind, IReadOnlyList<string> fields)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (!record.Has(field))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error,
                        DisplaySchemas.Map.Name,
                        CheckName,
                        $"kind=\"{kind}\" 要求字段 \"{field}\" 必填，但缺失",
                        recordKey: record.Key,
                        field: field);
                }
            }
        }

        private static IEnumerable<ValidationIssue> CheckMustBeEmpty(DataRecord record, string kind, IReadOnlyList<string> fields)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (record.Has(field))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error,
                        DisplaySchemas.Map.Name,
                        CheckName,
                        $"kind=\"{kind}\" 要求字段 \"{field}\" 留空，但已提供取值",
                        recordKey: record.Key,
                        field: field);
                }
            }
        }
    }
}
