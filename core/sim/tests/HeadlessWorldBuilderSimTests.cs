using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// T-N6-2a 任务书验收 6：<see cref="Core.Sim.HeadlessWorldBuilder.Build"/> 用
    /// <c>data/_framework</c> + <c>data/_sample</c> 构建后，<see cref="Core.Sim.HeadlessWorld.AnchorTable"/>
    /// 应有 5 行（<c>data/_sample/sim/sim.anchor.json</c> 五级示例数据）、
    /// <see cref="Core.Sim.HeadlessWorld.ScenarioCatalog"/> 应有 1 个场景
    /// （<c>data/_sample/sim/sim.scenario.json</c> 的 <c>sim.scenario.sample_arena</c>）。
    /// </summary>
    public sealed class HeadlessWorldBuilderSimTests
    {
        [Fact]
        public void Build_WithFrameworkAndSampleData_PopulatesAnchorTableAndScenarioCatalog()
        {
            var world = SimTestWorldFactory.BuildWorld(seed: 20260916003UL);

            Assert.NotNull(world.AnchorTable);
            Assert.Equal(5, world.AnchorTable!.MaxLevel);
            for (var level = 1; level <= 5; level++)
            {
                Assert.True(world.AnchorTable.TryGet(level, out _), $"sim.anchor 应含 level={level}");
            }

            Assert.NotNull(world.ScenarioCatalog);
            Assert.Single(world.ScenarioCatalog!.All);
            Assert.Equal("sim.scenario.sample_arena", world.ScenarioCatalog.All[0].Id.Value);
            Assert.Equal(Core.Sim.ScenarioKind.Arena, world.ScenarioCatalog.All[0].Kind);
        }
    }
}
