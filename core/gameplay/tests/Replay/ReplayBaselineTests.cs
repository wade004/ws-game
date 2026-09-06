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
    /// 判断记录 2；生成用的一次性代码跑完即从本文件删除，不留在最终提交里）。本类的 <c>[Fact]</c>
    /// 只读这些文件、重放、比对，不在测试运行期间生成或覆写它们——"固定录像"意味着录像本身也应该
    /// 是可审阅、可 diff 的仓库产物，不是每次跑测试都重新录一遍（那样任何行为变化都会被"录像"本身
    /// 悄悄吸收掉，起不到回归防护作用）。
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
    /// <para>
    /// 判断记录 3（离散模式回放完整性任务：<c>discrete_fight.replay.json</c> 改为纯录像驱动，
    /// <c>format_version</c> 由 2 升到 3）：此前的离散录像/基线由"手工交替 <c>SimStep.Discrete</c>
    /// 行动者"的脚本产出，不经过任何 <c>TurnScheduler</c>；本次改动后 <see cref="ReplayWorldBuilder.RecordDiscreteFight"/>
    /// 改为真正驱动一个装配好的 <c>TurnScheduler</c>（见该方法、<see cref="ReplayWorldBuilder.BuildDiscreteWorldWithScheduler"/>
    /// 判断记录），行动顺序由先攻规则算出来，<c>discrete_fight.replay.json</c> 因此重新生成——新
    /// 录像的 <c>inputs</c> 与旧录像内容形式相同（tick/actorId/intentKind/args，本次固定脚本的先攻
    /// 顺序恰好与旧脚本的手工交替顺序一致，见 <see cref="ReplayWorldBuilder.PlayerInitiative"/> 判断
    /// 记录），但 <c>steps</c> 字段现在只是诊断信息（重放不再读取它，见 <c>IReplayRecorder.RecordStep</c>
    /// 判断记录）；<c>replay_baseline.json</c>"discrete"一项的 <c>event_log</c>/<c>digest</c> 也
    /// 随之重新生成——新事件流额外包含 <c>sim.turn_started</c>/<c>sim.turn_ended</c>/
    /// <c>sim.round_ended</c>/<c>sim.awaiting_input</c>（此前的手工脚本从不驱动
    /// <c>TurnScheduler</c>，这些事件从未出现过），条数从 92 涨到 166，这是"离散重放现在真正经过
    /// <c>TurnScheduler</c>"这一行为改进的直接体现，不是数值漂移。
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

        // 判断记录（离散模式回放完整性任务：由 Load/WorldFactory 改为 LoadDiscrete/DiscreteWorldFactory）：
        // 离散录像本身不再含"哪个 tick 是谁的回合"（见 discrete_fight.replay.json 判断记录、
        // Core.Foundation.SaveSystem.IReplayPlayer.LoadDiscrete），重放必须经
        // ReplayWorldBuilder.BuildDiscreteWorldWithScheduler 提供一个真正的 TurnScheduler，
        // 由它在重放时重新算出行动顺序——这正是本任务要验证的核心性质："TurnScheduler 的顺序完全
        // 由确定性输入决定，重放自然复现同一序列"，不是靠录像里的 actorId/phase 字段。

        [Fact]
        public void ReplayRegression_Discrete_MatchesBaseline()
        {
            var tape = LoadTape("discrete_fight.replay.json");
            var (bus, audit) = ReplayWorldBuilder.CreateAuditedBus();
            var player = new ReplayPlayer(ReplayWorldBuilder.BuildContinuousWorld, bus, audit);
            player.LoadDiscrete(tape, ReplayWorldBuilder.BuildDiscreteWorldWithScheduler);

            var snapshot = player.StepTo(ReplayWorldBuilder.DiscreteFixedSteps);

            AssertMatchesBaselineEntry(LoadBaseline(), "discrete", snapshot);
        }

        [Fact]
        public void ReplayRegression_Discrete_ReplayMatchesDirectRun()
        {
            var tape = LoadTape("discrete_fight.replay.json");
            var (bus, audit) = ReplayWorldBuilder.CreateAuditedBus();
            var player = new ReplayPlayer(ReplayWorldBuilder.BuildContinuousWorld, bus, audit);
            player.LoadDiscrete(tape, ReplayWorldBuilder.BuildDiscreteWorldWithScheduler);
            var replaySnapshot = player.StepTo(ReplayWorldBuilder.DiscreteFixedSteps);

            var (directWorld, _, directAudit, finalTick) = ReplayWorldBuilder.RunDiscreteFixedScript();
            var directLog = directAudit.Records.Select(r => r.Key.Value).ToList();
            var directSnapshot = Core.Foundation.SaveSystem.WorldSnapshot.Capture(finalTick, directLog, directWorld);

            Assert.Equal(directSnapshot.EventLog, replaySnapshot.EventLog);
            Assert.Equal(directSnapshot.Digest, replaySnapshot.Digest);
        }

        /// <summary>
        /// 任务书新增验收点："录像重放的轮次/行动者序列与直跑一致"：直接订阅
        /// <see cref="Core.Foundation.SimLoop.SimTurnStartedEvent"/>（<c>TurnScheduler</c> 每次切到
        /// 一个新行动者都会发一次，携带 <c>ActorId</c>/<c>RoundIndex</c>，见该事件类型注释）——
        /// 分别在"直跑"侧（<see cref="ReplayWorldBuilder.RunDiscreteFixedScript"/> 复刻的驱动循环）
        /// 与"经录像重放"侧（<see cref="ReplayPlayer.LoadDiscrete"/>）各自独立的
        /// <see cref="IEventBus"/> 上挂一个收集器，逐项比较两条路径产生的 <c>(ActorId, RoundIndex)</c>
        /// 序列——这是比"最终 EventLog/Digest 相等"更直接的证据：直接证明重放侧的
        /// <c>TurnScheduler</c> 在每一步都算出了与直跑侧完全相同的"轮到谁"，而不是仅仅在终局的
        /// 实体位置/事件计数上偶然吻合。
        /// </summary>
        [Fact]
        public void ReplayRegression_Discrete_TurnSequence_MatchesDirectRun()
        {
            var directBus = ReplayWorldBuilder.CreateAuditedBus().Bus;
            var directTurns = new System.Collections.Generic.List<(string ActorId, int RoundIndex)>();
            directBus.Subscribe<Core.Foundation.SimLoop.SimTurnStartedEvent>(
                Core.Foundation.SimLoop.SimEventKeys.TurnStarted,
                e => directTurns.Add((e.ActorId.Value, e.RoundIndex)));

            var (directWorld, _, directScheduler) = ReplayWorldBuilder.BuildDiscreteWorldWithScheduler(0UL, directBus);
            long ticksAdvanced = 0;
            while (ticksAdvanced < ReplayWorldBuilder.DiscreteFixedSteps)
            {
                var step = directScheduler.NextStep();
                if (step == null)
                {
                    var actorId = directScheduler.GetCurrentActor()!.Value;
                    var skillId = actorId.Equals(ReplayWorldBuilder.PlayerId) ? ReplayWorldBuilder.SkillStrike : ReplayWorldBuilder.SkillBite;
                    directScheduler.SubmitIntent(actorId, new Core.Foundation.SimLoop.Intent(actorId, "cast", ReplayWorldBuilder.CastArgs(skillId)));
                    continue;
                }

                directWorld.Tick(step.Value);
                directScheduler.NotifyStepConsumed(step.Value.ActorId!.Value);
                ticksAdvanced++;
            }

            var tape = LoadTape("discrete_fight.replay.json");
            var (replayBus, replayAudit) = ReplayWorldBuilder.CreateAuditedBus();
            var replayTurns = new System.Collections.Generic.List<(string ActorId, int RoundIndex)>();
            replayBus.Subscribe<Core.Foundation.SimLoop.SimTurnStartedEvent>(
                Core.Foundation.SimLoop.SimEventKeys.TurnStarted,
                e => replayTurns.Add((e.ActorId.Value, e.RoundIndex)));

            var player = new ReplayPlayer(ReplayWorldBuilder.BuildContinuousWorld, replayBus, replayAudit);
            player.LoadDiscrete(tape, ReplayWorldBuilder.BuildDiscreteWorldWithScheduler);
            player.StepTo(ReplayWorldBuilder.DiscreteFixedSteps);

            Assert.NotEmpty(directTurns);
            Assert.Equal(directTurns, replayTurns);
            // 先攻属性玩家 > NPC（见 ReplayWorldBuilder.PlayerInitiative/NpcInitiative），首个行动者
            // 必须是玩家——这条断言把"顺序确实是 TurnScheduler 按先攻规则算出来的"钉死，不是巧合
            // 对上的。
            Assert.Equal(ReplayWorldBuilder.PlayerId.Value, directTurns[0].ActorId);
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

        /// <summary>录像本身应记录录制时的主种子（P1-04 收口：<c>ReplayData.MasterSeed</c>，见
        /// <see cref="ReplayWorldBuilder.RecordContinuousFight"/>/<see cref="ReplayWorldBuilder.RecordDiscreteFight"/>
        /// 均以 <c>0UL</c> 录制）——回归本仓库两份固定录像文件已补齐 <c>master_seed</c> 字段
        /// （<c>format_version</c> 随之由 3 升到 4，见本文件类型注释判断记录 3 的同类先例）。</summary>
        [Fact]
        public void Tapes_RecordMasterSeed()
        {
            Assert.Equal(0UL, LoadTape("continuous_fight.replay.json").MasterSeed);
            Assert.Equal(0UL, LoadTape("discrete_fight.replay.json").MasterSeed);
        }

        // -----------------------------------------------------------------
        // 4. F6 收口：回放摘要（WorldSnapshot.Digest）此前只覆盖事件 key 序列与
        //    (EntityId, Position, LayerDepth)，两份"同位置、同实体 id/layer、同事件 key，但伤害/HP
        //    payload 不同"的状态会得到相同 Digest——见 audit-20260907/delivery-validation.md F6。
        //    WorldSnapshot.Capture 现补充可选 entityStateProvider 参数（默认 null，行为与此前逐字节
        //    一致，见 Capture 判断记录——上面 1/2 节既有的 4 个回归用例与 replay_baseline.json 的
        //    既有 digest 因此不需要重新生成，只有 format_version/master_seed 两个字段随 P1-04
        //    的格式升级而变化，见本文件"3. 录像本身"一节）；本节新增用例验证：调用方（这里是持有
        //    RulesAssembly.Powers 的测试夹具，经 ReplayWorldBuilder.HpState 桥接）传入
        //    entityStateProvider 后，HP 变化确实改变 Digest，事件序列比较（EventLog）不受影响。
        // -----------------------------------------------------------------

        [Fact]
        public void WorldSnapshot_Capture_WithEntityStateProvider_IsSensitiveToHpChange_EventLogUnaffected()
        {
            var (bus, _) = ReplayWorldBuilder.CreateAuditedBus();
            var (world, _) = ReplayWorldBuilder.BuildContinuousWorld(0UL, bus);

            var eventLog = new System.Collections.Generic.List<string> { "sim.tick_started", "sim.tick_finished" };
            Core.Foundation.SaveSystem.WorldSnapshot HpAwareSnapshot() =>
                Core.Foundation.SaveSystem.WorldSnapshot.Capture(1, eventLog, world, id => ReplayWorldBuilder.HpState(world, id));

            var before = HpAwareSnapshot();

            // 造成一次伤害：NPC 掉血，实体 id/位置/LayerDepth/事件 key 序列全部不变。
            ReplayWorldBuilder.PowersOf(world).ModifyPower(
                ReplayWorldBuilder.NpcId, ReplayWorldBuilder.PowerHealth, -10, sourceId: new Core.Foundation.Common.Id("test.f6_damage"));

            var after = HpAwareSnapshot();

            Assert.Equal(before.EventLog, after.EventLog); // 事件序列比较仍保留、不受影响。
            Assert.NotEqual(before.Digest, after.Digest);   // HP 变化必须改变摘要（F6 收口核心断言）。

            // 不传 entityStateProvider（默认 null）时行为与此前逐字节一致：同样的 HP 变化不会反映到
            // 摘要——证明"既有基线文件的既有摘要口径不受本次改动影响"。
            var legacyBefore = Core.Foundation.SaveSystem.WorldSnapshot.Capture(1, eventLog, world);
            ReplayWorldBuilder.PowersOf(world).ModifyPower(
                ReplayWorldBuilder.NpcId, ReplayWorldBuilder.PowerHealth, -5, sourceId: new Core.Foundation.Common.Id("test.f6_damage_2"));
            var legacyAfter = Core.Foundation.SaveSystem.WorldSnapshot.Capture(1, eventLog, world);
            Assert.Equal(legacyBefore.Digest, legacyAfter.Digest);
        }

        /// <summary>同一条能力沿 <see cref="IReplayPlayer.StepTo"/> 打通（不是只在 Capture 单元层面
        /// 验证）：经录像重放到底后，传入 HP 的 entityStateProvider 得到的 Digest 应与不传时不同
        /// ——证明生产可用的重放路径（<see cref="ReplayPlayer"/>，非直接摆弄 WorldSim）也能感知
        /// HP，不是只有测试内联调用 Capture 才生效。</summary>
        [Fact]
        public void ReplayPlayer_StepTo_WithEntityStateProvider_DigestDiffersFromWithout()
        {
            var tape = LoadTape("continuous_fight.replay.json");

            var (busA, auditA) = ReplayWorldBuilder.CreateAuditedBus();
            var playerA = new ReplayPlayer(ReplayWorldBuilder.BuildContinuousWorld, busA, auditA);
            playerA.Load(tape);
            var snapshotWithoutProvider = playerA.StepTo(ReplayWorldBuilder.ContinuousFixedTicks);

            var (busB, auditB) = ReplayWorldBuilder.CreateAuditedBus();
            var playerB = new ReplayPlayer(ReplayWorldBuilder.BuildContinuousWorld, busB, auditB);
            playerB.Load(tape);
            var snapshotWithProvider = playerB.StepTo(
                ReplayWorldBuilder.ContinuousFixedTicks,
                id => ReplayWorldBuilder.HpState(playerB.World!, id));

            // 两条路径的战斗结算完全相同（同一份录像），事件序列/最终 tick 应一致；但一侧的摘要额外
            // 折入了 HP 字段，两个 Digest 不应相等——固定战斗脚本里 NPC 会掉血到非初始值（见
            // ReplayWorldBuilder 类型顶部"固定脚本"注释），HP 差异确保这里不是巧合相等。
            Assert.Equal(snapshotWithoutProvider.EventLog, snapshotWithProvider.EventLog);
            Assert.Equal(snapshotWithoutProvider.Tick, snapshotWithProvider.Tick);
            Assert.NotEqual(snapshotWithoutProvider.Digest, snapshotWithProvider.Digest);
        }
    }
}
