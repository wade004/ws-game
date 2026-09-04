using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>验收标准 2：脱离引擎构造固定场景，逐 tick 推进，断言状态转移序列符合预期
    /// （06 §6.5"测试方式"）。测试不依赖任何真实移动系统消费 <c>move</c> 意图——按 README"位移"
    /// 一节的约定，在两次 <see cref="AiHost.Step"/> 之间手动用 <see cref="FakeUnitAccess.SetPosition"/>
    /// 模拟"上一次产生的 move 意图已被移动系统应用"。</summary>
    public class AiStateMachineTests
    {
        private static readonly Id Mob = new Id("unit.state_machine_mob");
        private static readonly Id Enemy = new Id("unit.state_machine_enemy");
        private static readonly Id ProfileId = new Id("ai.profile.state_machine");
        private const string TrivialRotationId = "ai.rotation.trivial_false";

        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.trivial_false"", ""entries"": [
                { ""priority"": 1, ""condition"": ""false"", ""skill_id"": ""skill.never"" }
            ] }
        ]";

        private static string ProfileJson(double perception, double leash, string returnPolicy, double? fleeThreshold = null) =>
            "[{ \"id\": \"ai.profile.state_machine\", \"perception_radius\": " + perception +
            ", \"leash_range\": " + leash +
            ", \"combat_return_policy\": \"" + returnPolicy + "\"" +
            (fleeThreshold.HasValue ? ", \"flee_hp_pct_threshold\": " + fleeThreshold.Value : "") +
            ", \"rotation_ref\": \"" + TrivialRotationId + "\" }]";

        [Fact]
        public void FullSequence_IdleChaseCombatReturnIdle_MatchesExpectedEventOrder()
        {
            var harness = AiTestHarness.Build(ProfileJson(perception: 15, leash: 20, returnPolicy: "return_to_spawn"), RotationsJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(Enemy, new Vec2(5, 0), AiTestSupport.FactionPlayer);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);

            Assert.Equal(BehaviorState.Idle, harness.Host.GetBehaviorState(Mob));

            // idle -> chase：敌对单位进入感知半径。
            harness.Host.Step(Mob, 0.1);
            Assert.Equal(BehaviorState.Chase, harness.Host.GetBehaviorState(Mob));
            Assert.Equal(Enemy, harness.Host.GetTarget(Mob));

            // chase：未进入攻击范围，产生 move 意图，模拟移动系统应用后单位已接近目标。
            var moveIntents = harness.Host.Step(Mob, 1.0);
            Assert.Single(moveIntents);
            harness.MoveUnit(Mob, new Vec2(4, 0));

            // chase -> combat：进入攻击范围（距离 1 <= AttackRange 默认 2）。
            harness.Host.Step(Mob, 0.1);
            Assert.Equal(BehaviorState.Combat, harness.Host.GetBehaviorState(Mob));

            // combat：仇恨表为空但仍能感知到敌人，维持 combat（同时也会按 decision_interval 求值一次
            // Rotation——trivial 条件恒假，不会调用 CastSkill）。
            harness.Host.Step(Mob, 0.1);
            Assert.Equal(BehaviorState.Combat, harness.Host.GetBehaviorState(Mob));
            Assert.Empty(harness.Skills.Calls);

            // combat -> return：敌人死亡且感知范围内再无新目标。
            harness.Units.SetAlive(Enemy, false);
            harness.Host.Step(Mob, 0.1);
            Assert.Equal(BehaviorState.Return, harness.Host.GetBehaviorState(Mob));

            // return：未到达出生点，产生 move 意图；模拟移动系统应用后单位已接近出生点。
            var returnIntents = harness.Host.Step(Mob, 1.0);
            Assert.Single(returnIntents);
            harness.MoveUnit(Mob, new Vec2(0.3, 0));

            // return -> idle：到达出生点（距离 0.3 < ArrivalEpsilon 默认 0.5）。
            harness.Host.Step(Mob, 0.1);
            Assert.Equal(BehaviorState.Idle, harness.Host.GetBehaviorState(Mob));

            harness.Dispatch();
            var sequence = harness.StateChanges.Select(e => (e.OldState, e.NewState)).ToList();
            Assert.Equal(new[]
            {
                (BehaviorState.Idle, BehaviorState.Chase),
                (BehaviorState.Chase, BehaviorState.Combat),
                (BehaviorState.Combat, BehaviorState.Return),
                (BehaviorState.Return, BehaviorState.Idle),
            }, sequence);
        }

        [Fact]
        public void Chase_LeashExceeded_TransitionsToReturn()
        {
            var harness = AiTestHarness.Build(ProfileJson(perception: 15, leash: 3, returnPolicy: "return_to_spawn"), RotationsJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(Enemy, new Vec2(10, 0), AiTestSupport.FactionPlayer);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);

            harness.Host.Step(Mob, 0.1); // idle -> chase
            Assert.Equal(BehaviorState.Chase, harness.Host.GetBehaviorState(Mob));

            harness.MoveUnit(Mob, new Vec2(5, 0)); // 追出脱战范围（leash_range = 3）

            harness.Host.Step(Mob, 0.1);
            Assert.Equal(BehaviorState.Return, harness.Host.GetBehaviorState(Mob));
        }

        [Fact]
        public void Combat_LowHpBelowThreshold_TransitionsToFlee()
        {
            var harness = AiTestHarness.Build(ProfileJson(perception: 15, leash: 20, returnPolicy: "return_to_spawn", fleeThreshold: 0.3), RotationsJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.Powers.RegisterUnit(Mob, AiTestSupport.HealthList);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);
            harness.Host.ForceState(Mob, BehaviorState.Combat);

            harness.Powers.ModifyPower(Mob, WellKnownPowers.Health, -80, new Id("skill.test_hit")); // 100 -> 20 (20%)

            harness.Host.Step(Mob, 0.1);

            Assert.Equal(BehaviorState.Flee, harness.Host.GetBehaviorState(Mob));
        }

        [Fact]
        public void Flee_CaughtUpWithFleeReengage_TransitionsBackToCombat()
        {
            var options = new AiOptions { FleeReengage = true };
            var harness = AiTestHarness.Build(ProfileJson(perception: 15, leash: 20, returnPolicy: "return_to_spawn", fleeThreshold: 0.3), RotationsJson, options: options);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(Enemy, new Vec2(1, 0), AiTestSupport.FactionPlayer); // 距离 1 <= AttackRange 默认 2
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);
            harness.Host.ForceState(Mob, BehaviorState.Flee);

            harness.Host.Step(Mob, 0.1);

            Assert.Equal(BehaviorState.Combat, harness.Host.GetBehaviorState(Mob));
            Assert.Equal(Enemy, harness.Host.GetTarget(Mob));
        }

        [Fact]
        public void Flee_NoFleeReengage_StaysInFleeEvenWhenCaughtUp()
        {
            var options = new AiOptions { FleeReengage = false };
            var harness = AiTestHarness.Build(ProfileJson(perception: 15, leash: 20, returnPolicy: "return_to_spawn", fleeThreshold: 0.3), RotationsJson, options: options);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(Enemy, new Vec2(1, 0), AiTestSupport.FactionPlayer);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);
            harness.Host.ForceState(Mob, BehaviorState.Flee);

            harness.Host.Step(Mob, 0.1);

            Assert.Equal(BehaviorState.Flee, harness.Host.GetBehaviorState(Mob));
        }

        [Fact]
        public void AnyState_UnitDies_TransitionsToDeadAndStopsDeciding()
        {
            var harness = AiTestHarness.Build(ProfileJson(perception: 15, leash: 20, returnPolicy: "return_to_spawn"), RotationsJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);
            harness.Host.ForceState(Mob, BehaviorState.Combat);

            harness.Units.SetAlive(Mob, false);
            var intents = harness.Host.Step(Mob, 0.1);

            Assert.Equal(BehaviorState.Dead, harness.Host.GetBehaviorState(Mob));
            Assert.Empty(intents);

            // dead 态不再决策：再次 Step 不产生任何意图也不再变化状态。
            var again = harness.Host.Step(Mob, 1.0);
            Assert.Empty(again);
            Assert.Equal(BehaviorState.Dead, harness.Host.GetBehaviorState(Mob));
        }

        [Fact]
        public void ForceState_DirectlySwitchesStateAndEnqueuesEvent()
        {
            var harness = AiTestHarness.Build(ProfileJson(perception: 15, leash: 20, returnPolicy: "return_to_spawn"), RotationsJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);

            harness.Host.ForceState(Mob, BehaviorState.Combat);

            Assert.Equal(BehaviorState.Combat, harness.Host.GetBehaviorState(Mob));
            harness.Dispatch();
            var change = Assert.Single(harness.StateChanges);
            Assert.Equal(BehaviorState.Idle, change.OldState);
            Assert.Equal(BehaviorState.Combat, change.NewState);
        }

        [Fact]
        public void SetRotation_SwapsRotationTable_AffectsNextEvaluate()
        {
            const string rotationsJson = @"[
                { ""id"": ""ai.rotation.trivial_false"", ""entries"": [
                    { ""priority"": 1, ""condition"": ""false"", ""skill_id"": ""skill.never"" }
                ] },
                { ""id"": ""ai.rotation.boss_phase_2"", ""entries"": [
                    { ""priority"": 1, ""condition"": ""true"", ""skill_id"": ""skill.boss_nuke"" }
                ] }
            ]";
            var harness = AiTestHarness.Build(ProfileJson(perception: 15, leash: 20, returnPolicy: "return_to_spawn"), rotationsJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);
            harness.Host.ForceState(Mob, BehaviorState.Combat);

            Assert.Null(harness.Host.Evaluate(Mob)); // 原 rotation 条件恒假

            harness.Host.SetRotation(Mob, new Id("ai.rotation.boss_phase_2"));
            var request = harness.Host.Evaluate(Mob);

            Assert.NotNull(request);
            Assert.Equal(new Id("skill.boss_nuke"), request!.Value.SkillId);
        }
    }
}
