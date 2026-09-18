using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// "装备呈现字段组条件必填"检查项（消费方反馈第 63 条；见 04_数据与内容管线.md 第 7.1.2 节字段表、
    /// 本模块 <c>schema/README.md</c> "<c>display.equip_visual</c>"一节）：<c>display.equip_visual</c>
    /// 记录的 <c>mode</c> 字段决定哪组专属字段必填——同 <see cref="DisplayKindFieldGroupRule"/>
    /// 判断记录，<see cref="TableSchema"/> 的 <see cref="FieldSchema.Required"/> 只能表达无条件必填/
    /// 可选，无法表达"按 <c>mode</c> 取值决定另一组字段是否必填"这种条件必填，因此作为独立的
    /// <see cref="IValidationRule"/> 扩展点实现（同一惯例，类名/检查名沿用
    /// <c>&lt;对象&gt;FieldGroupRule</c>/<c>&lt;snake_case&gt;_field_group</c> 命名）。
    /// <para>
    /// 判断记录（只登记"必填"，不登记"另一组必须留空"）：04 第 7.1.2 节字段表与本模块
    /// <c>schema/README.md</c> 对 <c>slot_id</c>/<c>mesh_ref</c>/<c>socket_id</c>/<c>model_ref</c>
    /// 四个字段的文档描述只写了"<c>mode: slot_mesh</c> 时必填"/"<c>mode: socket_attach</c> 时必填"，
    /// 均未写"另一模式下必须留空"——与 <see cref="DisplayKindFieldGroupRule"/> 不同（该规则的
    /// <c>display.map</c> sprite/model 两组专属字段在 04 第 7.1 节原文明确"另一组字段必须留空"，
    /// 第 5 节检查项说明同样写"另一组字段必须留空"）。消费方反馈第 63 条本身也只要求补"条件必填"，
    /// 未要求补"互斥留空"。本规则因此只检查每条记录按自己的 <c>mode</c> 是否填齐对应必填字段组，不
    /// 检查是否同时携带了另一模式的字段——避免在文档未明确写"必填"的组合上自行发明更严格的语义
    /// （任务书取舍：有歧义处取最保守解释）。若后续要收紧为"互斥留空"，需先在 04/schema README 补
    /// 明确措辞，再扩展本规则或另开检查项。
    /// </para>
    /// </summary>
    public sealed class EquipVisualModeFieldGroupRule : IValidationRule
    {
        private const string CheckName = "equip_visual_mode_field_group";

        private static readonly string[] SlotMeshRequiredFields = { "slot_id", "mesh_ref" };

        private static readonly string[] SocketAttachRequiredFields = { "socket_id", "model_ref" };

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(DisplaySchemas.EquipVisual.Name))
            {
                // mode 缺失/取值不在枚举合法集合内属于 required_field/field_type/枚举合法检查项的
                // 职责（由 DataRegistry 内建校验负责），本规则只处理 mode 合法时的字段组完整性，
                // 避免与内建校验重复报错（同 DisplayKindFieldGroupRule 判断记录）。
                if (!record.TryGetString("mode", out var mode))
                {
                    continue;
                }

                string[] requiredFields;

                if (mode == "slot_mesh")
                {
                    requiredFields = SlotMeshRequiredFields;
                }
                else if (mode == "socket_attach")
                {
                    requiredFields = SocketAttachRequiredFields;
                }
                else
                {
                    continue;
                }

                foreach (var issue in CheckRequired(record, mode, requiredFields))
                {
                    yield return issue;
                }
            }
        }

        private static IEnumerable<ValidationIssue> CheckRequired(DataRecord record, string mode, IReadOnlyList<string> fields)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (!record.Has(field))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error,
                        DisplaySchemas.EquipVisual.Name,
                        CheckName,
                        $"mode=\"{mode}\" 要求字段 \"{field}\" 必填，但缺失",
                        recordKey: record.Key,
                        field: field);
                }
            }
        }
    }
}
