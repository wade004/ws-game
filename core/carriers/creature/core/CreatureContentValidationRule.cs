using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Numbers.PowerSet;

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
    /// <para>
    /// 判断记录（ADR-0106 新增 <c>creature_power_floor_min</c> 检查项，消费方反馈第五十五批"单一
    /// 模板受伤但不死"）：<c>creature.template.power_floors</c>（<see cref="CreatureSchemas.Template"/>
    /// 登记 <c>FieldSchema.WithMap(MapSchema.ReferenceKeyTable("arch.power_type", ...))</c>）的
    /// "键须为已登记的 <c>arch.power_type</c>"已由该登记天生的 <c>reference_integrity</c> 检查项覆盖
    /// （惯例同 <c>base_stats</c> 的 <c>stat.definition</c> 键），本类只补"值须 ≥ 该资源类型的
    /// <c>min</c>"这一条登记层无法表达的跨记录数值约束（<c>FieldRange</c> 只能表达字面常量区间，
    /// 不能表达"须 ≥ 另一张表另一条记录的某字段值"）。键不是已登记 <c>arch.power_type</c> 时本规则
    /// 直接跳过（<c>reference_integrity</c> 已经报告，不重复诊断同一处问题，惯例同本类型上一条
    /// 判断记录"消费方反馈第 28 条，收口重复校验"同一处理口径）。
    /// </para>
    /// </summary>
    public sealed class CreatureContentValidationRule : IValidationRule
    {
        private const string PowerFloorMinCheck = "creature_power_floor_min";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var issue in ValidatePowerFloorsMin(view))
            {
                yield return issue;
            }
        }

        private IEnumerable<ValidationIssue> ValidatePowerFloorsMin(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(CreatureSchemas.Template.Name))
            {
                if (!record.TryGetObject("power_floors", out var floors))
                {
                    continue;
                }

                foreach (var kv in floors)
                {
                    if (!(kv.Value is JsonNumber floorNumber))
                    {
                        // 值类型不符已由 field_type 报告，这里不重复。
                        continue;
                    }

                    if (!Id.TryParse(kv.Key, out var powerTypeId) ||
                        !view.TryGet(PowerSchemas.PowerType.Name, powerTypeId, out var powerTypeRecord) ||
                        powerTypeRecord == null)
                    {
                        // 键格式不合法/不是已登记 arch.power_type：已由 power_floors 的
                        // reference_integrity（MapSchema.ReferenceKeyTable）报告，这里不重复。
                        continue;
                    }

                    var min = powerTypeRecord.TryGetNumber("min", out var minValue) ? minValue : 0.0;
                    if (floorNumber.Value < min)
                    {
                        yield return new ValidationIssue(ValidationSeverity.Error, CreatureSchemas.Template.Name, PowerFloorMinCheck,
                            $"power_floors.{kv.Key} = {floorNumber.Value} 低于 \"{kv.Key}\" 的下限 min={min}（arch.power_type.min）",
                            recordKey: record.Key, field: $"power_floors.{kv.Key}");
                    }
                }
            }
        }
    }
}
