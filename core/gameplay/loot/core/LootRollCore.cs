using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Expr;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// 消费方第 35 条收口：<see cref="LootHost"/> 抽取核心里"权重归一 / 条件筛选 / 按阈值挑中一条"
    /// 这几步与随机数无关的纯计算步骤收拢到本类，<see cref="LootHost"/> 与
    /// <see cref="LootTableAnalyzer"/> 共用同一份实现——前者在真正抽样处把 <c>IRngHost.Next</c> 采出的
    /// [0,1) 值喂给 <see cref="SelectByThreshold"/>；后者不消耗随机数，直接用
    /// <see cref="TotalWeight"/>/<see cref="FilterEligible"/> 做解析式概率计算。二者永远看到同一套
    /// "什么算通过条件 / 权重怎么归一 / 给定阈值选中哪一条"的判定，避免出现"宿主认识的语义"与"分析器
    /// 认识的语义"分叉（同 <c>LootTableParser</c> 类型注释"运行期与内容校验共用同一份解析逻辑"的惯例）。
    /// <para>
    /// 判断记录——本类型只搬运既有 <see cref="LootHost"/> 私有方法体，不改变任何一步的浮点运算顺序：
    /// <see cref="LootHost.PickWeighted"/> 原实现是"算 totalWeight → 不够走 null → 消耗一次 RNG 得
    /// thresholdFraction → 乘以 totalWeight 得 threshold → 按列表顺序累加 cumulative、
    /// <c>threshold &lt; cumulative</c> 命中"，<see cref="SelectByThreshold"/> 原样保留这套算法，唯一区别
    /// 是"消耗 RNG"这一步留在 <see cref="LootHost"/>（本类不引用 <c>IRngHost</c>），确保抽取行为逐字节
    /// 不变——见 <c>LootHostRollTests</c> 固定种子回归、<c>tests/E35_*</c> 新增的"重构前后同种子同序列"
    /// 对照。
    /// </para>
    /// </summary>
    internal static class LootRollCore
    {
        public static double Clamp01(double value) => value < 0 ? 0 : (value > 1 ? 1 : value);

        /// <summary>单条候选是否通过其 <see cref="LootEntry.Condition"/>（null 视为恒真）。</summary>
        public static bool ConditionPasses(LootEntry entry, IExprHost exprHost, IExprDiagnostics diagnostics) =>
            entry.Condition == null || ExprEvaluator.EvaluateBool(entry.Condition, exprHost, diagnostics);

        /// <summary>按 <see cref="ConditionPasses"/> 过滤出一组候选里当前满足条件的子集，保持原始顺序
        /// （<see cref="LootHost.RollWeightedGroup"/>/guaranteed_min 候选池构建、
        /// <see cref="LootTableAnalyzer"/> 计算期望概率共用同一份筛选逻辑）。</summary>
        public static List<LootEntry> FilterEligible(IEnumerable<LootEntry> entries, IExprHost exprHost, IExprDiagnostics diagnostics)
        {
            var pool = new List<LootEntry>();
            foreach (var entry in entries)
            {
                if (ConditionPasses(entry, exprHost, diagnostics))
                {
                    pool.Add(entry);
                }
            }

            return pool;
        }

        /// <summary><c>weighted_pick_one</c> 归一化分母：候选池 <see cref="LootEntry.WeightOrChance"/>
        /// 之和（不要求预先归一化，见 <see cref="LootEntry.WeightOrChance"/> 类型注释）。</summary>
        public static double TotalWeight(IReadOnlyList<LootEntry> pool)
        {
            var total = 0.0;
            foreach (var entry in pool)
            {
                total += entry.WeightOrChance;
            }

            return total;
        }

        /// <summary>给定候选池与预先算好的 <paramref name="totalWeight"/>（<see cref="TotalWeight"/>
        /// 结果，避免重复求和）与一个 [0,1) 均匀阈值分数 <paramref name="thresholdFraction"/>（原
        /// <see cref="LootHost.PickWeighted"/> 里 <c>IRngHost.Next</c> 的采样值），按候选池原始顺序累加
        /// 权重、命中即返回——原样保留 <see cref="LootHost.PickWeighted"/> 的浮点误差兜底分支（理论上
        /// 不可达，cumulative 最终等于 totalWeight &gt; threshold）。调用方需自行保证
        /// <paramref name="totalWeight"/> &gt; 0（本函数不再重复判空，判空/降级为
        /// <see cref="LootHost.PickWeighted"/> 调用方自己的职责，见类型注释"不改变任何一步的浮点运算
        /// 顺序"）。</summary>
        public static LootEntry SelectByThreshold(IReadOnlyList<LootEntry> pool, double totalWeight, double thresholdFraction)
        {
            var threshold = thresholdFraction * totalWeight;
            var cumulative = 0.0;
            foreach (var entry in pool)
            {
                cumulative += entry.WeightOrChance;
                if (threshold < cumulative)
                {
                    return entry;
                }
            }

            return pool[pool.Count - 1];
        }

        /// <summary><c>chance_each</c> 的基础有效概率（未叠加伪随机），原
        /// <see cref="LootHost.RollChanceEachGroup"/> 局部变量 <c>baseChance</c> 的算法。</summary>
        public static double EffectiveChanceEach(double weightOrChance, double multiplier) =>
            Clamp01(weightOrChance * multiplier);

        /// <summary>伪随机启用时叠加"连续未中次数"加成，原
        /// <see cref="LootHost.RollChanceEachGroup"/> 局部变量 <c>effectiveChance</c> 的算法（线性递增，
        /// 封顶 1，见 <see cref="LootOptions.PseudoRandomStep"/> 类型注释）。</summary>
        public static double ApplyPseudoRandomStep(double baseChance, int missStreak, double step) =>
            Clamp01(baseChance * (1 + missStreak * step));

        /// <summary>一条候选在 <c>[CountMin,CountMax]</c> 内的期望数量（<see cref="LootHost.RollCount"/>
        /// 消耗的 <c>IRngHost.NextInt</c> 是闭区间均匀分布，见
        /// <c>core/foundation/rng/README.md</c>"NextInt：闭区间 [min, max]，用拒绝采样消除取模偏差"），
        /// 仅供 <see cref="LootTableAnalyzer"/> 解析式计算期望数量使用——<see cref="LootHost"/> 自身不
        /// 调用本方法（它需要的是具体一次采样结果，不是期望值）。</summary>
        public static double ExpectedCount(LootEntry entry) =>
            entry.CountMin == entry.CountMax ? entry.CountMin : (entry.CountMin + entry.CountMax) / 2.0;
    }
}
