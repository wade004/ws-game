using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// H4 补齐（意图路由缺口 1，见 <see cref="WorldSim.AttachDiscreteRouting"/> 判断记录）：
    /// 覆盖 <see cref="WorldSim.SubmitIntent"/> 在 <see cref="TimeModelMode.Discrete"/> 下的路由
    /// 行为——玩家经既有调用链（这里直接模拟为 <c>world.SubmitIntent</c>）提交的意图能正确解除
    /// <c>awaiting_input</c>、产出该行动者的离散步；非当前行动者的意图被拒绝、不入队；未接线/
    /// 连续模式下行为与 H4 之前完全一致（零回归）。另覆盖 H4 补齐（缺口 2）：<see cref="ISimTimers"/>
    /// 在离散模式下按 <c>sim.round_ended</c> 统一推进（每轮 1.0，而不是 Tick 内的 Dt——见
    /// <c>SimTimers.RescaleAll</c> 判断记录：切入离散模式时既有计时器已按
    /// <c>1/seconds_per_turn</c> 折算为"剩余回合数"，新建的计时器在离散作用域下同样直接以整数
    /// 回合为单位（04 第 3.1 节），因此"推进一轮"即数值上推进 1.0，不是 seconds_per_turn）。
    /// </summary>
    public sealed class WorldSimIntentRoutingTests
    {
        private static readonly Id Hero = new Id("unit.hero");
        private static readonly Id Foe = new Id("unit.foe");

        private static (WorldSim world, TurnScheduler scheduler, SimClockHost clock, List<IEvent> events) Build(
            bool attachRouting)
        {
            var bus = SimLoopTestSupport.CreateBus();
            var events = new List<IEvent>();
            bus.Subscribe(SimEventKeys.AwaitingInput, e => events.Add(e));
            bus.Subscribe(SimEventKeys.TurnStarted, e => events.Add(e));
            bus.Subscribe(SimEventKeys.TurnEnded, e => events.Add(e));
            bus.Subscribe(SimEventKeys.RoundEnded, e => events.Add(e));

            var world = new WorldSim(bus);
            var scheduler = new TurnScheduler(world, id => 0, id => id.Equals(Hero), bus);
            var clock = new SimClockHost(world, new SimLoopOptions { StepSeconds = 1.0, MaxCatchUpSteps = 2 })
            {
                Mode = TimeModelMode.Discrete,
            };

            if (attachRouting)
            {
                world.AttachDiscreteRouting(clock, scheduler);
            }

            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Hero, Foe });

            return (world, scheduler, clock, events);
        }

        private static Intent MoveIntent(Id actorId) =>
            new Intent(actorId, "move", new JsonObjectBuilder().Build());

        [Fact]
        public void Discrete_CurrentActorIntent_RoutesToScheduler_UnblocksNextStep()
        {
            var (world, scheduler, _, _) = Build(attachRouting: true);

            // 用一个委托处理器在阶段 1（IntentCollection）期间捕获 CurrentIntents——Tick 结束时
            // （阶段 8 末）CurrentIntents 会被清空，不能等 Tick 返回之后再读（见 IWorldSim.CurrentIntents
            // 注释），所以在 tick 执行期间捕获一份快照。
            var capturedActorIds = new List<Id>();
            world.RegisterPhaseHandler(TickPhase.IntentCollection, new DelegatePhaseHandler((step, w) =>
            {
                foreach (var intent in w.CurrentIntents)
                {
                    capturedActorIds.Add(intent.ActorId);
                }
            }));

            // 轮到 Hero（玩家），NextStep 因为没有待处理意图而返回 null、发出 awaiting_input。
            Assert.Null(scheduler.NextStep());

            // 玩家经既有调用链（CastSkill/MovementHost.Request 等最终都落到 IWorldSim.SubmitIntent）
            // 提交意图——不直接调用 Scheduler.SubmitIntent（那是本任务之前唯一能用的旁路）。
            world.SubmitIntent(MoveIntent(Hero));

            var step = scheduler.NextStep();
            Assert.NotNull(step);
            Assert.Equal(Hero, step!.Value.ActorId);
            Assert.Equal(SimStepKind.Discrete, step.Value.Kind);

            world.Tick(step.Value);
            scheduler.NotifyStepConsumed(step.Value.ActorId!.Value);

            Assert.Equal(new[] { Hero }, capturedActorIds);
            // 回合已推进到下一位（Foe），证明路由成功产出了离散步并被消费——不再卡在 awaiting_input。
            Assert.Equal(Foe, scheduler.GetCurrentActor());
        }

        [Fact]
        public void Discrete_NonCurrentActorIntent_IsRejected_NotEnqueued()
        {
            var (world, scheduler, _, _) = Build(attachRouting: true);

            // 当前行动者是 Hero；给 Foe（不是本回合行动者）提交意图应被拒绝。
            world.SubmitIntent(MoveIntent(Foe));

            // 拒绝不入队：Hero 仍然没有待处理意图，NextStep 仍然返回 null（awaiting_input）。
            Assert.Null(scheduler.NextStep());
            Assert.Contains(world.DiagnosticsWarnings, w => w.Contains("unit.foe"));
        }

        [Fact]
        public void Continuous_SubmitIntent_Unaffected_NoRoutingSideEffects()
        {
            var (world, _, clock, _) = Build(attachRouting: true);
            clock.Mode = TimeModelMode.Continuous;

            var capturedActorIds = new List<Id>();
            world.RegisterPhaseHandler(TickPhase.IntentCollection, new DelegatePhaseHandler((step, w) =>
            {
                foreach (var intent in w.CurrentIntents)
                {
                    capturedActorIds.Add(intent.ActorId);
                }
            }));

            // 连续模式下即便已经 AttachDiscreteRouting，也不做任何路由拦截——直接进入下一 tick 的
            // CurrentIntents，与 H4 之前完全一致。
            world.SubmitIntent(MoveIntent(Hero));
            world.Tick(SimStep.Continuous(1.0 / 60.0));

            Assert.Equal(new[] { Hero }, capturedActorIds);
        }

        [Fact]
        public void NotAttached_DiscreteMode_SubmitIntent_BehavesLikeBeforeH4()
        {
            var (world, scheduler, _, _) = Build(attachRouting: false);

            // 未调用 AttachDiscreteRouting：即便 clock.Mode == Discrete，SubmitIntent 也只是塞进
            // 待处理队列，不做任何路由/拒绝——这正是 H3a 如实记录的缺口 1（本用例锁定"未接线时
            // 行为不变"这条兼容性承诺，不是锁定"缺口本身应该存在"）。
            world.SubmitIntent(MoveIntent(Hero));
            Assert.Null(scheduler.NextStep()); // 调度器完全不知道玩家已提交，仍卡在 awaiting_input。
        }

        [Fact]
        public void RoundEnded_AdvancesWorldTimers_ByOneRound_NotByStepDt()
        {
            var (world, scheduler, _, _) = Build(attachRouting: true);

            var handle = world.Timers.Create(3); // 离散作用域下已是整数回合，见类型注释。

            // 走完一整轮（Hero、Foe 各一步）触发一次 sim.round_ended。
            DriveOneActor(world, scheduler, Hero);
            DriveOneActor(world, scheduler, Foe);

            Assert.Equal(2, world.Timers.Remaining(handle)); // 推进 1.0（一轮），不是 seconds_per_turn。
            Assert.False(world.Timers.IsExpired(handle));
        }

        private static void DriveOneActor(WorldSim world, TurnScheduler scheduler, Id actorId)
        {
            var step = scheduler.NextStep();
            if (step == null)
            {
                world.SubmitIntent(MoveIntent(actorId));
                step = scheduler.NextStep();
            }

            Assert.NotNull(step);
            world.Tick(step!.Value);
            scheduler.NotifyStepConsumed(step.Value.ActorId!.Value);
        }
    }
}
