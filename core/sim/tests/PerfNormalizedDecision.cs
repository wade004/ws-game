using System;

namespace Tests.Sim
{
    /// <summary>一对相邻时刻采到的样本：同一轮里先后各采一次参考负载与被测量值（毫秒）。</summary>
    internal readonly struct PerfSamplePair
    {
        public PerfSamplePair(double referenceMs, double measuredMs)
        {
            ReferenceMs = referenceMs;
            MeasuredMs = measuredMs;
        }

        public double ReferenceMs { get; }

        public double MeasuredMs { get; }
    }

    /// <summary>机器归一化判定结果（见 <see cref="PerfNormalizedDecision.Evaluate"/>）。</summary>
    internal readonly struct PerfDecisionResult
    {
        public PerfDecisionResult(double measuredMs, double referenceMs, double factor, double effectiveThresholdMs, bool passed)
        {
            MeasuredMs = measuredMs;
            ReferenceMs = referenceMs;
            Factor = factor;
            EffectiveThresholdMs = effectiveThresholdMs;
            Passed = passed;
        }

        /// <summary>参与判定的被测量值（毫秒）。</summary>
        public double MeasuredMs { get; }

        /// <summary>与被测量值同一轮采到的参考负载（毫秒）。</summary>
        public double ReferenceMs { get; }

        /// <summary>机器系数：clamp(参考负载 ÷ 基线机参考负载, 1, 系数上限)。</summary>
        public double Factor { get; }

        /// <summary>归一化后的有效阈值（毫秒）：基线阈值 × 机器系数。</summary>
        public double EffectiveThresholdMs { get; }

        /// <summary>被测量值是否在有效阈值之内。</summary>
        public bool Passed { get; }
    }

    /// <summary>
    /// 性能基线用例的纯判定与采样函数（从 <c>SimPerfBaselineTests</c> 里抽出来，使"归一化为什么在负载下仍会误报"
    /// 这个缺陷可以脱离真实墙钟被复现、被回归）。
    /// <para>
    /// 判断记录（2026-10-03，门禁连续三次被 <c>HeadlessBuild_MinTiming_WithinBaselineThreshold</c> 偶发红挡住的根因）：
    /// 原用例的参考负载由 <c>PerfMachineCalibration.ReferenceMs</c> 在进程内一次性缓存（第一次被问到时跑 5 次取中位数），
    /// 而被测量值（20 次 Build 的最小值）在另一个时刻采样——两者之间机器负载只要变了（别的测试工程并行启动/结束、
    /// 隔壁构建进程进入重编译阶段），"机器系数"就不再描述被测量值当时的机器状态：参考负载在安静时刻测得，而 Build 全程
    /// 落在繁忙窗口里，归一化形同虚设，实测值（188.5ms 对阈值 173.4ms，系数 6.89；140.9ms 对 136.0ms，系数 5.41）
    /// 就超了阈值。另外同进程里其它测试类并行跑也会让 Build（大量分配、受 GC 与线程争用影响）比纯计算的参考负载
    /// 慢得更多，这一条由用例所在集合禁用并行化解决（见 <c>SimPerfBaselineTests</c> 的 Collection 声明）。
    /// </para>
    /// <para>
    /// 做法：参考负载与被测量值<b>交错采样成相邻的样本对</b>，逐对归一化后取最佳一对。每一对里的参考负载与被测量值
    /// 在时间上紧挨着、共同承受同样的负载起伏，系数因此描述的正是该被测量值当时的机器状态；多轮采样让"碰上一个
    /// 安静窗口"的机会足够多。（备选做法"参考负载与被测量值各取全局最小值"不选：两个最小值可能来自不同窗口，
    /// 安静窗口窄于一对样本时仍会错配。）阈值放宽不是办法（真回归会被一起放过），采样不对齐才是病根；判定公式本身
    /// 不变（系数限幅、有效阈值），真回归（每一对里都是被测量值变大、参考负载不变）仍然每一对都超阈值。
    /// </para>
    /// </summary>
    internal static class PerfNormalizedDecision
    {
        /// <summary>
        /// 单对纯判定：<c>factor = clamp(referenceMs ÷ baselineReferenceMs, 1, factorMax)</c>，
        /// <c>effective = thresholdMs × factor</c>，<c>passed = measuredMs ≤ effective</c>。
        /// 与 <c>core/gameplay/tests/Perf/README.md</c>"机器归一化口径"同一公式。
        /// </summary>
        public static PerfDecisionResult Evaluate(
            double measuredMs, double referenceMs, double thresholdMs, double baselineReferenceMs, double factorMax)
        {
            if (!(baselineReferenceMs > 0)) throw new ArgumentOutOfRangeException(nameof(baselineReferenceMs), "基线参考负载必须为正");
            if (!(factorMax >= 1)) throw new ArgumentOutOfRangeException(nameof(factorMax), "系数上限必须不小于 1");

            var factor = Math.Clamp(referenceMs / baselineReferenceMs, 1.0, factorMax);
            var effectiveThreshold = thresholdMs * factor;
            return new PerfDecisionResult(measuredMs, referenceMs, factor, effectiveThreshold, measuredMs <= effectiveThreshold);
        }

        /// <summary>
        /// 多对判定：每一对各自按 <see cref="Evaluate"/> 归一化，取"被测量值 ÷ 本对系数"最小（离阈值余量最大）的一对
        /// 作为结论。有一对在自己的机器状态下达标即通过；真回归时每一对都超，所以仍然失败。
        /// </summary>
        public static PerfDecisionResult EvaluateBestPair(
            System.Collections.Generic.IReadOnlyList<PerfSamplePair> pairs,
            double thresholdMs, double baselineReferenceMs, double factorMax)
        {
            if (pairs == null) throw new ArgumentNullException(nameof(pairs));
            if (pairs.Count == 0) throw new ArgumentException("至少需要一对样本", nameof(pairs));

            PerfDecisionResult best = default;
            var bestNormalized = double.MaxValue;
            for (var i = 0; i < pairs.Count; i++)
            {
                var r = Evaluate(pairs[i].MeasuredMs, pairs[i].ReferenceMs, thresholdMs, baselineReferenceMs, factorMax);
                var normalized = r.MeasuredMs / r.Factor;
                if (normalized < bestNormalized)
                {
                    bestNormalized = normalized;
                    best = r;
                }
            }

            return best;
        }

        /// <summary>
        /// 交错采样：共 <paramref name="rounds"/> 轮，每轮先后各采一次参考负载与被测量值，组成一对（奇偶轮交换先后，
        /// 抵消"总是参考在前"带来的系统性偏差）。
        /// </summary>
        public static System.Collections.Generic.List<PerfSamplePair> SampleInterleavedPairs(
            int rounds, Func<double> sampleReferenceMs, Func<double> sampleMeasuredMs)
        {
            if (rounds <= 0) throw new ArgumentOutOfRangeException(nameof(rounds));
            if (sampleReferenceMs == null) throw new ArgumentNullException(nameof(sampleReferenceMs));
            if (sampleMeasuredMs == null) throw new ArgumentNullException(nameof(sampleMeasuredMs));

            var pairs = new System.Collections.Generic.List<PerfSamplePair>(rounds);
            for (var round = 0; round < rounds; round++)
            {
                double reference;
                double measured;
                if ((round & 1) == 0)
                {
                    reference = sampleReferenceMs();
                    measured = sampleMeasuredMs();
                }
                else
                {
                    measured = sampleMeasuredMs();
                    reference = sampleReferenceMs();
                }

                pairs.Add(new PerfSamplePair(reference, measured));
            }

            return pairs;
        }
    }
}
