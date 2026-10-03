using System;
using System.Collections.Generic;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// <see cref="PerfNormalizedDecision"/> 的复现与回归用例（不碰真实墙钟，全部用模拟的耗时序列）：
    /// 门禁连续三次被 <c>SimPerfBaselineTests.HeadlessBuild_MinTiming_WithinBaselineThreshold</c> 偶发红挡住，
    /// 根因是参考负载与被测量值不在同一时刻采样，负载起伏时归一化失效（见 <see cref="PerfNormalizedDecision"/> 判断记录）。
    /// 这里验证两件事：机器整体变慢（参考负载与被测量值同比放大）不得报失败；真回归（只有被测量值变大）必须仍然报失败。
    /// 数值取自 <c>perf_baseline.json</c> 的同口径常量（基线最小耗时 5.03ms、阈值 25.15ms、基线参考负载 44.9ms、系数上限 8）。
    /// </summary>
    public sealed class SimPerfNormalizedDecisionTests
    {
        private const double BaselineMeasuredMs = 5.03;
        private const double ThresholdMs = 25.15;
        private const double BaselineReferenceMs = 44.9;
        private const double FactorMax = 8.0;

        private static PerfDecisionResult EvaluateOne(double measuredMs, double referenceMs) =>
            PerfNormalizedDecision.Evaluate(measuredMs, referenceMs, ThresholdMs, BaselineReferenceMs, FactorMax);

        [Fact]
        public void Evaluate_BaselineMachine_Passes_WithFactorOne()
        {
            var r = EvaluateOne(BaselineMeasuredMs, BaselineReferenceMs);

            Assert.True(r.Passed);
            Assert.Equal(1.0, r.Factor, 9);
            Assert.Equal(ThresholdMs, r.EffectiveThresholdMs, 9);
        }

        [Theory]
        [InlineData(1.5)]
        [InlineData(3.0)]
        [InlineData(5.41)]
        [InlineData(6.89)]
        [InlineData(8.0)]
        public void Evaluate_MachineSlowdown_ReferenceAndMeasuredScaledTogether_DoesNotFail(double slowdown)
        {
            var r = EvaluateOne(BaselineMeasuredMs * slowdown, BaselineReferenceMs * slowdown);

            Assert.True(r.Passed,
                $"整机慢 {slowdown} 倍（参考负载与被测量值同比放大）不应报失败：measured={r.MeasuredMs:F3} effective={r.EffectiveThresholdMs:F3}");
        }

        [Theory]
        [InlineData(1.0, 6.0)]
        [InlineData(3.0, 6.0)]
        [InlineData(6.89, 6.0)]
        [InlineData(1.0, 12.0)]
        public void Evaluate_RealRegression_OnlyMeasuredGrows_StillFails(double machineSlowdown, double regression)
        {
            // 整机慢 machineSlowdown 倍的同时，Build 自身又退化 regression 倍（超过阈值 = 基线 × 5 的量级）。
            var r = EvaluateOne(BaselineMeasuredMs * machineSlowdown * regression, BaselineReferenceMs * machineSlowdown);

            Assert.False(r.Passed,
                $"真回归（被测量值相对基线再慢 {regression} 倍、参考负载只随机器变化）必须仍然报失败：" +
                $"measured={r.MeasuredMs:F3} effective={r.EffectiveThresholdMs:F3}");
        }

        [Fact]
        public void Evaluate_PathologicallySlowMachine_BeyondFactorMax_StillFails()
        {
            // 系数限幅在 8：整机慢到 50 倍已经不是"这次赶上机器忙"，不能被无限吸收。
            var r = EvaluateOne(BaselineMeasuredMs * 50, BaselineReferenceMs * 50);

            Assert.Equal(FactorMax, r.Factor, 9);
            Assert.False(r.Passed);
        }

        [Fact]
        public void Evaluate_MachineFasterThanBaseline_DoesNotTightenThreshold()
        {
            var r = EvaluateOne(BaselineMeasuredMs, BaselineReferenceMs * 0.5);

            Assert.Equal(1.0, r.Factor, 9);
            Assert.Equal(ThresholdMs, r.EffectiveThresholdMs, 9);
            Assert.True(r.Passed);
        }

        [Fact]
        public void Evaluate_RejectsInvalidBaseline()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PerfNormalizedDecision.Evaluate(1, 1, ThresholdMs, baselineReferenceMs: 0, FactorMax));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PerfNormalizedDecision.Evaluate(1, 1, ThresholdMs, BaselineReferenceMs, factorMax: 0.5));
        }

        [Fact]
        public void SampleInterleavedPairs_AlternatesOrder_AndPairsEachRoundWithItsOwnSamples()
        {
            var calls = new List<string>();
            var refSeq = 0;
            var measSeq = 0;

            var pairs = PerfNormalizedDecision.SampleInterleavedPairs(
                4,
                () => { calls.Add("R"); return 100 + refSeq++; },
                () => { calls.Add("M"); return 10 + measSeq++; });

            Assert.Equal(new[] { "R", "M", "M", "R", "R", "M", "M", "R" }, calls);
            Assert.Equal(4, pairs.Count);
            for (var i = 0; i < pairs.Count; i++)
            {
                Assert.Equal(10 + i, pairs[i].MeasuredMs);
            }
        }

        /// <summary>
        /// 模拟时间轴：每次采样占用一个时间步，耗时 = 该类工作的安静基线耗时 × 该时间步的机器负载倍数。
        /// 采样函数（参考负载 / 被测量值）共享同一个时间轴，所以采样先后决定它们各自看到的负载。
        /// </summary>
        private sealed class SimulatedMachine
        {
            private readonly Func<int, double> _load;
            private int _step;

            public SimulatedMachine(Func<int, double> load)
            {
                _load = load;
            }

            public double SampleReferenceMs() => BaselineReferenceMs * _load(_step++);

            public double SampleMeasuredMs() => BaselineMeasuredMs * _load(_step++);
        }

        /// <summary>旧口径的模拟：参考负载先一次性测 5 次取中位数（对应 <c>PerfMachineCalibration.ReferenceMs</c>），
        /// 随后 20 次被测量值取最小值，两段在时间轴上前后相接。</summary>
        private static PerfDecisionResult EvaluateLegacySampling(Func<int, double> load)
        {
            var machine = new SimulatedMachine(load);
            var refSamples = new List<double>();
            for (var i = 0; i < 5; i++) refSamples.Add(machine.SampleReferenceMs());
            refSamples.Sort();
            var legacyReference = refSamples[2];

            var legacyMin = double.MaxValue;
            for (var i = 0; i < 20; i++) legacyMin = Math.Min(legacyMin, machine.SampleMeasuredMs());

            return EvaluateOne(legacyMin, legacyReference);
        }

        private static PerfDecisionResult EvaluateInterleaved(Func<int, double> load)
        {
            var machine = new SimulatedMachine(load);
            var pairs = PerfNormalizedDecision.SampleInterleavedPairs(30, machine.SampleReferenceMs, machine.SampleMeasuredMs);
            return PerfNormalizedDecision.EvaluateBestPair(pairs, ThresholdMs, BaselineReferenceMs, FactorMax);
        }

        [Fact]
        public void LoadShiftAfterReferenceWasSampled_LegacySamplingFalselyFails_InterleavedPairsDoNot()
        {
            // 复现门禁里的误报：机器在最开始很安静（前 5 个时间步负载 1 倍），之后一直繁忙（7 倍）。
            // 旧做法：参考负载在开头安静时刻一次性测好（中位数 = 基线，系数 = 1），之后 20 次被测量值全落在繁忙窗口，
            // 最小值 = 基线 × 7 = 35.2ms > 阈值 25.15ms。
            Func<int, double> load = step => step < 5 ? 1.0 : 7.0;

            var legacy = EvaluateLegacySampling(load);
            Assert.False(legacy.Passed,
                $"前提：旧口径（参考负载与被测量值异时采样）在这条负载轨迹上会误报失败——这就是门禁偶发红的机理：" +
                $"measured={legacy.MeasuredMs:F3} effective={legacy.EffectiveThresholdMs:F3}");

            var decision = EvaluateInterleaved(load);
            Assert.True(decision.Passed,
                $"交错采样成相邻样本对后，同一窗口里的参考负载与被测量值同比放大，不应误报：" +
                $"measured={decision.MeasuredMs:F3} reference={decision.ReferenceMs:F3} factor={decision.Factor:F2} effective={decision.EffectiveThresholdMs:F3}");
        }

        [Fact]
        public void NarrowQuietWindowSeenOnlyByReference_LegacyAndGlobalMinimaFalselyFail_PairwiseDoesNot()
        {
            // 更刁钻的轨迹：只有时间步 0 是安静的（参考负载恰好落在其中），其余全部繁忙（7 倍）。
            // 旧口径：参考负载中位数是繁忙值，系数 7，本身不误报——但"各取全局最小值"的备选做法会让参考最小值取到安静的
            // 44.9ms（系数 1）、被测量值最小值是繁忙的 35.2ms，错配后误报；逐对归一化不受影响。
            Func<int, double> load = step => step == 0 ? 1.0 : 7.0;

            var machine = new SimulatedMachine(load);
            var pairs = PerfNormalizedDecision.SampleInterleavedPairs(30, machine.SampleReferenceMs, machine.SampleMeasuredMs);

            var minReference = double.MaxValue;
            var minMeasured = double.MaxValue;
            foreach (var pair in pairs)
            {
                minReference = Math.Min(minReference, pair.ReferenceMs);
                minMeasured = Math.Min(minMeasured, pair.MeasuredMs);
            }

            Assert.False(EvaluateOne(minMeasured, minReference).Passed, "前提：全局最小值错配会误报失败");

            var decision = PerfNormalizedDecision.EvaluateBestPair(pairs, ThresholdMs, BaselineReferenceMs, FactorMax);
            Assert.True(decision.Passed,
                $"逐对归一化：measured={decision.MeasuredMs:F3} reference={decision.ReferenceMs:F3} factor={decision.Factor:F2}");
        }

        [Fact]
        public void BurstyLoad_InterleavedPairs_DoNotFalselyFail()
        {
            // 负载在 1～7 倍之间来回起伏（周期 5 个时间步，参考负载与被测量值常落在不同的负载倍数上）。
            Func<int, double> load = step => 1.0 + (step * 3 % 5) * 1.5;

            var decision = EvaluateInterleaved(load);

            Assert.True(decision.Passed,
                $"起伏负载下只要有一对样本落在同一负载窗口就应达标：measured={decision.MeasuredMs:F3} " +
                $"reference={decision.ReferenceMs:F3} factor={decision.Factor:F2}");
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(4.0)]
        [InlineData(7.0)]
        public void RealRegressionUnderLoad_InterleavedPairs_StillFail(double steadyLoad)
        {
            // 稳定负载 steadyLoad 倍下，Build 自身退化 6 倍（参考负载不受影响）：每一对都超阈值，整体必须失败。
            const double regression = 6.0;
            var step = 0;
            var pairs = PerfNormalizedDecision.SampleInterleavedPairs(
                30,
                () => { step++; return BaselineReferenceMs * steadyLoad; },
                () => { step++; return BaselineMeasuredMs * steadyLoad * regression; });

            var decision = PerfNormalizedDecision.EvaluateBestPair(pairs, ThresholdMs, BaselineReferenceMs, FactorMax);

            Assert.False(decision.Passed,
                $"真回归必须仍然失败：measured={decision.MeasuredMs:F3} effective={decision.EffectiveThresholdMs:F3}");
            Assert.Equal(60, step);
        }

        [Fact]
        public void EvaluateBestPair_PicksPairWithLargestHeadroom()
        {
            var pairs = new List<PerfSamplePair>
            {
                new PerfSamplePair(BaselineReferenceMs, 40.0),          // 安静窗口里被测量值异常慢：本对不达标
                new PerfSamplePair(BaselineReferenceMs * 6, 6.0 * 6),   // 繁忙窗口里同比放大：达标，归一化后 6
                new PerfSamplePair(BaselineReferenceMs * 2, 5.5 * 2),   // 归一化后 5.5：余量最大
            };

            var decision = PerfNormalizedDecision.EvaluateBestPair(pairs, ThresholdMs, BaselineReferenceMs, FactorMax);

            Assert.True(decision.Passed);
            Assert.Equal(5.5 * 2, decision.MeasuredMs, 9);
            Assert.Equal(2.0, decision.Factor, 9);
        }

        [Fact]
        public void EvaluateBestPair_EmptyPairs_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                PerfNormalizedDecision.EvaluateBestPair(new List<PerfSamplePair>(), ThresholdMs, BaselineReferenceMs, FactorMax));
        }
    }
}
