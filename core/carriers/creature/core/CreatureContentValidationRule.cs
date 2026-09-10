using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// <c>creature.template</c> 专属的内容校验规则（见 <see cref="IValidationRule"/>"模块专属校验
    /// 规则的扩展点"、07 第 2.2 节"标志位是新增原语受控开口之一"）。
    /// <para>
    /// 判断记录（消费方反馈第 28 条，收口重复校验）：本类此前手写 <c>npc_flags</c> 数组每个元素必须是
    /// <see cref="NpcFlagIds"/> 登记的六个合法值之一的校验——<see cref="FieldKind.IdList"/> 的内置
    /// 校验当时只检查"每个元素是合法 Id 格式字符串"，不检查取值是否在职能标志的固定集合内。
    /// <c>FieldSchema</c> 现已支持 <see cref="FieldSchema.WithAllowedValues"/>（消费方反馈第 28 条），
    /// <c>CreatureSchemas.Template</c> 的 <c>npc_flags</c> 字段据此登记 <c>NpcFlagIds.All</c>，
    /// 非法取值改由 <c>DataRegistry.LoadAll</c> 加载期的 <c>field_allowed_value</c> 检查项拦截——与
    /// 本类此前手写的 <c>creature_content</c> 检查项是同一份数据上的重复校验，会对同一处非法取值
    /// 报告两条问题。本类因此收口（不再重复报告），只保留类型与 <see cref="IValidationRule"/> 实现，
    /// 供未来 creature 模块专属、且确实无法用登记表元数据表达的校验项落地（同 <c>AiContentValidationRule</c>
    /// 一类模块专属规则的既有扩展点惯例，不因当前暂无内容而移除类型本身）。见
    /// <c>core/carriers/creature/tests/CreatureValidationTests.cs</c>
    /// <c>Validate_RejectsUnknownNpcFlag</c>：断言非法值现由 <c>field_allowed_value</c> 单独报告一次，
    /// 不再出现 <c>creature_content</c> 重复报告。
    /// </para>
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方（组装层或本模块测试）需要显式
    /// <c>registry.RegisterValidationRule(new CreatureContentValidationRule())</c>（与
    /// <see cref="IValidationRule"/> 契约"由各内容模块自行登记"一致，惯例同
    /// <c>AiContentValidationRule</c> 判断记录）。
    /// </para>
    /// </summary>
    public sealed class CreatureContentValidationRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            yield break;
        }
    }
}
