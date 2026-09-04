using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Numbers.Progression
{
    /// <summary>
    /// <c>prog.level_curve</c> 专属的内容校验规则（见 04_数据与内容管线.md 第 5 节校验器扩展点
    /// <c>IValidationRule</c>；本表的字段是本模块实现期补录，04 未给出，见 schema/README.md）：
    /// <c>entries</c> 的元素个数必须等于 <c>max_level</c>，且每个元素的 <c>level</c> 字段必须从
    /// 1 连续递增到 <c>max_level</c>（任务书"校验规则：entries 等级连续、max_level ==
    /// entries.Count"）。调用方需要 <c>registry.RegisterValidationRule(new
    /// ProgLevelCurveValidationRule())</c> 才会生效——本模块不自动注册（一致于本模块不实现任何
    /// 具体业务规则以外的隐式副作用，见 data_registry/README.md"不负责什么"）。
    /// <para>
    /// 缺少 <c>max_level</c>/<c>entries</c> 字段本身由 04 的 <c>required_field</c>/
    /// <c>field_type</c> 检查负责，本规则只在字段存在时做进一步的结构校验，避免和内置检查重复
    /// 报错。
    /// </para>
    /// </summary>
    public sealed class ProgLevelCurveValidationRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll("prog.level_curve"))
            {
                if (!record.TryGetInt("max_level", out var maxLevelRaw) || !record.TryGetArray("entries", out var entries))
                {
                    continue;
                }

                var maxLevel = (int)maxLevelRaw;
                if (entries.Count != maxLevel)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "prog.level_curve", "level_curve_entries",
                        $"entries 数量（{entries.Count}）与 max_level（{maxLevel}）不一致",
                        record.Key);
                    continue;
                }

                for (var i = 0; i < entries.Count; i++)
                {
                    var expectedLevel = i + 1;
                    var ok = entries[i] is JsonObject entryObj
                        && entryObj.TryGetValue("level", out var levelValue)
                        && levelValue is JsonNumber levelNum
                        && levelNum.TryGetInt64(out var actualLevel)
                        && actualLevel == expectedLevel;

                    if (!ok)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "prog.level_curve", "level_curve_continuity",
                            $"entries[{i}] 的 level 应为 {expectedLevel}",
                            record.Key);
                    }
                }
            }
        }
    }
}
