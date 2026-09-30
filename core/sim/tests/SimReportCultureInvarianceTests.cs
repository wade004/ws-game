using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Sim;
using Tests.Sim.Culture;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// 测试覆盖梳理 T-H13：<see cref="SimReport.ToJson"/>、<see cref="SimBaseline.ToJson"/>/<see cref="SimBaseline.Parse"/>、
    /// <see cref="BaselineDiff.ToJson"/>/<see cref="BaselineDiff.ToText"/>（供 <c>toolchain/simrunner</c> 写入
    /// <c>*.json</c>/<c>*.diff.txt</c> 的基线产物）在非不变文化（de-DE 小数逗号、tr-TR、sv-SE 负号 U+2212）下
    /// 必须与不变文化下逐字节相同——仿真本身也在目标文化下整体重跑一遍（覆盖仿真内部的文本化路径）。
    /// </summary>
    public sealed class SimReportCultureInvarianceTests
    {
        /// <summary>整条产出链：跑一次 growth 场景（runs=1，与 <c>BaselineComparerTests</c> 同款缩小）→ 报告 JSON →
        /// 基线 JSON/往返 → 对一份整体放大 3.7% 的基线比对得到含小数的 Exceeded/Within 行 → diff 的 JSON 与文本。</summary>
        private static string ProduceArtifacts()
        {
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_growth_full")).WithRuns(1);

            var report = SimReport.FromGrowthReport(
                GrowthSimulation.Run(scenario, world.AnchorTable!, dataSources), scenario, world.Registry, "test");

            var baseline = SimBaseline.FromReport(report);
            var baselineJson = baseline.ToJson();
            var roundTripped = SimBaseline.Parse(baselineJson);
            Assert.Equal(baselineJson, roundTripped.ToJson());

            var scaled = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var kv in baseline.Stats)
            {
                scaled[kv.Key] = kv.Value * 1.037;
            }

            var scaledBaseline = new SimBaseline(
                SimReport.SchemaVersion, report.ScenarioId, report.Kind, report.DatasetFingerprint,
                report.GeneratedWithVersion, report.Seed, scaled);
            var diff = BaselineComparer.Compare(report, scaledBaseline);

            return string.Join(
                "\n=====\n",
                report.ToJson(), baselineJson, diff.ToJson(), diff.ToText());
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void ReportBaselineAndDiffText_AreByteIdenticalUnderNonInvariantCulture(string culture)
        {
            var baseline = CultureScope.Run("", ProduceArtifacts);
            var actual = CultureScope.Run(culture, ProduceArtifacts);

            Assert.Equal(baseline, actual);
            // 防空测试：产物里确实有带小数点的数值文本，且没有小数逗号形式的数字
            Assert.Matches(@"\d\.\d", baseline);
            Assert.DoesNotMatch(@"\d,\d", actual.Replace("\n=====\n", "\n"));
        }
    }
}
