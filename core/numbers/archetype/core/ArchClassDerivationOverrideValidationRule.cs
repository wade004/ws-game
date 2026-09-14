using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Numbers.Archetype
{
    /// <summary>
    /// <c>arch.class.derivation_overrides</c> 专属的内容校验规则（分阶段落地计划 T-N1-4；ADR-0030
    /// 决策 2"职业模板可覆盖派生系数"；任务书"覆盖只允许指向 <c>stat.definition</c> 中确实存在的
    /// <c>(stat, source)</c> 派生边；不存在的边报加载期 Error"）：每条覆盖的 <c>stat</c> 必须是
    /// <c>category=derived</c> 的属性，且 <c>source</c> 必须出现在该属性 <c>derived_from[].stat</c>
    /// 登记的来源列表里——两者合起来才是"一条真实存在的派生边"。调用方需要
    /// <c>registry.RegisterValidationRule(new ArchClassDerivationOverrideValidationRule())</c> 才会
    /// 生效，本模块不自动注册（同 <see cref="ArchTalentTreeCycleValidationRule"/>，注册时机由宿主
    /// 统一掌控）。
    /// <para>
    /// 判断记录（检查名）：04 第 5 节数值类校验项分级表未列出本项（只列了"派生无环"
    /// 一条，那条对应 <c>stat.definition</c> 自己的 <c>StatDefinitionDerivationCycleValidationRule</c>），
    /// 按任务派发提示词"契约未给检查名，用 <c>arch_class_*</c> 前缀"命名，与
    /// <c>StatDefinitionValidationRule.CheckDerivedFromRequiresDerivedCategory</c> 同一处理口径。
    /// 设计层裁定（2026-09-14）：采纳。
    /// </para>
    /// <para>
    /// 判断记录（跨表校验、不是引用完整性）：<c>derivation_overrides[].stat</c>/<c>.source</c> 各自
    /// 指向不存在的 <c>stat.definition</c> 记录，已由 <see cref="FieldKind.Reference"/> 的
    /// <c>reference_integrity</c> 检查拦下（两个字段都已在 <c>ArchSchemas.DerivationOverrideEntrySchema</c>
    /// 登记为 <see cref="FieldKind.Reference"/>）；本规则只关心"两个都存在时，这条边是否真的登记在
    /// 目标属性的 <c>derived_from</c> 里"，是比引用完整性更细的语义约束，因此本规则对"某一侧引用本身
    /// 就不存在"的情形不重复报错——直接跳过该条目（读不到 <c>stat.definition</c> 对应记录时，视为
    /// "尚未确定是否是一条有效边"，留给 <c>reference_integrity</c> 单独报，避免同一条数据两条规则各报
    /// 一次造成报告噪音）。
    /// </para>
    /// </summary>
    public sealed class ArchClassDerivationOverrideValidationRule : IValidationRule
    {
        /// <summary>检查名——04 第 5 节分级表未列出本项，按 <c>arch_class_*</c> 前缀命名，待设计层
        /// 确认（见类型顶部判断记录）。</summary>
        public const string CheckName = "arch_class_derivation_override_requires_existing_edge";

        public string RuleId => nameof(ArchClassDerivationOverrideValidationRule);

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public bool NonEscalatable => false;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            // validEdges[(stat, source)]：stat.definition 里 category=derived 的属性，其
            // derived_from[].stat（来源）登记过的边——键沿用 arch.class.derivation_overrides 的字段名
            // (stat=目标派生属性, source=来源属性)，与 stat.definition.derived_from 内层字段名 "stat"
            // 语义不同，见 ArchSchemas.DerivationOverrideEntrySchema 判断记录。
            var validEdges = new HashSet<(string Stat, string Source)>();

            foreach (var record in view.GetAll("stat.definition"))
            {
                var isDerived = record.TryGetString("category", out var category) && category == "derived";
                if (!isDerived || !record.TryGetArray("derived_from", out var derivedFromArray))
                {
                    continue;
                }

                foreach (var entryValue in derivedFromArray)
                {
                    if (entryValue is JsonObject entryObj
                        && entryObj.TryGetValue("stat", out var sourceRaw) && sourceRaw is JsonString sourceStr)
                    {
                        validEdges.Add((record.Key, sourceStr.Value));
                    }
                }
            }

            foreach (var record in view.GetAll("arch.class"))
            {
                if (!record.TryGetArray("derivation_overrides", out var overridesArray))
                {
                    continue;
                }

                for (int i = 0; i < overridesArray.Count; i++)
                {
                    if (!(overridesArray[i] is JsonObject entryObj))
                    {
                        continue; // 形状不符已由字段级 field_type 报过，不重复报。
                    }

                    var hasStat = entryObj.TryGetValue("stat", out var statRaw) && statRaw is JsonString statStr;
                    var hasSource = entryObj.TryGetValue("source", out var sourceRaw) && sourceRaw is JsonString sourceStr;
                    if (!hasStat || !hasSource)
                    {
                        continue; // 必填字段缺失已由 required_field 报过，不重复报。
                    }

                    var statValue = ((JsonString)statRaw!).Value;
                    var sourceValue = ((JsonString)sourceRaw!).Value;

                    if (!validEdges.Contains((statValue, sourceValue)))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "arch.class", CheckName,
                            $"derivation_overrides[{i}] 覆盖了属性 \"{statValue}\" 的来源 \"{sourceValue}\"，" +
                            "但 stat.definition 中不存在这条派生边（目标属性须 category=derived，且来源须登记在其 derived_from 列表内）",
                            recordKey: record.Key, field: $"derivation_overrides[{i}]");
                    }
                }
            }
        }
    }
}
