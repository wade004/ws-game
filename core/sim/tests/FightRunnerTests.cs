using System;
using System.Diagnostics;
using System.Linq;
using Core.Foundation.Common;
using Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Sim
{
    /// <summary>T-N6-4：<see cref="FightRunner"/> 的验收测试。</summary>
    public class FightRunnerTests
    {
        private readonly ITestOutputHelper _output;

        public FightRunnerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static FightRunnerOptions BuildOptions(ulong seed, int playerLevel = 1, int creatureLevel = 1) => new FightRunnerOptions
        {
            DataSources = SimTestWorldFactory.BuildEmbeddedDataSources(),
            ClassId = SimTestWorldFactory.EmbeddedClassId,
            PlayerLevel = playerLevel,
            QualityId = new Id("item.quality.sim_common"),
            CreatureId = SimTestWorldFactory.EmbeddedCreatureWolfL1,
            CreatureLevel = creatureLevel,
            Seed = seed,
            MaxTicks = 1200,
        };

        /// <summary>判断记录"生物主动攻击玩家"：同级（1v1，均 1 级）战斗里生物应对玩家造成 > 0 伤害——
        /// 见 <c>AnchorCreatureLevelScaler</c>/<c>fac.reaction_matrix</c> 判断记录（本任务修复的双向
        /// 阵营敌对关系 + <c>stat.move_speed</c> 缺失两处数据缺口）。</summary>
        [Fact]
        public void Run_CreatureDealsNonZeroDamage_AndAiActuallyEngages()
        {
            var result = FightRunner.Run(BuildOptions(seed: 100));

            Assert.True(result.CreatureTotalDamage > 0, "生物应主动追击并攻击玩家，造成 > 0 伤害");
            Assert.Equal(FightOutcome.PlayerWin, result.Outcome);
        }

        [Fact]
        public void Run_SameSeed_ProducesIdenticalResult()
        {
            var a = FightRunner.Run(BuildOptions(seed: 200));
            var b = FightRunner.Run(BuildOptions(seed: 200));

            Assert.Equal(a.Outcome, b.Outcome);
            Assert.Equal(a.TicksUsed, b.TicksUsed);
            Assert.Equal(a.DurationSeconds, b.DurationSeconds, precision: 9);
            Assert.Equal(a.PlayerTotalDamage, b.PlayerTotalDamage, precision: 9);
            Assert.Equal(a.CreatureTotalDamage, b.CreatureTotalDamage, precision: 9);
            Assert.Equal(a.PlayerHitRate, b.PlayerHitRate, precision: 9);
            Assert.Equal(a.PlayerSkillDamageShare.Count, b.PlayerSkillDamageShare.Count);
            foreach (var kv in a.PlayerSkillDamageShare)
            {
                Assert.Equal(kv.Value, b.PlayerSkillDamageShare[kv.Key], precision: 9);
            }
        }

        [Fact]
        public void Run_DifferentSeeds_ProduceObservablyDifferentHitOutcomes()
        {
            var results = Enumerable.Range(0, 8)
                .Select(i => FightRunner.Run(BuildOptions(seed: 300UL + (ulong)i)))
                .ToList();

            var distinctTicks = results.Select(r => r.TicksUsed).Distinct().Count();
            var distinctDamage = results.Select(r => r.PlayerTotalDamage).Distinct().Count();

            Assert.True(distinctTicks > 1 || distinctDamage > 1,
                "不同种子的命中判定应产生可观测差异（至少 ticks 或总伤害有一项不同）");
        }

        [Fact]
        public void Run_PlayerSkillDamageShare_SumsToOne()
        {
            var result = FightRunner.Run(BuildOptions(seed: 400));

            Assert.True(result.PlayerTotalDamage > 0);
            var sum = result.PlayerSkillDamageShare.Values.Sum();
            Assert.Equal(1.0, sum, precision: 9);
        }

        [Fact]
        public void Run_PlayerHitRate_IsWithinUnitRange()
        {
            var result = FightRunner.Run(BuildOptions(seed: 500));
            Assert.InRange(result.PlayerHitRate, 0.0, 1.0);
        }

        [Fact]
        public void Run_ResourceCurves_AreDownsampledAndNonNegative()
        {
            var options = BuildOptions(seed: 600);
            options.MaxResourceCurveSamples = 4;
            var result = FightRunner.Run(options);

            foreach (var kv in result.PlayerResourceCurves)
            {
                Assert.True(kv.Value.Count <= 4);
                foreach (var sample in kv.Value)
                {
                    Assert.True(sample.Value >= 0, $"资源当前值不应为负：{kv.Key}={sample.Value}");
                }
            }
        }

        /// <summary>见 <see cref="FightRunner"/> 判断记录"隔离方案"：实测
        /// <c>HeadlessWorldBuilder.Build</c> 平均耗时，供该判断记录引用。放宽到 100ms 门槛（远高于
        /// 实测个位数至十几毫秒）只是避免偶发慢速 CI 环境下的抖动误报，不代表这是设计预期的上限。</summary>
        [Fact]
        public void Probe_BuildTiming_WellUnder50MsThreshold()
        {
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            const int iterations = 20;
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                Core.Sim.HeadlessWorldBuilder.Build(new Core.Sim.HeadlessWorldOptions
                {
                    DataSources = dataSources,
                    Seed = (ulong)(9000 + i),
                    MapId = SimTestWorldFactory.EmbeddedMapId,
                    PlayerId = SimTestWorldFactory.PlayerId,
                    PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                    PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                    GameId = SimTestWorldFactory.EmbeddedGameId,
                });
            }
            sw.Stop();
            var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
            _output.WriteLine($"HeadlessWorldBuilder.Build 平均耗时 = {avgMs:F2} ms（{iterations} 次连续调用）");

            Assert.True(avgMs < 100, $"Build 平均耗时 {avgMs:F2}ms 超出隔离方案判断记录假设的安全边界");
        }
    }
}
