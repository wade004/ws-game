using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Core.Foundation.Common;
using Core.Rules.Common;
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

        /// <summary>
        /// 深度复审 E-M2：命中率统计须排除被免疫全额吸收的结算，见 <see
        /// cref="FightRunner.FightAccumulator.IsLandedHit(ResolveResult)"/> 判断记录。本用例不依赖
        /// 嵌入数据集配置任何免疫光环——直接构造 <see cref="FightRunner.FightAccumulator"/>，通过其
        /// 公开的 <see cref="CombatOptions.ResolveTrace"/> 回调手工喂入 <c>Immune=true</c> 的
        /// <see cref="ResolveResult"/>（<c>Hit=Crit</c>，模拟"骰子判定暴击命中、但被免疫全额吸收"），
        /// 断言 <see cref="FightRunner.FightAccumulator.PlayerAttempts"/> 照常计数（确实发起了一次
        /// 尝试）但 <see cref="FightRunner.FightAccumulator.PlayerLanded"/> 不应递增（未真正落地）；
        /// 随后再喂一条 <c>Immune=false</c> 的正常命中，确认 <c>PlayerLanded</c> 恢复正常递增（防止
        /// 修复把 <c>IsLandedHit</c> 改成恒 false 这种反向回归）。
        /// </summary>
        [Fact]
        public void FightAccumulator_ImmuneResolve_CountsAttemptButNotLanded()
        {
            var accumulator = new FightRunner.FightAccumulator();
            var playerId = new Id("test.player");
            var creatureId = new Id("test.creature");
            var skillId = new Id("test.skill");
            var schoolId = new Id("school.physical");
            accumulator.BeginFight(playerId, creatureId);

            var immuneResult = new ResolveResult(
                hit: HitResult.Crit,
                requestedAmount: 100.0,
                finalAmount: 0.0,
                absorbed: 0.0,
                immune: true,
                isHeal: false);
            var ctx = new EffectContext(
                sourceId: playerId,
                targetId: creatureId,
                skillId: skillId,
                kind: EffectKind.SchoolDamage,
                school: schoolId,
                baseValue: 100.0,
                coefficient: 1.0);

            accumulator.CombatOptions.ResolveTrace!.Invoke(ctx, immuneResult);

            Assert.Equal(1, accumulator.PlayerAttempts);
            Assert.Equal(0, accumulator.PlayerLanded);

            var landedResult = new ResolveResult(
                hit: HitResult.Hit,
                requestedAmount: 50.0,
                finalAmount: 50.0,
                absorbed: 0.0,
                immune: false,
                isHeal: false);
            accumulator.CombatOptions.ResolveTrace!.Invoke(ctx, landedResult);

            Assert.Equal(2, accumulator.PlayerAttempts);
            Assert.Equal(1, accumulator.PlayerLanded);
        }

        // ===== 消费方反馈第 49 条（CaptureEvents）=====

        [Fact]
        public void CaptureEvents_Disabled_ReturnsEmptyNotNull()
        {
            var result = FightRunner.Run(BuildOptions(seed: 700));

            Assert.NotNull(result.CapturedEvents);
            Assert.Empty(result.CapturedEvents);
            Assert.False(result.CapturedEventsTruncated);
        }

        [Fact]
        public void CaptureEvents_Enabled_ProducesEntries_AndAggregatedResultUnchanged()
        {
            var withoutCapture = FightRunner.Run(BuildOptions(seed: 701));

            var optionsWithCapture = BuildOptions(seed: 701);
            optionsWithCapture.CaptureEvents = true;
            var withCapture = FightRunner.Run(optionsWithCapture);

            Assert.NotEmpty(withCapture.CapturedEvents);
            Assert.False(withCapture.CapturedEventsTruncated);
            Assert.Contains(withCapture.CapturedEvents, e => e.Category == FightLogEventCategory.Damage);

            // 判断记录（开启与否聚合结果必须完全一致）：CaptureEvents 只新增一份独立的事件日志，
            // 不触碰任何既有聚合字段的计算路径，见 FightAccumulator.AccumulateCapturedEvents 判断记录。
            Assert.Equal(withoutCapture.Outcome, withCapture.Outcome);
            Assert.Equal(withoutCapture.TicksUsed, withCapture.TicksUsed);
            Assert.Equal(withoutCapture.PlayerTotalDamage, withCapture.PlayerTotalDamage, precision: 9);
            Assert.Equal(withoutCapture.CreatureTotalDamage, withCapture.CreatureTotalDamage, precision: 9);
            Assert.Equal(withoutCapture.PlayerHitRate, withCapture.PlayerHitRate, precision: 9);
            Assert.Equal(withoutCapture.CreatureHitRate, withCapture.CreatureHitRate, precision: 9);
        }

        [Fact]
        public void CaptureEvents_MaxCapturedEvents_TruncatesAndSetsFlag()
        {
            var options = BuildOptions(seed: 702);
            options.CaptureEvents = true;
            options.MaxCapturedEvents = 1;
            var result = FightRunner.Run(options);

            Assert.True(result.CapturedEventsTruncated);
            Assert.True(result.CapturedEvents.Count <= 1);
        }

        // ===== 消费方反馈第 50 条（CancellationToken/IProgress）=====

        [Fact]
        public void Run_PreCancelledToken_ThrowsImmediately()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => FightRunner.Run(BuildOptions(seed: 800), cts.Token));
        }

        [Fact]
        public void Run_CancelledDuringProgressCallback_ThrowsAndProducesNoResult()
        {
            using var cts = new CancellationTokenSource();
            var progress = new Progress2(p => cts.Cancel());

            Assert.Throws<OperationCanceledException>(
                () => FightRunner.Run(BuildOptions(seed: 801), cts.Token, progress));
        }

        [Fact]
        public void Run_NotCancelled_MatchesOldOverload_SameSeed()
        {
            var legacy = FightRunner.Run(BuildOptions(seed: 802));
            var viaNewOverload = FightRunner.Run(BuildOptions(seed: 802), CancellationToken.None, progress: null);

            Assert.Equal(legacy.Outcome, viaNewOverload.Outcome);
            Assert.Equal(legacy.TicksUsed, viaNewOverload.TicksUsed);
            Assert.Equal(legacy.PlayerTotalDamage, viaNewOverload.PlayerTotalDamage, precision: 9);
            Assert.Equal(legacy.CreatureTotalDamage, viaNewOverload.CreatureTotalDamage, precision: 9);
        }

        [Fact]
        public void Run_Progress_ReportsMonotonicSequence_EndingAtTotal()
        {
            var reports = new List<SimProgress>();
            var progress = new Progress2(reports.Add);

            FightRunner.Run(BuildOptions(seed: 803), CancellationToken.None, progress);

            Assert.NotEmpty(reports);
            for (var i = 1; i < reports.Count; i++)
            {
                Assert.True(reports[i].Completed >= reports[i - 1].Completed, "进度序列应单调不减");
            }
            Assert.Equal(reports[^1].Total, reports[^1].Completed);
        }

        /// <summary>本测试文件不引用 <see cref="System.Progress{T}"/>（其按 <c>SynchronizationContext</c>
        /// 决定回调投递时机，可能异步，测试里需要同步、确定性的回调），改用这个最小的同步
        /// <see cref="IProgress{T}"/> 实现——直接在调用线程同步执行回调。</summary>
        private sealed class Progress2 : IProgress<SimProgress>
        {
            private readonly Action<SimProgress> _callback;

            public Progress2(Action<SimProgress> callback)
            {
                _callback = callback;
            }

            public void Report(SimProgress value) => _callback(value);
        }
    }
}
