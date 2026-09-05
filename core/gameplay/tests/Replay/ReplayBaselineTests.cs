using System;
using System.IO;
using System.Linq;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Xunit;

namespace Tests.Gameplay.Replay
{
    /// <summary>
    /// 回放回归测试（11_工程规范与测试.md 第 6 节"回放回归：固定录像 + 固定种子重放，比对事件流
    /// 与既往基线"）：两份固定录像（连续一场、离散一场，见 <see cref="ReplayWorldBuilder"/>）经
    /// <see cref="ReplayPlayer"/> 重放到底，产出的事件流摘要（<see cref="WorldSnapshot.EventLog"/>
    /// 的稳定拼接 + <see cref="WorldSnapshot.Digest"/>）与仓库内 <c>replay_baseline.json</c> 逐项
    /// 比对，不一致即失败。
    /// <para>
    /// 判断记录（录像文件与基线文件都是固定产物，测试只读不写）：<c>continuous_fight.replay.json</c>/
    /// <c>discrete_fight.replay.json</c>/<c>replay_baseline.json</c> 三份文件均已生成并提交到仓库
    /// （生成方式：<see cref="ReplayWorldBuilder.RecordContinuousFight"/>/
    /// <see cref="ReplayWorldBuilder.RecordDiscreteFight"/> 各跑一遍导出 <see cref="ReplayData"/>，
    /// 序列化写盘；基线的 <c>EventLog</c>/<c>Digest</c> 取自同一次固定脚本"直跑"结果，见本文件
    /// 判断记录 2）。本类的三个 <c>[Fact]</c> 只读这三份文件、重放、比对，不在测试运行期间生成或
    /// 覆写它们——"固定录像"意味着录像本身也应该是可审阅、可 diff 的仓库产物，不是每次跑测试都
    /// 重新录一遍（那样任何行为变化都会被"录像"本身悄悄吸收掉，起不到回归防护作用）。
    /// </para>
    /// <para>
    /// 判断记录 2（额外验证"重放 = 直跑"，不只是"重放 = 基线"）：<see cref="ReplayRegression_Continuous_ReplayMatchesDirectRun"/>/
    /// <see cref="ReplayRegression_Discrete_ReplayMatchesDirectRun"/> 用同一份固定脚本重新"直跑"一遍
    /// （<see cref="ReplayWorldBuilder.RunContinuousFixedScript"/>/<c>RunDiscreteFixedScript</c>），
    /// 断言其 <see cref="WorldSnapshot"/> 与"经录像重放"得到的结果逐项相等——这一层覆盖"录像本身
    /// 是否忠实、播放器是否正确"，与"重放结果是否等于既往基线"（回归防护本体）是两个独立关心的
    /// 问题，即便两者恰好用同一份 <c>WorldSnapshot</c> 类型，也分成两组用例，理由与
    /// <c>DiscreteReplayTests</c> 一致。
    /// </para>
    /// </summary>
    public sealed class ReplayBaselineTests
    {
        private static string FindDirectory([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            return Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空");
        }

        private static ReplayData LoadTape(string fileName)
        {
            var path = Path.Combine(FindDirectory(), fileName);
            var json = File.ReadAllText(path);
            var root = (JsonObject)JsonReader.Parse(json);
            return ReplayData.FromJson(root);
        }

        private static JsonObject LoadBaseline()
        {
            var path = Path.Combine(FindDirectory(), "replay_baseline.json");
            var json = File.ReadAllText(path);
            return (JsonObject)JsonReader.Parse(json);
        }

        private static void AssertMatchesBaselineEntry(JsonObject baseline, string key, Core.Foundation.SaveSystem.WorldSnapshot snapshot)
        {
            var entry = (JsonObject)baseline[key];
            var expectedTick = (long)((JsonNumber)entry["final_tick"]).Value;
            var expectedDigest = ((JsonString)entry["digest"]).Value;
            var expectedEventLog = ((JsonArray)entry["event_log"]).Select(v => ((JsonString)v).Value).ToList();

            Assert.Equal(expectedTick, snapshot.Tick);
            Assert.Equal(expectedEventLog, snapshot.EventLog);
            Assert.Equal(expectedDigest, snapshot.Digest);
        }

        // -----------------------------------------------------------------
        // 1. 连续场景：重放固定录像，比对既往基线
        // -----------------------------------------------------------------

        [Fact]
        public void ReplayRegression_Continuous_MatchesBaseline()
        {
            var tape = LoadTape("continuous_fight.replay.json");
            var (bus, audit) = ReplayWorldBuilder.CreateAuditedBus();
            var player = new ReplayPlayer(ReplayWorldBuilder.BuildContinuousWorld, bus, audit);
            player.Load(tape);

            var snapshot = player.StepTo(ReplayWorldBuilder.ContinuousFixedTicks);

            AssertMatchesBaselineEntry(LoadBaseline(), "continuous", snapshot);
        }

        [Fact]
        public void ReplayRegression_Continuous_ReplayMatchesDirectRun()
        {
            var tape = LoadTape("continuous_fight.replay.json");
            var (bus, audit) = ReplayWorldBuilder.CreateAuditedBus();
            var player = new ReplayPlayer(ReplayWorldBuilder.BuildContinuousWorld, bus, audit);
            player.Load(tape);
            var replaySnapshot = player.StepTo(ReplayWorldBuilder.ContinuousFixedTicks);

            var (directWorld, _, directAudit, finalTick) = ReplayWorldBuilder.RunContinuousFixedScript();
            var directLog = directAudit.Records.Select(r => r.Key.Value).ToList();
            var directSnapshot = Core.Foundation.SaveSystem.WorldSnapshot.Capture(finalTick, directLog, directWorld);

            Assert.Equal(directSnapshot.EventLog, replaySnapshot.EventLog);
            Assert.Equal(directSnapshot.Digest, replaySnapshot.Digest);
        }

        // -----------------------------------------------------------------
        // 2. 离散场景：重放固定录像，比对既往基线
        // -----------------------------------------------------------------

        [Fact]
        public void ReplayRegression_Discrete_MatchesBaseline()
        {
            var tape = LoadTape("discrete_fight.replay.json");
            var (bus, audit) = ReplayWorldBuilder.CreateAuditedBus();
            var player = new ReplayPlayer(ReplayWorldBuilder.BuildDiscreteWorld, bus, audit);
            player.Load(tape);

            var snapshot = player.StepTo(ReplayWorldBuilder.DiscreteFixedSteps);

            AssertMatchesBaselineEntry(LoadBaseline(), "discrete", snapshot);
        }

        [Fact]
        public void ReplayRegression_Discrete_ReplayMatchesDirectRun()
        {
            var tape = LoadTape("discrete_fight.replay.json");
            var (bus, audit) = ReplayWorldBuilder.CreateAuditedBus();
            var player = new ReplayPlayer(ReplayWorldBuilder.BuildDiscreteWorld, bus, audit);
            player.Load(tape);
            var replaySnapshot = player.StepTo(ReplayWorldBuilder.DiscreteFixedSteps);

            var (directWorld, _, directAudit, finalTick) = ReplayWorldBuilder.RunDiscreteFixedScript();
            var directLog = directAudit.Records.Select(r => r.Key.Value).ToList();
            var directSnapshot = Core.Foundation.SaveSystem.WorldSnapshot.Capture(finalTick, directLog, directWorld);

            Assert.Equal(directSnapshot.EventLog, replaySnapshot.EventLog);
            Assert.Equal(directSnapshot.Digest, replaySnapshot.Digest);
        }

        // -----------------------------------------------------------------
        // 3. 录像本身的往返/内容基本校验（惯例同 DiscreteReplayTests）
        // -----------------------------------------------------------------

        [Fact]
        public void Tapes_ContainExpectedTickCountAndFormatVersion()
        {
            var continuousTape = LoadTape("continuous_fight.replay.json");
            Assert.Equal(ReplayWorldBuilder.ContinuousFixedTicks, continuousTape.TickCount);
            Assert.Equal(ReplayData.CurrentFormatVersion, continuousTape.FormatVersion);
            Assert.All(continuousTape.Steps, s => Assert.Equal(Core.Foundation.SimLoop.SimStepKind.Continuous, s.Kind));

            var discreteTape = LoadTape("discrete_fight.replay.json");
            Assert.Equal(ReplayWorldBuilder.DiscreteFixedSteps, discreteTape.TickCount);
            Assert.Equal(ReplayData.CurrentFormatVersion, discreteTape.FormatVersion);
            Assert.All(discreteTape.Steps, s => Assert.Equal(Core.Foundation.SimLoop.SimStepKind.Discrete, s.Kind));
        }
    }
}
