using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// 消费方内容编辑器第 48 条收口：<see cref="LootTableAnalyzer.ExpectedProbabilities"/> 只给出按叶子
    /// 物品聚合的掉落概率/期望数量，不含品质骰/词缀骰/货币换算三项结果的期望分布维度——本文件登记这三项
    /// 新增分析入口（<see cref="LootTableAnalyzer.ExpectedQualityDistribution"/>/
    /// <see cref="LootTableAnalyzer.ExpectedAffixInclusion"/>/<see cref="LootTableAnalyzer.ExpectedCurrency"/>）
    /// 各自的结果类型——纯数据、可序列化，只新增不改动既有 <see cref="LootExpectedOutcome"/>/
    /// <see cref="LootAnalysisContext"/>。三个结果类型各自的判断记录见其自身类型注释。
    /// </summary>
    /// <summary>
    /// <see cref="LootTableAnalyzer.ExpectedQualityDistribution"/> 单个叶子（<c>item.*</c>）的期望品质
    /// 分布——条件分布 <c>P(quality=q | 该叶子在这次 Roll 至少产出一次)</c>，按各贡献路径（不同分组的
    /// 直接条目、嵌套表展开、<c>guaranteed_min</c> 补抽）各自的期望产出数量加权混合（见
    /// <see cref="LootTableAnalyzer"/> 判断记录"品质分布加权混合"）。
    /// </summary>
    public sealed class LootQualityOutcome
    {
        /// <summary>叶子产出的模板引用，语义同 <see cref="LootExpectedOutcome.LeafRef"/>。</summary>
        public Id LeafRef { get; }

        /// <summary>品质 id → 条件概率，各值之和为 1（<see cref="IsDegraded"/> 为 true 且本字段恰好
        /// 全部贡献路径均降级时可能为空字典——不足以归一化，见 <see cref="IsDegraded"/> 判断记录）。</summary>
        public IReadOnlyDictionary<Id, double> QualityProbabilities { get; }

        /// <summary>true 表示至少一条贡献路径经过近似计算（不放回多抽超过阈值/嵌套引用保底命中），
        /// 语义同 <see cref="LootExpectedOutcome.IsApproximate"/>，与 <see cref="IsDegraded"/> 是两个
        /// 独立维度（近似 = 算法精度妥协；降级 = 数据读不到）。</summary>
        public bool IsApproximate { get; }

        /// <summary>true 表示至少一条贡献路径的 <c>item.template</c> 记录读取失败（registry 处于阻断态，
        /// 或该模板确实未登记）——本模块只读分析入口对阻断态不抛异常，改为显式标记降级（AGENTS.md 第 3
        /// 节"只读分析类入口在遇到阻断态时降级要显式标记"），对应路径未计入 <see
        /// cref="QualityProbabilities"/>，结果可能不完整。</summary>
        public bool IsDegraded { get; }

        /// <summary><see cref="IsDegraded"/> 为 true 时的可读说明；否则为 null。</summary>
        public string? Reason { get; }

        public LootQualityOutcome(
            Id leafRef, IReadOnlyDictionary<Id, double> qualityProbabilities, bool isApproximate,
            bool isDegraded, string? reason)
        {
            LeafRef = leafRef;
            QualityProbabilities = qualityProbabilities;
            IsApproximate = isApproximate;
            IsDegraded = isDegraded;
            Reason = reason;
        }
    }

    /// <summary>
    /// <see cref="LootTableAnalyzer.ExpectedAffixInclusion"/> 的结果——给定模板与品质骰结果后，
    /// <c>item.affix</c> 候选池按 <see cref="LootHost.RollAffixes"/> 同一套"按 <c>quality_pool</c>/
    /// 模板白名单过滤 → 按 id 序数排序 → 加权不放回抽 <c>affix_count</c> 条"语义算出的逐条入选概率。
    /// </summary>
    public sealed class LootAffixInclusionResult
    {
        public Id TemplateId { get; }

        public Id QualityId { get; }

        /// <summary>按 <c>quality_pool</c>/模板 <c>affixes</c> 白名单/<c>weight&gt;0</c> 过滤后的候选池
        /// 大小（同 <see cref="LootHost.RollAffixes"/> 候选构建口径）。</summary>
        public int CandidatePoolSize { get; }

        /// <summary>本次实际会抽取的词缀条数——<c>min(item.quality_definition.affix_count,
        /// CandidatePoolSize)</c>（候选池耗尽提前停止，见 <see cref="LootHost.RollAffixes"/> 判断记录）。
        /// 由于 <see cref="QualityId"/> 已经是掷骰结果（外部给定，不是随机变量），本字段在给定输入下是
        /// 确定性数值，不是分布——反馈第 48 条"词缀条数分布（若条数本身随品质/随机）"在本方法的参数化下
        /// 不适用（品质已固定，条数因此也已固定）。</summary>
        public int ActualAffixCount { get; }

        /// <summary>词缀 id → 入选概率（子集位掩码动态规划精确解，语义同 <see
        /// cref="LootTableAnalyzer"/> 内部 <c>InclusionProbabilitiesExact</c>）；<see
        /// cref="CandidatePoolSize"/> 超过精确阈值（<c>exactMaxEntries</c>，默认 16）时为 <c>null</c>，
        /// 见 <see cref="IsDegraded"/>/<see cref="Reason"/>。</summary>
        public IReadOnlyDictionary<Id, double>? InclusionProbabilities { get; }

        /// <summary>true 表示：候选池超过精确阈值（<see cref="InclusionProbabilities"/> 为 null，建议
        /// 改用蒙特卡洛模拟），或 registry 处于阻断态导致 <c>item.template</c>/<c>item.quality_definition</c>/
        /// <c>item.affix</c> 读取失败——两种情形均不抛异常，显式标记降级。</summary>
        public bool IsDegraded { get; }

        public string? Reason { get; }

        public LootAffixInclusionResult(
            Id templateId, Id qualityId, int candidatePoolSize, int actualAffixCount,
            IReadOnlyDictionary<Id, double>? inclusionProbabilities, bool isDegraded, string? reason)
        {
            TemplateId = templateId;
            QualityId = qualityId;
            CandidatePoolSize = candidatePoolSize;
            ActualAffixCount = actualAffixCount;
            InclusionProbabilities = inclusionProbabilities;
            IsDegraded = isDegraded;
            Reason = reason;
        }
    }

    /// <summary>
    /// <see cref="LootTableAnalyzer.ExpectedCurrency"/> 单种货币（<c>econ.*</c> 叶子引用）的期望产出——
    /// 与 <see cref="LootHost.ResolveCurrencyOutcome"/> 逐项对齐：概率 × 当量（<see
    /// cref="LootExpectedOutcome.ExpectedCount"/> 同款期望值口径）× 金币基数 × 分档倍率 × 难度倍率。
    /// <para>
    /// 判断记录（已知与真实抽取的偏差：不建模最终四舍五入 + "换算结果 &lt;=0 时静默跳过"这两步
    /// 非线性）：<see cref="LootHost.ResolveCurrencyOutcome"/> 最终把换算结果 <see
    /// cref="System.Math.Round(double, System.MidpointRounding)"/> 到整数、且 &lt;=0 时整条跳过不产出；
    /// 本类型给出的是不做这两步非线性处理的解析式期望值（线性期望，与 <see
    /// cref="LootTableAnalyzer"/> 类型注释"精确/近似边界"一节的既有惯例一致：期望值运算本身可加，
    /// 但"取整"“&gt;0 才产出"两步不是线性运算，无法解析式精确纳入）——当量/金币基数/倍率的乘积明显
    /// 大于 1（游戏内容通常如此）时该偏差可忽略，见 README"限制"一节；调用方需要精确到"是否会被四舍五入
    /// 到 0"这一位时，应改用 <see cref="LootHost.RollDetailed"/> 蒙特卡洛观测。
    /// </para>
    /// </summary>
    public sealed class LootExpectedCurrencyOutcome
    {
        public Id CurrencyRef { get; }

        /// <summary>该货币条目在一次 Roll 里至少产出一次的概率，语义同 <see
        /// cref="LootExpectedOutcome.DropProbability"/>。</summary>
        public double DropProbability { get; }

        /// <summary>期望产出数量（已换算为货币基础单位，非"当量"），公式见类型注释。</summary>
        public double ExpectedAmount { get; }

        /// <summary>贡献路径描述，语义同 <see cref="LootExpectedOutcome.SourcePaths"/>。</summary>
        public IReadOnlyList<string> SourcePaths { get; }

        public bool IsApproximate { get; }

        /// <summary>true 表示 <see cref="Core.Gameplay.Economy.IEconomyHost.TryGetGoldBaseAmount"/> 对
        /// 给定等级返回 null（金币基数曲线未登记或读取失败），<see cref="ExpectedAmount"/> 恒为 0，
        /// 不代表真实期望值——不抛异常，显式标记降级（同 <see cref="LootHost.ResolveCurrencyOutcome"/>
        /// 静默跳过的既有惯例，只是分析入口需要显式告知调用方"这不是算出来的 0"）。</summary>
        public bool IsDegraded { get; }

        public string? Reason { get; }

        public LootExpectedCurrencyOutcome(
            Id currencyRef, double dropProbability, double expectedAmount, IReadOnlyList<string> sourcePaths,
            bool isApproximate, bool isDegraded, string? reason)
        {
            CurrencyRef = currencyRef;
            DropProbability = dropProbability;
            ExpectedAmount = expectedAmount;
            SourcePaths = sourcePaths;
            IsApproximate = isApproximate;
            IsDegraded = isDegraded;
            Reason = reason;
        }
    }
}
