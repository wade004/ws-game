using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Sim
{
    /// <summary>T-N6-4：跑一次完整的 <c>sim.scenario.sim_arena_matrix</c>（5 等级 × 7 偏移 × 60 次/格
    /// = 2100 场，见 <c>core/sim/tests/data/README.md</c>"场景表"一节的 runs 调参记录），供
    /// <see cref="ArenaSimulationTests"/> 的全部 <c>FullScenario_*</c> 用例共享同一份结果——xunit
    /// <see cref="IClassFixture{TFixture}"/> 保证本类型的构造函数在整个测试类只执行一次，避免 5 个
    /// 断言各自重新跑一遍 8～9 秒的完整仿真（任务书"至少一个测试跑完整场景定义并把总耗时打印出来"，
    /// 不是"每个断言都各自重新跑一遍"）。</summary>
    public sealed class FullArenaScenarioFixture
    {
        public Core.Sim.HeadlessWorld World { get; }
        public ScenarioDef Scenario { get; }
        public ArenaReport Report { get; }
        public double ElapsedSeconds { get; }

        public FullArenaScenarioFixture()
        {
            World = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            Scenario = World.ScenarioCatalog!.Get(new Id("sim.scenario.sim_arena_matrix"));
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();

            var sw = Stopwatch.StartNew();
            Report = ArenaSimulation.Run(Scenario, World.AnchorTable!, dataSources);
            sw.Stop();
            ElapsedSeconds = sw.Elapsed.TotalSeconds;
        }
    }

    /// <summary>T-N6-4：<see cref="ArenaSimulation"/> 的验收测试。<c>FullScenario_*</c> 系列共享
    /// <see cref="FullArenaScenarioFixture"/> 跑出的同一份完整场景结果（不缩参数）；确定性相关测试
    /// 改用一份内嵌合成的小场景（2 等级 × 3 偏移 × 5 次/格）控制时长，见
    /// <see cref="BuildSyntheticScenarioWorld"/>。</summary>
    public class ArenaSimulationTests : IClassFixture<FullArenaScenarioFixture>
    {
        private readonly ITestOutputHelper _output;
        private readonly FullArenaScenarioFixture _fixture;

        public ArenaSimulationTests(FullArenaScenarioFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public void FullScenario_RunsToCompletion_AndReportsTiming()
        {
            var scenario = _fixture.Scenario;
            var report = _fixture.Report;

            _output.WriteLine(
                $"完整场景 {scenario.Id} 共 {report.Cells.Count} 格（{scenario.Levels.Count} 等级 × " +
                $"{scenario.Opponent.LevelOffsets.Count} 偏移 × {scenario.Runs} 次/格 = " +
                $"{report.Cells.Count * scenario.Runs} 场），总耗时 {_fixture.ElapsedSeconds:F2}s");

            var expectedCells = scenario.Levels.Count * scenario.Opponent.LevelOffsets.Count;
            Assert.Equal(expectedCells, report.Cells.Count);
            Assert.Equal(scenario.Levels.Count, report.Reconciliation.Count);
        }

        /// <summary>验收 1（T-N6-4b 复核后收紧：DPS/HP 改为 5 个等级全部在带宽内，不再是 ≥3）。</summary>
        [Fact]
        public void FullScenario_Reconciliation_AllLevelsWithinBandwidth()
        {
            var report = _fixture.Report;
            var dpsPassCount = report.Reconciliation.Count(r => r.DpsPass);
            var hpPassCount = report.Reconciliation.Count(r => r.HpPass);

            _output.WriteLine("对账表：");
            foreach (var row in report.Reconciliation)
            {
                _output.WriteLine(
                    $"  L{row.Level}: dps_td={row.DpsTopDown:F3} dps_bu={row.DpsBottomUp:F3} " +
                    $"dev={row.DpsDeviation:P1} bw={row.DpsBandwidth:P0} pass={row.DpsPass} | " +
                    $"hp_td={row.HpTopDown:F1} hp_bu={row.HpBottomUp:F1} dev={row.HpDeviation:P1} pass={row.HpPass} | " +
                    $"ttd_td={row.TtdTopDown:F1} ttd_bu={row.TtdBottomUp:F1} dev={row.TtdDeviation:P1} pass={row.TtdPass}");
            }

            Assert.True(dpsPassCount == report.Reconciliation.Count,
                $"DPS 对账应 5 个等级全部在带宽内，实际 {dpsPassCount}/{report.Reconciliation.Count}");
            Assert.True(hpPassCount == report.Reconciliation.Count,
                $"HP 对账应 5 个等级全部在带宽内，实际 {hpPassCount}/{report.Reconciliation.Count}");
        }

        /// <summary>验收补充：TTD 估计与 anchor.ttd 偏离在带宽内——任务书原文只要求"至少 1 个等级"，
        /// T-N6-4b 调参后实测 5 个等级全部在带宽内（偏离 &lt;1%），按实际达到的水平收紧断言，作为
        /// 更强的回归防护（不是任务书门槛本身的变化）。</summary>
        [Fact]
        public void FullScenario_TtdReconciliation_AllLevelsWithinBandwidth()
        {
            var report = _fixture.Report;
            var ttdPassCount = report.Reconciliation.Count(r => r.TtdPass);
            Assert.True(ttdPassCount == report.Reconciliation.Count,
                $"TTD 对账实测应 5 个等级全部在带宽内，实际 {ttdPassCount}/{report.Reconciliation.Count}");
        }

        /// <summary>验收 2（T-N6-4b 复核后收紧，对场景定义的全部等级成立，不再有"至少一个"的
        /// 例外）：同一玩家等级内，胜率沿偏移单调不增（允许 ±0.05 抖动）；偏移 ≤ −3 胜率 ≥ 0.95；
        /// 偏移 ≥ +3 的每一个偏移点胜率都 ≤ 0.5。根因与解法见
        /// <c>core/sim/tests/data/README.md</c>"T-N6-4b 调参记录"：L20 行此前断层/不满足，根因是
        /// 玩家按等级 &gt; 1 直接出生时成长未写入（见 <c>HeadlessWorldBuilder.Build</c> 判断记录）
        /// 叠加 <c>AnchorTable</c> 原本只到 20 级、越级矩阵 +5 偏移在玩家满级时把生物等级 21～25 全部
        /// 夹到与 20 级相同强度两个因素——前者已在装配根修复，后者已把 <c>AnchorTable</c>/
        /// <c>skill.base_curve.sim_creature_bite*</c> 扩到 25 级并重新标定。</summary>
        [Fact]
        public void FullScenario_MatrixShape_WinRateDegradesWithPositiveOffset()
        {
            var scenario = _fixture.Scenario;
            var report = _fixture.Report;

            const double jitter = 0.05;
            var offsetsAscending = scenario.Opponent.LevelOffsets.OrderBy(o => o).ToList();

            foreach (var level in scenario.Levels)
            {
                var cellsByOffset = offsetsAscending.ToDictionary(
                    o => o, o => report.Cells.First(c => c.PlayerLevel == level && c.LevelOffset == o));

                // 单调不增（允许 ±0.05 抖动）：偏移越大（生物越强），胜率不应显著回升。
                for (var i = 1; i < offsetsAscending.Count; i++)
                {
                    var prev = cellsByOffset[offsetsAscending[i - 1]].WinRate;
                    var curr = cellsByOffset[offsetsAscending[i]].WinRate;
                    Assert.True(curr <= prev + jitter,
                        $"L{level}：偏移 {offsetsAscending[i]} 胜率 {curr:P0} 相对偏移 {offsetsAscending[i - 1]} " +
                        $"的 {prev:P0} 回升超过 ±5% 抖动");
                }

                foreach (var negOffset in offsetsAscending.Where(o => o <= -3))
                {
                    Assert.True(cellsByOffset[negOffset].WinRate >= 0.95,
                        $"L{level}：偏移 {negOffset} 胜率应 ≥ 0.95，实际 {cellsByOffset[negOffset].WinRate:P0}");
                }

                foreach (var posOffset in offsetsAscending.Where(o => o >= 3))
                {
                    Assert.True(cellsByOffset[posOffset].WinRate <= 0.5,
                        $"L{level}：偏移 {posOffset} 胜率应 ≤ 0.5，实际 {cellsByOffset[posOffset].WinRate:P0}");
                }
            }
        }

        [Fact]
        public void FullScenario_SkillDamageShareMean_SumsToOne_ForCellsWithDamage()
        {
            foreach (var cell in _fixture.Report.Cells.Where(c => c.PlayerDpsMean > 0))
            {
                var sum = cell.SkillShareMean.Values.Sum();
                Assert.True(Math.Abs(sum - 1.0) < 1e-6 || cell.SkillShareMean.Count == 0,
                    $"L{cell.PlayerLevel} off={cell.LevelOffset}：技能占比之和应为 1，实际 {sum}");
            }
        }

        // -----------------------------------------------------------------
        // 确定性测试：用一份内嵌合成的小场景（不复用真实 sim_arena_matrix，控制时长）
        // -----------------------------------------------------------------

        private static (Core.Sim.HeadlessWorld World, ScenarioDef ScenarioA, ScenarioDef ScenarioB) BuildSyntheticScenarioWorld()
        {
            const string syntheticScenarioJson = @"{
  ""table"": ""sim.scenario"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""sim.scenario.sim_arena_matrix_test_a"",
      ""kind"": ""arena"",
      ""player"": { ""class_id"": ""arch.class.sim_warrior"", ""level"": 1, ""quality_id"": ""item.quality.sim_common"" },
      ""opponent"": { ""creature_id"": ""creature.sim_wolf_l1"", ""tier_id"": ""creature.tier.sim_normal"", ""level_offsets"": [-3, 0, 3] },
      ""levels"": [1, 10],
      ""runs"": 5,
      ""base_seed"": 777001,
      ""max_ticks"": 1200,
      ""bandwidths"": { ""dps"": 0.25, ""hp"": 0.25, ""ttk"": 0.25, ""ttd"": 0.25, ""hit_rate"": 0.25 }
    },
    {
      ""id"": ""sim.scenario.sim_arena_matrix_test_b"",
      ""kind"": ""arena"",
      ""player"": { ""class_id"": ""arch.class.sim_warrior"", ""level"": 1, ""quality_id"": ""item.quality.sim_common"" },
      ""opponent"": { ""creature_id"": ""creature.sim_wolf_l1"", ""tier_id"": ""creature.tier.sim_normal"", ""level_offsets"": [-3, 0, 3] },
      ""levels"": [1, 10],
      ""runs"": 5,
      ""base_seed"": 777002,
      ""max_ticks"": 1200,
      ""bandwidths"": { ""dps"": 0.25, ""hp"": 0.25, ""ttk"": 0.25, ""ttd"": 0.25, ""hit_rate"": 0.25 }
    }
  ]
}";

            var fs = new StubFileSystem();
            fs.WriteTextAtomic("sim_scenario_test_overlay/sim/sim.scenario.json", syntheticScenarioJson);
            var overlaySource = new FileSystemDataSource(fs, "sim_scenario_test_overlay");

            var frameworkAndEmbedded = SimTestWorldFactory.BuildEmbeddedDataSources();
            var allSources = frameworkAndEmbedded.Concat(new IDataSource[] { overlaySource }).ToList();

            var world = Core.Sim.HeadlessWorldBuilder.Build(new Core.Sim.HeadlessWorldOptions
            {
                DataSources = allSources,
                Seed = 1,
                MapId = SimTestWorldFactory.EmbeddedMapId,
                PlayerId = SimTestWorldFactory.PlayerId,
                PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                GameId = SimTestWorldFactory.EmbeddedGameId,
                FailOnUnknownTable = false,
            });

            var scenarioA = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_arena_matrix_test_a"));
            var scenarioB = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_arena_matrix_test_b"));
            return (world, scenarioA, scenarioB);
        }

        [Fact]
        public void ToJson_SameScenario_TwoRuns_ByteIdentical()
        {
            var (world, scenarioA, _) = BuildSyntheticScenarioWorld();
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();

            var reportA1 = ArenaSimulation.Run(scenarioA, world.AnchorTable!, dataSources);
            var reportA2 = ArenaSimulation.Run(scenarioA, world.AnchorTable!, dataSources);

            Assert.Equal(reportA1.ToJson(), reportA2.ToJson());
        }

        [Fact]
        public void ToJson_DifferentBaseSeed_ProducesDifferentJson()
        {
            var (world, scenarioA, scenarioB) = BuildSyntheticScenarioWorld();
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();

            var reportA = ArenaSimulation.Run(scenarioA, world.AnchorTable!, dataSources);
            var reportB = ArenaSimulation.Run(scenarioB, world.AnchorTable!, dataSources);

            Assert.NotEqual(reportA.ToJson(), reportB.ToJson());
        }

        // ===== 消费方反馈第 50 条（CancellationToken/IProgress）=====

        private static (Core.Sim.HeadlessWorld World, ScenarioDef Scenario, System.Collections.Generic.IReadOnlyList<IDataSource> DataSources)
            BuildSmallScenario(int runs)
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_arena_matrix")).WithRuns(runs);
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
                () => ArenaSimulation.Run(scenario, world.AnchorTable!, dataSources, failOnUnknownTable: false, cts.Token));
        }

        [Fact]
        public void Run_CancelledMidway_ViaProgressCallback_ThrowsAndProducesNoResult()
        {
            var (world, scenario, dataSources) = BuildSmallScenario(runs: 1);
            using var cts = new CancellationTokenSource();
            var progress = new RecordingProgress(p =>
            {
                if (p.Completed >= 2) cts.Cancel();
            });

            Assert.Throws<OperationCanceledException>(
                () => ArenaSimulation.Run(scenario, world.AnchorTable!, dataSources, failOnUnknownTable: false, cts.Token, progress));
        }

        [Fact]
        public void Run_NotCancelled_MatchesOldOverload_SameSeed()
        {
            var (world, scenario, dataSources) = BuildSmallScenario(runs: 1);

            var legacy = ArenaSimulation.Run(scenario, world.AnchorTable!, dataSources);
            var viaNewOverload = ArenaSimulation.Run(
                scenario, world.AnchorTable!, dataSources, failOnUnknownTable: false, CancellationToken.None, progress: null);

            Assert.Equal(legacy.ToJson(), viaNewOverload.ToJson());
        }

        [Fact]
        public void Run_Progress_ReportsOncePerCell_MonotonicAndEndsAtTotal()
        {
            var (world, scenario, dataSources) = BuildSmallScenario(runs: 1);
            var expectedTotal = scenario.Levels.Count * scenario.Opponent.LevelOffsets.Count;
            var reports = new List<SimProgress>();

            ArenaSimulation.Run(
                scenario, world.AnchorTable!, dataSources, failOnUnknownTable: false, CancellationToken.None,
                new RecordingProgress(reports.Add));

            Assert.Equal(expectedTotal, reports.Count);
            for (var i = 1; i < reports.Count; i++)
            {
                Assert.True(reports[i].Completed >= reports[i - 1].Completed);
            }
            Assert.Equal(expectedTotal, reports[^1].Total);
            Assert.Equal(reports[^1].Total, reports[^1].Completed);
            Assert.All(reports, r => Assert.Equal(ArenaSimulation.ProgressStageCell, r.Stage));
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
