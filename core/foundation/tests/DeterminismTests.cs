using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Tests.Foundation.Determinism;
using Xunit;

namespace Tests.Foundation
{
    /// <summary>
    /// T1-9 确定性回放集成测试（见落地方案与分阶段计划.md T1-9 行、
    /// 10_存档与持久化.md 第 8 节"确定性与回放"、03_运行时骨架.md 第 8 节"时间与计时器约定"）。
    /// 组装一个"小而有戏"的世界（<see cref="DeterministicWorld"/>：<c>EventBus</c> +
    /// <c>RngHost</c> + <c>WorldSim</c> + 三个测试阶段处理器），验证：
    /// 同种子同意图序列两次独立运行事件流完全一致；不同种子/不同意图序列产生不同结果；
    /// 录制 → 回放的世界快照摘要与直接运行一致（含中途快照）；<c>ReplayData</c> 可
    /// JSON 往返后仍回放出同样的摘要；事件审计序号连续、每 tick 恰好一对边界事件；
    /// 暂停/慢动作不改变确定性。全程不引入任何真实系统时间或线程调度：不读取系统日期时间、
    /// 不使用高精度计时器，不使用线程/异步任务调度原语，不使用系统级伪随机数生成器
    /// （全部随机数经 <see cref="IRngHost"/> 分流取得）。
    /// </summary>
    public class DeterminismTests
    {
        // ---- 用例 1：同种子同意图序列两次运行，事件流完全一致 -------------------------------

        [Fact]
        public void SameSeedSameIntents_EventStreamAndPositionsMatch()
        {
            const ulong seed = 12345UL;

            var (worldA, _, _, auditA) = DeterministicWorld.Build(seed);
            DeterministicWorld.RunScript(worldA, DeterministicWorld.PlayerId);

            var (worldB, _, _, auditB) = DeterministicWorld.Build(seed);
            DeterministicWorld.RunScript(worldB, DeterministicWorld.PlayerId);

            var logA = DeterministicWorld.ToEventLog(auditA);
            var logB = DeterministicWorld.ToEventLog(auditB);

            Assert.Equal(logA, logB);

            var entitiesA = worldA.QueryEntities(new EntityFilter());
            var entitiesB = worldB.QueryEntities(new EntityFilter());
            Assert.Equal(entitiesA.Count, entitiesB.Count);
            for (var i = 0; i < entitiesA.Count; i++)
            {
                Assert.Equal(entitiesA[i].EntityId, entitiesB[i].EntityId);
                Assert.Equal(entitiesA[i].Position, entitiesB[i].Position);
                Assert.Equal(entitiesA[i].LayerDepth, entitiesB[i].LayerDepth);
            }
        }

        // ---- 用例 2a：不同种子，wander 相关行为不同 ------------------------------------------

        [Fact]
        public void DifferentSeed_WanderBehaviorDiffers()
        {
            var (worldA, _, _, auditA) = DeterministicWorld.Build(1001UL);
            DeterministicWorld.RunScript(worldA, DeterministicWorld.PlayerId);

            var (worldB, _, _, auditB) = DeterministicWorld.Build(2002UL);
            DeterministicWorld.RunScript(worldB, DeterministicWorld.PlayerId);

            var logA = DeterministicWorld.ToEventLog(auditA);
            var logB = DeterministicWorld.ToEventLog(auditB);

            // move/entity.created/entity.destroyed/tick 边界事件两边完全一致（意图脚本、生成/
            // 销毁 tick 都是固定的），唯一可能不同的来源是 wander：不同种子下两条完整事件流
            // 应当不同。
            Assert.NotEqual(logA, logB);

            var npcAPositionA = worldA.GetEntity(DeterministicWorld.NpcAId)!.Position;
            var npcAPositionB = worldB.GetEntity(DeterministicWorld.NpcAId)!.Position;
            var npcBPositionA = worldA.GetEntity(DeterministicWorld.NpcBId)!.Position;
            var npcBPositionB = worldB.GetEntity(DeterministicWorld.NpcBId)!.Position;

            Assert.True(
                npcAPositionA != npcAPositionB || npcBPositionA != npcBPositionB,
                "不同种子下两个 NPC 的 wander 终点位置不应完全相同");
        }

