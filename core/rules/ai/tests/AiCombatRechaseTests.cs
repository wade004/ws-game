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
    /// 两条不变量：(a) 目标持续超距时单位必须在有限个决策间隔内朝目标移动，combat 期间不存在
    /// "目标已超距但单位既不回追也不能打到"的死区；(b) 目标停在 <c>AttackRange</c> 边界附近时不得
    /// 每个决策间隔在 <c>combat</c>/<c>chase</c> 间来回翻转，且这段时间应始终能打到目标。滞回余量
    /// 放在 <c>chase_to_combat</c> 一侧（<see cref="AiOptions.CombatReentryRangeRatio"/>），
    /// <c>combat_to_chase</c> 固定用裸 <c>AttackRange</c>，两个方向阈值不同、构成不对称滞回区间。
    /// 另覆盖 Expr 分别覆盖 <c>combat_to_chase</c>/<c>chase_to_combat</c> 生效（覆盖时完全绕开收紧后
    /// 的默认比例判定）、以及拴绳（<c>leash_range</c>）仍按既有 <c>chase</c> 态逻辑脱战、不会无限回追。
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

        private static string ProfileJson(double perception, double leash, string? combatToChaseOverride = null,
            string? chaseToCombatOverride = null)
        {
            var overrides = new System.Collections.Generic.List<string>();
            if (combatToChaseOverride != null)
            {
                overrides.Add("\"combat_to_chase\": \"" + combatToChaseOverride + "\"");
            }
            if (chaseToCombatOverride != null)
            {
                overrides.Add("\"chase_to_combat\": \"" + chaseToCombatOverride + "\"");
            }
            var transitions = overrides.Count > 0 ? ", \"transitions\": { " + string.Join(", ", overrides) + " }" : "";
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
            string? combatToChaseOverride = null, string? chaseToCombatOverride = null)
        {
            var harness = AiTestHarness.Build(
                ProfileJson(perception, leash, combatToChaseOverride, chaseToCombatOverride),
                RotationsJson, options: options);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(Enemy, new Vec2(1, 0), AiTestSupport.FactionPlayer); // 1 <= AttackRange 默认 2
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);
            harness.Threat.AddThreat(Mob, Enemy, 10);
            harness.Host.ForceState(Mob, BehaviorState.Combat);
            return harness;
        }

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

        /// <summary>驱动一次"目标固定不动、A 从 combat 回追直至再次进 combat"的完整流程，断言不变量
        /// (a)：必须在有限个决策间隔内回到 chase 并朝目标移动、距离单调缩小、最终重新进入 combat。
        /// 供近距离（原死区代表点）与远距离两个场景复用。</summary>
        private static void AssertRechasesUntilBackInCombat(AiTestHarness harness)
        {
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

            Assert.True(sawChase, "目标脱离攻击范围后单位应回到 chase（combat_to_chase 未生效）");
            Assert.True(sawMoveIntent, "chase 态应产生 move 意图并让坐标朝目标推进（根治前恒为空意图、坐标恒不变）");
            Assert.True(reachedCombatAgain, "追近到 AttackRange * CombatReentryRangeRatio 以内后应重新进入 combat");
        }

        // -----------------------------------------------------------------
        // 先红后绿的复现用例（不变量 a）：目标停在旧死区代表点。
        // -----------------------------------------------------------------

        [Fact]
        public void Combat_TargetJustOutsideAttackRange_MovesTowardTarget_UntilBackInCombat()
        {
            var harness = EnterCombat(null); // AttackRange 默认 2，CombatReentryRangeRatio 默认 0.75

            // 2.4：根治前 combat_to_chase 要求 distance > AttackRange + Hysteresis(2+1=3) 才触发回追，
            // 2.4 不满足——单位会卡死在 combat、每次施法 OutOfRange、坐标永不变化，这正是消费方报的
            // 症状（本用例在根治前的实现下应为红）。根治后 combat_to_chase 只要求 distance >
            // AttackRange(2)，2.4 立即满足，combat 期间不存在死区。
            harness.MoveUnit(Enemy, new Vec2(2.4, 0));

            AssertRechasesUntilBackInCombat(harness);
        }

        [Fact]
        public void Combat_TargetFarOutsideAttackRange_MovesTowardTarget_UntilBackInCombat()
        {
            var harness = EnterCombat(null);

            // 消费方最小构造之一："之后每 tick 把 B 移到距 A 约 10 米（仍在 leash_range 内）"——B 一次性
            // 跳到 10 米外，此后固定不动，只让 A 追。远距离场景下根治前的实现也能触发回追（10 远超
            // 旧的死区上沿 3），本用例覆盖的是"机制在更大距离下依然成立"，不是红绿复现证据。
            harness.MoveUnit(Enemy, new Vec2(10, 0));

            AssertRechasesUntilBackInCombat(harness);
        }

        // -----------------------------------------------------------------
        // 不变量 (b)：目标停在攻击距离边界附近，不得每个决策间隔在 combat/chase 间翻转，且这段时间
        // 应始终能打到目标（一直停在 combat）。
        // -----------------------------------------------------------------

        [Fact]
        public void Combat_TargetJittersJustInsideAttackRange_StaysInCombat_NoFlap()
        {
            var harness = EnterCombat(null); // AttackRange=2，CombatReentryRangeRatio=0.75 → 重进战阈值 1.5

            // [AttackRange*Ratio, AttackRange] = [1.5, 2.0]：combat_to_chase 只在 distance >
            // AttackRange(2) 时才触发回追，这段区间内的抖动全部 <= 2，永远不满足回追条件——单位应
            // 全程留在 combat、且始终打得到目标（不是"停在死区打不到"，是"全程能打到"）。
            var offsets = new[] { 1.60, 1.95, 1.55, 1.90, 1.70, 2.00, 1.50, 1.85 };

            var flipCount = 0;
            var previousState = harness.Host.GetBehaviorState(Mob);
            foreach (var offset in offsets)
            {
                harness.MoveUnit(Enemy, new Vec2(offset, 0));
                harness.Host.Step(Mob, 0.5);
                var state = harness.Host.GetBehaviorState(Mob);
                if (state != previousState)
                {
                    flipCount++;
                }
                previousState = state;
                Assert.Equal(BehaviorState.Combat, state); // 全程处于 combat，能打到目标
            }

            Assert.Equal(0, flipCount); // 不变量 (b)：零次 combat/chase 翻转

            harness.Dispatch();
            // 全程只有构造时 ForceState 产生的一次 Idle -> Combat，之后再没有任何状态切换事件——
            // 没有 combat -> chase 也没有 chase -> combat 的来回翻转。
            var change = Assert.Single(harness.StateChanges);
            Assert.Equal(BehaviorState.Idle, change.OldState);
            Assert.Equal(BehaviorState.Combat, change.NewState);
        }

        [Fact]
        public void Combat_TargetJustBeyondAttackRange_TransitionsToChaseExactlyOnce()
        {
            var harness = EnterCombat(null);

            // 2.01 > AttackRange(2)：真正越过裸攻击距离，应该触发一次回追；此后固定在该距离不再
            // 变化（本例不驱动移动，只用原始 Step），也不应该再翻回 combat——chase 态里 2.01 >
            // AttackRange*CombatReentryRangeRatio(1.5)，chase_to_combat 判定不通过。
            harness.MoveUnit(Enemy, new Vec2(2.01, 0));

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
        // 可覆盖性：Expr 分别覆盖 combat_to_chase / chase_to_combat，覆盖时完全绕开收紧后的默认判定。
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

        [Fact]
        public void ChaseToCombat_TransitionOverriddenToTrue_BypassesDefaultRatio_ContentDataUnaffectedByChange()
        {
            // ADR-0084：chase_to_combat 的默认判定收紧为 AttackRange * CombatReentryRangeRatio，但这
            // 只改变"没有覆盖时"的兜底计算——已经用 Expr 覆盖 chase_to_combat 的内容数据完全不受影响，
            // 覆盖表达式本身决定转移是否发生，本次改动不会让被覆盖的判定"意外变严"或"意外变松"。
            var harness = EnterCombat(null, chaseToCombatOverride: "true");

            harness.MoveUnit(Enemy, new Vec2(10, 0));
            harness.Host.Step(Mob, 0.5); // combat -> chase（走 combat_to_chase 默认判定，未被覆盖）
            Assert.Equal(BehaviorState.Chase, harness.Host.GetBehaviorState(Mob));

            // 本 tick 未驱动任何位移，距离仍然是 10，远在 AttackRange*CombatReentryRangeRatio(1.5)
            // 之外——若真的在跑收紧后的默认比例判定，这里必然还留在 chase。chase_to_combat 被覆盖为
            // 恒真，下一次 Step 应该立即回到 combat，证明覆盖生效时完全绕开了默认判定。
            harness.Host.Step(Mob, 0.5);
            Assert.Equal(BehaviorState.Combat, harness.Host.GetBehaviorState(Mob));
            Assert.NotEmpty(harness.ExprFactory.Calls);
        }

        // -----------------------------------------------------------------
        // 拴绳：目标超出 leash_range 时仍按既有逻辑脱战，不因新转移而无限追。
        // -----------------------------------------------------------------

        [Fact]
        public void Combat_TargetBeyondLeashRange_RechasesThenLeashStopsIt_NotInfiniteChase()
        {
            var options = new AiOptions { AttackRange = 2.0 };
            var harness = EnterCombat(options, perception: 15, leash: 5);

            // 25 远超 AttackRange(2) 也远超 leash_range(5)：combat_to_chase 先触发回追；chase 态朝
            // 目标推进几个 tick、单位自身离出生点超过 leash_range 后，既有 chase_to_return（leash
            // 判定）应接管，阻止无限朝目标移动（目标本身远在 leash_range 之外，永远追不上）。
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
