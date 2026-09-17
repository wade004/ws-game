using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Core.Foundation.Common;
using Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Sim
{
    /// <summary>T-N6-5：跑一次完整的 <c>sim.scenario.sim_coverage_all</c>，供本测试类共享同一份结果。</summary>
    public sealed class FullCoverageScenarioFixture
    {
        public Core.Sim.HeadlessWorld World { get; }
        public ScenarioDef Scenario { get; }
        public CoverageReport Report { get; }
        public double ElapsedSeconds { get; }

        public FullCoverageScenarioFixture()
        {
            World = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            Scenario = World.ScenarioCatalog!.Get(new Id("sim.scenario.sim_coverage_all"));
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();

            var sw = Stopwatch.StartNew();
            Report = CoverageSimulation.Run(Scenario, World.AnchorTable!, dataSources);
            sw.Stop();
            ElapsedSeconds = sw.Elapsed.TotalSeconds;
        }
    }

    /// <summary>T-N6-5：<see cref="CoverageSimulation"/> 的验收测试。</summary>
    public class CoverageSimulationTests : IClassFixture<FullCoverageScenarioFixture>
    {
        private readonly ITestOutputHelper _output;
        private readonly FullCoverageScenarioFixture _fixture;

        public CoverageSimulationTests(FullCoverageScenarioFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public void FullScenario_RunsToCompletion_AndReportsTiming()
        {
            var report = _fixture.Report;
            _output.WriteLine(
                $"完整场景 {_fixture.Scenario.Id} 共 {report.Skills.Count} 技能 / {report.Items.Count} 装备 / " +
                $"{report.Creatures.Count} 生物，总耗时 {_fixture.ElapsedSeconds:F2}s");

            _output.WriteLine("技能表前 5：");
            foreach (var row in report.Skills.Take(5))
            {
                _output.WriteLine($"  {row.Id} ratio={row.Statistic:F2} dev={row.Deviation:P0} flag={row.Level} share={row.SecondaryMeasurement:F2}");
            }
            _output.WriteLine("装备表前 5：");
            foreach (var row in report.Items.Take(5))
            {
                _output.WriteLine($"  {row.Id} ratio={row.Statistic:F2} dev={row.Deviation:P0} flag={row.Level} marginal={row.SecondaryMeasurement:F2}");
            }
            _output.WriteLine("生物表前 5：");
            foreach (var row in report.Creatures.Take(5))
            {
                _output.WriteLine($"  {row.Id} ttk={row.Statistic:F2} anchor_ttk={row.Baseline:F2} dev={row.Deviation:P0} flag={row.Level}");
            }

            Assert.True(report.Skills.Count > 0, "技能覆盖表应非空");
            Assert.True(report.Items.Count > 0, "装备覆盖表应非空");
            Assert.True(report.Creatures.Count > 0, "生物覆盖表应非空");
        }

        [Fact]
        public void Skills_AreSortedByDeviationDescending()
        {
            var skills = _fixture.Report.Skills;
            for (var i = 1; i < skills.Count; i++)
            {
                Assert.True(skills[i - 1].Deviation >= skills[i].Deviation,
                    $"技能表未按 |偏离| 降序：第 {i - 1} 项 {skills[i - 1].Deviation} < 第 {i} 项 {skills[i].Deviation}");
            }
        }

        [Fact]
        public void Items_AreSortedByDeviationDescending()
        {
            var items = _fixture.Report.Items;
            for (var i = 1; i < items.Count; i++)
            {
                Assert.True(items[i - 1].Deviation >= items[i].Deviation,
                    $"装备表未按 |偏离| 降序：第 {i - 1} 项 {items[i - 1].Deviation} < 第 {i} 项 {items[i].Deviation}");
            }
        }

        /// <summary>验收 4：注入探针应分别排在技能表、装备表的第一位（口径：分类第一，不做跨类别
        /// 合并总表，见 <see cref="CoverageSimulation"/> 类型判断记录"探针位次验收口径"）。</summary>
        [Fact]
        public void InjectedProbes_RankFirst_InTheirOwnCategory()
        {
            var report = _fixture.Report;

            Assert.True(report.Skills.Count > 0);
            Assert.Equal("skill.def.sim_probe_overbudget", report.Skills[0].Id.Value);
            Assert.True(report.Skills[0].Statistic > 1.0, $"超模探针预算比值应 > 1.0，实际 {report.Skills[0].Statistic}");

            Assert.True(report.Items.Count > 0);
            Assert.Equal("item.template.sim_probe_underbudget", report.Items[0].Id.Value);
            Assert.True(report.Items[0].Statistic < 1.0, $"欠模探针预算消耗比应 < 1.0，实际 {report.Items[0].Statistic}");
        }

        [Fact]
        public void ToJson_Deterministic_SameScenario_TwoRuns_ByteIdentical()
        {
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var report1 = CoverageSimulation.Run(_fixture.Scenario, _fixture.World.AnchorTable!, dataSources);
            var report2 = CoverageSimulation.Run(_fixture.Scenario, _fixture.World.AnchorTable!, dataSources);
            Assert.Equal(report1.ToJson(), report2.ToJson());
        }

        /// <summary>
        /// 深度复审 E-S1：<see cref="CoverageOutlierRow.SortByDeviationDescendingThenById"/> 并列
        /// tie-break——手工构造三条 <c>Deviation</c> 恰好相等（含一条与其它 <c>Deviation</c> 不同的
        /// 对照行）的记录，不依赖嵌入数据集是否真的产生并列离群值。断言：(1) 不同 <c>Deviation</c>
        /// 之间仍按降序排列；(2) <c>Deviation</c> 相等的行之间按 <see cref="Id"/>（<c>Value</c>，
        /// <c>Ordinal</c>）升序排列，即便输入顺序是乱序/降序也稳定收敛到同一结果（验证的是排序结果
        /// 本身的确定性，不是"不改变输入相对顺序"这种真正的稳定排序语义）。
        /// </summary>
        [Fact]
        public void SortByDeviationDescendingThenById_TiedDeviation_SortsByIdAscending()
        {
            CoverageOutlierRow MakeRow(string id, double deviation) => new CoverageOutlierRow(
                new Id(id), CoverageCategory.Item, statistic: 1.0, baseline: 1.0, deviation: deviation,
                level: CoverageOutlierLevel.Info, secondaryMeasurement: null, note: null);

            // 故意打乱输入顺序（先放 id 靠后的并列行，中间插一条更高 deviation 的行）。
            var input = new List<CoverageOutlierRow>
            {
                MakeRow("item.template.zzz_tied", 0.5),
                MakeRow("item.template.higher", 0.9),
                MakeRow("item.template.aaa_tied", 0.5),
                MakeRow("item.template.mmm_tied", 0.5),
            };

            var sorted = CoverageOutlierRow.SortByDeviationDescendingThenById(input);

            Assert.Equal(4, sorted.Count);
            Assert.Equal("item.template.higher", sorted[0].Id.Value);
            Assert.Equal("item.template.aaa_tied", sorted[1].Id.Value);
            Assert.Equal("item.template.mmm_tied", sorted[2].Id.Value);
            Assert.Equal("item.template.zzz_tied", sorted[3].Id.Value);
        }

        // ===== 消费方反馈第 50 条（CancellationToken/IProgress）=====

        private static (Core.Sim.HeadlessWorld World, ScenarioDef Scenario, IReadOnlyList<Core.Foundation.DataRegistry.IDataSource> DataSources)
            BuildSmallScenario(int runs)
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_coverage_all")).WithRuns(runs);
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            return (world, scenario, dataSources);
        }

        [Fact]
        public void Run_PreCancelledToken_ThrowsImmediately()
        {
            var (world, scenario, dataSources) = BuildSmallScenario(runs: 1);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => CoverageSimulation.Run(scenario, world.AnchorTable!, dataSources, failOnUnknownTable: false, cts.Token));
        }

        [Fact]
        public void Run_CancelledMidway_ViaProgressCallback_ThrowsAndProducesNoResult()
        {
            var (world, scenario, dataSources) = BuildSmallScenario(runs: 1);
            using var cts = new CancellationTokenSource();
            var progress = new RecordingProgress(p =>
            {
                if (p.Completed >= 1) cts.Cancel();
            });

            Assert.Throws<OperationCanceledException>(
                () => CoverageSimulation.Run(scenario, world.AnchorTable!, dataSources, failOnUnknownTable: false, cts.Token, progress));
        }

        [Fact]
        public void Run_NotCancelled_MatchesOldOverload_SameSeed()
        {
            var (world, scenario, dataSources) = BuildSmallScenario(runs: 1);

            var legacy = CoverageSimulation.Run(scenario, world.AnchorTable!, dataSources);
            var viaNewOverload = CoverageSimulation.Run(
                scenario, world.AnchorTable!, dataSources, failOnUnknownTable: false, CancellationToken.None, progress: null);

            Assert.Equal(legacy.ToJson(), viaNewOverload.ToJson());
        }

        [Fact]
        public void Run_Progress_ReportsMonotonicSequence_EndingAtTotal()
        {
            var (world, scenario, dataSources) = BuildSmallScenario(runs: 1);
            var reports = new List<SimProgress>();

            CoverageSimulation.Run(
                scenario, world.AnchorTable!, dataSources, failOnUnknownTable: false, CancellationToken.None,
                new RecordingProgress(reports.Add));

            Assert.NotEmpty(reports);
            for (var i = 1; i < reports.Count; i++)
            {
                Assert.True(reports[i].Completed >= reports[i - 1].Completed);
            }
            Assert.Equal(reports[^1].Total, reports[^1].Completed);

            // 判断记录：三段遍历各自的阶段标识都应该出现过（技能/装备/生物三张表在嵌入数据集里均非空）。
            var stages = reports.Select(r => r.Stage).Distinct().ToList();
            Assert.Contains(CoverageSimulation.ProgressStageSkill, stages);
            Assert.Contains(CoverageSimulation.ProgressStageItem, stages);
            Assert.Contains(CoverageSimulation.ProgressStageCreature, stages);
        }

        private sealed class RecordingProgress : IProgress<SimProgress>
        {
            private readonly Action<SimProgress> _callback;

            public RecordingProgress(Action<SimProgress> callback)
            {
                _callback = callback;
            }

            public void Report(SimProgress value) => _callback(value);
        }
    }
}
