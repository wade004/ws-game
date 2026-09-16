using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Sim
{
    /// <summary>
    /// T-N6-6（ADR-0035 决策 5、计划书 §12 验收 5）：<see cref="SimReport"/>/<see cref="SimBaseline"/>/
    /// <see cref="BaselineComparer"/> 的验收测试。
    /// <para>
    /// 判断记录（determinism 测试用缩小的 <c>runs</c>，不是完整场景）：验收 5-① 要证明的是
    /// "<see cref="BaselineComparer"/> 对一次真正逐字节相同的重跑判定全部 <see cref="BaselineDiffStatus.Same"/>"
    /// ——这条不变量与"跑多少次/多少场"无关（同种子重跑给出逐字节相同结果，不管
    /// <c>scenario.Runs</c> 是 2 还是 60）。"完整场景至少跑一次、把耗时打印出来"这条要求已经由
    /// <c>ArenaSimulationTests</c>/<c>GrowthSimulationTests</c>/<c>CoverageSimulationTests</c> 各自的
    /// <c>FullScenario_*</c> 系列覆盖（T-N6-4/T-N6-5 既有验收），本文件用
    /// <see cref="ScenarioDef.WithRuns"/> 把三个场景的 <c>runs</c> 缩小到 2，只为验证
    /// <see cref="BaselineComparer"/> 自身的分类逻辑，避免不必要的重复计算把 <c>Tests.Sim</c> 总耗时
    /// 推高（见 <c>core/sim/README.md</c>"验收测试"一节"Tests.Sim 总耗时仍 ≤150 秒"这条硬性预算）。
    /// </para>
    /// </summary>
    public class BaselineComparerTests
    {
        private readonly ITestOutputHelper _output;

        public BaselineComparerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>验收 5-①：三个场景各自同种子独立重跑两次（互相独立的两次 <c>*Simulation.Run</c>
        /// 调用，不是同一个报告对象比较自己），<see cref="SimReport.ToJson"/> 逐字节相同，
        /// <see cref="BaselineComparer.Compare"/> 对全部统计量判定 <see cref="BaselineDiffStatus.Same"/>
        /// （无 Exceeded/Added/Removed）。</summary>
        [Fact]
        public void Compare_SameSeedRerun_AllThreeScenarios_AllSame()
        {
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var catalog = world.ScenarioCatalog!;
            var anchors = world.AnchorTable!;
            var registry = world.Registry;

            var arena = catalog.Get(new Id("sim.scenario.sim_arena_matrix")).WithRuns(2);
            var growth = catalog.Get(new Id("sim.scenario.sim_growth_full")).WithRuns(2);
            var coverage = catalog.Get(new Id("sim.scenario.sim_coverage_all")).WithRuns(2);

            AssertRerunAllSame("arena", () => SimReport.FromArenaReport(
                ArenaSimulation.Run(arena, anchors, dataSources), arena, registry, "test"));
            AssertRerunAllSame("growth", () => SimReport.FromGrowthReport(
                GrowthSimulation.Run(growth, anchors, dataSources), growth, registry, "test"));
            AssertRerunAllSame("coverage", () => SimReport.FromCoverageReport(
                CoverageSimulation.Run(coverage, anchors, dataSources), coverage, registry, "test"));
        }

        private void AssertRerunAllSame(string label, Func<SimReport> run)
        {
            var sw = Stopwatch.StartNew();
            var first = run();
            var second = run();
            sw.Stop();
            _output.WriteLine($"{label}：独立重跑两次共耗时 {sw.Elapsed.TotalSeconds:F2}s，共 {first.Stats.Count} 条统计量");

            Assert.Equal(first.ToJson(), second.ToJson());

            var baseline = SimBaseline.FromReport(first);
            var diff = BaselineComparer.Compare(second, baseline);
            Assert.Equal(0, diff.ExceededCount);
            Assert.Equal(0, diff.AddedCount);
            Assert.Equal(0, diff.RemovedCount);
            Assert.False(diff.DatasetChanged);
            Assert.All(diff.Rows, row => Assert.Equal(BaselineDiffStatus.Same, row.Status));
        }

        /// <summary>验收 5-②：在内存中经数据源覆盖（不改磁盘文件）把
        /// <c>arch.class.sim_warrior.base_stats.stat.strength</c>（经该职业
        /// <c>derivation_overrides</c> 以系数 1.0 派生 <c>stat.attack_power</c>，直接决定
        /// <c>school_damage</c> 效果的 <c>coefficient × attack_power</c> 项）从 12 放大到 200 后重跑
        /// arena，<see cref="BaselineDiff"/> 列出受影响统计量且至少一条
        /// <see cref="BaselineDiffStatus.Exceeded"/>，未受影响的 <c>player_max_health</c>（生命值只
        /// 由 <c>stat.stamina</c> 决定，本次未改动）为 Same/Within。
        /// <para>判断记录（为何改 <c>arch.class</c> 而不是 <c>item.weapon_dps_curve</c>/装备伤害）：
        /// 实测核实过两条更"符合任务书字面例子"的路径均对本数据集的实际战斗结算无效——①
        /// <c>item.weapon_dps_curve</c> 只被 <see cref="ExpectedStatCalculator"/>/
        /// <see cref="StandardPlayerBuilder"/> 用作数值设计层面的期望武器秒伤对照曲线，不参与真实
        /// 战斗结算；② <c>item.template.weapon_profile.damage_min/max</c> 同样不被
        /// <c>skill.def</c> 的 <c>school_damage</c> 效果引用（后者按
        /// <c>base_value + coefficient × stat.attack_power</c> 计算，见
        /// <c>core/sim/tests/data/skill/skill.def.json</c>，与武器字面伤害无关，
        /// <c>weapon_profile</c> 更可能只驱动本数据集未启用的"普通攻击"分支）——两次改动后
        /// <c>player_dps_mean</c> 等统计量均与未改动时逐位相同，如实记录在此，避免下一次维护者重踩
        /// 同一个坑。改为直接改 <c>stat.attack_power</c> 的真实数据源头（<c>base_stats.stat
        /// .strength</c>，经 <c>derivation_overrides</c> 1:1 转化）才能验证"数据变化 → 统计量偏离"
        /// 这条链路，任务书原文"如……或 sim.anchor 某行"本就是示例，不是强制指定字段。</para></summary>
        [Fact]
        public void Compare_MutatedStrengthBaseStat_ArenaScenario_ExceededOnDpsRows_HpUnaffected()
        {
            var baselineDataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var mutatedDataSources = new List<IDataSource>(baselineDataSources) { BuildStrengthOverrideSource() };

            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_arena_matrix")).WithRuns(6);
            var anchors = world.AnchorTable!;
            var registry = world.Registry;

            var baselineReport = SimReport.FromArenaReport(
                ArenaSimulation.Run(scenario, anchors, baselineDataSources), scenario, registry, "test");
            var mutatedReport = SimReport.FromArenaReport(
                ArenaSimulation.Run(scenario, anchors, mutatedDataSources), scenario, registry, "test");

            var baseline = SimBaseline.FromReport(baselineReport);
            var diff = BaselineComparer.Compare(mutatedReport, baseline);

            _output.WriteLine($"exceeded={diff.ExceededCount} within={diff.WithinCount} same={diff.SameCount}");
            foreach (var row in diff.SortedRows().Take(5))
            {
                _output.WriteLine($"  {row.Status} {row.Path} baseline={row.BaselineValue} current={row.CurrentValue}");
            }

            Assert.True(diff.ExceededCount > 0, "放大武器秒伤曲线后应至少有一条统计量的偏离超出容差");

            var hpRows = diff.Rows.Where(r => r.Path.EndsWith(".player_max_health", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(hpRows);
            Assert.All(hpRows, row => Assert.NotEqual(BaselineDiffStatus.Exceeded, row.Status));
        }

        private static IDataSource BuildStrengthOverrideSource()
        {
            const string json = @"{
  ""table"": ""arch.class"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""arch.class.sim_warrior"",
      ""override"": true,
      ""name_key"": ""l10n.class.sim_warrior.name"",
      ""primary_stat"": ""stat.strength"",
      ""base_stats"": {
        ""stat.strength"": 200,
        ""stat.agility"": 6,
        ""stat.intellect"": 4,
        ""stat.stamina"": 150
      },
      ""power_types"": [
        ""arch.power.health"",
        ""arch.power.sim_fury""
      ],
      ""skill_book_ref"": ""skill.book.sim_warrior"",
      ""talent_tree_ref"": ""arch.talent_tree.sim_warrior"",
      ""level_curve_ref"": ""prog.level_curve.sim_warrior"",
      ""derivation_overrides"": [
        {
          ""stat"": ""stat.attack_power"",
          ""source"": ""stat.strength"",
          ""coefficient"": 1.0
        }
      ]
    }
  ]
}";
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("override/arch/arch.class.json", json);
            return new FileSystemDataSource(fs, "override");
        }

        /// <summary>验收 4"浮点比对无精确相等"：对一份真实报告的一条统计量做 1e-12 量级的相对扰动
        /// （远小于默认相对容差 1%，也远大于 <see cref="BaselineCompareOptions.ExactMatchEpsilon"/>
        /// 默认值 1e-9 绝对值——扰动后的绝对差约为该统计量数值 × 1e-12，对量级 ~10~2000 的统计量而言
        /// 是 1e-11~1e-9 之间，仍落在阈值之上，不会被 <see cref="BaselineDiffStatus.Same"/> 分支吞掉），
        /// 断言分类结果为 <see cref="BaselineDiffStatus.Within"/>（既不是靠 <c>==</c> 判定的 Same，也
        /// 不会被误判 Exceeded），证明比较全程走阈值比较、不出现浮点精确相等判据。</summary>
        [Fact]
        public void Compare_TinyRelativePerturbation_1e12_IsWithin_NotSameNotExceeded()
        {
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_growth_full")).WithRuns(1);
            var anchors = world.AnchorTable!;
            var registry = world.Registry;

            var report = SimReport.FromGrowthReport(GrowthSimulation.Run(scenario, anchors, dataSources), scenario, registry, "test");

            // 挑一条非零、有代表性的统计量做扰动，其余统计量原样构成基线（保证只有这一条路径变化）。
            var target = report.Stats.First(s => s.Path == "growth.summary.cumulative_gold_via_balance");
            Assert.True(Math.Abs(target.Value) > 1.0, "被扰动的统计量数值应足够大，扰动后的绝对偏离才会稳定落在 Same 阈值之上");

            var perturbedStats = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var stat in report.Stats)
            {
                perturbedStats[stat.Path] = stat.Path == target.Path
                    ? target.Value * (1.0 + 1e-12)
                    : stat.Value;
            }

            var baseline = new SimBaseline(
                SimReport.SchemaVersion, report.ScenarioId, report.Kind, report.DatasetFingerprint,
                report.GeneratedWithVersion, report.Seed, perturbedStats);

            // 判断记录：ExactMatchEpsilon 显式收紧到 1e-15——默认值 1e-9（见
            // BaselineCompareOptions.DefaultExactMatchEpsilon 判断记录）是为"同种子重跑、逐位相同"
            // 这类真实用例挑的边界，本用例的统计量数值量级（数百到数千）乘以 1e-12 相对扰动后绝对差
            // 可能落在 1e-9 附近，与默认阈值边界过近；显式传一个远小于扰动绝对差的 epsilon，让测试
            // 结果不依赖被扰动统计量恰好多大，聚焦验证"哪怕差异小到 1e-12 量级也走阈值分支，不会被
            // 精确相等判据吞掉、也不会被误判超限"这条行为本身。
            var options = new BaselineCompareOptions { ExactMatchEpsilon = 1e-15 };
            var diff = BaselineComparer.Compare(report, baseline, options);
            var targetRow = diff.Rows.Single(r => r.Path == target.Path);

            _output.WriteLine(
                $"{targetRow.Path}: baseline={targetRow.BaselineValue} current={targetRow.CurrentValue} " +
                $"abs_dev={targetRow.AbsDeviation} tolerance={targetRow.Tolerance} status={targetRow.Status}");

            Assert.Equal(BaselineDiffStatus.Within, targetRow.Status);

            // 其余未被扰动的统计量必须仍是 Same——证明扰动只影响了目标路径，比较逐路径独立进行。
            Assert.All(diff.Rows.Where(r => r.Path != target.Path), row => Assert.Equal(BaselineDiffStatus.Same, row.Status));
        }

        /// <summary>验收 5-③（API 层面）：<see cref="SimBaseline.FromReport"/> 产出的基线经
        /// <see cref="SimBaseline.ToJson"/>/<see cref="SimBaseline.Parse"/> 往返后与原报告比较仍全部
        /// Same——对应命令行 <c>--update-baseline</c> 落盘后立刻重跑应得到零差异这一验收点（命令行/
        /// 文件层面的验证见 <c>core/sim/README.md</c>"基线更新流程"一节记录的手动验证结果）。</summary>
        [Fact]
        public void SimBaseline_RoundTripThroughJson_ComparesAllSame()
        {
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_coverage_all")).WithRuns(2);
            var anchors = world.AnchorTable!;
            var registry = world.Registry;

            var report = SimReport.FromCoverageReport(CoverageSimulation.Run(scenario, anchors, dataSources), scenario, registry, "test");

            var baseline = SimBaseline.FromReport(report);
            var roundTripped = SimBaseline.Parse(baseline.ToJson());

            var diff = BaselineComparer.Compare(report, roundTripped);
            Assert.Equal(0, diff.ExceededCount);
            Assert.Equal(0, diff.AddedCount);
            Assert.Equal(0, diff.RemovedCount);
            Assert.All(diff.Rows, row => Assert.Equal(BaselineDiffStatus.Same, row.Status));
        }

        [Fact]
        public void SimReport_ToJson_IsValidJsonWithExpectedTopLevelFields()
        {
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_growth_full")).WithRuns(1);
            var anchors = world.AnchorTable!;
            var registry = world.Registry;

            var report = SimReport.FromGrowthReport(GrowthSimulation.Run(scenario, anchors, dataSources), scenario, registry, "1.35.0-test");
            var json = report.ToJson();

            var parsed = (Core.Foundation.Common.Json.JsonObject)Core.Foundation.Common.Json.JsonReader.Parse(json);
            Assert.Equal("growth", ((Core.Foundation.Common.Json.JsonString)parsed["kind"]).Value);
            Assert.Equal("1.35.0-test", ((Core.Foundation.Common.Json.JsonString)parsed["generated_with_version"]).Value);
            Assert.True(parsed.ContainsKey("dataset_fingerprint"));
            Assert.True(parsed.ContainsKey("stats"));
            Assert.True(parsed.ContainsKey("raw_report"));
        }
    }
}
