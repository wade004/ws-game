using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Core.Foundation.Common;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// <see cref="FightOutcome"/> 全部取值的直接用例、<see cref="FightRunner"/> 入口守卫，以及
    /// <see cref="GrowthSimulation.MaxKillsPerLevelSafety"/> 安全阀分支（T-M5 / T-M15 sim 半，2026-10-01 测试覆盖第四批）。
    /// 此前 <c>FightOutcome.CreatureWin/Draw/Timeout</c> 与安全阀没有任何用例。
    /// </summary>
    public sealed class SimFightOutcomeAndSafetyTests
    {
        private static readonly Id QualityCommon = new Id("item.quality.sim_common");

        private sealed class Setup
        {
            public Core.Sim.HeadlessWorld World = null!;
            public FightRunner.FightAccumulator Accumulator = null!;
            public Id CreatureId;
            public Id RotationId;
        }

        /// <summary>装配一个已有玩家与一只同级狼的世界，不开打：调用方可在 <see cref="FightRunner.RunWithinWorld(HeadlessWorld,FightRunner.FightAccumulator,Id,Id,Id,double,int,double,int)"/>
        /// 之前把任一方置为死亡，精确构造"某一 tick 入口处的存活状态"，而不依赖战斗随机性。</summary>
        private static Setup NewSetup()
        {
            var accumulator = new FightRunner.FightAccumulator();
            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = SimTestWorldFactory.BuildEmbeddedDataSources(),
                Seed = 31UL,
                MapId = SimTestWorldFactory.EmbeddedMapId,
                PlayerId = SimTestWorldFactory.PlayerId,
                PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                GameId = SimTestWorldFactory.EmbeddedGameId,
                CombatOptions = accumulator.CombatOptions,
            });
            var standardPlayer = StandardPlayerBuilder.Build(world, SimTestWorldFactory.EmbeddedClassId, 1, QualityCommon);
            var position = new Vec2(3, 0);
            var creatureId = world.Gameplay.Carriers.Creatures.Spawn(
                SimTestWorldFactory.EmbeddedCreatureWolfL1, SimTestWorldFactory.EmbeddedMapId, position, facing: Math.PI);
            world.Spatial.Register(creatureId, position, 0.5);
            return new Setup { World = world, Accumulator = accumulator, CreatureId = creatureId, RotationId = standardPlayer.RotationId };
        }

        private static FightResult Fight(Setup s, int maxTicks) =>
            FightRunner.RunWithinWorld(
                s.World, s.Accumulator, SimTestWorldFactory.PlayerId, s.CreatureId, s.RotationId,
                stepSeconds: SimTestWorldFactory.StepSeconds, maxTicks: maxTicks,
                moveSpeed: SimpleMoveModel.DefaultMoveSpeed, maxResourceCurveSamples: 4);

        // -----------------------------------------------------------------
        // FightOutcome：每个取值都由入口处的存活状态唯一决定
        // -----------------------------------------------------------------

        [Fact]
        public void Outcome_PlayerDead_CreatureAlive_IsCreatureWin_WithZeroTicks()
        {
            var s = NewSetup();
            s.World.Gameplay.Carriers.Units.SetAlive(SimTestWorldFactory.PlayerId, false);

            var result = Fight(s, maxTicks: 50);

            Assert.Equal(FightOutcome.CreatureWin, result.Outcome);
            Assert.Equal(0, result.TicksUsed);
            Assert.Equal(0.0, result.DurationSeconds);
        }

        [Fact]
        public void Outcome_BothDead_IsDraw_WithZeroTicks()
        {
            var s = NewSetup();
            s.World.Gameplay.Carriers.Units.SetAlive(SimTestWorldFactory.PlayerId, false);
            s.World.Gameplay.Carriers.Units.SetAlive(s.CreatureId, false);

            var result = Fight(s, maxTicks: 50);

            Assert.Equal(FightOutcome.Draw, result.Outcome);
            Assert.Equal(0, result.TicksUsed);
        }

        [Fact]
        public void Outcome_CreatureDead_PlayerAlive_IsPlayerWin_WithZeroTicks()
        {
            var s = NewSetup();
            s.World.Gameplay.Carriers.Units.SetAlive(s.CreatureId, false);

            var result = Fight(s, maxTicks: 50);

            Assert.Equal(FightOutcome.PlayerWin, result.Outcome);
            Assert.Equal(0, result.TicksUsed);
        }

        [Fact]
        public void Outcome_NoTicksAllowed_BothAlive_IsTimeout()
        {
            var s = NewSetup();

            var result = Fight(s, maxTicks: 0);

            Assert.Equal(FightOutcome.Timeout, result.Outcome);
            Assert.Equal(0, result.TicksUsed);
        }

        [Fact]
        public void Outcome_TimeoutLoopEnd_ReclassifiesByFinalAliveState()
        {
            // maxTicks 内没分出胜负（1 tick 不足以结束战斗）：仍按结束时的存活状态判 Timeout。
            var s = NewSetup();

            var result = Fight(s, maxTicks: 1);

            Assert.Equal(FightOutcome.Timeout, result.Outcome);
            Assert.Equal(1, result.TicksUsed);
            Assert.Equal(1 * SimTestWorldFactory.StepSeconds, result.DurationSeconds);
        }

        [Fact]
        public void Outcome_FightOutcomeEnum_HasTheFourDocumentedMembers()
        {
            Assert.Equal(
                new[] { FightOutcome.PlayerWin, FightOutcome.CreatureWin, FightOutcome.Draw, FightOutcome.Timeout },
                (FightOutcome[])Enum.GetValues(typeof(FightOutcome)));
        }

        [Fact]
        public void Run_LevelOnePlayerAgainstFarHigherLevelCreature_EndsInCreatureWin()
        {
            // 自然路径：玩家 1 级、生物 20 级（锚点缩放后血量/伤害都高出数量级），生物应在超时前击杀玩家。
            var result = FightRunner.Run(new FightRunnerOptions
            {
                DataSources = SimTestWorldFactory.BuildEmbeddedDataSources(),
                ClassId = SimTestWorldFactory.EmbeddedClassId,
                PlayerLevel = 1,
                QualityId = QualityCommon,
                CreatureId = SimTestWorldFactory.EmbeddedCreatureWolfL1,
                CreatureLevel = 20,
                Seed = 77UL,
                MaxTicks = 2400,
            });

            Assert.Equal(FightOutcome.CreatureWin, result.Outcome);
            Assert.True(result.CreatureTotalDamage > 0);
        }

        // -----------------------------------------------------------------
        // FightRunner 入口守卫
        // -----------------------------------------------------------------

        [Fact]
        public void Run_NullOptions_ThrowsArgumentNullException()
        {
            Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => FightRunner.Run(null!)).ParamName);
            Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => FightRunner.Run(null!, CancellationToken.None)).ParamName);
        }

        [Fact]
        public void RunWithinWorld_NullWorldOrAccumulator_ThrowsArgumentNullException()
        {
            var s = NewSetup();
            var player = SimTestWorldFactory.PlayerId;

            Assert.Equal("world", Assert.Throws<ArgumentNullException>(() =>
                FightRunner.RunWithinWorld(null!, s.Accumulator, player, s.CreatureId, s.RotationId, 0.5, 5, 1.0, 4)).ParamName);
            Assert.Equal("accumulator", Assert.Throws<ArgumentNullException>(() =>
                FightRunner.RunWithinWorld(s.World, null!, player, s.CreatureId, s.RotationId, 0.5, 5, 1.0, 4)).ParamName);
        }

        // -----------------------------------------------------------------
        // GrowthSimulation.MaxKillsPerLevelSafety
        // -----------------------------------------------------------------

        /// <summary>把嵌入式等级曲线里全部非零的 <c>xp_to_next</c> 改成天文数字（保持"沿等级不递减"的校验规则成立）：
        /// 击杀经验再多也升不了级，成长仿真只能靠安全阀退出本级。</summary>
        private static string MakeLevelCurveUnreachable(string relativePath, string text)
        {
            if (!relativePath.EndsWith("prog/prog.level_curve.json", StringComparison.Ordinal))
            {
                return text;
            }

            return Regex.Replace(text, "\"xp_to_next\":\\s*([1-9][0-9]*)", "\"xp_to_next\": 1000000000");
        }

        [Fact]
        public void Growth_LevelThatNeverLevelsUp_StopsAtTheSafetyValve_AndRecordsWhatHappened()
        {
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSourcesWithEdit(MakeLevelCurveUnreachable);
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_growth_full")).WithRuns(1);

            var report = GrowthSimulation.Run(scenario, world.AnchorTable!, dataSources);

            // 本级一直打赢但永远升不了级：恰好打满安全阀次数后中止，不继续尝试后续等级，也没有无限循环。
            var sample = Assert.Single(report.Levels);
            Assert.Equal(scenario.LevelFrom, sample.Level);
            Assert.Equal(GrowthSimulation.MaxKillsPerLevelSafety, sample.Kills);
            Assert.True(sample.ActualDurationSeconds > 0);
        }

        [Fact]
        public void Growth_SafetyValveConstant_IsPositive_AndDocumentedValue()
        {
            Assert.True(GrowthSimulation.MaxKillsPerLevelSafety > 0);
            Assert.Equal(400, GrowthSimulation.MaxKillsPerLevelSafety);
        }
    }
}
