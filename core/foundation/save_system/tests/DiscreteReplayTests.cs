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
    /// <see cref="Entity.Position"/>.<c>X</c> 承载"HP"，一个自定义 <see cref="ITickPhaseHandler"/>
    /// 在离散步里对"对手"造成固定伤害——全程无随机数、无玩家参与者，专注验证录像/回放机制本身
    /// （具体游戏规则的确定性回归见 <c>core/gameplay/tests/Discrete/DiscreteTickTests.cs</c>）。
    /// </para>
    /// <para>
    /// 判断记录（离散模式回放完整性任务：本类新增一组不经"手工交替行动者"的用例）：上一段所指的
    /// <see cref="RecordFight"/> 一组用例手工交替两个单位产生 <see cref="SimStep.Discrete"/>，不
    /// 构造 <see cref="TurnScheduler"/>（见该方法判断记录"不经 TurnScheduler"）；本任务新增
    /// <see cref="BuildWorldWithScheduler"/>/<see cref="RecordFightWithScheduler"/> 一组用例，
    /// 真正装配并驱动一个 <see cref="TurnScheduler"/>（<c>initiative_stat</c> 策略），验证
    /// <see cref="IReplayPlayer.LoadDiscrete"/> 播放路径——两组用例分工不同、互不替代：前者验证
    /// "SimStep 序列本身能不能被忠实录制/重放"这一更底层的机制，后者验证"离散模式下由
    /// TurnScheduler 决定行动顺序，重放能否自然复现同一顺序"这一 03 §3.2 步骤 6 要求的性质。
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
            recorder.BeginRecording(0UL, new Dictionary<string, RngStreamState>(StringComparer.Ordinal));

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

        // -----------------------------------------------------------------
        // 离散模式回放完整性任务新增：TurnScheduler 真正驱动（见类型顶部判断记录）——
        // IReplayPlayer.LoadDiscrete 播放路径的验证。
        // -----------------------------------------------------------------

        public const int SchedulerDrivenMaxSteps = 20;

        /// <summary><c>Core.Foundation.SaveSystem.DiscreteWorldFactory</c> 形状：装配一个真正的
        /// <see cref="TurnScheduler"/>（<c>initiative_stat</c> 策略，<see cref="UnitA"/> 先攻高于
        /// <see cref="UnitB"/>，先手）并 <c>BeginCombat</c>，供 <see cref="RecordFightWithScheduler"/>
        /// /直跑循环/<see cref="IReplayPlayer.LoadDiscrete"/> 共用。</summary>
        private static (IWorldSim World, IRngHost Rng, TurnScheduler Scheduler) BuildWorldWithScheduler(ulong masterSeed, IEventBus bus)
        {
            var world = new WorldSim(bus);
            world.AddEntity(new TestEntity(UnitA, MapId) { Position = new Vec2(AHp, 0) });
            world.AddEntity(new TestEntity(UnitB, MapId) { Position = new Vec2(BHp, 0) });
            world.RegisterPhaseHandler(TickPhase.CombatResolution, new FixedDamageHandler());

            double Initiative(Id id) => id.Equals(UnitA) ? 20.0 : 10.0;
            bool IsExternalActor(Id id) => true; // 两个测试实体都不接 AI，全靠外部（脚本/录像）提交意图。

            var scheduler = new TurnScheduler(world, Initiative, IsExternalActor, bus);
            scheduler.Configure(InitiativePolicy.InitiativeStat, new Dictionary<string, object>(StringComparer.Ordinal));
            scheduler.BeginCombat(new[] { UnitA, UnitB });

            return (world, new RngHost(masterSeed), scheduler);
        }

        /// <summary>直接驱动一遍（不经录像）：反复 <c>NextStep</c>，为空即代表轮到的行动者需要外部
        /// 输入，提交一个内容无关紧要的 <c>"act"</c> 意图（<see cref="FixedDamageHandler"/> 只看
        /// <c>step.ActorId</c>，不看意图内容）；非空即 <c>Tick</c> + <c>NotifyStepConsumed</c>。
        /// 与 <see cref="Core.Gameplay.Assembly.GameplayAssembly.Advance"/>/<see cref="IReplayPlayer.LoadDiscrete"/>
        /// 同一驱动算法（见后者判断记录）。直到一方"HP"（<c>Position.X</c>）归零或到达
        /// <see cref="SchedulerDrivenMaxSteps"/> 步数上限。</summary>
        private static (IWorldSim World, InMemoryEventAudit Audit, long FinalTick) RunFightWithScheduler()
        {
            var (bus, audit) = CreateAuditedBus();
            var (world, _, scheduler) = BuildWorldWithScheduler(0UL, bus);

            long ticksAdvanced = 0;
            while (ticksAdvanced < SchedulerDrivenMaxSteps
                   && world.GetEntity(UnitA)!.Position.X > 0 && world.GetEntity(UnitB)!.Position.X > 0)
            {
                var step = scheduler.NextStep();
                if (step == null)
                {
                    var actorId = scheduler.GetCurrentActor()!.Value;
                    scheduler.SubmitIntent(actorId, new Intent(actorId, "act", new JsonObjectBuilder().Build()));
                    continue;
                }

                world.Tick(step.Value);
                scheduler.NotifyStepConsumed(step.Value.ActorId!.Value);
                ticksAdvanced++;
            }

            return (world, audit, ticksAdvanced);
        }

        /// <summary>录制一遍同一场战斗，产出 <see cref="ReplayData"/>——只记"第几个 tick、哪个行动者
        /// 提交了什么意图"（<see cref="IReplayRecorder.RecordInput"/>），不记录 <c>TurnScheduler</c>
        /// 算出的行动者/阶段（见 <see cref="IReplayPlayer.LoadDiscrete"/> 判断记录）。</summary>
        private static ReplayData RecordFightWithScheduler()
        {
            var bus = CreateAuditedBus().Bus;
            var (world, _, scheduler) = BuildWorldWithScheduler(0UL, bus);
            var recorder = new ReplayRecorder(StepSeconds);
            recorder.BeginRecording(0UL, new Dictionary<string, RngStreamState>(StringComparer.Ordinal));

            long ticksAdvanced = 0;
            while (ticksAdvanced < SchedulerDrivenMaxSteps
                   && world.GetEntity(UnitA)!.Position.X > 0 && world.GetEntity(UnitB)!.Position.X > 0)
            {
                var step = scheduler.NextStep();
                if (step == null)
                {
                    var actorId = scheduler.GetCurrentActor()!.Value;
                    var tickNumber = ticksAdvanced + 1;
                    var args = new JsonObjectBuilder().Build();
                    recorder.RecordInput(tickNumber, new ReplayInputRecord(tickNumber, actorId, "act", args));
                    scheduler.SubmitIntent(actorId, new Intent(actorId, "act", args));
                    continue;
                }

                world.Tick(step.Value);
                scheduler.NotifyStepConsumed(step.Value.ActorId!.Value);
                ticksAdvanced++;
            }

            recorder.SetTickCount(ticksAdvanced);
            return recorder.Export();
        }

        [Fact]
        public void ReplayPlayer_LoadDiscrete_TurnSchedulerDrivenReplay_EventLogAndDigestMatchDirectRun()
        {
            var (directWorld, directAudit, finalTick) = RunFightWithScheduler();
            var directSnapshot = WorldSnapshot.Capture(finalTick, ToEventLog(directAudit), directWorld);
            Assert.True(finalTick > 1, "夹具应打满至少几步，tick 数应大于 1");

            var replay = RecordFightWithScheduler();
            var (replayBus, replayAudit) = CreateAuditedBus();
            var player = new ReplayPlayer(BuildWorld, replayBus, replayAudit);
            player.LoadDiscrete(replay, BuildWorldWithScheduler);
            var replaySnapshot = player.StepTo(finalTick);

            Assert.Equal(directSnapshot.EventLog, replaySnapshot.EventLog);
            Assert.Equal(directSnapshot.Digest, replaySnapshot.Digest);
        }

        /// <summary>任务书验收点"录像重放的轮次/行动者序列与直跑一致"：订阅
        /// <see cref="SimTurnStartedEvent"/>，直跑与重放两条路径各自独立收集
        /// <c>(ActorId, RoundIndex)</c> 序列，逐项比较——直接证明重放侧的 <see cref="TurnScheduler"/>
        /// 每一步都算出了与直跑侧相同的"轮到谁"，不是仅终局数值巧合相等。</summary>
        [Fact]
        public void ReplayPlayer_LoadDiscrete_TurnSequence_MatchesDirectRun()
        {
            var directTurns = new List<(string ActorId, int RoundIndex)>();
            var (directBus, directAudit) = CreateAuditedBus();
            directBus.Subscribe<SimTurnStartedEvent>(SimEventKeys.TurnStarted, e => directTurns.Add((e.ActorId.Value, e.RoundIndex)));
            var (directWorld, _, directScheduler) = BuildWorldWithScheduler(0UL, directBus);

            long ticksAdvanced = 0;
            while (ticksAdvanced < SchedulerDrivenMaxSteps
                   && directWorld.GetEntity(UnitA)!.Position.X > 0 && directWorld.GetEntity(UnitB)!.Position.X > 0)
            {
                var step = directScheduler.NextStep();
                if (step == null)
                {
                    var actorId = directScheduler.GetCurrentActor()!.Value;
                    directScheduler.SubmitIntent(actorId, new Intent(actorId, "act", new JsonObjectBuilder().Build()));
                    continue;
                }

                directWorld.Tick(step.Value);
                directScheduler.NotifyStepConsumed(step.Value.ActorId!.Value);
                ticksAdvanced++;
            }

            var replay = RecordFightWithScheduler();
            var replayTurns = new List<(string ActorId, int RoundIndex)>();
            var (replayBus, replayAudit) = CreateAuditedBus();
            replayBus.Subscribe<SimTurnStartedEvent>(SimEventKeys.TurnStarted, e => replayTurns.Add((e.ActorId.Value, e.RoundIndex)));
            var player = new ReplayPlayer(BuildWorld, replayBus, replayAudit);
            player.LoadDiscrete(replay, BuildWorldWithScheduler);
            player.StepTo(ticksAdvanced);

            Assert.NotEmpty(directTurns);
            Assert.Equal(directTurns, replayTurns);
            Assert.Equal(UnitA.Value, directTurns[0].ActorId); // 先攻更高的 UnitA 先手。
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

        // -----------------------------------------------------------------
        // FND-06 收口回归（外部审核 code-review.md）：同一 ReplayPlayer 实例二次 Load 此前既不清空
        // 事件审计基线、也不释放旧世界的 EventBus 订阅——见 ReplayPlayer.cs 类型顶部
        // "_auditBaselineIndex"/"DisposeCurrentWorld" 判断记录。
        // -----------------------------------------------------------------

        [Fact]
        public void ReplayPlayer_SecondLoad_DisposesPreviousWorld_AndSecondPlaybackEventLogMatchesDirectRun_NotDoubled()
        {
            var capturedWorlds = new List<WorldSim>();

            (IWorldSim World, IRngHost Rng) BuildWorldCapturing(ulong masterSeed, IEventBus bus)
            {
                var world = new WorldSim(bus);
                world.AddEntity(new TestEntity(UnitA, MapId) { Position = new Vec2(AHp, 0) });
                world.AddEntity(new TestEntity(UnitB, MapId) { Position = new Vec2(BHp, 0) });
                world.RegisterPhaseHandler(TickPhase.CombatResolution, new FixedDamageHandler());
                capturedWorlds.Add(world);
                return (world, new RngHost(masterSeed));
            }

            var (liveWorld, liveAudit, replay, finalTick) = RecordFight();
            var liveSnapshot = WorldSnapshot.Capture(finalTick, ToEventLog(liveAudit), liveWorld);

            var (replayBus, replayAudit) = CreateAuditedBus();
            var player = new ReplayPlayer(BuildWorldCapturing, replayBus, replayAudit);

            player.Load(replay);
            var firstSnapshot = player.StepTo(finalTick);
            Assert.Equal(liveSnapshot.EventLog, firstSnapshot.EventLog);
            Assert.Single(capturedWorlds);
            Assert.False(capturedWorlds[0].IsDisposed);

            // 复现前置条件（对应 FND-06"触发"描述）：同一实例二次 Load 同一份录像。
            player.Load(replay);

            // 1) 旧世界应已被释放（不再因构造函数里的 sim.round_ended 订阅被 bus 强引用存活）。
            Assert.Equal(2, capturedWorlds.Count);
            Assert.True(capturedWorlds[0].IsDisposed, "旧世界应在第二次 Load 时被释放（FND-06）");
            Assert.False(capturedWorlds[1].IsDisposed);

            // 2) 第二次播放的 EventLog/Digest 应与直跑完全一致——不夹带第一次播放遗留的事件 key
            //    （修复前 BuildEventLog 恒从 _audit.Records[0] 开始，第二次的 EventLog 长度会是
            //    直跑的两倍，且 Digest 必然不等）。
            var secondSnapshot = player.StepTo(finalTick);
            Assert.Equal(liveSnapshot.EventLog, secondSnapshot.EventLog);
            Assert.Equal(liveSnapshot.Digest, secondSnapshot.Digest);
            Assert.Equal(firstSnapshot.EventLog, secondSnapshot.EventLog);
            Assert.Equal(firstSnapshot.Digest, secondSnapshot.Digest);

            // Dispose：显式终结点释放当前世界，之后任何调用都拒绝静默继续。
            player.Dispose();
            Assert.True(capturedWorlds[1].IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => player.StepTo(finalTick));
            Assert.Throws<ObjectDisposedException>(() => player.Load(replay));

            // Dispose 幂等：重复调用不抛异常。
            player.Dispose();
        }
    }
}
