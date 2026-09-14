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
    /// entries.Count"）；分阶段落地计划 T-N0-5（拍板 3：本表保持逐级密集枚举、不迁移到断点表形态，
    /// 只接入单调校验；数值总纲第 3 节原则 1、ADR-0033）再加一条：<c>xp_to_next</c> 沿等级不递减
    /// （检查名 <c>level_curve_xp_monotonic</c>），末级条目除外——满级没有"下一级"，其 <c>xp_to_next</c>
    /// 按约定填 0（数值设计 04"满级后经验直接不产生"），不参与比较。调用方需要
    /// <c>registry.RegisterValidationRule(new ProgLevelCurveValidationRule())</c> 才会生效——本模块
    /// 不自动注册（一致于本模块不实现任何具体业务规则以外的隐式副作用，见 data_registry/README.md
    /// "不负责什么"）。
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

                // T-N0-5：xp_to_next 沿等级不递减（末级条目除外）。元素非对象/缺字段/类型不对已由
                // required_field/field_type 报过，这里跳过不重复报。
                for (var i = 0; i + 1 < entries.Count - 1; i++)
                {
                    if (!TryGetXpToNext(entries[i], out var current) || !TryGetXpToNext(entries[i + 1], out var next))
                    {
                        continue;
                    }

                    if (next < current)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "prog.level_curve", "level_curve_xp_monotonic",
                            $"entries[{i + 1}].xp_to_next（{next}）低于前一级（{current}）：升级所需经验须沿等级单调递增（允许相等；末级条目不参与比较）",
                            record.Key, $"entries[{i + 1}].xp_to_next");
                    }
                }
            }
        }

        private static bool TryGetXpToNext(JsonValue entry, out long value)
        {
            if (entry is JsonObject obj && obj.TryGetValue("xp_to_next", out var raw) && raw is JsonNumber num && num.TryGetInt64(out value))
            {
                return true;
            }

            value = 0;
            return false;
        }
    }
}
