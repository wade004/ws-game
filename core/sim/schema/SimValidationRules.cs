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

    /// <summary>
    /// 消费方反馈第 53 条：<c>Core.Sim.GrowthSimulation</c> 选同 <c>tier</c>+<c>level</c> 的
    /// <c>creature.template</c> 时，运行时已根治为"按阵营过滤 + 歧义诊断"（见
    /// <see cref="GrowthSimulation.ResolveCreatureFamily"/> 判断记录）；本规则是同一问题在内容校验层
    /// 的对应检查——数据配平阶段就能发现"同档位登记了多条对玩家阵营敌对的生物"这一容易被忽略的疏漏，
    /// 不必等到跑一次成长仿真才能看到 <c>Kills=0</c>/等级卡死的症状。
    /// <para>
    /// 判断记录（静态校验与运行时口径的差异：玩家阵营取字面量 <c>"fac.player"</c>，不是某个具体标准
    /// 玩家实例的实际阵营）：本规则在数据加载期运行，此时没有任何已装配的
    /// <see cref="Core.Sim.HeadlessWorld"/>/<c>PlayerUnit</c> 实例可读——
    /// <see cref="GrowthSimulation.ResolveCreatureFamily"/> 的运行时修法（读
    /// <c>world.Player.FactionId</c>）在此不可行。仓库全部无头仿真入口
    /// （<see cref="HeadlessWorldOptions.PlayerFactionId"/>/<see cref="FightRunnerOptions.PlayerFactionId"/>）
    /// 都把 <c>"fac.player"</c> 作为玩家阵营的默认值且从未被 <c>sim.scenario</c> 覆盖（该表 schema
    /// 未登记任何"玩家阵营"字段），本规则据此按同一约定俗成的默认值判定，与运行时实际生效的阵营一致；
    /// 若未来 <c>sim.scenario</c> 新增可覆盖玩家阵营的字段，本规则需要跟着改，届时属于新增字段/ADR
    /// 讨论范围，不在本次改动内。
    /// </para>
    /// <para>
    /// 判断记录（不复用 <see cref="Core.Numbers.Faction.FactionMatrix"/> 类型）：该类型构造函数要求一个
    /// 真实 <c>IEventBus</c> 实例（供 <c>SetReaction</c> 运行期覆盖时发布事件），而
    /// <see cref="IValidationRule.Validate(IDataRegistryView)"/> 只拿到只读视图、没有事件总线——本规则
    /// 只需要"只读查询"这一半能力（数据校验期不存在任何运行期覆盖），因此内联一份与
    /// <see cref="Core.Numbers.Faction.FactionMatrix.GetReaction"/> 逐条同构的最小只读解析（同阵营恒
    /// Friendly → <c>fac.reaction_matrix</c> 显式行 → <c>fac.faction.default_reaction</c> 回退），不
    /// 新增任何跨程序集依赖或事件总线桩对象。
    /// </para>
    /// <para>
    /// 判断记录（<c>fac.faction</c>/<c>fac.player</c> 未登记时的降级）：<c>fac.faction</c>/
    /// <c>fac.reaction_matrix</c> 均由 <c>Core.Numbers.Faction</c> 模块登记，<see
    /// cref="IDataRegistryView.GetAll"/> 对未注册/未加载的表返回空集合、不抛异常（见
    /// <c>Core.Foundation.DataRegistry.DataRegistry.GetAllUnchecked</c>）；若某数据根未装配阵营系统或
    /// 未按约定使用 <c>fac.player</c> 这个 id，本规则直接 <c>yield break</c>——不臆造"全部生物默认
    /// 敌对"的假设，避免在不适用的数据根上产生无意义告警。
    /// </para></summary>
    public sealed class SimGrowthOpponentAmbiguityValidationRule : IValidationRule
    {
        /// <summary>反馈第 53 条新增检查名（警告级，不可提升）："同 tier+level 存在多条对玩家阵营
        /// 敌对的 creature.template 记录"。</summary>
        public const string OpponentAmbiguousCheck = "sim_growth_opponent_ambiguous";

        private static readonly string AssumedPlayerFactionId = "fac.player"; // 见类型判断记录。

        public bool NonEscalatable => true;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var factionRows = view.GetAll("fac.faction");
            if (factionRows.Count == 0)
            {
                yield break; // 数据根未启用阵营系统，见类型判断记录。
            }

            var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in factionRows)
            {
                if (row.TryGetId("id", out var id) && row.TryGetString("default_reaction", out var reaction))
                {
                    defaults[id.Value] = reaction;
                }
            }
            if (!defaults.ContainsKey(AssumedPlayerFactionId))
            {
                yield break; // 本数据根未按约定使用 fac.player，静态规则无法判定，见类型判断记录。
            }

            var explicitReactions = new Dictionary<(string From, string To), string>();
            foreach (var row in view.GetAll("fac.reaction_matrix"))
            {
                if (row.TryGetId("from", out var from) && row.TryGetId("to", out var to) && row.TryGetString("reaction", out var reaction))
                {
                    explicitReactions[(from.Value, to.Value)] = reaction;
                }
            }

            // 判断方向须与 GrowthSimulation.ResolveCreatureFamily 一致：GetReaction(玩家, 候选)——
            // "从玩家视角看这个候选是否敌对"，不是反过来（见该方法判断记录"阵营过滤"关于
            // data/_sample 非对称反应矩阵的反例说明：GetReaction(from,to) 回退到 defaults[from]，
            // from 固定是玩家阵营，因此没有显式行时的回退值取玩家阵营自己的 default_reaction，不是
            // 候选阵营的）。
            bool IsHostileToPlayer(string factionId)
            {
                if (string.Equals(factionId, AssumedPlayerFactionId, StringComparison.Ordinal))
                {
                    return false; // 同阵营恒 Friendly（同 FactionMatrix.GetReaction 优先级①）。
                }
                if (explicitReactions.TryGetValue((AssumedPlayerFactionId, factionId), out var explicitReaction))
                {
                    return explicitReaction == "hostile";
                }
                return defaults.TryGetValue(AssumedPlayerFactionId, out var def) && def == "hostile";
            }

            // 按 tier 分组，组内再按 level 分组：找出敌对候选 >= 2 条的 (tier, level) 组合。
            var byTier = new Dictionary<string, List<(int Level, string TemplateId)>>(StringComparer.Ordinal);
            foreach (var record in view.GetAll("creature.template"))
            {
                if (!record.TryGetId("tier", out var tier) || !record.TryGetId("faction_id", out var factionId))
                {
                    continue; // 缺字段已由内置 required_field 检查报过，这里不重复报。
                }
                if (!IsHostileToPlayer(factionId.Value))
                {
                    continue;
                }
                var level = record.TryGetInt("level", out var lvl) ? (int)lvl : 1;
                if (!byTier.TryGetValue(tier.Value, out var bucket))
                {
                    bucket = new List<(int, string)>();
                    byTier[tier.Value] = bucket;
                }
                bucket.Add((level, record.Key));
            }

            foreach (var tierKv in byTier.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                foreach (var group in tierKv.Value.GroupBy(e => e.Level).OrderBy(g => g.Key))
                {
                    var groupList = group.ToList();
                    if (groupList.Count <= 1)
                    {
                        continue;
                    }

                    var ids = string.Join(", ", groupList.Select(e => e.TemplateId));
                    foreach (var entry in groupList)
                    {
                        yield return new ValidationIssue(ValidationSeverity.Warning, "creature.template", OpponentAmbiguousCheck,
                            $"tier={tierKv.Key}、level={group.Key} 下存在多条对玩家阵营（{AssumedPlayerFactionId}）敌对的 " +
                            $"creature.template 候选（{ids}）：GrowthSimulation.ResolveCreatureFamily 同档位多条候选时只取" +
                            "登记顺序第一条、其余静默丢弃，请确认是否为数据配平疏漏",
                            entry.TemplateId, "tier");
                    }
                }
            }
        }
    }
}
