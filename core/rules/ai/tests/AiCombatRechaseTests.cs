using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>
    /// ADR-0084：<c>combat_to_chase</c> 回追转移（消费方反馈第二十九批，阻塞项）。复现消费方给出的
    /// 最小构造——A 注册 AI 行为、与敌对 B 相距不超过 <c>AttackRange</c> 使 A 进 <c>combat</c>，
    /// 仇恨表里有 B；之后把 B 移到 <c>AttackRange</c> 之外、仍在 <c>leash_range</c> 内——覆盖任务书
    /// 两条不变量：(a) 目标持续超距时单位必须在有限个决策间隔内朝目标移动；(b) 目标停在
    /// <c>AttackRange</c> 边界附近时不得每个决策间隔在 <c>combat</c>/<c>chase</c> 间来回翻转。
    /// 另覆盖 Expr 覆盖 `combat_to_chase` 生效、以及拴绳（<c>leash_range</c>）仍按既有 `chase` 态
    /// 逻辑脱战、不会无限回追。
    /// </summary>
    public class AiCombatRechaseTests
    {
        private static readonly Id Mob = new Id("unit.rechase_mob");
        private static readonly Id Enemy = new Id("unit.rechase_enemy");
        private static readonly Id ProfileId = new Id("ai.profile.rechase_test");

        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.trivial_false"", ""entries"": [
                { ""priority"": 1, ""condition"": ""false"", ""skill_id"": ""skill.never"" }
            ] }
        ]";

        private static string ProfileJson(double perception, double leash, string? combatToChaseOverride = null)
        {
            var transitions = combatToChaseOverride != null
                ? ", \"transitions\": { \"combat_to_chase\": \"" + combatToChaseOverride + "\" }"
                : "";
            return "[{ \"id\": \"ai.profile.rechase_test\", \"perception_radius\": " + perception +
                ", \"leash_range\": " + leash +
                ", \"combat_return_policy\": \"return_to_spawn\"" +
                ", \"rotation_ref\": \"ai.rotation.trivial_false\"" +
                transitions + " }]";
        }

        /// <summary>把 A 直接摆进 combat：登记单位、给仇恨表塞入 Enemy、ForceState 到 Combat——
        /// 与消费方最小构造等价（"与敌对 B 相距不超过 AttackRange 使 A 进 combat，仇恨表里有 B"），
        /// 跳过 idle/chase 的进场过程，只聚焦 combat 态本身的回追判定。</summary>
        private static AiTestHarness EnterCombat(AiOptions? options, double perception = 15, double leash = 20,
            string? combatToChaseOverride = null)
        {
            var harness = AiTestHarness.Build(ProfileJson(perception, leash, combatToChaseOverride), RotationsJson, options: options);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(Enemy, new Vec2(1, 0), AiTestSupport.FactionPlayer); // 1 <= AttackRange 默认 2
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);
            harness.Threat.AddThreat(Mob, Enemy, 10);
            harness.Host.ForceState(Mob, BehaviorState.Combat);
            return harness;
        }

        // -----------------------------------------------------------------
        // 先红后绿的复现用例（不变量 a）：本用例在根治前会一直失败——AiHost.Step 恒返回空意图，
        // GetBehaviorState 恒为 Combat，A 坐标恒不变（消费方描述的"原地反复施法失败"）。
        // -----------------------------------------------------------------

        /// <summary>Step 一次并把返回的 move 意图（如有）应用到 <see cref="Mob"/> 的坐标上，模拟
        /// "上一次产生的 move 意图已被移动系统应用"（惯例同 <see cref="AiTestHarness.MoveUnit"/> 文档）。
        /// 返回本次 Step 的意图列表，供调用方按需断言。</summary>
        private static System.Collections.Generic.IReadOnlyList<Intent> StepAndApplyMove(AiTestHarness harness, double dt)
        {
            var intents = harness.Host.Step(Mob, dt);
            foreach (var intent in intents)
            {
                if (intent.Kind != "move") continue;
                var dx = ((Core.Foundation.Common.Json.JsonNumber)intent.Args["dx"]).Value;
                var dy = ((Core.Foundation.Common.Json.JsonNumber)intent.Args["dy"]).Value;
                var pos = harness.Units.GetPosition(Mob);
                harness.MoveUnit(Mob, new Vec2(pos.X + dx, pos.Y + dy));
            }
            return intents;
        }

        [Fact]
        public void Combat_TargetMovesOutOfAttackRange_MovesTowardTarget_UntilBackInCombat()
        {
            var harness = EnterCombat(null);

            // 消费方最小构造："之后每 tick 把 B 移到距 A 约 10 米（仍在 leash_range 内）"——B 一次性
            // 跳到 10 米外，此后固定不动，只让 A 追。
            harness.MoveUnit(Enemy, new Vec2(10, 0));

            var lastDistance = double.MaxValue;
            var sawChase = false;
            var sawMoveIntent = false;
            var reachedCombatAgain = false;

            for (var tick = 0; tick < 20; tick++)
            {
                var intents = StepAndApplyMove(harness, 0.5);
                var state = harness.Host.GetBehaviorState(Mob);

                if (state == BehaviorState.Chase)
                {
                    sawChase = true;
                    if (intents.Count == 0)
                    {
                        // combat -> chase 转移发生的本 tick：转移本身不产生位移，位移从下一 tick 的
                        // HandleChase 开始（AiHost.Step 逐 tick 状态机推进的既有约定，见
                        // AiStateMachineTests.FullSequence_... 同一模式）。
                        continue;
                    }

                    // 不变量 (a)：目标持续超距时，必须在朝目标移动——非转移 tick 恰好产生一条 move
                    // 意图，坐标确实在变、距离确实在缩小。
                    sawMoveIntent = true;
                    var intent = Assert.Single(intents);
                    Assert.Equal("move", intent.Kind);

                    var newDistance = Vec2.Distance(harness.Units.GetPosition(Mob), harness.Units.GetPosition(Enemy));
                    Assert.True(newDistance < lastDistance, $"距离应单调缩小：上次 {lastDistance}，本次 {newDistance}");
                    lastDistance = newDistance;
                }
                else if (state == BehaviorState.Combat)
                {
                    if (sawChase)
                    {
                        reachedCombatAgain = true;
                        break;
                    }
                }
                else
                {
                    Assert.Fail($"意外状态 {state}（应只在 combat/chase 之间转移）");
                }
            }

            Assert.True(sawChase, "目标脱离攻击范围后单位应回到 chase（combat_to_chase 未生效——根治前的失败症状）");
            Assert.True(sawMoveIntent, "chase 态应产生 move 意图并让坐标朝目标推进（根治前恒为空意图、坐标恒不变）");
            Assert.True(reachedCombatAgain, "追近之后应重新进入 combat（chase_to_combat 应保持不变）");
        }

        // -----------------------------------------------------------------
        // 不变量 (b)：目标停在攻击距离边界附近，不得每个决策间隔在 combat/chase 间翻转。
        // -----------------------------------------------------------------

        [Fact]
        public void Combat_TargetHoversNearAttackRangeBoundary_DoesNotFlapBetweenCombatAndChase()
        {
            var options = new AiOptions { AttackRange = 2.0, CombatChaseHysteresis = 1.0 };
            var harness = EnterCombat(options);

            // 边界值：AttackRange(2) < 2.4 <= AttackRange+Hysteresis(3)——落在滞回区间内，
            // 且带 ±0.05 的亚阈值抖动模拟"停在边界附近"，两侧都不越过 AttackRange 或
            // AttackRange+Hysteresis 任何一个阈值。
            var offsets = new[] { 2.40, 2.45, 2.35, 2.44, 2.36, 2.41 };

            for (var tick = 0; tick < offsets.Length; tick++)
            {
                harness.MoveUnit(Enemy, new Vec2(offsets[tick], 0));
                harness.Host.Step(Mob, 0.5);
                Assert.Equal(BehaviorState.Combat, harness.Host.GetBehaviorState(Mob));
            }

            harness.Dispatch();
            // 全程只有构造时 ForceState 产生的一次 Idle -> Combat，之后再没有任何状态切换事件——
            // 没有 combat -> chase 也没有 chase -> combat 的来回翻转。
            var change = Assert.Single(harness.StateChanges);
            Assert.Equal(BehaviorState.Idle, change.OldState);
            Assert.Equal(BehaviorState.Combat, change.NewState);
        }

        [Fact]
        public void Combat_TargetJustBeyondHysteresisBand_TransitionsToChaseExactlyOnce()
        {
            var options = new AiOptions { AttackRange = 2.0, CombatChaseHysteresis = 1.0 };
            var harness = EnterCombat(options);

            // 3.01 > AttackRange+Hysteresis(3)：真正越过滞回区间上沿，应该触发一次回追，
            // 且此后固定在该距离不再变化，不应该再翻回 combat（chase 态里 3.01 > AttackRange，
            // chase_to_combat 判定不通过）。
            harness.MoveUnit(Enemy, new Vec2(3.01, 0));

            harness.Host.Step(Mob, 0.5); // combat -> chase
            Assert.Equal(BehaviorState.Chase, harness.Host.GetBehaviorState(Mob));

            for (var tick = 0; tick < 5; tick++)
            {
                harness.Host.Step(Mob, 0.5);
                Assert.Equal(BehaviorState.Chase, harness.Host.GetBehaviorState(Mob));
            }

            harness.Dispatch();
            var sequence = harness.StateChanges.Select(e => (e.OldState, e.NewState)).ToList();
            Assert.Equal(new[]
            {
                (BehaviorState.Idle, BehaviorState.Combat),
                (BehaviorState.Combat, BehaviorState.Chase),
            }, sequence);
        }

        // -----------------------------------------------------------------
        // 可覆盖性：Expr 覆盖 combat_to_chase 为恒假，单位不回追。
        // -----------------------------------------------------------------

        [Fact]
        public void Combat_TransitionOverriddenToFalse_SuppressesRechase_UnitStaysInCombat()
        {
            var harness = EnterCombat(null, combatToChaseOverride: "false");

            harness.MoveUnit(Enemy, new Vec2(10, 0)); // 默认判定下本会触发回追

            harness.Host.Step(Mob, 0.5);

            Assert.Equal(BehaviorState.Combat, harness.Host.GetBehaviorState(Mob));
            Assert.NotEmpty(harness.ExprFactory.Calls); // 证明确实走了 IExprHostFactory 而非默认代码判定
        }

        // -----------------------------------------------------------------
        // 拴绳：目标超出 leash_range 时仍按既有逻辑脱战，不因新转移而无限追。
        // -----------------------------------------------------------------

        [Fact]
        public void Combat_TargetBeyondLeashRange_RechasesThenLeashStopsIt_NotInfiniteChase()
        {
            var options = new AiOptions { AttackRange = 2.0, CombatChaseHysteresis = 1.0 };
            var harness = EnterCombat(options, perception: 15, leash: 5);

            // 25 远超 AttackRange+Hysteresis(3) 也远超 leash_range(5)：combat_to_chase 先触发回追；
            // chase 态朝目标推进几个 tick、单位自身离出生点超过 leash_range 后，既有 chase_to_return
            // （leash 判定）应接管，阻止无限朝目标移动（目标本身远在 leash_range 之外，永远追不上）。
            harness.MoveUnit(Enemy, new Vec2(25, 0));

            var reachedReturn = false;
            for (var tick = 0; tick < 10; tick++)
            {
                StepAndApplyMove(harness, 0.5);
                var state = harness.Host.GetBehaviorState(Mob);
                if (state == BehaviorState.Return)
                {
                    reachedReturn = true;
                    break;
                }
                Assert.True(state == BehaviorState.Combat || state == BehaviorState.Chase,
                    $"追击目标远超 leash_range 时应经 chase 被 leash 拦下转 return，不应出现 {state}");
            }

            Assert.True(reachedReturn, "目标远超 leash_range 时应被既有 chase_to_return 拦下，不会无限回追");

            // 拴绳确实生效：单位被拦在远小于"目标距离 25"的地方（容许一个 tick 的移动余量越过
            // leash_range 本身，但不会继续无限朝目标推进）。
            var distanceFromSpawn = Vec2.Distance(harness.Units.GetPosition(Mob), Vec2.Zero);
            Assert.True(distanceFromSpawn < 10.0, $"leash 应远早于追到目标附近就拦下，实测距出生点 {distanceFromSpawn}");

            harness.Dispatch();
            var sequence = harness.StateChanges.Select(e => (e.OldState, e.NewState)).ToList();
            Assert.Equal(new[]
            {
                (BehaviorState.Idle, BehaviorState.Combat),
                (BehaviorState.Combat, BehaviorState.Chase),
                (BehaviorState.Chase, BehaviorState.Return),
            }, sequence);
        }
    }
}
