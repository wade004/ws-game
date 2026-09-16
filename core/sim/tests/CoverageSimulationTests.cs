using System.Diagnostics;
using System.Linq;
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
    }
}
