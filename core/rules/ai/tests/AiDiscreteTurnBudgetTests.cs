using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>
    /// W1 收边补齐（A3 审计 #3/#11、DECISIONS.md 拍板 2）：验证 ADR-0013 决策 6"AI 在自己回合求值
    /// 一次 <c>ai.rotation</c>，可连续执行多个满足条件的条目直到本回合行动点耗尽"这一语义的真实
    /// 落地路径——不是 <see cref="AiHost"/> 内部显式感知预算/主动调用 <c>EndTurn</c>，而是
    /// <see cref="TurnScheduler.NotifyStepConsumed"/> 的 <c>action_points</c> 记账机制 +
    /// <see cref="AiTickHandler"/> 被外部反复调用同一个 <see cref="AiHost.Step"/> 入口间接达成
    /// （本测试代替 <c>core/gameplay/assembly.GameplayAssembly.Advance</c> 驱动
    /// <c>NextStep→Tick→NotifyStepConsumed</c> 这条主循环协议，见 <see cref="TurnScheduler"/>
    /// 类型注释"主循环驱动协议"）。且这一"连续行动直到预算耗尽"的语义只在
    /// <see cref="InitiativePolicy.ActionPoints"/> 先攻策略下成立——06 第 6.2 节勘误（拍板 2）：
    /// <see cref="InitiativePolicy.InitiativeStat"/>/<see cref="InitiativePolicy.FixedOrder"/>
    /// 策略下每个行动者一回合固定只执行一个 Rotation 条目。
    /// </summary>
    public sealed class AiDiscreteTurnBudgetTests
    {
        private static readonly Id Unit = new Id("unit.test_mob");
        private static readonly Id Enemy = new Id("unit.test_enemy");
        private static readonly Id ProfileId = new Id("ai.profile.budget_test");

        private const string ProfilesJson = @"[
            { ""id"": ""ai.profile.budget_test"", ""perception_radius"": 10, ""leash_range"": 20,
              ""combat_return_policy"": ""stay"", ""rotation_ref"": ""ai.rotation.single_entry"" }
        ]";

        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.single_entry"", ""entries"": [
                { ""priority"": 10, ""condition"": ""true"", ""skill_id"": ""skill.rotation_hit"" }
            ] }
        ]";

        /// <summary>组好一个"单参与者、恒有仇恨目标、处于 Combat 态"的 AI 单位，并接入一个真实
        /// <see cref="WorldSim"/>（挂 <see cref="AiTickHandler"/> 于 <see cref="TickPhase.AiDecision"/>）
        /// + <see cref="TurnScheduler"/>（<paramref name="policy"/> 策略、<c>isPlayerActor</c> 恒
        /// false——该单位是纯 AI 行动者，不需要等待外部输入）。</summary>
        private static (AiTestHarness Harness, WorldSim World, TurnScheduler Scheduler) Build(
            InitiativePolicy policy, double actionPointsPerTurn)
        {
            var harness = AiTestHarness.Build(ProfilesJson, RotationsJson);
            harness.Units.Add(Unit, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Unit, ProfileId, Vec2.Zero);
            harness.Host.ForceState(Unit, BehaviorState.Combat);
            // 恒有仇恨目标：避免 HandleCombat 在"无威胁且附近无敌对目标"时转入 Return 态，
            // 与本测试要验证的"预算记账"机制无关，属于前置条件搭建。
            harness.Threat.AddThreat(Unit, Enemy, 1);
            harness.Bus.DispatchPending(); // 冲掉 RegisterUnit/ForceState 产生的 ai.state_changed。
            harness.StateChanges.Clear();

            var world = new WorldSim(harness.Bus);
            world.RegisterPhaseHandler(TickPhase.AiDecision, new AiTickHandler(harness.Host));

            var scheduler = new TurnScheduler(
                world, initiativeStatProvider: _ => 0, isPlayerActor: _ => false, harness.Bus);
            scheduler.Configure(policy, new Dictionary<string, object>
            {
                ["action_points_per_turn"] = actionPointsPerTurn,
            });
            scheduler.BeginCombat(new[] { Unit });

            return (harness, world, scheduler);
        }

        /// <summary>驱动一次"NextStep → Tick → NotifyStepConsumed"，即 <see cref="TurnScheduler"/>
        /// 类型注释"主循环驱动协议"的完整一步。</summary>
        private static void DriveOneStep(WorldSim world, TurnScheduler scheduler, Id actorId)
        {
            var step = scheduler.NextStep();
            Assert.NotNull(step);
            world.Tick(step!.Value);
            scheduler.NotifyStepConsumed(actorId);
        }

        [Fact]
        public void ActionPointsPolicy_ExecutesRotationRepeatedly_UntilBudgetExhausted_ThenAdvancesRound()
        {
            var (harness, world, scheduler) = Build(InitiativePolicy.ActionPoints, actionPointsPerTurn: 3);

            // 前两步：单参与者场景，行动点账本 3 -> 2 -> 1，均大于 0，NotifyStepConsumed 不推进到
            // 下一行动者——同一回合内连续执行了两次 Rotation 条目。
            for (var i = 0; i < 2; i++)
            {
                DriveOneStep(world, scheduler, Unit);
                Assert.Equal(0, scheduler.RoundIndex);
                Assert.Equal(Unit, scheduler.GetCurrentActor());
            }

            Assert.Equal(2, harness.Skills.Calls.Count);
            Assert.All(harness.Skills.Calls, call => Assert.Equal(new Id("skill.rotation_hit"), call.skillId));

            // 第三步：行动点耗尽（1 -> 0），单参与者场景下 AdvanceToNextActor 直接跨入下一轮
            // （回绕回同一个参与者，但 RoundIndex 递增）——本回合的"连续行动"到此为止，恰好 3 次。
            DriveOneStep(world, scheduler, Unit);

            Assert.Equal(3, harness.Skills.Calls.Count);
            Assert.Equal(1, scheduler.RoundIndex);
        }

        [Theory]
        [InlineData(InitiativePolicy.InitiativeStat)]
        [InlineData(InitiativePolicy.FixedOrder)]
        public void NonActionPointsPolicy_ExecutesOneRotationEntry_PerRound_RegardlessOfActionPointsPerTurn(
            InitiativePolicy policy)
        {
            // action_points_per_turn 特意配得比 action_points 策略那条用例更大（5），验证非
            // action_points 策略下这个参数根本不生效——NotifyStepConsumed 对 InitiativeStat/
            // FixedOrder 无条件推进，与"行动点账本还剩多少"无关（见 TurnScheduler.NotifyStepConsumed
            // 判断记录：`if (_policy == InitiativePolicy.ActionPoints)` 才会走扣减分支）。
            var (harness, world, scheduler) = Build(policy, actionPointsPerTurn: 5);

            DriveOneStep(world, scheduler, Unit);

            Assert.Single(harness.Skills.Calls);
            Assert.Equal(new Id("skill.rotation_hit"), harness.Skills.Calls[0].skillId);
            // 单参与者场景下，"无条件推进到下一行动者"表现为立即跨入下一轮。
            Assert.Equal(1, scheduler.RoundIndex);
        }
    }
}
