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
    /// <item><c>units[]</c> 的 <c>spawn_ref</c>/<c>template_ref</c> 必须二选一（08 第 4.1 节；跨字段
    /// 约束，<see cref="FieldSchema.Fields"/> 无法表达"二选一"，见 <see cref="EncounterSchemas.UnitItemSchema"/>
    /// 判断记录，本规则继续承担）。</item>
    /// <item><c>units[].spawn_ref</c>/<c>waves[].spawn_refs[]</c> 的 domain 必须是 <c>spawn</c>
    /// （08 第 8 节"spawnRefs 域名 spawn"；判断记录：本模块与 <c>core/gameplay/spawn</c> 刻意只经
    /// <see cref="SpawnRequester"/> 委托解耦，两处均退回 <see cref="FieldKind.Id"/> 而非
    /// <see cref="FieldKind.Reference"/>(ReferenceDomain)，domain 校验因此继续留在本规则）。</item>
    /// </list>
    /// <para>
    /// 判断记录（ADR-0019 / F1b 退役：<c>waves[].trigger_condition</c>/<c>phases[].enter_condition</c>
    /// 的 Expr 可解析检查）：本规则此前对这两个字段手写了一份 <see cref="ExprParser.Parse"/> 尝试
    /// （见旧版 <c>TryParseExpr</c>），因为它们嵌套在 <see cref="FieldKind.Array"/> 内、DataRegistry
    /// 内置的 <c>expr_parsable</c> 校验项此前只覆盖顶层 <see cref="FieldKind.Expr"/> 字段。ADR-0019
    /// 起 <see cref="EncounterSchemas.WaveItemSchema"/>/<see cref="EncounterSchemas.PhaseItemSchema"/>
    /// 把这两处显式登记为 <see cref="FieldKind.Expr"/> 子字段，<c>DataRegistry.ValidateExprField</c>
    /// 递归覆盖到子结构后已完整取代（且比旧版更严格：旧版只 <c>ExprParser.Parse</c>，新版额外跑
    /// <c>ExprValidator.Validate</c> 静态校验，见 <c>DataRegistry.ValidateExprField</c>）——两处手写
    /// 检查已整条删除，测试迁移到 <c>SubstructureValidationTests</c> 风格的
    /// <c>EncounterSchemaCoverageTests</c>（用 <c>expr_parsable</c> + 嵌套字段路径断言，见该文件）。
    /// <c>GameplaySchemaCatalog.RegisterEncounterSchemas</c> 里
    /// <c>new EncounterContentValidationRule(exprSchema)</c> 这一行不需要改动——构造签名未变，仍接受
    /// 一个非空 <see cref="IExprSchema"/>（保持调用方不动）；但 <paramref name="exprSchema"/> 不再被
    /// 存成字段——两处 Expr 检查删除后，本规则内部已没有任何地方需要读取它，若继续存成私有字段会因
    /// 本仓库 <c>TreatWarningsAsErrors</c>（见 Directory.Build.props）触发 CS0414（"字段已赋值但从未
    /// 使用"）编译错误；改为只在构造期做一次非空校验、不保留引用，构造签名与"exprSchema 为 null 时
    /// 抛 <see cref="ArgumentNullException"/>"这一行为契约都与改动前完全一致。
    /// </para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方需要显式
    /// <c>registry.RegisterValidationRule(new EncounterContentValidationRule(schema))</c>。
    /// </summary>
    public sealed class EncounterContentValidationRule : IValidationRule
    {
        private const string Check = "encounter_content";
        private const string SpawnDomain = "spawn";

        public EncounterContentValidationRule(IExprSchema exprSchema)
        {
            if (exprSchema == null) throw new ArgumentNullException(nameof(exprSchema));
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

                        // 判断记录（ADR-0019 / F1b 退役）：trigger_condition 的 Expr 可解析检查已删除，
                        // 由 EncounterSchemas.WaveItemSchema 登记的 FieldKind.Expr 子字段经 DataRegistry
                        // 递归校验覆盖（见本类型顶部判断记录）。
                    }
                }

                // 判断记录（ADR-0019 / F1b 退役）：phases[].enter_condition 的 Expr 可解析检查已整段
                // 删除，由 EncounterSchemas.PhaseItemSchema 登记的 FieldKind.Expr 子字段经 DataRegistry
                // 递归校验覆盖（见本类型顶部判断记录）。phases[] 目前没有其它需要手写校验的部分，
                // 不再需要单独遍历。
            }
        }
    }
}