        // ---- 用例 2b：同种子、不同意图序列，结果不同 -----------------------------------------

        [Fact]
        public void SameSeedDifferentIntentSequence_ResultDiffers()
        {
            const ulong seed = 555UL;

            var (worldA, _, _, _) = DeterministicWorld.Build(seed);
            DeterministicWorld.RunScript(worldA, DeterministicWorld.PlayerId);

            var altScript = new (long Tick, double Dx, double Dy)[]
            {
                (3L, 5.0, 0.0), // 与默认脚本 tick 3 的 (1,0) 不同，其余三个 tick 保持一致
                (7L, 0.0, 1.0),
                (12L, -1.0, 2.0),
                (25L, 3.0, -1.0),
            };

            var (worldB, _, _, _) = DeterministicWorld.Build(seed);
            DeterministicWorld.RunScript(worldB, DeterministicWorld.PlayerId, altScript);

            // 同种子 ⇒ wander 结果两边一致；move 参数在 tick 3 不同 ⇒ 玩家最终位置必然不同
            // （X 差 4，纯算术差异，不依赖随机数，断言可靠、非概率性）。
            var playerPositionA = worldA.GetEntity(DeterministicWorld.PlayerId)!.Position;
            var playerPositionB = worldB.GetEntity(DeterministicWorld.PlayerId)!.Position;

            Assert.NotEqual(playerPositionA, playerPositionB);
        }

        // ---- 用例 3：录制 → 回放一致（末尾摘要 + 中途快照） ----------------------------------

        [Fact]
        public void RecordAndReplay_FinalDigestMatchesDirectRun()
        {
            var (snapshots, replayData) = RunDirectAndRecord(777UL, new HashSet<long> { DeterministicWorld.TotalTicks });
            var directFinal = snapshots[DeterministicWorld.TotalTicks];

            var replayedFinal = PlayReplayUpTo(replayData, DeterministicWorld.TotalTicks);

            Assert.Equal(directFinal.EventLog, replayedFinal.EventLog);
            Assert.Equal(directFinal.Digest, replayedFinal.Digest);
        }

        [Fact]
        public void RecordAndReplay_MidTickSnapshotMatchesDirectRun()
        {
            var (snapshots, replayData) = RunDirectAndRecord(888UL, new HashSet<long> { 15 });
            var directMid = snapshots[15];

            var replayedMid = PlayReplayUpTo(replayData, 15);

            Assert.Equal(15, replayedMid.Tick);
            Assert.Equal(directMid.EventLog, replayedMid.EventLog);
            Assert.Equal(directMid.Digest, replayedMid.Digest);
        }

        // ---- 用例 4：ReplayData 经 JSON 往返后仍回放出同样的摘要（证明录像可存盘） -----------

        [Fact]
        public void ReplayData_JsonRoundTrip_ReplayProducesSameDigest()
        {
            var (_, replayData) = RunDirectAndRecord(999UL, new HashSet<long>());

            var replayedBefore = PlayReplayUpTo(replayData, DeterministicWorld.TotalTicks);

            var json = replayData.ToJson();
            var text = JsonWriter.Write(json);
            var parsed = (JsonObject)JsonReader.Parse(text);
            var roundTripped = ReplayData.FromJson(parsed);

            var replayedAfter = PlayReplayUpTo(roundTripped, DeterministicWorld.TotalTicks);

            Assert.Equal(replayedBefore.EventLog, replayedAfter.EventLog);
            Assert.Equal(replayedBefore.Digest, replayedAfter.Digest);
        }

