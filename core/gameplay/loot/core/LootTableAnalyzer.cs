using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// 消费方内容编辑器第 35 条收口：<see cref="LootHost"/> 只提供"抽一次给我结果"的黑箱接口，编辑器
    /// 预览"这张表大致会掉出什么、各自概率多少"时没有公开入口——嵌套表、条件、多抽不放回、保底等情形
    /// 的期望概率只能复制 <see cref="LootHost"/> 私有抽取语义或蒙特卡洛逼近。本类型提供解析式（或标注
    /// 近似的）期望概率计算，与 <see cref="LootHost"/> 共用 <see cref="LootRollCore"/> 的条件筛选/权重
    /// 归一步骤（见该类型注释），不重新发明一套语义。
    /// <para>
    /// 抽取语义清单（对照 <see cref="LootHost.RollTableInto"/>/<see
    /// cref="LootHost.RollChanceEachGroup"/>/<see cref="LootHost.RollWeightedGroup"/>，本类型的每一步
    /// 计算都在下面标注它对应哪一条运行期语义）：
    /// </para>
    /// <list type="number">
    /// <item><description><c>chance_each</c> 条目：每条独立伯努利判定，命中概率
    /// = <c>Clamp01(weightOrChance × multiplier)</c>，启用伪随机时再按连续未中次数线性放大（封顶 1）；
    /// 一次 <see cref="LootHost.Roll"/> 只判定一次（不是"重复抽到保底为止"），未命中不产出，命中则数量
    /// 在 <c>[min,max]</c> 闭区间均匀分布。</description></item>
    /// <item><description><c>weighted_pick_one</c> 分组：按条件过滤出候选池后按
    /// <c>weightOrChance</c> 归一化权重、不放回抽取 <c>pickCount</c>（默认 1）条；单抽时条目 i 的命中
    /// 概率就是 <c>w_i / ΣW</c>，多抽不放回时是"不放回顺序抽样"过程（每次按当前剩余候选池的归一权重
    /// 抽一条、移出候选池、重复），候选池权重合计 &lt;=0 时整组提前停止（不产出任何条目，见
    /// <see cref="LootHost.PickWeighted"/> 返回 null 即 break 的既有行为）。</description></item>
    /// <item><description>条件（<see cref="LootEntry.Condition"/>）：null 恒真；非 null 时按
    /// <see cref="LootRollCore.ConditionPasses"/> 求值，不满足的条目在该次判定里视同不存在（既不参与
    /// <c>chance_each</c> 判定，也不进入 <c>weighted_pick_one</c>/保底候选池）。</description></item>
    /// <item><description>嵌套 <c>loot.*</c> 引用：<c>ref</c> 命中后，把 <c>[min,max]</c> 里抽出的具体
    /// <c>count</c> 值解释为"把被引用的整张表独立再抽取 <c>count</c> 次"（见 <c>LootHost</c> 判断记录
    /// 2），每次独立走一遍该嵌套表自己的分组/保底逻辑，产出并入外层结果；递归深度超过
    /// <see cref="LootAnalysisContext.MaxNestedDepth"/> 或引用了 <see cref="LootAnalysisContext.Tables"/>
    /// 里不存在的表时静默跳过该分支（不贡献概率/期望数量），不抛异常，与 <see cref="LootHost"/> 运行期
    /// 行为一致。</description></item>
    /// <item><description>保底 <c>guaranteed_min</c>：本表按各组正常规则跑完一遍后，若"产出条目数"
    /// （每条命中的 <c>chance_each</c> 或被抽中的 <c>weighted_pick_one</c> 各算一条，不是最终合并堆叠
    /// 数）仍不足 <c>guaranteed_min</c>，从全表全部条目（跨所有分组、按条件过滤）组成的候选池按权重
    /// 不放回补抽，直到达标或候选池耗尽——同一条目可能"自然命中一次 + 保底又补中一次"，两次都计入
    /// （不去重，见 <see cref="LootHostRollTests.GuaranteedMin_TopsUpResultWhenNaturalRollFallsShort"/>
    /// 用例"两次命中同一 ref，合并堆叠数量应为 2"）。</description></item>
    /// <item><description>合并：<see cref="LootHost.Roll"/> 最终按模板 id 合并堆叠数量返回；本类型的
    /// <see cref="LootExpectedOutcome"/> 同样按 <see cref="LootEntry.Ref"/>（嵌套展开后的叶子）聚合，
    /// 语义对应。</description></item>
    /// <item><description>货币：本模块的 <see cref="LootEntry.Ref"/> 只有 <c>item.*</c>/<c>loot.*</c>
    /// 两种合法域（见 <see cref="LootTableParser.ParseEntry"/> 的域名校验），没有独立的"货币"引用域——
    /// 货币类掉落在本架构下就是一个 <c>item.template</c>，与其它物品走同一条聚合路径，本类型不需要为
    /// "货币项"单独分支。</description></item>
    /// </list>
    /// <para>
    /// 精确 / 近似边界：
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>精确</b>：单抽 <c>weighted_pick_one</c>（权重除以合计）、<c>chance_each</c>
    /// 单次伯努利（含伪随机的"当前有效概率"，因为一次 <c>Roll</c> 只判定一次，不是级数求和）、嵌套
    /// 展开的期望数量线性组合（期望值运算永远可加，与是否独立无关）都是解析式精确值。</description></item>
    /// <item><description><b>不放回多抽（<c>pick_count &gt; 1</c> 或保底补抽）在候选池条目数
    /// &lt;= <see cref="LootAnalysisContext.ExactWithoutReplacementMaxEntries"/>（默认 12）时</b>：用
    /// 位掩码动态规划精确枚举"抽取到第 k 步为止、某条目是否已被移出候选池"的全部概率分支——与
    /// <see cref="LootHost.PickWeighted"/> 逐步"按剩余候选池归一权重抽一条、移出、重复"完全同构（含
    /// "剩余候选池权重合计 &lt;=0 时整个分支提前停止"这一行为，见 <c>InclusionProbabilitiesExact</c>
    /// 判断记录），是精确值，不是近似。</description></item>
    /// <item><description><b>不放回多抽超过该条目数阈值时</b>：退化为"视作放回抽样"的近似估计
    /// <c>1-(1-w_i/ΣW)^k</c>（阈值以内精确算法的状态数按候选池条目数指数增长，超阈值改用这个更廉价、
    /// 但会略微高估"重权重条目多次被抽中"概率的近似式），对应产出标注
    /// <see cref="LootExpectedOutcome.IsApproximate"/> = true。</description></item>
    /// <item><description><b>保底（<c>guaranteed_min</c>）补抽</b>：自然产出条目数本身是随机变量
    /// （<c>chance_each</c> 部分是独立但概率不同的伯努利之和，<c>weighted_pick_one</c> 部分是确定性
    /// 数值——见下一条），本类型精确计算这个分布（泊松二项分布，O(条目数²) 动态规划）。"该条目自然
    /// 命中"与"该条目被保底补抽命中"两件事是否能当独立事件合并，按条目类型分两种情形（见
    /// <see cref="ResolveGuaranteedMin"/> 方法注释逐条判断记录）：<c>chance_each</c> 直接条目（自身
    /// 是否命中直接是"自然产出数 c"的一个加数，与补抽规模天然相关）用条件概率精确展开——"自然命中
    /// 概率 + 自然未命中概率 × (排除该条目自身贡献后的 c 分布下)补抽命中概率"，不是近似；
    /// <c>weighted_pick_one</c> 条目（一次抽取固定选出 <c>k</c> 条，<c>k</c> 本身不随机，选中哪几条
    /// 不影响 <c>k</c>，因此该条目是否被本组自然选中与 c 的分布无关）用边际分布独立合并同样是精确值；
    /// 只有 <c>loot.*</c> 嵌套条目（自然命中与补抽命中若同时发生，等价于两次独立的嵌套子抽取，本类型
    /// 不展开建模这一层）与"补抽候选池条目数超过精确阈值"两种情形仍标注
    /// <see cref="LootExpectedOutcome.IsApproximate"/> = true；已用蒙特卡洛（固定种子、>=100000 次）
    /// 对照验证，见 <c>tests/E35_LootTableAnalyzerTests.cs</c> 的 <c>GuaranteedMin_*</c> 用例。
    /// </description></item>
    /// </list>
    /// </summary>
    public static class LootTableAnalyzer
    {
        /// <summary>
        /// 计算 <paramref name="def"/> 一次 <see cref="LootHost.Roll"/> 调用的期望产出分布（按叶子
        /// <see cref="LootEntry.Ref"/> 聚合，嵌套 <c>loot.*</c> 展开后只剩 <c>item.*</c>）。不修改
        /// <paramref name="def"/>/<paramref name="context"/> 的任何状态，不消耗随机数，可重复调用
        /// （见 <c>tests/E35_*</c>"分析不改变宿主状态"对照）。
        /// </summary>
        public static IReadOnlyList<LootExpectedOutcome> ExpectedProbabilities(LootTableDef def, LootAnalysisContext context)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.ConditionMode == LootConditionEvaluationMode.Evaluate && context.ExprHost == null)
            {
                throw new InvalidOperationException(
                    "LootAnalysisContext.ConditionMode=Evaluate 时必须提供 ExprHost");
            }

            var diagnostics = context.Diagnostics ?? new ExprDiagnosticsRecorder();
            var accumulator = AnalyzeTableSingleRoll(def, context, context.ExprHost, diagnostics, depth: 0);

            var result = new List<LootExpectedOutcome>(accumulator.Count);
            foreach (var kv in accumulator)
            {
                var acc = kv.Value;
                result.Add(new LootExpectedOutcome(
                    kv.Key,
                    LootRollCore.Clamp01(1 - acc.ProbabilityNone),
                    acc.ExpectedCount,
                    acc.Paths,
                    acc.IsApproximate));
            }

            return result;
        }

        // -----------------------------------------------------------------
        // 单次 Roll 的解析式期望分布（可递归用于嵌套表）
        // -----------------------------------------------------------------

        private static Dictionary<Id, LeafAccumulator> AnalyzeTableSingleRoll(
            LootTableDef def, LootAnalysisContext context, IExprHost? exprHost, IExprDiagnostics diagnostics, int depth)
        {
            var accumulator = new Dictionary<Id, LeafAccumulator>();

            // 同 LootHost.RollTableInto 判断记录：递归深度超过上限时运行期静默停止，不贡献任何产出。
            if (depth > context.MaxNestedDepth)
            {
                return accumulator;
            }

            var chanceEachFireProbabilities = new List<double>();
            var chanceEachIndexByKey = new Dictionary<(int GroupIndex, int EntryIndex), int>();
            var naturalProbabilityByKey = new Dictionary<(int GroupIndex, int EntryIndex), double>();
            var candidatePool = new List<(LootEntry Entry, int GroupIndex, int EntryIndex, double NaturalProbability)>();
            var deterministicWeightedTotal = 0;

            for (var gi = 0; gi < def.Groups.Count; gi++)
            {
                var group = def.Groups[gi];

                if (group.RollMode == LootRollMode.ChanceEach)
                {
                    for (var ei = 0; ei < group.Entries.Count; ei++)
                    {
                        var entry = group.Entries[ei];
                        if (!ConditionPasses(entry, context, exprHost, diagnostics))
                        {
                            continue;
                        }

                        var baseChance = LootRollCore.EffectiveChanceEach(entry.WeightOrChance, context.Multiplier);
                        var effectiveChance = baseChance;
                        if (context.PseudoRandom)
                        {
                            var streak = ResolveMissStreak(context, def.Id, gi, ei);
                            effectiveChance = LootRollCore.ApplyPseudoRandomStep(baseChance, streak, context.PseudoRandomStep);
                        }

                        chanceEachIndexByKey[(gi, ei)] = chanceEachFireProbabilities.Count;
                        naturalProbabilityByKey[(gi, ei)] = effectiveChance;
                        chanceEachFireProbabilities.Add(effectiveChance);
                        ResolveEntryContribution(entry, effectiveChance, isApproximate: false,
                            $"groups[{gi}].entries[{ei}]", context, exprHost, diagnostics, depth, accumulator);
                    }
                }
                else
                {
                    var poolIndexed = FilterEligibleIndexed(group.Entries, context, exprHost, diagnostics);
                    var pool = new List<LootEntry>(poolIndexed.Count);
                    foreach (var item in poolIndexed)
                    {
                        pool.Add(item.Entry);
                    }

                    var k = Math.Min(group.PickCount ?? 1, pool.Count);
                    deterministicWeightedTotal += k;

                    var inclusion = InclusionProbabilities(pool, k, context.ExactWithoutReplacementMaxEntries);
                    for (var pi = 0; pi < poolIndexed.Count; pi++)
                    {
                        naturalProbabilityByKey[(gi, poolIndexed[pi].Index)] = inclusion.Probabilities[pi];
                        ResolveEntryContribution(poolIndexed[pi].Entry, inclusion.Probabilities[pi], inclusion.Approximate,
                            $"groups[{gi}].entries[{poolIndexed[pi].Index}]", context, exprHost, diagnostics, depth, accumulator);
                    }
                }

                // 保底候选池：不论本组 roll_mode，按条件过滤后的全部条目都算候选（同 LootHost 判断记录 3
                // "从全表全部条目（跨所有分组、按条件过滤后）组成候选池"），用刚算出的自然命中概率
                // （chance_each 的有效概率 / weighted_pick_one 组内自身的命中概率）回填，供
                // <see cref="ResolveGuaranteedMin"/> 做"自然命中"与"保底补抽命中"的精确合并。
                for (var ei = 0; ei < group.Entries.Count; ei++)
                {
                    if (ConditionPasses(group.Entries[ei], context, exprHost, diagnostics))
                    {
                        var naturalProbability = naturalProbabilityByKey.TryGetValue((gi, ei), out var np) ? np : 0.0;
                        candidatePool.Add((group.Entries[ei], gi, ei, naturalProbability));
                    }
                }
            }

            if (def.GuaranteedMin.HasValue)
            {
                ResolveGuaranteedMin(def, context, exprHost, diagnostics, depth, chanceEachFireProbabilities,
                    chanceEachIndexByKey, deterministicWeightedTotal, candidatePool, accumulator);
            }

            return accumulator;
        }

        /// <summary>
        /// 保底补抽命中的合并规则——见类型注释"精确 / 近似边界"一节：
        /// <list type="bullet">
        /// <item><description><c>chance_each</c> 直接条目（<c>item.*</c>）：自身是否自然命中直接影响
        /// "自然产出数 c"的分布（它本身就是 c 的一个加数），因此与补抽规模存在相关——精确处理为
        /// "自然命中概率 + 自然未命中概率 × (在排除该条目自身贡献后的 c 分布下)补抽命中概率"，两个条件
        /// 分支互斥且穷尽，不是近似。</description></item>
        /// <item><description><c>weighted_pick_one</c> 条目（含直接与嵌套）：本组一次抽取固定选出
        /// <c>k</c> 条（<c>k</c> 本身是确定性数值，不随机——见 <see cref="LootHost.RollWeightedGroup"/>
        /// 循环体"pool.Count>0 就必然按权重选中一条"），具体选中了候选池里哪几条不影响 <c>k</c> 的值，
        /// 因此"该条目是否被本组自然选中"与"c 的分布"无关，用边际分布做独立合并同样是精确值。</description></item>
        /// <item><description><c>loot.*</c> 嵌套条目：即便上面两类条目本身的"命中概率"合并是精确的,
        /// 嵌套展开还叠加了"自然命中与补抽命中若同时发生，等于两次独立的嵌套子抽取"这一层，本类型
        /// 不展开这一步的精确建模（见类型注释"保底"一节判断记录），统一按独立路径合并处理，标注
        /// 近似。</description></item>
        /// </list>
        /// </summary>
        private static void ResolveGuaranteedMin(
            LootTableDef def, LootAnalysisContext context, IExprHost? exprHost, IExprDiagnostics diagnostics, int depth,
            List<double> chanceEachFireProbabilities, Dictionary<(int GroupIndex, int EntryIndex), int> chanceEachIndexByKey,
            int deterministicWeightedTotal,
            List<(LootEntry Entry, int GroupIndex, int EntryIndex, double NaturalProbability)> candidatePool,
            Dictionary<Id, LeafAccumulator> accumulator)
        {
            var guaranteedMin = def.GuaranteedMin!.Value;
            var poolSize = candidatePool.Count;
            if (poolSize == 0)
            {
                return;
            }

            var candidatePoolEntries = new List<LootEntry>(poolSize);
            foreach (var c in candidatePool)
            {
                candidatePoolEntries.Add(c.Entry);
            }

            var topUpCache = new Dictionary<int, InclusionResult>();
            InclusionResult GetInclusion(int size)
            {
                if (!topUpCache.TryGetValue(size, out var cached))
                {
                    cached = InclusionProbabilities(candidatePoolEntries, size, context.ExactWithoutReplacementMaxEntries);
                    topUpCache[size] = cached;
                }

                return cached;
            }

            // 边际（不区分某条目自身是否命中）补抽命中概率——供期望数量（永远可加，与相关性无关）与
            // weighted_pick_one/loot.* 条目的合并使用。
            var marginalDistribution = PoissonBinomialDistribution(chanceEachFireProbabilities);
            var marginalTopUpProbability = new double[poolSize];
            var marginalApproximate = false;
            for (var c = 0; c < marginalDistribution.Length; c++)
            {
                var probabilityOfC = marginalDistribution[c];
                if (probabilityOfC <= 0)
                {
                    continue;
                }

                var naturalTotal = c + deterministicWeightedTotal;
                var topUpSize = Math.Max(0, Math.Min(guaranteedMin - naturalTotal, poolSize));
                var inclusion = GetInclusion(topUpSize);
                marginalApproximate = marginalApproximate || inclusion.Approximate;
                for (var i = 0; i < poolSize; i++)
                {
                    marginalTopUpProbability[i] += probabilityOfC * inclusion.Probabilities[i];
                }
            }

            for (var i = 0; i < poolSize; i++)
            {
                var (entry, gi, ei, naturalProbability) = candidatePool[i];

                if (entry.Ref.Domain != "loot" && chanceEachIndexByKey.TryGetValue((gi, ei), out var idx))
                {
                    // chance_each 直接条目：精确条件合并（见方法注释第一条）。
                    var excludedDistribution = PoissonBinomialDistributionExcluding(chanceEachFireProbabilities, idx);
                    var topUpGivenNotFired = 0.0;
                    var approximate = false;
                    for (var c = 0; c < excludedDistribution.Length; c++)
                    {
                        var probabilityOfC = excludedDistribution[c];
                        if (probabilityOfC <= 0)
                        {
                            continue;
                        }

                        var naturalTotal = c + deterministicWeightedTotal;
                        var topUpSize = Math.Max(0, Math.Min(guaranteedMin - naturalTotal, poolSize));
                        var inclusion = GetInclusion(topUpSize);
                        approximate = approximate || inclusion.Approximate;
                        topUpGivenNotFired += probabilityOfC * inclusion.Probabilities[i];
                    }

                    // 判断记录（避免与主循环已经加入的"自然命中"路径重复计入）：主循环（chance_each
                    // 分支）已经用 naturalProbability 给这个叶子加过一条路径，累加器当前的
                    // ProbabilityNone 已经乘过一次 (1-naturalProbability)。这里不能再传"自然命中 或
                    // 补抽命中"的合并概率 atLeastOnce（会让 (1-naturalProbability) 被重复计入一次，
                    // 见提交说明"double-counting"排查记录）——标准恒等式
                    // 1-(1-p1)(1-q) = p1+(1-p1)q 对任意 q 都成立，只要传入 q = topUpGivenNotFired
                    // （"给定未自然命中"的条件补抽命中概率），AddPath 的乘积组合本身就会算出正确的
                    // p1+(1-p1)q，不需要（也不能）再把 p1 加回去。期望数量则单独按边际（无条件）概率
                    // 相加——期望值运算本就可加，不需要这层条件修正。
                    if (topUpGivenNotFired > 0)
                    {
                        var expectedQty = marginalTopUpProbability[i] * LootRollCore.ExpectedCount(entry);
                        AddPath(accumulator, entry.Ref, topUpGivenNotFired, expectedQty, approximate,
                            $"groups[{gi}].entries[{ei}](guaranteed_min top-up, exact)");
                    }
                }
                else if (marginalTopUpProbability[i] > 0)
                {
                    // weighted_pick_one 直接条目：边际独立合并即精确值（见方法注释第二条）；
                    // loot.* 嵌套条目：边际独立合并，标注近似（见方法注释第三条）——
                    // ResolveEntryContribution 内部会按 entry.Ref.Domain 走对应分支，isApproximate
                    // 对直接条目最终不影响其精确性（该分支下 marginalTopUpProbability 本身对
                    // weighted_pick_one 条目是精确值，仅当 marginalApproximate 为 true——即候选池超过
                    // 精确阈值——才真正近似；对 loot.* 条目恒标注近似，见方法注释）。
                    var isNestedOrApproximate = entry.Ref.Domain == "loot" || marginalApproximate;
                    ResolveEntryContribution(entry, marginalTopUpProbability[i], isNestedOrApproximate,
                        $"groups[{gi}].entries[{ei}](guaranteed_min top-up)", context, exprHost, diagnostics, depth, accumulator);
                }
            }
        }

        /// <summary>把一条候选（已知它以 <paramref name="fireProbability"/> 的概率在本次 Roll 里被
        /// 命中/抽中）解析为对叶子累加器的贡献：<see cref="LootEntry.Ref"/> 是 <c>item.*</c> 时直接
        /// 按 <see cref="LootRollCore.ExpectedCount"/> 结算；是 <c>loot.*</c> 时递归分析嵌套表单次
        /// Roll 的分布，再按"命中后把嵌套表独立再抽 <c>count</c> 次（count 在 [min,max] 闭区间均匀
        /// 分布）"的语义（同 <see cref="LootHost.ResolveEntryAtDepth"/> 判断记录 2）组合——对应
        /// <see cref="LootHost"/> 类型注释判断记录 2。</summary>
        private static void ResolveEntryContribution(
            LootEntry entry, double fireProbability, bool isApproximate, string pathLabel,
            LootAnalysisContext context, IExprHost? exprHost, IExprDiagnostics diagnostics,
            int depth, Dictionary<Id, LeafAccumulator> accumulator)
        {
            if (fireProbability <= 0)
            {
                return;
            }

            var expectedCount = LootRollCore.ExpectedCount(entry);

            if (entry.Ref.Domain == "loot")
            {
                // 同 LootHost.ResolveEntryAtDepth 判断记录：递归深度超限或引用了未提供的嵌套表都是
                // 运行期静默跳过，不贡献概率/期望数量，不抛异常。
                if (depth + 1 > context.MaxNestedDepth)
                {
                    return;
                }

                if (!context.Tables.TryGetValue(entry.Ref, out var nestedDef))
                {
                    return;
                }

                var nestedAccumulator = AnalyzeTableSingleRoll(nestedDef, context, exprHost, diagnostics, depth + 1);
                if (nestedAccumulator.Count == 0)
                {
                    return;
                }

                var pathPrefix = pathLabel + " -> " + entry.Ref.Value + ".";

                foreach (var kv in nestedAccumulator)
                {
                    var leaf = kv.Key;
                    var nestedAcc = kv.Value;
                    var nestedProbability = LootRollCore.Clamp01(1 - nestedAcc.ProbabilityNone);

                    // count 次独立嵌套 Roll 全部落空的概率，count 在 [CountMin,CountMax] 闭区间均匀
                    // 分布（同 core/foundation/rng 的 NextInt 语义）——见类型注释"嵌套 loot.* 引用"一节。
                    var probabilityNoneAcrossRolls = AverageOverUniformCount(
                        entry.CountMin, entry.CountMax, n => Math.Pow(1 - nestedProbability, n));

                    var pathProbability = fireProbability * (1 - probabilityNoneAcrossRolls);
                    var pathExpectedQty = fireProbability * expectedCount * nestedAcc.ExpectedCount;
                    var approximate = isApproximate || nestedAcc.IsApproximate;

                    AddPath(accumulator, leaf, pathProbability, pathExpectedQty, approximate,
                        pathPrefix + JoinPaths(nestedAcc.Paths));
                }
            }
            else
            {
                AddPath(accumulator, entry.Ref, fireProbability, fireProbability * expectedCount, isApproximate, pathLabel);
            }
        }

        private static string JoinPaths(IReadOnlyList<string> paths)
        {
            if (paths.Count == 1)
            {
                return paths[0];
            }

            var joined = string.Empty;
            for (var i = 0; i < paths.Count; i++)
            {
                joined += i == 0 ? paths[i] : " | " + paths[i];
            }

            return joined;
        }

        private static void AddPath(
            Dictionary<Id, LeafAccumulator> accumulator, Id leaf, double pathProbability, double pathExpectedQty,
            bool approximate, string pathLabel)
        {
            if (pathProbability <= 0 && pathExpectedQty <= 0)
            {
                return;
            }

            if (!accumulator.TryGetValue(leaf, out var acc))
            {
                acc = new LeafAccumulator();
                accumulator[leaf] = acc;
            }

            acc.ProbabilityNone *= 1 - LootRollCore.Clamp01(pathProbability);
            acc.ExpectedCount += pathExpectedQty;
            acc.IsApproximate = acc.IsApproximate || approximate;
            acc.Paths.Add(pathLabel);
        }

        // -----------------------------------------------------------------
        // 条件求值（三种模式，见 LootAnalysisContext.ConditionMode 类型注释）
        // -----------------------------------------------------------------

        private static bool ConditionPasses(LootEntry entry, LootAnalysisContext context, IExprHost? exprHost, IExprDiagnostics diagnostics)
        {
            if (entry.Condition == null)
            {
                return true;
            }

            switch (context.ConditionMode)
            {
                case LootConditionEvaluationMode.AssumeTrue:
                    return true;
                case LootConditionEvaluationMode.AssumeFalse:
                    return false;
                case LootConditionEvaluationMode.Evaluate:
                default:
                    // exprHost 为 null 时 ExpectedProbabilities 已在入口抛出，这里不会走到。
                    return LootRollCore.ConditionPasses(entry, exprHost!, diagnostics);
            }
        }

        private static List<(LootEntry Entry, int Index)> FilterEligibleIndexed(
            IReadOnlyList<LootEntry> entries, LootAnalysisContext context, IExprHost? exprHost, IExprDiagnostics diagnostics)
        {
            var pool = new List<(LootEntry, int)>();
            for (var i = 0; i < entries.Count; i++)
            {
                if (ConditionPasses(entries[i], context, exprHost, diagnostics))
                {
                    pool.Add((entries[i], i));
                }
            }

            return pool;
        }

        private static int ResolveMissStreak(LootAnalysisContext context, Id tableId, int groupIndex, int entryIndex)
        {
            if (context.PseudoRandomMissStreaks == null || context.ContextId == null)
            {
                return 0;
            }

            var key = new LootPseudoRandomKey(context.ContextId.Value, tableId, groupIndex, entryIndex);
            return context.PseudoRandomMissStreaks.TryGetValue(key, out var streak) ? streak : 0;
        }

        // -----------------------------------------------------------------
        // 不放回多抽：候选池条目数在阈值内精确（位掩码动态规划），否则近似
        // -----------------------------------------------------------------

        private readonly struct InclusionResult
        {
            public InclusionResult(IReadOnlyList<double> probabilities, bool approximate)
            {
                Probabilities = probabilities;
                Approximate = approximate;
            }

            public IReadOnlyList<double> Probabilities { get; }

            public bool Approximate { get; }
        }

        /// <summary>候选池 <paramref name="pool"/> 里每一条被"不放回抽取 <paramref name="k"/> 次"命中
        /// 的概率——语义同 <see cref="LootHost.PickWeighted"/> 被连续调用 <paramref name="k"/> 次、每次
        /// 命中后把该条目从候选池移出（权重合计 &lt;=0 时提前整体停止）。</summary>
        private static InclusionResult InclusionProbabilities(IReadOnlyList<LootEntry> pool, int k, int exactMaxEntries)
        {
            var n = pool.Count;
            var result = new double[n];
            if (n == 0 || k <= 0)
            {
                return new InclusionResult(result, false);
            }

            if (k >= n)
            {
                for (var i = 0; i < n; i++)
                {
                    result[i] = 1.0;
                }

                return new InclusionResult(result, false);
            }

            var weights = new double[n];
            var total = 0.0;
            for (var i = 0; i < n; i++)
            {
                weights[i] = pool[i].WeightOrChance;
                total += weights[i];
            }

            if (total <= 0)
            {
                // 同 LootHost.PickWeighted：totalWeight<=0 直接返回 null，一条都不会被抽中。
                return new InclusionResult(result, false);
            }

            if (n <= exactMaxEntries)
            {
                return new InclusionResult(InclusionProbabilitiesExact(weights, k), false);
            }

            // 近似：退化为"视作放回抽样"估计，见类型注释"精确 / 近似边界"一节。
            for (var i = 0; i < n; i++)
            {
                var p = weights[i] / total;
                result[i] = 1 - Math.Pow(1 - p, k);
            }

            return new InclusionResult(result, true);
        }

        /// <summary>
        /// 位掩码前向动态规划：state 是"候选池剩余条目"的位掩码（bit 置 1 = 仍在候选池里、还没被抽
        /// 中），value 是"抽取过程走到这一步、候选池恰好长这样"的概率。每一步按当前 state 内剩余权重
        /// 归一抽一条、清除该 bit、转移到新 state；当某个 state 内剩余权重合计 &lt;=0 时（同
        /// <see cref="LootHost.PickWeighted"/> 返回 null），该分支原样结转到下一步（不再变化，直到
        /// <paramref name="k"/> 步跑完），与 <see cref="LootHost.RollWeightedGroup"/>/保底补抽循环里
        /// "抽不到就 break、后续步骤全部跳过"完全同构。跑完 <paramref name="k"/> 步后，条目 i 被抽中的
        /// 概率 = 全部终态里"bit i 已清除"的概率之和。
        /// </summary>
        private static double[] InclusionProbabilitiesExact(double[] weights, int k)
        {
            var n = weights.Length;
            var fullMask = (1 << n) - 1;
            var stateProbability = new Dictionary<int, double> { { fullMask, 1.0 } };

            for (var step = 0; step < k; step++)
            {
                var next = new Dictionary<int, double>();
                foreach (var state in stateProbability)
                {
                    var mask = state.Key;
                    var probability = state.Value;

                    var remainingTotal = 0.0;
                    for (var i = 0; i < n; i++)
                    {
                        if ((mask & (1 << i)) != 0)
                        {
                            remainingTotal += weights[i];
                        }
                    }

                    if (remainingTotal <= 0)
                    {
                        AccumulateState(next, mask, probability);
                        continue;
                    }

                    for (var i = 0; i < n; i++)
                    {
                        if ((mask & (1 << i)) == 0)
                        {
                            continue;
                        }

                        var pickProbability = weights[i] / remainingTotal;
                        var newMask = mask & ~(1 << i);
                        AccumulateState(next, newMask, probability * pickProbability);
                    }
                }

                stateProbability = next;
            }

            var included = new double[n];
            foreach (var state in stateProbability)
            {
                var mask = state.Key;
                var probability = state.Value;
                for (var i = 0; i < n; i++)
                {
                    if ((mask & (1 << i)) == 0)
                    {
                        included[i] += probability;
                    }
                }
            }

            return included;
        }

        private static void AccumulateState(Dictionary<int, double> states, int mask, double probability)
        {
            states.TryGetValue(mask, out var existing);
            states[mask] = existing + probability;
        }

        // -----------------------------------------------------------------
        // 保底自然产出数分布：独立但概率不同的伯努利之和（泊松二项分布）
        // -----------------------------------------------------------------

        /// <summary>返回一个长度 <c>probabilities.Count + 1</c> 的数组，下标 c 处是"这些独立伯努利
        /// 恰好有 c 个命中"的概率（标准 O(n²) 动态规划，n = <paramref name="probabilities"/>.Count）。
        /// </summary>
        private static double[] PoissonBinomialDistribution(IReadOnlyList<double> probabilities)
        {
            var distribution = new double[probabilities.Count + 1];
            distribution[0] = 1.0;

            for (var i = 0; i < probabilities.Count; i++)
            {
                var p = probabilities[i];
                var next = new double[probabilities.Count + 1];
                for (var c = 0; c <= i; c++)
                {
                    var prev = distribution[c];
                    if (prev <= 0)
                    {
                        continue;
                    }

                    next[c] += prev * (1 - p);
                    next[c + 1] += prev * p;
                }

                distribution = next;
            }

            return distribution;
        }

        /// <summary>同 <see cref="PoissonBinomialDistribution"/>，但排除下标 <paramref
        /// name="excludeIndex"/> 那一个伯努利——供 <see cref="ResolveGuaranteedMin"/> 精确计算"某
        /// <c>chance_each</c> 条目未自然命中"条件下、其余独立伯努利之和的分布（该条目自身是否命中
        /// 会直接影响"自然产出数 c"，把它排除在外才能正确表达"给定它未命中"这个条件，见
        /// <see cref="ResolveGuaranteedMin"/> 方法注释）。返回数组长度为
        /// <c>probabilities.Count</c>（排除一个后剩余伯努利个数 + 1）。</summary>
        private static double[] PoissonBinomialDistributionExcluding(IReadOnlyList<double> probabilities, int excludeIndex)
        {
            var remaining = probabilities.Count - 1;
            var distribution = new double[remaining + 1];
            distribution[0] = 1.0;
            var used = 0;

            for (var i = 0; i < probabilities.Count; i++)
            {
                if (i == excludeIndex)
                {
                    continue;
                }

                var p = probabilities[i];
                var next = new double[remaining + 1];
                for (var c = 0; c <= used; c++)
                {
                    var prev = distribution[c];
                    if (prev <= 0)
                    {
                        continue;
                    }

                    next[c] += prev * (1 - p);
                    next[c + 1] += prev * p;
                }

                distribution = next;
                used++;
            }

            return distribution;
        }

        private static double AverageOverUniformCount(int min, int max, Func<int, double> f)
        {
            if (min == max)
            {
                return f(min);
            }

            var sum = 0.0;
            for (var n = min; n <= max; n++)
            {
                sum += f(n);
            }

            return sum / (max - min + 1);
        }

        private sealed class LeafAccumulator
        {
            public double ProbabilityNone = 1.0;
            public double ExpectedCount;
            public bool IsApproximate;
            public readonly List<string> Paths = new List<string>();
        }
    }
}
