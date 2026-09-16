using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// T-N6-8b（复核建议修复）：逐字复刻 <c>adapters/headless/README.md</c>/
    /// <c>toolchain/registry/manifests/adapter-headless/README.md</c>"数值仿真骨架"一节"无头宿主
    /// 怎么用：装配 + 三类仿真 + 基线比对"示例代码路径的步骤 1（<see cref="HeadlessWorldBuilder.Build"/>）/
    /// 2（<see cref="StandardPlayerBuilder.Build"/>）/3b（<see cref="ArenaSimulation.Run"/>）/
    /// 4（<see cref="SimReport.FromArenaReport"/> + <see cref="SimBaseline"/> +
    /// <see cref="BaselineComparer.Compare"/>），证明修复前那份示例——<c>HeadlessWorld.Options</c>
    /// 不存在的属性、<c>ArenaSimulation.Run(world.Options, scenarioDef)</c> 参数个数/顺序与真实签名
    /// <c>Run(ScenarioDef, AnchorTable, IReadOnlyList&lt;IDataSource&gt;, bool)</c> 不符、
    /// <c>SimReport.FromArenaReport(report, scenario, frameworkVersion: "...")</c> 缺
    /// <c>registry</c> 参数且具名参数拼错——已被替换为真实签名，可编译可运行。步骤 3a
    /// <c>FightRunner.Run(new FightRunnerOptions { /* ... */ })</c> 在两份文档里本就是占位写法
    /// （字段全部省略，不含可编译的真实参数），不是本次修复对象，不在本测试复刻范围。
    /// <para>
    /// 判断记录（与两份 README 同步维护，谁改都要带着另一方一起改）：本文件与两份 README 描述的是
    /// 同一条代码路径，任何一方的公开签名变化（<c>HeadlessWorldOptions</c> 字段、
    /// <c>ArenaSimulation.Run</c>/<c>SimReport.FromArenaReport</c> 参数）都必须让本测试与两份
    /// README 同步更新——本测试挂在 `dotnet test Core.sln`/`check.ps1` 既有门槛下，签名一旦漂移，
    /// 本测试会先于任何人工发现之前编译失败，不依赖人工记得去翻文档核对。
    /// </para>
    /// </summary>
    public class HeadlessReadmeExampleTests
    {
        [Fact]
        public void ReadmeExample_HeadlessWorldBuilderThroughBaselineCompare_CompilesAndRuns()
        {
            IReadOnlyList<IDataSource> dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var mapId = SimTestWorldFactory.EmbeddedMapId;
            var playerClassId = SimTestWorldFactory.EmbeddedClassId;
            var expectedQualityId = new Id("item.quality.sim_common");

            // 1. 构造数据源并装配一整套 L0～L4 世界（复用桩适配层，见 HeadlessWorldOptions 各字段）。
            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = dataSources,
                Seed = 20260916001UL,
                FileSystem = new StubFileSystem(),
                MapId = mapId,
                PlayerClassId = playerClassId,
                PlayerLevel = 20,
            });
            var anchors = world.AnchorTable!;
            var registry = world.Registry;
            // 缩小 runs（惯例同 BaselineComparerTests 判断记录"用缩小的 runs，不是完整场景"）：
            // 只为验证本测试要复刻的代码路径本身能跑通，不重复 ArenaSimulationTests 已覆盖的完整
            // 场景耗时。
            var arenaScenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_arena_matrix")).WithRuns(2);

            // 2. 标准玩家生成器（等级+职业 -> 学技能 -> 按预算生成并装备"标准装"）。
            var player = StandardPlayerBuilder.Build(world, playerClassId, level: 20, expectedQualityId);
            Assert.NotNull(player);

            // 3b. 场景运行器：真实签名 Run(scenario, anchors, dataSources, failOnUnknownTable=false)，
            //     不是修复前误写的 Run(world.Options, scenarioDef)。
            var arenaReport = ArenaSimulation.Run(arenaScenario, anchors, dataSources);

            // 4. 拍平成统一报告信封，与基线比对——FromArenaReport 真实签名需要 registry 与
            //    generatedWithVersion，不是修复前误写的 frameworkVersion 具名参数。
            var report = SimReport.FromArenaReport(arenaReport, arenaScenario, registry, generatedWithVersion: "test");
            var baseline = SimBaseline.FromReport(report);
            var diff = BaselineComparer.Compare(report, baseline, new BaselineCompareOptions());

            Assert.False(diff.HasBlockingDifference);
            Assert.All(diff.Rows, row => Assert.Equal(BaselineDiffStatus.Same, row.Status));
        }
    }
}