        // ---- 用例 5：审计序号连续、tick 边界事件成对 -----------------------------------------

        [Fact]
        public void EventAudit_SequenceNumbersAreContinuous()
        {
            var (world, _, _, audit) = DeterministicWorld.Build(4242UL);
            DeterministicWorld.RunScript(world, DeterministicWorld.PlayerId);

            var records = audit.Records;
            Assert.True(records.Count > 0);
            for (var i = 0; i < records.Count; i++)
            {
                Assert.Equal(i, records[i].Sequence);
            }
        }

        [Fact]
        public void EveryTick_HasExactlyOneTickStartedAndOneTickFinished()
        {
            var (world, _, _, audit) = DeterministicWorld.Build(4343UL);
            DeterministicWorld.RunScript(world, DeterministicWorld.PlayerId);

            Assert.Equal(DeterministicWorld.TotalTicks, CountKey(audit, EventKeys.SimTickStarted));
            Assert.Equal(DeterministicWorld.TotalTicks, CountKey(audit, EventKeys.SimTickFinished));
        }

        // ---- 用例 6：暂停/慢动作不改变确定性 -------------------------------------------------

        [Fact]
        public void PauseAndTimeScaleViaSimClockHost_DoesNotChangeEventStream()
        {
            const ulong seed = 6161UL;

            var (worldDirect, _, _, auditDirect) = DeterministicWorld.Build(seed);
            DeterministicWorld.RunScript(worldDirect, DeterministicWorld.PlayerId);
            var directLog = DeterministicWorld.ToEventLog(auditDirect);

            var (worldClocked, _, _, auditClocked) = DeterministicWorld.Build(seed);
            var clock = new SimClockHost(
                worldClocked,
                new SimLoopOptions { StepSeconds = DeterministicWorld.StepSeconds, MaxCatchUpSteps = 64 });

            for (long tickNumber = 1; tickNumber <= DeterministicWorld.TotalTicks; tickNumber++)
            {
                // 途中插入暂停与时间缩放变化：暂停期间无论推进多少真实时间都不产生 tick
                // （SimClockHost.Advance 在 IsPaused 时直接跳过累积/出 tick，见其实现），
                // 时间缩放只改变"多少真实秒对应一步"，见下方 dt 反算——两者都不改变
                // WorldSim.Tick 实际收到的 SimStep（恒为 Continuous(stepSeconds)），
                // 也不改变每个逻辑 tick 提交意图的时机。
                if (tickNumber == 6)
                {
                    clock.SetPaused(true);
                    clock.Advance(5.0);
                    clock.Advance(5.0);
                    clock.SetPaused(false);
                }

                if (tickNumber == 11)
                {
                    clock.SetTimeScale(0.5);
                }

                if (tickNumber == 21)
                {
                    clock.SetTimeScale(2.0);
                }

                if (tickNumber == 26)
                {
                    clock.SetTimeScale(1.0);
                }

                foreach (var entry in DeterministicWorld.MoveScript)
                {
                    if (entry.Tick == tickNumber)
                    {
                        worldClocked.SubmitIntent(
                            new Intent(DeterministicWorld.PlayerId, "move", DeterministicWorld.MoveArgs(entry.Dx, entry.Dy)));
                    }
                }

                // 反算 realDeltaSeconds，使本次 Advance 在当前 TimeScale 下恰好推进一个 tick：
                // scaledDelta = dt * TimeScale = stepSeconds。
                var dt = DeterministicWorld.StepSeconds / clock.TimeScale;
                clock.Advance(dt);
            }

            var clockedLog = DeterministicWorld.ToEventLog(auditClocked);
            Assert.Equal(DeterministicWorld.TotalTicks, clock.TickIndex);
            Assert.Equal(directLog, clockedLog);

            var directEntities = worldDirect.QueryEntities(new EntityFilter());
            var clockedEntities = worldClocked.QueryEntities(new EntityFilter());
            Assert.Equal(directEntities.Count, clockedEntities.Count);
            for (var i = 0; i < directEntities.Count; i++)
            {
                Assert.Equal(directEntities[i].EntityId, clockedEntities[i].EntityId);
                Assert.Equal(directEntities[i].Position, clockedEntities[i].Position);
            }
        }

