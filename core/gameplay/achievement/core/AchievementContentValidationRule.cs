using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Core.Gameplay.Achievement
{
    /// <summary>
    /// <c>achv.def.criteria</c> 专属的内容校验规则（见 <see cref="IValidationRule"/>"模块专属校验
    /// 规则的扩展点"，惯例同 <c>core/carriers/creature</c> 的 <c>CreatureContentValidationRule</c>）：
    /// <c>criteria</c> 登记为 <see cref="FieldKind.Array"/>（见 <see cref="AchievementSchemas"/> 判断
    /// 记录），内置的 <c>field_type</c> 校验只检查"存在且是数组"，本规则补上任务书要求的两项业务
    /// 校验——<c>type</c> 合法（<see cref="CriterionTypeIds.AllValues"/> 六值之一）；
    /// <c>observe_event</c> 为已登记事件 key（<see cref="EventKeys.All"/>）。
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方（组装层或本模块测试）需要显式
    /// <c>registry.RegisterValidationRule(new AchievementContentValidationRule())</c>（惯例同
    /// <c>CreatureContentValidationRule</c>）。
    /// </para>
    /// </summary>
    public sealed class AchievementContentValidationRule : IValidationRule
    {
        private const string Check = "achievement_content";

        private static readonly HashSet<string> RegisteredEventKeys = BuildRegisteredEventKeys();

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(AchievementSchemas.Def.Name))
            {
                if (!record.TryGetArray("criteria", out var criteriaArray))
                {
                    continue;
                }

                for (var i = 0; i < criteriaArray.Count; i++)
                {
                    if (!(criteriaArray[i] is JsonObject criterionJson))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, AchievementSchemas.Def.Name, Check,
                            $"criteria[{i}] 必须是对象", recordKey: record.Key, field: "criteria");
                        continue;
                    }

                    if (!criterionJson.TryGetValue("type", out var typeRaw) || !(typeRaw is JsonString typeStr)
                        || !CriterionTypeIds.TryParse(typeStr.Value, out _))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, AchievementSchemas.Def.Name, Check,
                            $"criteria[{i}].type 必须是 {string.Join("|", CriterionTypeIds.AllValues)} 之一",
                            recordKey: record.Key, field: "criteria");
                    }

                    if (!criterionJson.TryGetValue("observe_event", out var observeRaw) || !(observeRaw is JsonString observeStr)
                        || !RegisteredEventKeys.Contains(observeStr.Value))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, AchievementSchemas.Def.Name, Check,
                            $"criteria[{i}].observe_event 必须是 found.event_catalog 已登记的事件 key",
                            recordKey: record.Key, field: "criteria");
                    }
                }
            }
        }

        private static HashSet<string> BuildRegisteredEventKeys()
        {
            var set = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var key in EventKeys.All)
            {
                set.Add(key.Value);
            }
            return set;
        }
    }
}
