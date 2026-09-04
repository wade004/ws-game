using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <c>encounter.def</c> 专属的内容校验规则（见 <see cref="IValidationRule"/>"模块专属校验规则的
    /// 扩展点"，惯例同 <c>core/carriers/creature</c> 的 <c>CreatureContentValidationRule</c>）：
    /// <list type="bullet">
    /// <item><c>units[]</c> 的 <c>spawn_ref</c>/<c>template_ref</c> 必须二选一（08 第 4.1 节）。</item>
    /// <item><c>units[].spawn_ref</c>/<c>waves[].spawn_refs[]</c> 的 domain 必须是 <c>spawn</c>
    /// （08 第 8 节"spawnRefs 域名 spawn"）。</item>
    /// <item><c>waves[].trigger_condition</c>/<c>phases[].enter_condition</c> 必须能用
    /// <see cref="ExprParser.Parse"/> 解析（08 校验要求"Expr 可解析"——这两个字段嵌套在
    /// <see cref="FieldKind.Array"/> 内，DataRegistry 内置的 <c>expr_parsable</c> 校验项只覆盖
    /// 顶层 <see cref="FieldKind.Expr"/> 字段，见 <see cref="EncounterSchemas"/> 判断记录，本规则
    /// 补上嵌套部分）。</item>
    /// </list>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方需要显式
    /// <c>registry.RegisterValidationRule(new EncounterContentValidationRule(schema))</c>。
    /// </summary>
    public sealed class EncounterContentValidationRule : IValidationRule
    {
        private const string Check = "encounter_content";
        private const string SpawnDomain = "spawn";

        private readonly IExprSchema _exprSchema;

        public EncounterContentValidationRule(IExprSchema exprSchema)
        {
            _exprSchema = exprSchema ?? throw new ArgumentNullException(nameof(exprSchema));
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(EncounterSchemas.Def.Name))
            {
                if (record.TryGetArray("units", out var unitsArray))
                {
                    for (var i = 0; i < unitsArray.Count; i++)
                    {
                        if (unitsArray[i] is JsonObject unitJson)
                        {
                            var hasSpawnRef = unitJson.TryGetValue("spawn_ref", out var spawnRaw) && spawnRaw is JsonString spawnStr;
                            var hasTemplateRef = unitJson.TryGetValue("template_ref", out _);

                            if (hasSpawnRef == hasTemplateRef)
                            {
                                yield return new ValidationIssue(
                                    ValidationSeverity.Error, EncounterSchemas.Def.Name, Check,
                                    $"units[{i}] 的 spawn_ref/template_ref 必须二选一", recordKey: record.Key, field: "units");
                            }
                            else if (hasSpawnRef && spawnRaw is JsonString spawnIdStr && Id.TryParse(spawnIdStr.Value, out var spawnId) && spawnId.Domain != SpawnDomain)
                            {
                                yield return new ValidationIssue(
                                    ValidationSeverity.Error, EncounterSchemas.Def.Name, Check,
                                    $"units[{i}].spawn_ref \"{spawnIdStr.Value}\" 的 domain 必须是 \"spawn\"", recordKey: record.Key, field: "units");
                            }
                        }
                    }
                }

                if (record.TryGetArray("waves", out var wavesArray))
                {
                    for (var i = 0; i < wavesArray.Count; i++)
                    {
                        if (!(wavesArray[i] is JsonObject waveJson))
                        {
                            continue;
                        }

                        if (waveJson.TryGetValue("spawn_refs", out var spawnRefsRaw) && spawnRefsRaw is JsonArray spawnRefsArr)
                        {
                            for (var j = 0; j < spawnRefsArr.Count; j++)
                            {
                                if (spawnRefsArr[j] is JsonString s && Id.TryParse(s.Value, out var id) && id.Domain != SpawnDomain)
                                {
                                    yield return new ValidationIssue(
                                        ValidationSeverity.Error, EncounterSchemas.Def.Name, Check,
                                        $"waves[{i}].spawn_refs[{j}] \"{s.Value}\" 的 domain 必须是 \"spawn\"", recordKey: record.Key, field: "waves");
                                }
                            }
                        }

                        if (waveJson.TryGetValue("trigger_condition", out var triggerRaw) && triggerRaw is JsonString triggerStr)
                        {
                            var issue = TryParseExpr(record, $"waves[{i}].trigger_condition", triggerStr.Value);
                            if (issue.HasValue)
                            {
                                yield return issue.Value;
                            }
                        }
                    }
                }

                if (record.TryGetArray("phases", out var phasesArray))
                {
                    for (var i = 0; i < phasesArray.Count; i++)
                    {
                        if (!(phasesArray[i] is JsonObject phaseJson))
                        {
                            continue;
                        }

                        if (phaseJson.TryGetValue("enter_condition", out var enterRaw) && enterRaw is JsonString enterStr)
                        {
                            var issue = TryParseExpr(record, $"phases[{i}].enter_condition", enterStr.Value);
                            if (issue.HasValue)
                            {
                                yield return issue.Value;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>把 <c>ExprParser.Parse</c> 的解析尝试收敛成一个可能为空的 <see cref="ValidationIssue"/>，
        /// 不在 <c>catch</c> 子句体内直接 <c>yield return</c>（C# 不允许在 <c>catch</c> 内 yield，
        /// 见 <see cref="Validate"/> 调用处改为先收集结果再在 try/catch 之外 yield 的写法）。</summary>
        private ValidationIssue? TryParseExpr(DataRecord record, string field, string exprText)
        {
            try
            {
                ExprParser.Parse(exprText, _exprSchema);
                return null;
            }
            catch (ExprParseException ex)
            {
                return new ValidationIssue(
                    ValidationSeverity.Error, EncounterSchemas.Def.Name, Check,
                    $"{field} 无法解析：{ex.Message}", recordKey: record.Key, field: field);
            }
        }
    }
}
