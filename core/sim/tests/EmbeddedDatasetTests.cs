using System.Linq;
using Core.Foundation.Common;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// T-N6-2b：嵌入式最小仿真数据集（<c>core/sim/tests/data</c>，见该目录 README.md）的装配/内容
    /// 断言——数据本身的合法性已由 <c>toolchain/validate_data.py --strict --framework-root
    /// data/_framework --data-root core/sim/tests/data</c>（errors 0）覆盖，本文件只断言任务书列出的
    /// 几条"能被装配根消费"的验收点：装载零阻断、<see cref="Core.Sim.AnchorTable"/> 1～20 级连续、
    /// 三个场景可取、生物/物品模板档位数量、标准职业学技能后能打死一只 1 级普通怪且玩家获胜、同种子
    /// 两次逐 tick 一致。
    /// </summary>
    public sealed class EmbeddedDatasetTests
    {
        [Fact]
        public void Build_WithEmbeddedDataset_LoadsWithoutBlockingIssues()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916100UL);

            Assert.False(world.LoadReport.IsBlocking);
        }

        [Fact]
        public void AnchorTable_HasAllTwentyLevelsContinuous()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916101UL);

            Assert.NotNull(world.AnchorTable);
            Assert.Equal(20, world.AnchorTable!.MaxLevel);
            for (var level = 1; level <= 20; level++)
            {
                Assert.True(world.AnchorTable.TryGet(level, out _), $"sim.anchor 应含 level={level}");
            }
        }

        [Fact]
        public void ScenarioCatalog_HasArenaGrowthAndCoverageScenarios()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916102UL);

            Assert.NotNull(world.ScenarioCatalog!);
            Assert.Equal(3, world.ScenarioCatalog!.All.Count);

            Assert.True(world.ScenarioCatalog.TryGet(new Id("sim.scenario.sim_arena_matrix"), out var arena));
            Assert.Equal(Core.Sim.ScenarioKind.Arena, arena.Kind);

            Assert.True(world.ScenarioCatalog.TryGet(new Id("sim.scenario.sim_growth_full"), out var growth));
            Assert.Equal(Core.Sim.ScenarioKind.Growth, growth.Kind);
            Assert.Equal(1, growth.LevelFrom);
            Assert.Equal(20, growth.LevelTo);

            Assert.True(world.ScenarioCatalog.TryGet(new Id("sim.scenario.sim_coverage_all"), out var coverage));
            Assert.Equal(Core.Sim.ScenarioKind.Coverage, coverage.Kind);

            Assert.Single(world.ScenarioCatalog.ByKind(Core.Sim.ScenarioKind.Arena));
            Assert.Single(world.ScenarioCatalog.ByKind(Core.Sim.ScenarioKind.Growth));
            Assert.Single(world.ScenarioCatalog.ByKind(Core.Sim.ScenarioKind.Coverage));
        }

        [Fact]
        public void CreatureTemplate_HasAtLeastFiveLevelTiers()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916103UL);

            var templates = world.Registry.GetAll("creature.template");
            Assert.True(templates.Count >= 5, $"creature.template 行数应 >= 5，实际 {templates.Count}");

            var normalCount = templates.Count(r =>
                r.TryGetString("tier", out var tier) && tier == "creature.tier.sim_normal");
            Assert.True(normalCount >= 5, $"普通怪模板应 >= 5 档，实际 {normalCount}");
        }

        [Fact]
        public void ItemTemplate_CoversAllLevelTiersQualitiesAndSlots()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916104UL);

            var templates = world.Registry.GetAll("item.template");
            var slots = world.Registry.GetAll("item.slot_definition").Count;
            var qualities = world.Registry.GetAll("item.quality_definition").Count;
            var levelTiers = templates
                .Select(r => r.TryGetInt("item_level", out var lvl) ? (int)lvl : -1)
                .Distinct()
                .Count();

            Assert.True(levelTiers >= 5, $"item.template 覆盖的物品等级档位应 >= 5，实际 {levelTiers}");
            Assert.Equal(2, qualities);
            Assert.Equal(5, slots);
            Assert.Equal(levelTiers * qualities * slots, templates.Count);
            Assert.True(templates.Count >= 50, $"item.template 行数应 >= 50，实际 {templates.Count}");
        }

        [Fact]
        public void StandardWarrior_LearnsSkillAtLevelOne_AndDefeatsNormalCreature()
        {
            var result = SimTestWorldFactory.RunEmbeddedFightScript(seed: 20260916105UL, playerLevel: 1);

            Assert.True(result.KnownSkillCount >= 1, "1 级玩家 LearnFromBook 后应至少学到一个技能");
            Assert.True(result.TargetDied, "1 级普通怪应在 maxAttempts 次施法内死亡");
            Assert.True(result.PlayerAlive, "战斗结束时玩家应仍存活（玩家获胜）");
        }

        [Fact]
        public void RunEmbeddedFightScript_SameSeed_ProducesIdenticalTickSnapshots()
        {
            var a = SimTestWorldFactory.RunEmbeddedFightScript(seed: 20260916106UL, playerLevel: 1);
            var b = SimTestWorldFactory.RunEmbeddedFightScript(seed: 20260916106UL, playerLevel: 1);

            Assert.True(a.TargetDied);
            Assert.True(b.TargetDied);
            Assert.Equal(a.TicksUsed, b.TicksUsed);
            Assert.Equal(a.TickSnapshots.Count, b.TickSnapshots.Count);
            for (var i = 0; i < a.TickSnapshots.Count; i++)
            {
                Assert.Equal(a.TickSnapshots[i], b.TickSnapshots[i]);
            }
        }
    }
}
