using System;
using System.Diagnostics;
using System.Linq;
using Core.Foundation.Common;
using Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Sim
{
    /// <summary>T-N6-5：跑一次完整的 <c>sim.scenario.sim_growth_full</c>（<c>runs=5</c>，1→20 级全程
    /// 成长），供本测试类的全部 <c>FullScenario_*</c> 用例共享同一份结果——惯例同
    /// <see cref="FullArenaScenarioFixture"/>（任务书"至少一条用完整场景定义并打印耗时"，不是每个
    /// 断言各自重新跑一遍）。</summary>
    public sealed class FullGrowthScenarioFixture
    {
        public Core.Sim.HeadlessWorld World { get; }
        public ScenarioDef Scenario { get; }
        public GrowthReport Report { get; }
        public double ElapsedSeconds { get; }

        public FullGrowthScenarioFixture()
        {
            World = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            Scenario = World.ScenarioCatalog!.Get(new Id("sim.scenario.sim_growth_full"));
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();

            var sw = Stopwatch.StartNew();
            Report = GrowthSimulation.Run(Scenario, World.AnchorTable!, dataSources);
            sw.Stop();
            ElapsedSeconds = sw.Elapsed.TotalSeconds;
        }
    }

    /// <summary>T-N6-5：<see cref="GrowthSimulation"/> 的验收测试。</summary>
    public class GrowthSimulationTests : IClassFixture<FullGrowthScenarioFixture>
    {
        private readonly ITestOutputHelper _output;
        private readonly FullGrowthScenarioFixture _fixture;

        public GrowthSimulationTests(FullGrowthScenarioFixture fixture, ITestOutputHelper output)
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
                $"完整场景 {scenario.Id} 共 {report.Levels.Count} 个等级样本（level_from={scenario.LevelFrom} " +
                $"level_to={scenario.LevelTo} runs={scenario.Runs}），总耗时 {_fixture.ElapsedSeconds:F2}s");

            foreach (var sample in report.Levels)
            {
                _output.WriteLine(
                    $"  L{sample.Level}: kills={sample.Kills} " +
                    $"duration={sample.ActualDurationSeconds:F1}s(期望{sample.ExpectedDurationSeconds:F1}s dev={sample.DurationDeviation:P0} pass={sample.DurationPass}) " +
                    $"item_lv={sample.AvgItemLevel:F1}(期望{sample.ExpectedItemLevel:F1} dev={sample.ItemLevelDeviation:P0} pass={sample.ItemLevelPass}) " +
                    $"hit_rate={sample.HitRate:P0}(期望{sample.ExpectedHitRate:P0} dev={sample.HitRateDeviation:P0} pass={sample.HitRatePass}) " +
                    $"gold={sample.CumulativeGold:F0}(期望{sample.ExpectedCumulativeGold:F0} dev={sample.GoldDeviation:P0} pass={sample.GoldPass})");
            }

            Assert.True(report.Levels.Count >= (scenario.LevelTo!.Value - scenario.LevelFrom!.Value) - 2,
                $"应有接近 level_to-level_from 个等级样本，实际 {report.Levels.Count}（提前终止容忍最多缺 2 个）");
        }

        /// <summary>验收 3：四条轨迹逐级在带宽内（带宽 ≤ 0.35，见 <c>sim.scenario.sim_growth_full
        /// .bandwidths</c>）。</summary>
        [Fact]
        public void FullScenario_FourTrajectories_WithinBandwidth()
        {
            var report = _fixture.Report;
            Assert.True(report.Levels.Count > 0, "成长仿真应至少产出一个等级样本");

            var durationFails = report.Levels.Where(l => !l.DurationPass).ToList();
            var itemLevelFails = report.Levels.Where(l => !l.ItemLevelPass).ToList();
            var hitRateFails = report.Levels.Where(l => !l.HitRatePass).ToList();
            var goldFails = report.Levels.Where(l => !l.GoldPass).ToList();

            void ReportFails(string name, System.Collections.Generic.List<GrowthLevelSample> fails)
            {
                foreach (var f in fails)
                {
                    _output.WriteLine($"  [{name} 未达标] L{f.Level}");
                }
            }
            ReportFails("时长", durationFails);
            ReportFails("装备等级", itemLevelFails);
            ReportFails("命中率", hitRateFails);
            ReportFails("金币", goldFails);

            Assert.True(durationFails.Count == 0, $"每级时长应逐级在带宽内，未达标 {durationFails.Count}/{report.Levels.Count}");
            Assert.True(itemLevelFails.Count == 0, $"装备等级轨迹应逐级在带宽内，未达标 {itemLevelFails.Count}/{report.Levels.Count}");
            Assert.True(hitRateFails.Count == 0, $"命中率轨迹应逐级在带宽内，未达标 {hitRateFails.Count}/{report.Levels.Count}");
            Assert.True(goldFails.Count == 0, $"金币累积轨迹应逐级在带宽内，未达标 {goldFails.Count}/{report.Levels.Count}");
        }

        /// <summary>经验/金币走真实模块，不是本运行器手算：同一次运行内两条独立路径（API 返回值求和
        /// vs 事件流求和）应逐值相等，见 <see cref="GrowthReport"/> 判断记录"经验/金币的双路径
        /// 自证"。</summary>
        [Fact]
        public void FullScenario_XpAndGold_ApiAndEventPathsAgree()
        {
            var report = _fixture.Report;
            Assert.Equal(report.CumulativeXpGrantedViaApi, report.CumulativeXpGrantedViaEvents, precision: 6);
            Assert.Equal(report.CumulativeGoldViaBalance, report.CumulativeGoldViaEvents, precision: 6);
            Assert.True(report.CumulativeXpGrantedViaApi > 0, "整条成长轨迹应产生真实经验发放");
            Assert.True(report.CumulativeGoldViaBalance > 0, "整条成长轨迹应产生真实金币入账");
        }

        // -----------------------------------------------------------------
        // 确定性 + 单种子白盒检查：用缩小的合成小场景（不复用完整 sim_growth_full，控制时长）
        // -----------------------------------------------------------------

        private static (Core.Sim.HeadlessWorld World, ScenarioDef Scenario) BuildSyntheticGrowthScenarioViaOverlay(ulong baseSeed)
        {
            var syntheticScenarioJson = @"{
  ""table"": ""sim.scenario"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""sim.scenario.sim_growth_test_small"",
      ""kind"": ""growth"",
      ""player"": { ""class_id"": ""arch.class.sim_warrior"", ""level"": 1, ""quality_id"": ""item.quality.sim_common"" },
      ""opponent"": { ""creature_id"": ""creature.sim_wolf_l1"", ""tier_id"": ""creature.tier.sim_normal"" },
      ""level_from"": 1,
      ""level_to"": 3,
      ""runs"": 2,
      ""base_seed"": " + baseSeed + @",
      ""max_ticks"": 1200,
      ""bandwidths"": { ""level_duration"": 0.35, ""item_level"": 0.35, ""hit_rate"": 0.35, ""gold"": 0.35 }
    }
  ]
}";

            var fs = new Adapters.Stub.StubFileSystem();
            fs.WriteTextAtomic($"sim_growth_test_overlay_{baseSeed}/sim/sim.scenario.json", syntheticScenarioJson);
            var overlaySource = new Core.Foundation.DataRegistry.FileSystemDataSource(fs, $"sim_growth_test_overlay_{baseSeed}");

            var frameworkAndEmbedded = SimTestWorldFactory.BuildEmbeddedDataSources();
            var allSources = frameworkAndEmbedded.Concat(new Core.Foundation.DataRegistry.IDataSource[] { overlaySource }).ToList();

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

            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_growth_test_small"));
            return (world, scenario);
        }

        [Fact]
        public void ToJson_SameBaseSeed_TwoRuns_ByteIdentical()
        {
            var (world, scenario) = BuildSyntheticGrowthScenarioViaOverlay(baseSeed: 990001);
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();

            var report1 = GrowthSimulation.Run(scenario, world.AnchorTable!, dataSources);
            var report2 = GrowthSimulation.Run(scenario, world.AnchorTable!, dataSources);

            Assert.Equal(report1.ToJson(), report2.ToJson());
        }

        [Fact]
        public void ToJson_DifferentBaseSeed_ProducesDifferentJson()
        {
            var (worldA, scenarioA) = BuildSyntheticGrowthScenarioViaOverlay(baseSeed: 990002);
            var (worldB, scenarioB) = BuildSyntheticGrowthScenarioViaOverlay(baseSeed: 990003);
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();

            var reportA = GrowthSimulation.Run(scenarioA, worldA.AnchorTable!, dataSources);
            var reportB = GrowthSimulation.Run(scenarioB, worldB.AnchorTable!, dataSources);

            Assert.NotEqual(reportA.ToJson(), reportB.ToJson());
        }

        /// <summary>联动重算：对每个 L，<c>prog.level_curve.sim_warrior.entries[level].xp_to_next</c>
        /// 应等于数值总纲 4.7 节公式独立算出的值（本测试独立按公式计算，不读取本次改动写入的数据，
        /// 避免"改了公式又用同一份代码算一遍验证自己"这种自证循环）。</summary>
        [Fact]
        public void LevelCurve_XpToNext_MatchesFormula_ForEveryLevel()
        {
            var world = _fixture.World;
            var anchors = world.AnchorTable!;
            var levelCurveRecord = world.Registry.Get("prog.level_curve", new Id("prog.level_curve.sim_warrior"));
            Assert.NotNull(levelCurveRecord);
            Assert.True(levelCurveRecord!.TryGetArray("entries", out var entries));

            foreach (var raw in entries)
            {
                if (raw is not Core.Foundation.Common.Json.JsonObject obj) continue;
                var level = (int)((Core.Foundation.Common.Json.JsonNumber)obj["level"]).Value;
                var xpToNext = (long)((Core.Foundation.Common.Json.JsonNumber)obj["xp_to_next"]).Value;

                if (level >= 20)
                {
                    Assert.Equal(0, xpToNext);
                    continue;
                }

                var anchor = anchors.Get(level);
                var killXpBase = 20 + 2 * (level - 1); // prog.xp_base_curve.sim_default 的闭式公式（本表在 1/5/10/15/20 断点线性插值，对这条线性公式在整数等级上精确重现）。
                var monsterEquivalent = anchor.LevelDurationSeconds / (anchor.TtkSeconds + anchor.KillIntervalSeconds);
                var expected = killXpBase * monsterEquivalent * (1 + anchor.QuestShare);

                Assert.Equal(Math.Round(expected), xpToNext, precision: 0);
            }
        }
    }
}
