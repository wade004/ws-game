using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Foundation.Common;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>消费方反馈第 51 条（2026-09-17，core/sim/README.md"线程安全与并发"章节新增承诺）：
    /// 四个 <c>Run</c> 入口本身无静态可变状态、两次独立调用之间互不共享任何框架侧对象——本类型用
    /// "并行跑 N 个相同种子的 Run，结果与串行逐字段（经 <c>ToJson()</c>）一致"验证这条承诺，不是
    /// 只验证"没有抛异常"（并发下的数据竞争经常表现为结果偶发性错误而不是崩溃，逐字段/逐字节比对
    /// 才是真正有意义的断言）。四个场景都用最小参数（<c>runs=1</c> 或单场战斗）控制总耗时，多次
    /// 独立调用共享同一份 <c>dataSources</c>/<c>anchors</c>/<c>scenario</c> 只读输入实例（同一进程内
    /// 的多个线程），符合 README 判断记录"调用方须保证参数对象在调用期间不被其它线程修改"的前提
    /// （本测试从不修改这些输入，只并发读取）。</summary>
    public sealed class ConcurrencyTests
    {
        private const int Degree = 8;

        [Fact]
        public void FightRunner_Run_ParallelSameSeed_MatchesSerialResult()
        {
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            FightRunnerOptions BuildOptions() => new FightRunnerOptions
            {
                DataSources = dataSources,
                ClassId = SimTestWorldFactory.EmbeddedClassId,
                PlayerLevel = 1,
                QualityId = new Id("item.quality.sim_common"),
                CreatureId = SimTestWorldFactory.EmbeddedCreatureWolfL1,
                CreatureLevel = 1,
                Seed = 9001,
                MaxTicks = 1200,
            };

            var reference = FightRunner.Run(BuildOptions());

            var parallelResults = new FightResult[Degree];
            Parallel.For(0, Degree, i => { parallelResults[i] = FightRunner.Run(BuildOptions()); });

            foreach (var result in parallelResults)
            {
                Assert.Equal(reference.Outcome, result.Outcome);
                Assert.Equal(reference.TicksUsed, result.TicksUsed);
                Assert.Equal(reference.PlayerTotalDamage, result.PlayerTotalDamage, precision: 9);
                Assert.Equal(reference.CreatureTotalDamage, result.CreatureTotalDamage, precision: 9);
                Assert.Equal(reference.PlayerHitRate, result.PlayerHitRate, precision: 9);
            }
        }

        [Fact]
        public void GrowthSimulation_Run_ParallelSameSeed_MatchesSerialResult()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_growth_full")).WithRuns(1);
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var anchors = world.AnchorTable!;

            var reference = GrowthSimulation.Run(scenario, anchors, dataSources).ToJson();

            var parallelJson = new string[Degree];
            Parallel.For(0, Degree, i => { parallelJson[i] = GrowthSimulation.Run(scenario, anchors, dataSources).ToJson(); });

            Assert.All(parallelJson, json => Assert.Equal(reference, json));
        }

        [Fact]
        public void ArenaSimulation_Run_ParallelSameSeed_MatchesSerialResult()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_arena_matrix")).WithRuns(1);
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var anchors = world.AnchorTable!;

            var reference = ArenaSimulation.Run(scenario, anchors, dataSources).ToJson();

            var parallelJson = new string[Degree];
            Parallel.For(0, Degree, i => { parallelJson[i] = ArenaSimulation.Run(scenario, anchors, dataSources).ToJson(); });

            Assert.All(parallelJson, json => Assert.Equal(reference, json));
        }

        [Fact]
        public void CoverageSimulation_Run_ParallelSameSeed_MatchesSerialResult()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_coverage_all")).WithRuns(1);
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var anchors = world.AnchorTable!;

            var reference = CoverageSimulation.Run(scenario, anchors, dataSources).ToJson();

            var parallelJson = new string[Degree];
            Parallel.For(0, Degree, i => { parallelJson[i] = CoverageSimulation.Run(scenario, anchors, dataSources).ToJson(); });

            Assert.All(parallelJson, json => Assert.Equal(reference, json));
        }
    }
}