        // ---- 私有测试基础设施 -----------------------------------------------------------------

        /// <summary>
        /// 组装一个新世界，边跑满 30 个 tick 边用 <see cref="ReplayRecorder"/> 录制（先给用到的
        /// 分流随机源"预热"一次 <see cref="IRngHost.GetStreamState"/>——只派生初始状态、不消耗
        /// 随机数，供 <see cref="IReplayRecorder.BeginRecording"/> 记下"这条流被首次访问前"的
        /// 状态，回放据此 <see cref="IRngHost.SetStreamState"/> 即可与原始运行完全对齐），
        /// 并在 <paramref name="captureAtTicks"/> 指定的 tick 处额外用 <see cref="WorldSnapshot.Capture"/>
        /// 直接对同一个世界拍一份快照（不经过回放），供与回放结果比对。
        /// </summary>
        private static (IReadOnlyDictionary<long, WorldSnapshot> Snapshots, ReplayData Replay) RunDirectAndRecord(
            ulong seed, HashSet<long> captureAtTicks)
        {
            var (world, rng, _, audit) = DeterministicWorld.Build(seed);
            var recorder = new ReplayRecorder(DeterministicWorld.StepSeconds);

            var seeds = new Dictionary<string, RngStreamState>(StringComparer.Ordinal)
            {
                [DeterministicWorld.WanderStream.Value] = rng.GetStreamState(DeterministicWorld.WanderStream),
            };
            recorder.BeginRecording(rng.MasterSeed, seeds);

            var snapshots = new Dictionary<long, WorldSnapshot>();

            for (long tickNumber = 1; tickNumber <= DeterministicWorld.TotalTicks; tickNumber++)
            {
                foreach (var entry in DeterministicWorld.MoveScript)
                {
                    if (entry.Tick != tickNumber)
                    {
                        continue;
                    }

                    var intent = new Intent(DeterministicWorld.PlayerId, "move", DeterministicWorld.MoveArgs(entry.Dx, entry.Dy));
                    world.SubmitIntent(intent);
                    recorder.RecordInput(tickNumber, new ReplayInputRecord(tickNumber, intent.ActorId, intent.Kind, intent.Args));
                }

                world.Tick(SimStep.Continuous(DeterministicWorld.StepSeconds));

                if (captureAtTicks.Contains(tickNumber))
                {
                    snapshots[tickNumber] = WorldSnapshot.Capture(tickNumber, DeterministicWorld.ToEventLog(audit), world);
                }
            }

            recorder.SetTickCount(DeterministicWorld.TotalTicks);
            return (snapshots, recorder.Export());
        }

        /// <summary>用一个全新的 (bus, audit, ReplayPlayer) 三元组把 <paramref name="data"/>
        /// 播放到 <paramref name="tick"/> 并返回该 tick 的快照。</summary>
        private static WorldSnapshot PlayReplayUpTo(ReplayData data, long tick)
        {
            var replayAudit = new InMemoryEventAudit();
            var replayBus = new EventBus(
                DeterministicWorld.BuildCatalog(),
                new EventBusOptions { AuditLog = true },
                audit: replayAudit);

            var player = new ReplayPlayer(DeterministicWorld.BuildOnBus, replayBus, replayAudit);
            player.Load(data);
            return player.StepTo(tick);
        }

        private static int CountKey(InMemoryEventAudit audit, Id key)
        {
            var count = 0;
            var records = audit.Records;
            for (var i = 0; i < records.Count; i++)
            {
                if (records[i].Key == key)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
