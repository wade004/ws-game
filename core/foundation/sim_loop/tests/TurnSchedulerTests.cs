using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="TurnScheduler"/>（ADR-0013、03_运行时骨架.md 第 3.2、9 节）的行为测试：三种先攻
    /// 策略排序与稳定性、玩家回合等待、提交意图产步、行动点耗尽自动结束、轮结束事件与重排、
    /// 存读档往返。
    /// </summary>
    public sealed class TurnSchedulerTests
    {
        private static readonly Id Hero = new Id("unit.hero");
        private static readonly Id Ally = new Id("unit.ally");
        private static readonly Id Foe1 = new Id("unit.foe_1");
        private static readonly Id Foe2 = new Id("unit.foe_2");

        private static (TurnScheduler scheduler, WorldSim world, IEventBus bus, List<IEvent> events) Build(
            Func<Id, double>? initiativeStatProvider = null,
            Func<Id, bool>? isPlayerActor = null,
            Func<Id, bool>? isBusyContinuing = null)
        {
            var bus = SimLoopTestSupport.CreateBus();
            var events = new List<IEvent>();
            bus.Subscribe(SimEventKeys.TurnStarted, e => events.Add(e));
            bus.Subscribe(SimEventKeys.TurnEnded, e => events.Add(e));
            bus.Subscribe(SimEventKeys.RoundEnded, e => events.Add(e));
            bus.Subscribe(SimEventKeys.AwaitingInput, e => events.Add(e));

            var world = new WorldSim(bus);
            var scheduler = new TurnScheduler(
                world,
                initiativeStatProvider ?? (id => 0),
                isPlayerActor ?? (id => false),
                bus,
                isBusyContinuing);

            return (scheduler, world, bus, events);
        }

        // -----------------------------------------------------------------
        // 先攻策略：initiative_stat
        // -----------------------------------------------------------------

        [Fact]
        public void InitiativeStat_OrdersDescendingByStatValue()
        {
            var stats = new Dictionary<Id, double> { [Hero] = 10, [Ally] = 30, [Foe1] = 20 };
            var (scheduler, _, _, _) = Build(id => stats[id]);

            scheduler.Configure(InitiativePolicy.InitiativeStat, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Hero, Ally, Foe1 });

            Assert.Equal(new[] { Ally, Foe1, Hero }, scheduler.GetOrder());
        }

        [Fact]
        public void InitiativeStat_TiesBreakByIdOrdinal_Stable()
        {
            var (scheduler, _, _, _) = Build(id => 10); // 全部同值

            scheduler.Configure(InitiativePolicy.InitiativeStat, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe2, Foe1, Hero });

            // 同值按 Id 字典序（Ordinal）升序：unit.foe_1 < unit.foe_2 < unit.hero
            Assert.Equal(new[] { Foe1, Foe2, Hero }, scheduler.GetOrder());
        }

        // -----------------------------------------------------------------
        // 先攻策略：fixed_order
        // -----------------------------------------------------------------

        [Fact]
        public void FixedOrder_KeepsParticipantsOrderAsPassed()
        {
            var (scheduler, _, _, _) = Build();

            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1, Hero, Ally });

            Assert.Equal(new[] { Foe1, Hero, Ally }, scheduler.GetOrder());
        }

        // -----------------------------------------------------------------
        // 先攻策略：action_points
        // -----------------------------------------------------------------

        [Fact]
        public void ActionPoints_SameActor_RepeatsStepsUntilBudgetExhausted_ThenAutoEndsTurn()
        {
            var (scheduler, world, _, events) = Build(isPlayerActor: id => false);

            scheduler.Configure(InitiativePolicy.ActionPoints,
                new Dictionary<string, object> { ["action_points_per_turn"] = 2.0 });
            scheduler.BeginCombat(new[] { Hero, Ally });

            // 第一步：Hero 的第 1 个行动。
            var step1 = scheduler.NextStep();
            Assert.NotNull(step1);
            Assert.Equal(Hero, step1!.Value.ActorId);
            world.Tick(step1.Value);
            scheduler.NotifyStepConsumed(Hero);

            // 行动点还剩 1，仍是 Hero 的回合。
            Assert.Equal(Hero, scheduler.GetCurrentActor());

            var step2 = scheduler.NextStep();
            Assert.NotNull(step2);
            Assert.Equal(Hero, step2!.Value.ActorId);
            world.Tick(step2.Value);
            scheduler.NotifyStepConsumed(Hero);

            // 行动点耗尽，自动切到下一行动者 Ally。
            Assert.Equal(Ally, scheduler.GetCurrentActor());
            Assert.Contains(events, e => e is SimTurnEndedEvent ended && ended.ActorId.Equals(Hero));
        }

        /// <summary>GP-PRES-09 收口回归：<see cref="TurnScheduler.GetActionPointsRemaining"/> 是不
        /// 产生副作用的只读查询（不像 <see cref="TurnScheduler.TryConsumeActionPoints"/> 那样真的
        /// 扣减），供 HUD 一类只读消费方使用；不在战斗中或未参战的 id 返回 0，不抛异常。</summary>
        [Fact]
        public void GetActionPointsRemaining_ReflectsLedger_WithoutConsuming_ReturnsZeroWhenNotTracked()
        {
            var (scheduler, _, _, _) = Build();

            // 未开战：任意 id 恒 0，不抛异常。
            Assert.Equal(0.0, scheduler.GetActionPointsRemaining(Hero));

            scheduler.Configure(InitiativePolicy.ActionPoints,
                new Dictionary<string, object> { ["action_points_per_turn"] = 3.0 });
            scheduler.BeginCombat(new[] { Hero, Ally });

            Assert.Equal(3.0, scheduler.GetActionPointsRemaining(Hero));
            Assert.Equal(3.0, scheduler.GetActionPointsRemaining(Ally));

            // 只读查询本身不消耗；重复调用结果不变。
            Assert.Equal(3.0, scheduler.GetActionPointsRemaining(Hero));

            Assert.True(scheduler.TryConsumeActionPoints(Hero, 1.0));
            Assert.Equal(2.0, scheduler.GetActionPointsRemaining(Hero));
            Assert.Equal(3.0, scheduler.GetActionPointsRemaining(Ally)); // 未受影响。

            // 未参战的 id：不在账本里，返回 0。
            Assert.Equal(0.0, scheduler.GetActionPointsRemaining(Foe1));
        }

        [Fact]
        public void ActionPoints_ExplicitEndTurn_EndsEarlyEvenWithBudgetRemaining()
        {
            var (scheduler, _, _, _) = Build();

            scheduler.Configure(InitiativePolicy.ActionPoints,
                new Dictionary<string, object> { ["action_points_per_turn"] = 5.0 });
            scheduler.BeginCombat(new[] { Hero, Ally });

            scheduler.EndTurn(Hero);

            Assert.Equal(Ally, scheduler.GetCurrentActor());
        }

        // -----------------------------------------------------------------
        // 玩家回合等待（awaiting_input）
        // -----------------------------------------------------------------

        [Fact]
        public void PlayerTurn_WithoutSubmittedIntent_NextStepReturnsNull_AndSignalsAwaitingInputOnce()
        {
            var (scheduler, _, _, events) = Build(isPlayerActor: id => id.Equals(Hero));

            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Hero, Ally });

            Assert.Null(scheduler.NextStep());
            Assert.Null(scheduler.NextStep()); // 重复调用不重复产生 awaiting_input 事件。

            Assert.Single(events, e => e is SimAwaitingInputEvent);
        }

        [Fact]
        public void PlayerTurn_SubmitIntent_ThenNextStepProducesDiscreteStep()
        {
            var (scheduler, world, _, _) = Build(isPlayerActor: id => id.Equals(Hero));

            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Hero, Ally });

            Assert.Null(scheduler.NextStep());

            scheduler.SubmitIntent(Hero, new Intent(Hero, "move"));

            var step = scheduler.NextStep();
            Assert.NotNull(step);
            Assert.Equal(Hero, step!.Value.ActorId);
            Assert.Equal(StepPhase.Act, step.Value.Phase);

            // 意图应已进入世界的待收集队列，供下一次 Tick 的 IntentCollection 阶段消费。
            world.Tick(step.Value);
        }

        [Fact]
        public void PlayerTurn_BusyContinuing_NextStepProducesStep_WithoutNewIntent()
        {
            // H4 补齐（读条跨回合）：即便玩家本回合没有提交新意图，只要 isBusyContinuing 报告
            // "正忙于跨轮动作"，NextStep 也应照常产步，不再等待一个不存在的新意图（也不重复发
            // awaiting_input）。
            var busyIds = new HashSet<Id> { Hero };
            var (scheduler, _, _, events) = Build(
                isPlayerActor: id => id.Equals(Hero),
                isBusyContinuing: id => busyIds.Contains(id));

            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Hero, Ally });

            var step = scheduler.NextStep();
            Assert.NotNull(step);
            Assert.Equal(Hero, step!.Value.ActorId);
            Assert.DoesNotContain(events, e => e is SimAwaitingInputEvent);
        }

        [Fact]
        public void PlayerTurn_NotBusy_StillWaitsForIntent_DespiteIsBusyContinuingDelegate()
        {
            var (scheduler, _, _, _) = Build(
                isPlayerActor: id => id.Equals(Hero),
                isBusyContinuing: id => false); // 委托存在，但报告"不忙"。

            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Hero, Ally });

            Assert.Null(scheduler.NextStep()); // 行为与未传 isBusyContinuing 时一致。
        }

        [Fact]
        public void SubmitIntent_ForNonCurrentActor_Throws()
        {
            var (scheduler, _, _, _) = Build(isPlayerActor: id => true);

            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Hero, Ally });

            Assert.Throws<ArgumentException>(() => scheduler.SubmitIntent(Ally, new Intent(Ally, "move")));
        }

        // -----------------------------------------------------------------
        // 非玩家（AI）行动者：每步一回合，自动推进
        // -----------------------------------------------------------------

        [Fact]
        public void NonPlayerActor_NextStep_ReturnsDiscreteStepImmediately_NoIntentRequired()
        {
            var (scheduler, _, _, _) = Build();

            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1 });

            var step = scheduler.NextStep();
            Assert.NotNull(step);
            Assert.Equal(Foe1, step!.Value.ActorId);
        }

        // -----------------------------------------------------------------
        // 轮结束事件与重排
        // -----------------------------------------------------------------

        [Fact]
        public void RoundEnds_AfterAllActorsConsumed_FiresRoundEndedEvent_ThenStartsNextRound()
        {
            var (scheduler, world, _, events) = Build();

            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1, Foe2 });

            RunOneStep(scheduler, world); // Foe1
            RunOneStep(scheduler, world); // Foe2 → 轮结束

            Assert.Contains(events, e => e is SimRoundEndedEvent ended && ended.RoundIndex == 0);
            Assert.Equal(1, scheduler.RoundIndex);
            Assert.Equal(Foe1, scheduler.GetCurrentActor()); // 未开启重排，回到队首同一个人。
        }

        [Fact]
        public void RoundEnds_WithResortEachRound_ReSortsByInitiativeStat()
        {
            var stats = new Dictionary<Id, double> { [Foe1] = 10, [Foe2] = 20 };
            var (scheduler, world, _, _) = Build(id => stats[id]);

            scheduler.Configure(InitiativePolicy.InitiativeStat,
                new Dictionary<string, object> { ["resort_each_round"] = true });
            scheduler.BeginCombat(new[] { Foe1, Foe2 });
            Assert.Equal(new[] { Foe2, Foe1 }, scheduler.GetOrder());

            // 战斗中途先攻反转。
            stats[Foe1] = 99;

            RunOneStep(scheduler, world); // Foe2
            RunOneStep(scheduler, world); // Foe1 → 轮结束、重排

            Assert.Equal(new[] { Foe1, Foe2 }, scheduler.GetOrder());
        }

        // -----------------------------------------------------------------
        // EndCombat
        // -----------------------------------------------------------------

        [Fact]
        public void EndCombat_ClearsOrderAndCurrentActor()
        {
            var (scheduler, _, _, _) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Hero });

            scheduler.EndCombat();

            Assert.Empty(scheduler.GetOrder());
            Assert.Null(scheduler.GetCurrentActor());
            Assert.Null(scheduler.NextStep());
        }

        // -----------------------------------------------------------------
        // AddParticipant/RemoveParticipant：中途加入/离场（ADR-0013 补齐任务）
        // -----------------------------------------------------------------

        [Fact]
        public void AddParticipant_FixedOrder_AppendsToEndOfOrder()
        {
            var (scheduler, _, _, _) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1, Foe2 });

            scheduler.AddParticipant(Hero);

            Assert.Equal(new[] { Foe1, Foe2, Hero }, scheduler.GetOrder());
        }

        /// <summary>FND-05 收口回归（外部审核 code-review.md，验证复现 validation-repros.txt R5）：
        /// fixed_order 分支此前在 <c>_order.Add(id)</c> 后直接 return，跳过了给中途加入的参与者
        /// 分配本轮行动点账本这一步（该步骤在 initiative_stat/action_points 两个分支能正常执行，
        /// 只有 fixed_order 因为提前 return 漏掉了）——中途加入的单位因此
        /// <see cref="ITurnScheduler.GetActionPointsRemaining"/> 恒为 0，
        /// <see cref="ITurnScheduler.TryConsumeActionPoints"/>（供移动预算等系统使用，见该方法
        /// 判断记录"先攻策略只决定顺序，不决定是否记账"）恒返回 false。</summary>
        [Fact]
        public void AddParticipant_FixedOrder_GrantsFullActionPointBudget_LikeOtherPolicies()
        {
            var (scheduler, _, _, _) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder,
                new Dictionary<string, object> { ["action_points_per_turn"] = 3.0 });
            scheduler.BeginCombat(new[] { Foe1 });

            scheduler.AddParticipant(Hero);

            Assert.Equal(3.0, scheduler.GetActionPointsRemaining(Hero));
            Assert.True(scheduler.TryConsumeActionPoints(Hero, 1.0));
            Assert.Equal(2.0, scheduler.GetActionPointsRemaining(Hero));
        }

        [Fact]
        public void AddParticipant_InitiativeStat_InsertsByValue_AmongFutureActorsOnly_NotAheadOfCurrent()
        {
            var stats = new Dictionary<Id, double> { [Foe1] = 30, [Foe2] = 10, [Ally] = 5, [Hero] = 20 };
            var (scheduler, world, _, _) = Build(id => stats[id]);
            scheduler.Configure(InitiativePolicy.InitiativeStat, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1, Foe2, Ally }); // 降序：Foe1(30), Foe2(10), Ally(5)

            RunOneStep(scheduler, world); // Foe1 已行动，当前指向 Foe2（本轮"正在行动"的位置）。

            // Hero 先攻 20：虽然高于当前行动者 Foe2(10)，但不能插到 Foe2 前面（当前行动位置受
            // 保护，见 AddParticipant 判断记录"不含正在行动的位置本身"）；比未行动的 Ally(5) 高，
            // 应插在 Foe2 与 Ally 之间。
            scheduler.AddParticipant(Hero);

            Assert.Equal(new[] { Foe1, Foe2, Hero, Ally }, scheduler.GetOrder());
        }

        [Fact]
        public void AddParticipant_ActionPoints_InsertsByInitiativeValue_AndGrantsFullBudget()
        {
            var stats = new Dictionary<Id, double> { [Foe1] = 10, [Hero] = 20 };
            var (scheduler, _, _, _) = Build(id => stats[id]);
            scheduler.Configure(InitiativePolicy.ActionPoints,
                new Dictionary<string, object> { ["action_points_per_turn"] = 3.0 });
            scheduler.BeginCombat(new[] { Foe1 });

            // Hero 先攻 20 高于 Foe1(10)，但 Foe1 正是当前行动者（_currentIndex == 0，"正在行动的
            // 位置"受保护，见 AddParticipant 判断记录），插入范围只看 _currentIndex 之后——此时
            // 之后没有任何其它人，Hero 只能排在 Foe1 之后。
            scheduler.AddParticipant(Hero);

            Assert.Equal(new[] { Foe1, Hero }, scheduler.GetOrder());

            // 新参与者应已获得满额行动点：轮到 Hero 时应可以连续行动 3 步才耗尽。
            scheduler.EndTurn(Foe1);
            Assert.Equal(Hero, scheduler.GetCurrentActor());

            for (var i = 0; i < 2; i++)
            {
                var step = scheduler.NextStep();
                Assert.Equal(Hero, step!.Value.ActorId);
                scheduler.NotifyStepConsumed(Hero);
                Assert.Equal(Hero, scheduler.GetCurrentActor()); // 预算未耗尽，仍是 Hero。
            }
        }

        [Fact]
        public void AddParticipant_AlreadyInOrder_IsIdempotent()
        {
            var (scheduler, _, _, _) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1, Foe2 });

            scheduler.AddParticipant(Foe2);

            Assert.Equal(new[] { Foe1, Foe2 }, scheduler.GetOrder());
        }

        [Fact]
        public void AddParticipant_NotInCombat_IsNoOp()
        {
            var (scheduler, _, _, _) = Build();

            scheduler.AddParticipant(Hero);

            Assert.Empty(scheduler.GetOrder());
        }

        [Fact]
        public void RemoveParticipant_FutureActor_ShrinksOrder_CurrentActorUnaffected()
        {
            var (scheduler, _, _, _) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1, Foe2, Hero });

            scheduler.RemoveParticipant(Hero); // Hero 排在最后，尚未行动。

            Assert.Equal(new[] { Foe1, Foe2 }, scheduler.GetOrder());
            Assert.Equal(Foe1, scheduler.GetCurrentActor());
        }

        [Fact]
        public void RemoveParticipant_PastActor_DecrementsCurrentIndex_KeepsCurrentActor()
        {
            var (scheduler, world, _, _) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1, Foe2, Hero });

            RunOneStep(scheduler, world); // Foe1 已行动，当前指向 Foe2。
            scheduler.RemoveParticipant(Foe1); // 移除已经行动过的单位。

            Assert.Equal(new[] { Foe2, Hero }, scheduler.GetOrder());
            Assert.Equal(Foe2, scheduler.GetCurrentActor()); // 当前行动者不变。
        }

        [Fact]
        public void RemoveParticipant_CurrentActor_NotLastInRound_AdvancesToNextWithoutTurnEndedEvent()
        {
            var (scheduler, _, _, events) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1, Foe2, Hero });

            scheduler.RemoveParticipant(Foe1); // 移除的正是当前行动者本人（死于自己回合内）。

            Assert.Equal(new[] { Foe2, Hero }, scheduler.GetOrder());
            Assert.Equal(Foe2, scheduler.GetCurrentActor());
            // 不应为被移除者（Foe1）发 sim.turn_ended（它没有正常结束自己的回合，见方法判断记录）。
            Assert.DoesNotContain(events, e => e is SimTurnEndedEvent ended && ended.ActorId.Equals(Foe1));
            Assert.Contains(events, e => e is SimTurnStartedEvent started && started.ActorId.Equals(Foe2));
        }

        [Fact]
        public void RemoveParticipant_CurrentActor_LastInRound_WrapsToNextRound()
        {
            var (scheduler, world, _, events) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1, Foe2 });

            RunOneStep(scheduler, world); // Foe1 已行动，当前指向 Foe2（本轮最后一位）。
            scheduler.RemoveParticipant(Foe2); // 移除的正是当前行动者，且是本轮最后一位。

            Assert.Equal(new[] { Foe1 }, scheduler.GetOrder());
            Assert.Equal(Foe1, scheduler.GetCurrentActor()); // 回绕到新一轮，唯一剩下的 Foe1 重新变成当前行动者。
            Assert.Equal(1, scheduler.RoundIndex);
            Assert.Contains(events, e => e is SimRoundEndedEvent ended && ended.RoundIndex == 0);
        }

        [Fact]
        public void RemoveParticipant_LastRemainingParticipant_ClearsOrderAndCurrentActor()
        {
            var (scheduler, _, _, _) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1 }); // 唯一参与者。

            scheduler.RemoveParticipant(Foe1);

            Assert.Empty(scheduler.GetOrder());
            Assert.Null(scheduler.GetCurrentActor());
        }

        [Fact]
        public void RemoveParticipant_NotInOrder_IsNoOp()
        {
            var (scheduler, _, _, _) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1 });

            scheduler.RemoveParticipant(Hero);

            Assert.Equal(new[] { Foe1 }, scheduler.GetOrder());
        }

        // -----------------------------------------------------------------
        // atb：预留扩展位
        // -----------------------------------------------------------------

        [Fact]
        public void Configure_AtbPolicy_ThrowsNotSupported()
        {
            var (scheduler, _, _, _) = Build();
            Assert.Throws<NotSupportedException>(() =>
                scheduler.Configure(InitiativePolicy.Atb, new Dictionary<string, object>()));
        }

        // -----------------------------------------------------------------
        // 存读档往返
        // -----------------------------------------------------------------

        [Fact]
        public void SaveThenLoad_RestoresOrderCurrentActorAndRound()
        {
            var (scheduler, world, _, _) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Foe1, Foe2 });
            RunOneStep(scheduler, world); // 推进到 Foe2

            var saved = scheduler.Save();

            var (scheduler2, _, _, _) = Build();
            scheduler2.Load(saved);

            Assert.Equal(scheduler.GetOrder(), scheduler2.GetOrder());
            Assert.Equal(scheduler.GetCurrentActor(), scheduler2.GetCurrentActor());
            Assert.Equal(scheduler.RoundIndex, scheduler2.RoundIndex);
        }

        [Fact]
        public void Load_NullData_EndsCombat()
        {
            var (scheduler, _, _, _) = Build();
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Hero });

            scheduler.Load(JsonNull.Instance);

            Assert.Null(scheduler.GetCurrentActor());
        }

        [Fact]
        public void SectionKey_IsSimTurnState()
        {
            var (scheduler, _, _, _) = Build();
            Assert.Equal("sim.turn_state", scheduler.SectionKey);
        }

        private static void RunOneStep(TurnScheduler scheduler, WorldSim world)
        {
            var step = scheduler.NextStep();
            Assert.NotNull(step);
            world.Tick(step!.Value);
            scheduler.NotifyStepConsumed(step.Value.ActorId!.Value);
        }
    }
}
