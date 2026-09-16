using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Sim
{
    /// <summary>
    /// <c>sim.anchor</c> 专属的表级校验规则（T-N6-2a）：任务书"校验规则：等级从 1 起连续无缺口、无
    /// 重复；expected_item_level 单调不减（Warning 级）"。两项检查合在同一条规则里（同
    /// <c>Core.Numbers.Progression.ProgLevelCurveValidationRule</c> 一个规则类内产出多个检查名的既有
    /// 惯例），因为它们共用同一次"按 level 排序全表记录"的遍历。
    /// <para>
    /// 判断记录（<see cref="NonEscalatable"/> 为何整条规则登记为 <c>true</c>）：<see cref="IValidationRule.NonEscalatable"/>
    /// 只影响 <see cref="ValidationReport"/> 对 <see cref="ValidationSeverity.Warning"/> 级问题的阻断
    /// 判定（见该属性文档），<see cref="ValidationSeverity.Error"/> 级问题永远阻断、不受此标记影响
    /// （<c>ValidationReport</c> 构造函数只把 <c>NonEscalatable</c> 应用到按 RuleId 匹配到的 Warning
    /// 计数上）。本规则的"等级连续无缺口无重复"检查固定产出 Error，"expected_item_level 单调不减"
    /// 固定产出 Warning——整条规则登记 <c>NonEscalatable = true</c> 只让后者在
    /// <see cref="DataRegistryStrictness.WarningsBlock"/> 下也不阻断（04 第 5 节数值类校验项分级表
    /// "警告级抓意图不抓手滑"同一惯例），前者的阻断力不受任何影响。
    /// </para>
    /// </summary>
    public sealed class SimAnchorValidationRule : IValidationRule
    {
        /// <summary>04 第 5 节勘误新增检查名："等级从 1 起连续、无缺口、无重复"。</summary>
        public const string LevelContinuityCheck = "sim_anchor_level_continuity";

        /// <summary>04 第 5 节勘误新增检查名（警告级，不可提升）："expected_item_level 沿 level 单调不减"。</summary>
        public const string ExpectedItemLevelMonotonicCheck = "sim_anchor_expected_item_level_monotonic";

        public bool NonEscalatable => true;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var records = view.GetAll("sim.anchor");
            if (records.Count == 0)
            {
                yield break;
            }

            // 按 level 分组：既检测"同一 level 被多条记录重复登记"，又为下面的连续性/单调性检查
            // 提供一份按 level 排序的视图。缺失/类型不对的 level 字段已由内置 required_field/
            // field_type 检查报过，这里跳过不重复报。
            var byLevel = new SortedDictionary<int, List<DataRecord>>();
            foreach (var record in records)
            {
                if (!record.TryGetInt("level", out var levelRaw))
                {
                    continue;
                }

                var level = (int)levelRaw;
                if (!byLevel.TryGetValue(level, out var bucket))
                {
                    bucket = new List<DataRecord>();
                    byLevel[level] = bucket;
                }

                bucket.Add(record);
            }

            foreach (var kv in byLevel)
            {
                if (kv.Value.Count <= 1)
                {
                    continue;
                }

                var keys = string.Join(", ", kv.Value.Select(r => r.Key));
                foreach (var duplicate in kv.Value)
                {
                    yield return new ValidationIssue(ValidationSeverity.Error, "sim.anchor", LevelContinuityCheck,
                        $"level={kv.Key} 被多条记录重复登记（{keys}）：sim.anchor 须每级恰好一行", duplicate.Key, "level");
                }
            }

            var maxLevel = byLevel.Count == 0 ? 0 : byLevel.Keys.Max();
            for (var expectedLevel = 1; expectedLevel <= maxLevel; expectedLevel++)
            {
                if (!byLevel.ContainsKey(expectedLevel))
                {
                    yield return new ValidationIssue(ValidationSeverity.Error, "sim.anchor", LevelContinuityCheck,
                        $"level 须从 1 到 {maxLevel} 连续无缺口，缺少 level={expectedLevel}");
                }
            }

            double? previousExpectedItemLevel = null;
            var previousLevel = 0;
            foreach (var kv in byLevel)
            {
                // 同一 level 重复的记录上面已经整体报过 Error，这里任取第一条参与单调性比较，
                // 不再为同一个 level 重复参与比较。
                var record = kv.Value[0];
                if (!record.TryGetNumber("expected_item_level", out var expectedItemLevel))
                {
                    continue;
                }

                if (previousExpectedItemLevel.HasValue && expectedItemLevel < previousExpectedItemLevel.Value)
                {
                    yield return new ValidationIssue(ValidationSeverity.Warning, "sim.anchor", ExpectedItemLevelMonotonicCheck,
                        $"level={kv.Key} 的 expected_item_level（{expectedItemLevel}）低于 level={previousLevel}（{previousExpectedItemLevel.Value}）：期望装备等级曲线 E(L)（07 第 1.2 节）须沿角色等级单调不减",
                        record.Key, "expected_item_level");
                }

                previousExpectedItemLevel = expectedItemLevel;
                previousLevel = kv.Key;
            }
        }
    }

    /// <summary>
    /// <c>sim.scenario</c> 专属的表级校验规则（T-N6-2a）：字段登记表达不了的条件必填/互斥约束——
    /// "kind=arena/coverage 时 levels 非空""kind=growth 时 level_from/level_to 均填且 from&lt;=to"
    /// "opponent.level 与 opponent.level_offsets 至多指定一个"，三者共用同一个检查名（同一表内多种
    /// 条件必填场景合并登记一个检查名，惯例同 <c>Core.Gameplay.Economy.EconomyContentValidationRule</c>
    /// 里 <c>restock_policy=timer</c> 时 <c>restock_timer</c> 必填一类条件必填检查）。
    /// <para>
    /// 判断记录（2026-09-16，深度复审 E-S2：<c>bandwidths</c> 键名拼写错误检查）：<c>bandwidths</c>
    /// 在 schema 里登记为 <see cref="MapSchema.FreeKeyed"/>（自由字符串键，"本表不枚举合法键"），
    /// 全链路（schema、本规则、<c>BaselineComparer.ResolveTolerance</c>、
    /// <c>GrowthSimulation</c>/<c>ArenaSimulation</c> 的 <c>ResolveBandwidths</c>）此前都只用
    /// <c>TryGetValue</c> 静默回退默认值——任何一处拼错键名（如误把 <c>level_duration</c> 写成
    /// <c>leve_duration</c>）都不会在任何环节报出诊断，内容作者永远不会知道自己配置的带宽从未生效，
    /// 与仓库其它数值类警告一贯的"抓意图不抓手滑"哲学（如 <c>stat_definition_no_consumer</c> 专门
    /// 用来抓"登记了却没接上"这类疏漏）不一致。新增 <see cref="BandwidthKeyUnknownCheck"/>（警告级，
    /// 不可提升）：<c>bandwidths</c> 的键若不属于 <see cref="SimBandwidthKeys.KnownKeys"/>（与
    /// <see cref="BaselineCompareOptions.DefaultLeafBandwidthKeys"/> 单一来源，见该类型判断记录），
    /// 报一条"未识别的带宽键，可能是拼写错误，本次不会生效"的诊断——本条与 <see
    /// cref="LevelCoverageCheck"/>（阻断）共享同一个规则类实例，本类型 <see cref="NonEscalatable"/>
    /// 因此改为 <c>true</c>（同 <see cref="SimAnchorValidationRule"/> 判断记录"为何整条规则登记为
    /// true"同一处理口径：只影响本类产出的 Warning 在 <see cref="DataRegistryStrictness.WarningsBlock"/>
    /// 下是否阻断，Error 恒阻断不受影响）。
    /// </para>
    /// </summary>
    public sealed class SimScenarioValidationRule : IValidationRule
    {
        /// <summary>04 第 5 节勘误新增检查名："场景等级覆盖字段按 kind 条件必填，且 opponent 等级
        /// 指定方式二选一"。</summary>
        public const string LevelCoverageCheck = "sim_scenario_level_coverage_required";

        /// <summary>深度复审 E-S2 新增检查名（警告级，不可提升）："bandwidths 键名不在已知带宽键
        /// 集合内，可能是拼写错误"。</summary>
        public const string BandwidthKeyUnknownCheck = "sim_scenario_bandwidth_key_unknown";

        public bool NonEscalatable => true;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll("sim.scenario"))
            {
                if (record.TryGetString("kind", out var kind))
                {
                    switch (kind)
                    {
                        case "arena":
                        case "coverage":
                            if (!record.TryGetArray("levels", out var levels) || levels.Count == 0)
                            {
                                yield return new ValidationIssue(ValidationSeverity.Error, "sim.scenario", LevelCoverageCheck,
                                    $"kind={kind} 场景必须登记非空的 levels（要覆盖的玩家等级集合）", record.Key, "levels");
                            }
                            break;
                        case "growth":
                            var hasFrom = record.TryGetInt("level_from", out var from);
                            var hasTo = record.TryGetInt("level_to", out var to);
                            if (!hasFrom || !hasTo)
                            {
                                yield return new ValidationIssue(ValidationSeverity.Error, "sim.scenario", LevelCoverageCheck,
                                    "kind=growth 场景必须同时登记 level_from 与 level_to", record.Key);
                            }
                            else if (from > to)
                            {
                                yield return new ValidationIssue(ValidationSeverity.Error, "sim.scenario", LevelCoverageCheck,
                                    $"level_from（{from}）不能大于 level_to（{to}）", record.Key, "level_to");
                            }
                            break;
                        // kind 的合法取值集合已由 FieldKind.Enum 在加载期阻断，走不到其它分支。
                    }
                }

                if (record.TryGetObject("opponent", out var opponent))
                {
                    var hasLevel = opponent.TryGetValue("level", out var levelVal) && levelVal.Kind != JsonKind.Null;
                    var hasOffsets = opponent.TryGetValue("level_offsets", out var offsetsVal) && offsetsVal.Kind != JsonKind.Null;
                    if (hasLevel && hasOffsets)
                    {
                        yield return new ValidationIssue(ValidationSeverity.Error, "sim.scenario", LevelCoverageCheck,
                            "opponent.level 与 opponent.level_offsets 至多指定一个（同级战斗 vs 越级矩阵二选一）", record.Key, "opponent");
                    }
                }

                if (record.TryGetObject("bandwidths", out var bandwidths))
                {
                    foreach (var kv in bandwidths)
                    {
                        if (!SimBandwidthKeys.KnownKeys.Contains(kv.Key))
                        {
                            // 判断记录：已知键集合按 Ordinal 排序后拼进提示文本，避免 HashSet 枚举
                            // 顺序不确定性泄漏进报告文本（仓库通篇"报告输出确定性"惯例）。
                            var knownKeysText = string.Join("/", SimBandwidthKeys.KnownKeys.OrderBy(k => k, StringComparer.Ordinal));
                            yield return new ValidationIssue(ValidationSeverity.Warning, "sim.scenario", BandwidthKeyUnknownCheck,
                                $"bandwidths 键 \"{kv.Key}\" 不是已知带宽键（{knownKeysText}），" +
                                "可能是拼写错误，本次不会生效——本表登记为自由字符串键，不会在加载期报错，只能靠本项警告发现",
                                record.Key, $"bandwidths.{kv.Key}");
                        }
                    }
                }
            }
        }
    }
}
