using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Gameplay.Economy;

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
        // 反馈 48：品质/词缀/货币三段期望分布——只新增，不改动 ExpectedProbabilities 的签名与结果。
        // -----------------------------------------------------------------

        /// <summary>
        /// 消费方内容编辑器第 48 条：<paramref name="def"/> 一次 <see cref="LootHost.Roll"/> 调用里，
        /// 每个叶子 <c>item.*</c> 物品的期望品质分布——条件概率 <c>P(quality=q | 该叶子至少产出一次)</c>，
        /// 与 <see cref="LootHost.RollQuality"/>（<paramref name="itemRegistry"/> 提供 <c>item.template</c>/
        /// <c>quality</c> 字段的默认品质回退）的真实抽样口径一致：条目配置了 <see
        /// cref="LootEntry.QualityWeights"/> 时按正权重归一（≤0 的权重视同未配置该品质候选，同 <see
        /// cref="LootHost.RollQuality"/> 判断记录）；未配置/全部权重≤0 时退回模板自身 <c>quality</c>
        /// （p=1，不掷骰）。<c>econ.*</c> 叶子没有品质概念（<see cref="LootHost.ResolveCurrencyOutcome"/>
        /// 不掷品质骰），不出现在结果里。
        /// <para>
        /// 判断记录（品质分布加权混合）：同一叶子可能经多条不同 <see cref="LootEntry"/>（不同分组直接
        /// 条目、不同嵌套表分支、<c>guaranteed_min</c> 补抽命中同一条目）各自贡献期望产出数量——本方法
        /// 按"每条贡献路径的期望产出数量"为权重，把各自的品质概率分布线性加权混合后再归一化（与 <see
        /// cref="ExpectedProbabilities"/> 对同一叶子合并 <see cref="LootExpectedOutcome.ExpectedCount"/>
        /// 用的"期望值永远可加"同一原则；不是"取任意一条路径的分布"，也不是"按条目数简单平均"）。
        /// </para>
        /// <para>
        /// 判断记录（只读分析入口容错）：<paramref name="itemRegistry"/> 一律先包一层 <see
        /// cref="TolerantRegistryView"/>（同消费方反馈第 45 条既有惯例）——registry 处于阻断态、或某个
        /// 叶子模板确实未登记 <c>item.template</c> 记录时，该叶子对应贡献路径不计入品质分布，并在返回
        /// 结果里显式标记 <see cref="LootQualityOutcome.IsDegraded"/>，不抛异常、不悄悄吞掉问题。
        /// </para>
        /// </summary>
        public static IReadOnlyList<LootQualityOutcome> ExpectedQualityDistribution(
            LootTableDef def, LootAnalysisContext context, IDataRegistryView itemRegistry)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (itemRegistry == null) throw new ArgumentNullException(nameof(itemRegistry));
            if (context.ConditionMode == LootConditionEvaluationMode.Evaluate && context.ExprHost == null)
            {
                throw new InvalidOperationException(
                    "LootAnalysisContext.ConditionMode=Evaluate 时必须提供 ExprHost");
            }

            var diagnostics = context.Diagnostics ?? new ExprDiagnosticsRecorder();
            var tolerant = TolerantRegistryView.Wrap(itemRegistry);
            var qualityAccum = new QualityAccum(tolerant);
            var accumulator = AnalyzeTableSingleRoll(def, context, context.ExprHost, diagnostics, depth: 0, qualityAccum);

            var result = new List<LootQualityOutcome>();
            foreach (var kv in accumulator)
            {
                var leaf = kv.Key;
                if (leaf.Domain != "item")
                {
                    // econ.* 叶子没有品质概念，见 LootHost.ResolveCurrencyOutcome 判断记录——不掷品质骰。
                    continue;
                }

                var leafAcc = kv.Value;
                var isDegraded = qualityAccum.DegradedLeaves.Contains(leaf);
                var probabilities = new Dictionary<Id, double>();
                if (qualityAccum.ByLeaf.TryGetValue(leaf, out var byQuality))
                {
                    var total = 0.0;
                    foreach (var v in byQuality.Values)
                    {
                        total += v;
                    }

                    if (total > 0)
                    {
                        foreach (var qkv in byQuality)
                        {
                            probabilities[qkv.Key] = qkv.Value / total;
                        }
                    }
                }

                result.Add(new LootQualityOutcome(
                    leaf, probabilities, leafAcc.IsApproximate, isDegraded,
                    isDegraded
                        ? "该叶子至少一条贡献路径的 item.template 记录读取失败（registry 阻断或该模板未登记），" +
                          "对应路径未计入品质分布，QualityProbabilities 可能不完整"
                        : null));
            }

            return result;
        }

        /// <summary>
        /// 消费方内容编辑器第 48 条：给定模板与一次品质骰结果 <paramref name="qualityId"/>（通常来自
        /// <see cref="ExpectedQualityDistribution"/> 的某个分桶，或调用方已知的具体品质），算出词缀骰
        /// （<see cref="LootHost.RollAffixes"/>）候选池里每条 <c>item.affix</c> 的入选概率——先按
        /// <c>quality_pool == qualityId</c> 与模板 <c>affixes</c> 白名单（非空时取交集）、<c>weight &gt; 0</c>
        /// 过滤出候选池并按 <c>Id</c> 序数排序（与 <see cref="LootHost.RollAffixes"/> 逐字节同一套过滤/
        /// 排序逻辑，保证候选顺序一致——不放回加权抽取的入选概率依赖候选池的确定但抽取顺序本身不影响
        /// 最终概率，排序只是为了与运行期候选构建口径对齐），再对"不放回抽取 <c>min(affix_count,
        /// 候选池大小)</c> 条"这一步复用与 <c>weighted_pick_one</c> 多抽完全同构的子集位掩码动态规划
        /// （<see cref="InclusionProbabilitiesExact"/>，与 <see cref="ExpectedProbabilities"/> 内部
        /// <c>weighted_pick_one</c> 不放回多抽共用同一份算法，不重新发明）精确求解。
        /// <para>
        /// 判断记录（候选池 &gt; <paramref name="exactMaxEntries"/> 时返回 <c>null</c>，不退化为近似）：
        /// 与 <see cref="ExpectedProbabilities"/> 对候选池超过 <see
        /// cref="LootAnalysisContext.ExactWithoutReplacementMaxEntries"/> 时退化为"视作放回抽样"近似式
        /// 不同——反馈第 48 条候选文本明确词缀骰"相对复杂，建议作为后续独立评估项"，本方法只提供精确解，
        /// 超阈值时如实标记 <see cref="LootAffixInclusionResult.IsDegraded"/> 并在 <see
        /// cref="LootAffixInclusionResult.Reason"/> 里建议改用 <see cref="LootHost.RollDetailed"/>
        /// 模拟观测，不提供一个"看起来精确、实际有偏"的近似值。默认阈值 16（与反馈原文"池大小 ≤ 16"
        /// 一致，比 <see cref="LootAnalysisContext.ExactWithoutReplacementMaxEntries"/> 默认 12 更宽，
        /// 因为词缀池通常比掉落表分组条目数更大、且不放回多抽的“抽取次数”<c>affix_count</c> 一般较小，
        /// 状态数 2^16=65536 单次调用仍可接受）。
        /// </para>
        /// </summary>
        public static LootAffixInclusionResult ExpectedAffixInclusion(
            Id templateId, Id qualityId, IDataRegistryView registry, int exactMaxEntries = 16)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var tolerant = TolerantRegistryView.Wrap(registry);
            var templateRecord = tolerant.Get(ItemTemplateTableName, templateId);
            var qualityDef = tolerant.Get(ItemQualityDefinitionTableName, qualityId);
            var affixCount = qualityDef != null && qualityDef.TryGetInt("affix_count", out var ac) ? (int)ac : 0;

            string? registryDegradedReason = tolerant.IsDegraded
                ? "registry 处于阻断态，以下表读取失败：" + string.Join(", ", tolerant.MissingTables)
                : null;

            if (affixCount <= 0)
            {
                return new LootAffixInclusionResult(
                    templateId, qualityId, candidatePoolSize: 0, actualAffixCount: 0,
                    inclusionProbabilities: EmptyAffixProbabilities, tolerant.IsDegraded, registryDegradedReason);
            }

            IReadOnlyList<Id>? whitelist = null;
            if (templateRecord != null && templateRecord.TryGetIdList("affixes", out var wl) && wl.Count > 0)
            {
                whitelist = wl;
            }

            var candidates = new List<(Id Id, double Weight)>();
            foreach (var record in tolerant.GetAll(ItemAffixTableName))
            {
                if (record.Id == null)
                {
                    continue;
                }

                if (!record.TryGetId("quality_pool", out var pool) || !pool.Equals(qualityId))
                {
                    continue;
                }

                if (whitelist != null && !ContainsId(whitelist, record.Id.Value))
                {
                    continue;
                }

                var weight = record.TryGetNumber("weight", out var w) ? w : 0.0;
                if (weight <= 0.0)
                {
                    continue;
                }

                candidates.Add((record.Id.Value, weight));
            }

            candidates.Sort((a, b) => string.CompareOrdinal(a.Id.Value, b.Id.Value));

            var poolSize = candidates.Count;
            var actualAffixCount = Math.Min(affixCount, poolSize);

            if (poolSize == 0 || actualAffixCount <= 0)
            {
                return new LootAffixInclusionResult(
                    templateId, qualityId, poolSize, actualAffixCount,
                    EmptyAffixProbabilities, tolerant.IsDegraded, registryDegradedReason);
            }

            if (poolSize > exactMaxEntries)
            {
                var reason = $"候选词缀池 {poolSize} 条超过精确阈值 {exactMaxEntries}" +
                    "（子集动态规划状态数按候选池条目数指数增长），请改用 LootHost.RollDetailed 做蒙特卡洛模拟" +
                    "观测该品质下的词缀分布" + (registryDegradedReason != null ? "；另外，" + registryDegradedReason : "。");
                return new LootAffixInclusionResult(
                    templateId, qualityId, poolSize, actualAffixCount, null, isDegraded: true, reason);
            }

            var weights = new double[poolSize];
            for (var i = 0; i < poolSize; i++)
            {
                weights[i] = candidates[i].Weight;
            }

            var inclusion = InclusionProbabilitiesExact(weights, actualAffixCount);
            var probabilities = new Dictionary<Id, double>(poolSize);
            for (var i = 0; i < poolSize; i++)
            {
                probabilities[candidates[i].Id] = inclusion[i];
            }

            return new LootAffixInclusionResult(
                templateId, qualityId, poolSize, actualAffixCount, probabilities, tolerant.IsDegraded, registryDegradedReason);
        }

        /// <summary>
        /// 消费方内容编辑器第 48 条：<paramref name="def"/> 一次 <see cref="LootHost.Roll"/> 调用里，
        /// 每种货币（<c>econ.*</c> 叶子引用）的期望产出数量——与 <see
        /// cref="LootHost.ResolveCurrencyOutcome"/> 逐项对齐：概率 × 当量（<see cref="LootRollCore.
        /// ExpectedCount"/> 同款期望值口径，经嵌套/保底路径线性叠加）× <paramref name="economy"/>.<see
        /// cref="IEconomyHost.TryGetGoldBaseAmount"/>（<paramref name="sourceLevel"/> 为空时回退等级 1，
        /// 与 <see cref="LootHost.ResolveCurrencyOutcome"/> 判断记录同一惯例）× <paramref
        /// name="goldMultiplierProvider"/> 对 <paramref name="tierId"/> 解析出的分档金币倍率（未注入
        /// 委托或委托对该 <c>tierId</c> 无法解析时恒 1，同 <see cref="LootGoldMultiplierProvider"/> 判断
        /// 记录）× <paramref name="context"/>.<see cref="LootAnalysisContext.Multiplier"/>（对应 <see
        /// cref="RollContext.Multiplier"/> 难度倍率，两者是同一个数值——本方法只接受
        /// <see cref="LootAnalysisContext"/> 一份倍率输入，不重复要求调用方在 <see cref="RollContext"/>
        /// 与本方法之间填两遍，避免两个来源不一致）。已知与真实抽取的偏差见 <see
        /// cref="LootExpectedCurrencyOutcome"/> 类型注释"不建模最终四舍五入"判断记录。
        /// </summary>
        public static IReadOnlyList<LootExpectedCurrencyOutcome> ExpectedCurrency(
            LootTableDef def, LootAnalysisContext context, IEconomyHost economy,
            int? sourceLevel = null, Id? tierId = null, LootGoldMultiplierProvider? goldMultiplierProvider = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            if (context.ConditionMode == LootConditionEvaluationMode.Evaluate && context.ExprHost == null)
            {
                throw new InvalidOperationException(
                    "LootAnalysisContext.ConditionMode=Evaluate 时必须提供 ExprHost");
            }

            var diagnostics = context.Diagnostics ?? new ExprDiagnosticsRecorder();
            var accumulator = AnalyzeTableSingleRoll(def, context, context.ExprHost, diagnostics, depth: 0);

            var level = sourceLevel ?? 1;
            var goldBase = economy.TryGetGoldBaseAmount(level);
            var tierMultiplier = goldMultiplierProvider?.Invoke(tierId) ?? 1.0;

            var result = new List<LootExpectedCurrencyOutcome>();
            foreach (var kv in accumulator)
            {
                var leaf = kv.Key;
                if (leaf.Domain != "econ")
                {
                    continue;
                }

                var acc = kv.Value;
                var dropProbability = LootRollCore.Clamp01(1 - acc.ProbabilityNone);

                if (!goldBase.HasValue)
                {
                    result.Add(new LootExpectedCurrencyOutcome(
                        leaf, dropProbability, 0.0, acc.Paths, acc.IsApproximate, isDegraded: true,
                        reason: $"IEconomyHost.TryGetGoldBaseAmount({level}) 返回 null" +
                            "（金币基数曲线未登记或读取失败），无法换算该货币的期望数量"));
                    continue;
                }

                var expectedAmount = acc.ExpectedCount * goldBase.Value * tierMultiplier * context.Multiplier;
                result.Add(new LootExpectedCurrencyOutcome(
                    leaf, dropProbability, expectedAmount, acc.Paths, acc.IsApproximate, isDegraded: false, reason: null));
            }

            return result;
        }

        private const string ItemTemplateTableName = "item.template";

        private const string ItemQualityDefinitionTableName = "item.quality_definition";

        private const string ItemAffixTableName = "item.affix";

        private static readonly IReadOnlyDictionary<Id, double> EmptyAffixProbabilities = new Dictionary<Id, double>();

        private static bool ContainsId(IReadOnlyList<Id> list, Id value)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>反馈 48：<see cref="ExpectedQualityDistribution"/> 递归遍历过程中的品质分布累加器——
        /// 每次 <see cref="AnalyzeTableSingleRoll"/> 调用（含递归）都会创建一个新实例，与该次调用自己的
        /// <c>Dictionary&lt;Id, LeafAccumulator&gt;</c> 是"同一遍遍历的两份平行累加结果"，不是互相派生
        /// 关系——两者共用同一套条件筛选/权重归一/保底逻辑，只是分别累加"是否命中"与"命中后品质分布"。
        /// </summary>
        private sealed class QualityAccum
        {
            public QualityAccum(IDataRegistryView registry)
            {
                Registry = registry;
            }

            public IDataRegistryView Registry { get; }

            /// <summary>叶子 → 品质 → 期望产出数量（未归一化，见 <see cref="ExpectedQualityDistribution"/>
            /// 归一化步骤）。</summary>
            public Dictionary<Id, Dictionary<Id, double>> ByLeaf { get; } = new Dictionary<Id, Dictionary<Id, double>>();

            /// <summary>因 <c>item.template</c> 读取失败而至少丢失一条贡献路径的叶子集合，见 <see
            /// cref="ExpectedQualityDistribution"/> 判断记录"只读分析入口容错"。</summary>
            public HashSet<Id> DegradedLeaves { get; } = new HashSet<Id>();
        }

        /// <summary>取模板 <paramref name="templateId"/> 自身登记的品质（<c>item.template.quality</c>），
        /// 供未配置 <see cref="LootEntry.QualityWeights"/> 的条目回退——语义同 <see
        /// cref="LootHost.ResolveItemOutcome"/> 第一步 <c>templateRecord.GetId("quality")</c>；读不到该
        /// 模板记录（registry 阻断，或该模板确实未登记）时返回 <c>null</c>（不抛异常，同 <see
        /// cref="TolerantRegistryView"/> 惯例）。</summary>
        private static Id? ResolveTemplateQuality(Id templateId, IDataRegistryView registry)
        {
            var record = registry.Get(ItemTemplateTableName, templateId);
            if (record == null)
            {
                return null;
            }

            return record.TryGetId("quality", out var quality) ? quality : (Id?)null;
        }

        /// <summary>一条候选（已知命中/被抽中，见调用方 <paramref name="entry"/> 的语境）在其命中的那一
        /// 刻会解析成的品质分布——与 <see cref="LootHost.RollQuality"/> 逐字节同一套判定：<see
        /// cref="LootEntry.QualityWeights"/> 存在且过滤出至少一条正权重时按正权重归一；否则（未配置/
        /// 全部权重 &lt;=0）回退 <paramref name="templateQuality"/>（p=1，不掷骰）；<paramref
        /// name="templateQuality"/> 本身为 <c>null</c>（<see cref="ResolveTemplateQuality"/> 读取失败）
        /// 且没有可用的 <see cref="LootEntry.QualityWeights"/> 时返回空列表——调用方据此标记该贡献路径
        /// 降级（见 <see cref="ExpectedQualityDistribution"/> 判断记录）。</summary>
        private static List<(Id Quality, double Probability)> QualityWeightDistribution(LootEntry entry, Id? templateQuality)
        {
            if (entry.QualityWeights != null && entry.QualityWeights.Count > 0)
            {
                var ids = new List<Id>(entry.QualityWeights.Count);
                var weights = new List<double>(entry.QualityWeights.Count);
                var total = 0.0;
                foreach (var kv in entry.QualityWeights)
                {
                    if (kv.Value <= 0)
                    {
                        continue;
                    }

                    ids.Add(kv.Key);
                    weights.Add(kv.Value);
                    total += kv.Value;
                }

                if (ids.Count > 0)
                {
                    var list = new List<(Id, double)>(ids.Count);
                    for (var i = 0; i < ids.Count; i++)
                    {
                        list.Add((ids[i], weights[i] / total));
                    }

                    return list;
                }
            }

            if (templateQuality.HasValue)
            {
                return new List<(Id, double)> { (templateQuality.Value, 1.0) };
            }

            return new List<(Id, double)>();
        }

        private static Dictionary<Id, double> GetOrCreateQualityBucket(Dictionary<Id, Dictionary<Id, double>> byLeaf, Id leaf)
        {
            if (!byLeaf.TryGetValue(leaf, out var bucket))
            {
                bucket = new Dictionary<Id, double>();
                byLeaf[leaf] = bucket;
            }

            return bucket;
        }

        /// <summary>把 <paramref name="entry"/>（已知贡献了 <paramref name="pathExpectedQty"/> 的期望
        /// 产出数量）按 <see cref="QualityWeightDistribution"/> 解析出的品质分布累加进 <paramref
        /// name="qualityAccum"/>；<paramref name="entry"/> 引用 <c>econ.*</c> 时不做任何事（货币没有
        /// 品质概念）。</summary>
        private static void AccumulateEntryQuality(
            LootEntry entry, double pathExpectedQty, QualityAccum? qualityAccum)
        {
            if (qualityAccum == null || entry.Ref.Domain != "item")
            {
                return;
            }

            var templateQuality = ResolveTemplateQuality(entry.Ref, qualityAccum.Registry);
            var distribution = QualityWeightDistribution(entry, templateQuality);
            if (distribution.Count == 0)
            {
                qualityAccum.DegradedLeaves.Add(entry.Ref);
                return;
            }

            var bucket = GetOrCreateQualityBucket(qualityAccum.ByLeaf, entry.Ref);
            foreach (var (quality, probability) in distribution)
            {
                bucket[quality] = bucket.TryGetValue(quality, out var existing)
                    ? existing + pathExpectedQty * probability
                    : pathExpectedQty * probability;
            }
        }

        // -----------------------------------------------------------------
        // 单次 Roll 的解析式期望分布（可递归用于嵌套表）
        // -----------------------------------------------------------------

        private static Dictionary<Id, LeafAccumulator> AnalyzeTableSingleRoll(
            LootTableDef def, LootAnalysisContext context, IExprHost? exprHost, IExprDiagnostics diagnostics, int depth,
            QualityAccum? qualityAccum = null)
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
                            $"groups[{gi}].entries[{ei}]", context, exprHost, diagnostics, depth, accumulator, qualityAccum);
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
                            $"groups[{gi}].entries[{poolIndexed[pi].Index}]", context, exprHost, diagnostics, depth, accumulator, qualityAccum);
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
                    chanceEachIndexByKey, deterministicWeightedTotal, candidatePool, accumulator, qualityAccum);
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
            Dictionary<Id, LeafAccumulator> accumulator, QualityAccum? qualityAccum = null)
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

                        // 反馈 48：保底补抽命中的仍是同一条 LootEntry，品质分布取决于该条目自身的
                        // QualityWeights，与"是自然命中还是补抽命中"无关（见 AccumulateEntryQuality 判断
                        // 记录）——按边际期望数量（不区分条件）累加，与本分支下 AddPath 的期望数量口径
                        // 一致（期望值可加，不需要走 topUpGivenNotFired 那套条件合并）。
                        AccumulateEntryQuality(entry, marginalTopUpProbability[i] * LootRollCore.ExpectedCount(entry), qualityAccum);
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
                        $"groups[{gi}].entries[{ei}](guaranteed_min top-up)", context, exprHost, diagnostics, depth, accumulator, qualityAccum);
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
            int depth, Dictionary<Id, LeafAccumulator> accumulator, QualityAccum? qualityAccum = null)
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

                // 反馈 48：嵌套表自己的叶子各自按自己的 LootEntry.QualityWeights 掷品质骰（外层这条
                // loot.* 条目本身没有品质概念），需要一个"只属于本次递归调用"的品质累加器——不能直接
                // 复用外层 qualityAccum，否则嵌套表内部的期望数量（尚未乘上"count 次独立重抽"这个外层
                // 系数）会被误当成最终贡献直接并入外层，见下方合并步骤。
                var nestedQualityAccum = qualityAccum != null ? new QualityAccum(qualityAccum.Registry) : null;
                var nestedAccumulator = AnalyzeTableSingleRoll(nestedDef, context, exprHost, diagnostics, depth + 1, nestedQualityAccum);
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

                if (qualityAccum != null && nestedQualityAccum != null)
                {
                    // 合并：嵌套表单次 Roll 里"叶子 L 在品质 q 上的期望数量"乘上外层这条 loot.* 条目
                    // 自己的 fireProbability × expectedCount（"命中后独立重抽 count 次"的期望值线性
                    // 缩放，与上面 pathExpectedQty 的推导同一套系数，只是分母换成品质分桶）后累加进外层。
                    foreach (var leafKv in nestedQualityAccum.ByLeaf)
                    {
                        var bucket = GetOrCreateQualityBucket(qualityAccum.ByLeaf, leafKv.Key);
                        foreach (var qualityKv in leafKv.Value)
                        {
                            var scaled = fireProbability * expectedCount * qualityKv.Value;
                            bucket[qualityKv.Key] = bucket.TryGetValue(qualityKv.Key, out var existing)
                                ? existing + scaled
                                : scaled;
                        }
                    }

                    foreach (var degradedLeaf in nestedQualityAccum.DegradedLeaves)
                    {
                        qualityAccum.DegradedLeaves.Add(degradedLeaf);
                    }
                }
            }
            else
            {
                AddPath(accumulator, entry.Ref, fireProbability, fireProbability * expectedCount, isApproximate, pathLabel);
                AccumulateEntryQuality(entry, fireProbability * expectedCount, qualityAccum);
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
        /// 命中后把该条目从候选池移出（权重合计 &lt;=0 时提前整体停止）。
        /// <para>
        /// 判断记录（反馈 48 验收发现，反馈 35/1.24.0 遗留）——<c>k&gt;=n</c> 快速路径不能无条件把所有
        /// 条目报 1.0：权重 &lt;=0 的 <c>weighted_pick_one</c> 条目在 <see cref="LootHost.PickWeighted"/>
        /// 语义下永远选不中（<see cref="LootRollCore.SelectByThreshold"/> 按累计权重比较，权重 0 的条目
        /// 累计值不增长，绝不会满足 <c>threshold &lt; cumulative</c>），必须先算出权重、按"权重 &gt;0 才
        /// 给 1.0，否则给 0"来对齐，而不是先判 <c>k&gt;=n</c> 再算权重。
        /// </para>
        /// </summary>
        private static InclusionResult InclusionProbabilities(IReadOnlyList<LootEntry> pool, int k, int exactMaxEntries)
        {
            var n = pool.Count;
            var result = new double[n];
            if (n == 0 || k <= 0)
            {
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

            if (k >= n)
            {
                for (var i = 0; i < n; i++)
                {
                    // 权重 <=0 的条目在 PickWeighted 语义下永远选不中，即使 k>=n 也不能报 1.0（见本方法
                    // 判断记录）。
                    result[i] = weights[i] > 0 ? 1.0 : 0.0;
                }

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
