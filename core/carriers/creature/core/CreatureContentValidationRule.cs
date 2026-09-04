using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// <c>creature.template</c> 专属的内容校验规则（见 <see cref="IValidationRule"/>"模块专属校验
    /// 规则的扩展点"、07 第 2.2 节"标志位是新增原语受控开口之一"）：<c>npc_flags</c> 数组每个元素
    /// 必须是 <see cref="NpcFlagIds"/> 登记的六个合法值之一——<see cref="FieldKind.IdList"/> 的内置
    /// 校验只检查"每个元素是合法 Id 格式字符串"（见 <see cref="FieldKind"/> 注释），不检查取值是否
    /// 在职能标志的固定集合内，这属于"模块专属校验规则"的职责，本类据此承担（惯例同
    /// <c>core/rules/ai</c> 的 <c>AiContentValidationRule</c>）。
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方（组装层或本模块测试）需要显式
    /// <c>registry.RegisterValidationRule(new CreatureContentValidationRule())</c>（与
    /// <see cref="IValidationRule"/> 契约"由各内容模块自行登记"一致，惯例同
    /// <c>AiContentValidationRule</c> 判断记录）。
    /// </para>
    /// </summary>
    public sealed class CreatureContentValidationRule : IValidationRule
    {
        private const string Check = "creature_content";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(CreatureSchemas.Template.Name))
            {
                if (!record.TryGetIdList("npc_flags", out var flags))
                {
                    continue;
                }

                for (var i = 0; i < flags.Count; i++)
                {
                    if (!NpcFlagIds.IsValid(flags[i]))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, CreatureSchemas.Template.Name, Check,
                            $"npc_flags 含未登记的职能标志 \"{flags[i]}\"（合法集合见 07 第 2.2 节六值）",
                            recordKey: record.Key, field: "npc_flags");
                    }
                }
            }
        }
    }
}
