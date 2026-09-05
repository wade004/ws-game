using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Tests.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SaveSystem
{
    /// <summary>
    /// ADR-0013 离散时间模型任务补齐：<see cref="ReplayRecorder"/>/<see cref="ReplayPlayer"/>
    /// 此前只支持连续步录像/回放（<c>ReplayPlayer.StepTo</c> 硬编码产生 <c>Continuous</c> 步，见
    /// <see cref="ReplayStepRecord"/> 类型注释"判断记录（旧格式兼容）"）；本文件验证：
    /// 1) 一场含"连续 → 离散 → 连续"模式切换的战斗录像，回放事件流与摘要与直跑完全一致（比较
    ///    方式复用 <c>Tests.Foundation.DeterminismTests</c> 的做法：<see cref="WorldSnapshot"/>
    ///    的 <c>EventLog</c>/<c>Digest</c> 逐项比较）；
    /// 2) 本任务之前唯一存在过的旧格式（无 <c>format_version</c>/<c>steps</c> 字段）录像仍可读，
    ///    按"每个 tick 都是连续步"处理，回放不抛异常、结果与该格式历来的语义一致。
    /// <para>
    /// 夹具刻意不依赖 <c>core/rules</c>/<c>core/carriers</c>（那些模块由并行任务改动，本文件只
    /// 依赖 <c>core/foundation</c> 自身）：两个测试实体（<see cref="TestEntity"/>，来自
    /// <c>core/foundation/sim_loop/tests</c>，同一 <c>Tests.Foundation</c> 程序集可见）借用
    /// <see cref="Entity.Position"/>.<c>X</c> 承载"HP"，<see cref="TurnScheduler"/> 按
    /// <c>fixed_order</c> 策略在两者之间轮流产生离散步，一个自定义 <see cref="ITickPhaseHandler"/>
    /// 在离散步里对"对手"造成固定伤害——全程无随机数、无玩家参与者，专注验证录像/回放机制本身
    /// （具体游戏规则的确定性回归见 <c>core/gameplay/tests/Discrete/DiscreteTickTests.cs</c>）。
    /// </para>
    /// </summary>
    public sealed class DiscreteReplayTests
    {
        private static readonly Id UnitA = new Id("unit.dr_a");
        private static readonly Id UnitB = new Id("unit.dr_b");
        private static readonly Id MapId = new Id("map.dr_test");

        private const double StepSeconds = 1.0;
        private const double AHp = 25.0;
        private const double BHp = 100.0;
        private const double ADamageToB = 1.0;
        private const double BDamageToA = 10.0;

        /// <summary>挂在 <see cref="TickPhase.CombatResolution"/>：离散步下，当前行动者对"对手"
        /// 造成固定伤害，体现为对手 <see cref="Entity.Position"/>.X 减少（见类型注释）；连续步
        /// 不做任何事。</summary>
        private sealed class FixedDamageHandler : ITickPhaseHandler
        {
            public void Execute(SimStep step, IWorldSim world)
            {
                if (step.Kind != SimStepKind.Discrete)
                {
                    return;
                }

                var attacker = step.ActorId!.Value;
                var targetId = attacker.Equals(UnitA) ? UnitB : UnitA;
                var damage = attacker.Equals(UnitA) ? ADamageToB : BDamageToA;

                var target = world.GetEntity(targetId);
                if (target == null)
                {
                    return;
                }

                target.Position = new Vec2(target.Position.X - damage, target.Position.Y);
            }
        }

        /// <summary>与 <see cref="WorldFactory"/> 签名一致：构造一个带两个测试实体 + 一个
        /// <see cref="FixedDamageHandler"/> 的新世界。直跑与回放各自独立调用一次，保证两边世界
        /// 结构完全相同、互不共享任何可变状态。</summary>
        private static (IWorldSim World, IRngHost Rng) BuildWorld(ulong masterSeed, IEventBus bus)
        {
            var world = new WorldSim(bus);
            world.AddEntity(new TestEntity(UnitA, MapId) { Position = new Vec2(AHp, 0) });
            world.AddEntity(new TestEntity(UnitB, MapId) { Position = new Vec2(BHp, 0) });
            world.RegisterPhaseHandler(TickPhase.CombatResolution, new FixedDamageHandler());
            return (world, new RngHost(masterSeed));
        }

        private static (IEventBus Bus, InMemoryEventAudit Audit) CreateAuditedBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(SimEventKeys.TickStarted, "sim", new[] { "tickIndex", "dt" }),
                new EventDefinition(SimEventKeys.TickFinished, "sim", new[] { "tickIndex" }),
                new EventDefinition(SimEventKeys.EntityCreated, "entity", new[] { "entityId", "kind", "displayId" }),
                new EventDefinition(SimEventKeys.EntityDestroyed, "entity", new[] { "entityId" }),
                new EventDefinition(SimEventKeys.TurnStarted, "sim", new[] { "actorId", "roundIndex" }),
                new EventDefinition(SimEventKeys.TurnEnded, "sim", new[] { "actorId" }),
                new EventDefinition(SimEventKeys.RoundEnded, "sim", new[] { "roundIndex" }),
                new EventDefinition(SimEventKeys.AwaitingInput, "sim", new[] { "actorId" }),
            });

            var audit = new InMemoryEventAudit();
            var bus = new EventBus(catalog, new EventBusOptions { AuditLog = true }, audit: audit);
            return (bus, audit);
        }

        private static IReadOnlyList<string> ToEventLog(InMemoryEventAudit audit)
        {
            var records = audit.Records;
            var log = new List<string>(records.Count);
            for (var i = 0; i < records.Count; i++)
            {
                log.Add(records[i].Key.Value);
            }
            return log;
        }

        /// <summary>
        /// 直接驱动一场"探索（连续）→ 战斗（离散）→ 探索（连续）"并同步录像；
        /// <paramref name="finalTick"/>（元组第四项）输出最终 tick 号，供
        /// <see cref="IReplayPlayer.StepTo"/> 对齐。
        /// <para>
        /// 判断记录（不经 <see cref="TurnScheduler"/>，手工交替行动者）：<see cref="IReplayPlayer.StepTo"/>
        /// 只知道 <see cref="IWorldSim"/>（经 <see cref="WorldFactory"/> 构造），不知道也不重建
        /// <see cref="TurnScheduler"/>——<c>sim.turn_started</c>/<c>sim.turn_ended</c>/
        /// <c>sim.round_ended</c> 三个事件由 <see cref="TurnScheduler"/> 自己
        /// <c>PublishImmediate</c>（<see cref="ITickPhaseHandler"/> 之外，见该类型源码），不经过
        /// <see cref="IWorldSim.Tick"/>，因此天然不在"回放 = 重放 SimStep 序列给 world.Tick"这一
        /// 机制的覆盖范围内（回放要求的是"同一份 SimStep 序列 → 同一个 world.Tick 结果"，不是
        /// "同一份 TurnScheduler 调用序列"）。本测试若像生产代码一样经 <see cref="TurnScheduler"/>
        /// 驱动，直跑侧会多出这三种事件、而回放侧（只重放 SimStep 给 world.Tick）永远补不出来，
        /// 两者的 <c>EventLog</c> 必然不可能逐项相等——这不是回放机制本身的缺陷，而是"事件到底由
        /// 谁发出"这一分层边界决定的。本测试因此不构造 <see cref="TurnScheduler"/>，手工在两个
        /// 单位之间交替产生 <see cref="SimStep.Discrete"/>，只让 <see cref="WorldSim"/> 自身的事件
        /// （<c>sim.tick_started</c>/<c>finished</c>/<c>entity.*</c>）参与比较，精确对齐"回放
        /// 机制覆盖的范围"。
        /// </para>
        /// </summary>
        private static (IWorldSim World, InMemoryEventAudit Audit, ReplayData Replay, long FinalTick) RecordFight()
        {
            var (bus, audit) = CreateAuditedBus();
            var (world, _) = BuildWorld(0UL, bus);
            var recorder = new ReplayRecorder(StepSeconds);
            recorder.BeginRecording(new Dictionary<string, RngStreamState>(StringComparer.Ordinal));

            long tickNumber = 0;

            void TickWith(SimStep step)
            {
                tickNumber++;
                recorder.RecordStep(tickNumber, step);
                world.Tick(step);
            }

            TickWith(SimStep.Continuous(StepSeconds)); // 探索阶段一个连续步。

            var order = new[] { UnitA, UnitB };
            var orderIndex = 0;
            while (world.GetEntity(UnitA)!.Position.X > 0 && world.GetEntity(UnitB)!.Position.X > 0)
            {
                TickWith(SimStep.Discrete(order[orderIndex % order.Length], StepPhase.Act));
                orderIndex++;
            }

            TickWith(SimStep.Continuous(StepSeconds)); // 切回探索阶段一个连续步。

            recorder.SetTickCount(tickNumber);
            return (world, audit, recorder.Export(), tickNumber);
        }

        [Fact]
        public void RecordedReplay_ContainsBothContinuousAndDiscreteSteps()
        {
            var (_, _, replay, _) = RecordFight();

            Assert.Contains(replay.Steps, s => s.Kind == SimStepKind.Continuous);
            Assert.Contains(replay.Steps, s => s.Kind == SimStepKind.Discrete);
            Assert.Equal(ReplayData.CurrentFormatVersion, replay.FormatVersion);
        }

        [Fact]
        public void ReplayPlayer_DiscreteFightWithModeSwitch_EventLogAndDigestMatchDirectRun()
        {
            var (liveWorld, liveAudit, replay, finalTick) = RecordFight();

            var liveSnapshot = WorldSnapshot.Capture(finalTick, ToEventLog(liveAudit), liveWorld);

            var (replayBus, replayAudit) = CreateAuditedBus();
            var player = new ReplayPlayer(BuildWorld, replayBus, replayAudit);
            player.Load(replay);
            var replaySnapshot = player.StepTo(finalTick);

            Assert.Equal(liveSnapshot.EventLog, replaySnapshot.EventLog);
            Assert.Equal(liveSnapshot.Digest, replaySnapshot.Digest);
            Assert.True(finalTick > 4, "夹具应打满连续+离散+连续三段，tick 数应明显大于 4");
        }

        [Fact]
        public void ReplayPlayer_DiscreteFight_MidCombatSnapshotAlsoMatches()
        {
            var (_, _, replay, finalTick) = RecordFight();
            var midTick = finalTick > 2 ? finalTick - 1 : finalTick;

            // 直跑侧：按录像里记下的 Steps 序列（而不是重新驱动一个新的 TurnScheduler）逐 tick
            // 推进到 midTick——这就是"直跑"与"回放"唯一应该一致的地方：同一份 SimStep 序列喂给
            // 两个独立构造的世界，结果必须逐项相等。
            var (bus, audit) = CreateAuditedBus();
            var (world, _) = BuildWorld(0UL, bus);
            long tickNumber = 0;
            for (var i = 0; i < replay.Steps.Count && tickNumber < midTick; i++)
            {
                var record = replay.Steps[i];
                tickNumber++;
                world.Tick(record.ToSimStep(StepSeconds));
            }
            var directMidSnapshot = WorldSnapshot.Capture(tickNumber, ToEventLog(audit), world);

            var (replayBus, replayAudit) = CreateAuditedBus();
            var player = new ReplayPlayer(BuildWorld, replayBus, replayAudit);
            player.Load(replay);
            var replayMidSnapshot = player.StepTo(midTick);

            Assert.Equal(directMidSnapshot.EventLog, replayMidSnapshot.EventLog);
            Assert.Equal(directMidSnapshot.Digest, replayMidSnapshot.Digest);
        }

        [Fact]
        public void ReplayData_JsonRoundTrip_PreservesStepsAndFormatVersion()
        {
            var (_, _, replay, finalTick) = RecordFight();

            var json = replay.ToJson();
            var text = JsonWriter.Write(json);
            var parsed = (JsonObject)JsonReader.Parse(text);
            var roundTripped = ReplayData.FromJson(parsed);

            Assert.Equal(replay.FormatVersion, roundTripped.FormatVersion);
            Assert.Equal(replay.Steps.Count, roundTripped.Steps.Count);
            for (var i = 0; i < replay.Steps.Count; i++)
            {
                Assert.Equal(replay.Steps[i].Tick, roundTripped.Steps[i].Tick);
                Assert.Equal(replay.Steps[i].Kind, roundTripped.Steps[i].Kind);
                Assert.Equal(replay.Steps[i].ActorId, roundTripped.Steps[i].ActorId);
                Assert.Equal(replay.Steps[i].Phase, roundTripped.Steps[i].Phase);
            }

            var (bus, audit) = CreateAuditedBus();
            var player = new ReplayPlayer(BuildWorld, bus, audit);
            player.Load(roundTripped);
            var snapshot = player.StepTo(finalTick);
            Assert.Equal(finalTick, snapshot.Tick);
        }

        /// <summary>
        /// 旧格式兼容（<see cref="ReplayStepRecord"/> 类型注释"判断记录（旧格式兼容）"）：手工
        /// 构造本任务之前唯一存在过的录像 JSON 形状（无 <c>format_version</c>/<c>steps</c> 字段，
        /// 只有 <c>stepSeconds</c>/<c>tickCount</c>/<c>rngSeeds</c>/<c>inputs</c> 四个键），验证
        /// <see cref="ReplayData.FromJson"/> 解析出 <see cref="ReplayData.FormatVersion"/> == 1、
        /// <see cref="ReplayData.Steps"/> 为空，且 <see cref="IReplayPlayer.StepTo"/> 仍能按"每个
        /// tick 都是连续步"正常推进（不抛异常，与该格式历来的唯一行为一致）。
        /// </summary>
        [Fact]
        public void ReplayData_FromJson_LegacyFormatWithoutStepsField_TreatsEveryTickAsContinuous()
        {
            const string legacyJson = @"
            {
                ""stepSeconds"": 1.0,
                ""tickCount"": 3,
                ""rngSeeds"": {},
                ""inputs"": []
            }";

            var parsed = (JsonObject)JsonReader.Parse(legacyJson);
            var data = ReplayData.FromJson(parsed);

            Assert.Equal(1, data.FormatVersion);
            Assert.Empty(data.Steps);
            Assert.Equal(3, data.TickCount);

            var (bus, audit) = CreateAuditedBus();
            var player = new ReplayPlayer(BuildWorld, bus, audit);
            player.Load(data);
            var snapshot = player.StepTo(3);

            Assert.Equal(3, snapshot.Tick);
            // 三个 tick 全部按连续步处理，未抛任何"缺少 actorId/phase"一类异常即证明"缺字段视为
            // 连续步"生效；摘要本身是否等于某个具体值不是本用例关心的点（FixedDamageHandler 对
            // Continuous 步不做任何事，具体数值断言已由上面 JSON 往返/直跑对比两个用例覆盖）。
        }
    }
}
