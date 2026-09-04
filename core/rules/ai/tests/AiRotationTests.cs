using Core.Foundation.Common;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>验收标准 1：给定优先级表，AI 决策结果符合预期优先级顺序（06 §6.2、任务书 T2-10）。</summary>
    public class AiRotationTests
    {
        private static readonly Id Unit = new Id("unit.test_mob");
        private static readonly Id ProfileId = new Id("ai.profile.rotation_test");
        private static readonly Id RotationId = new Id("ai.rotation.three_entries");

        private const string ProfilesJson = @"[
            { ""id"": ""ai.profile.rotation_test"", ""perception_radius"": 10, ""leash_range"": 20,
              ""combat_return_policy"": ""stay"", ""rotation_ref"": ""ai.rotation.three_entries"" }
        ]";

        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.three_entries"", ""entries"": [
                { ""priority"": 30, ""condition"": ""false"", ""skill_id"": ""skill.rotation_a"" },
                { ""priority"": 20, ""condition"": ""true"", ""skill_id"": ""skill.rotation_b"" },
                { ""priority"": 10, ""condition"": ""true"", ""skill_id"": ""skill.rotation_c"" }
            ] }
        ]";

        private static AiTestHarness BuildAndRegister()
        {
            var harness = AiTestHarness.Build(ProfilesJson, RotationsJson);
            harness.Units.Add(Unit, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Unit, ProfileId, Vec2.Zero);
            harness.Host.ForceState(Unit, BehaviorState.Combat);
            harness.Bus.DispatchPending(); // 冲掉 idle -> combat 的 ai.state_changed，避免干扰下面的断言
            harness.StateChanges.Clear();
            return harness;
        }

        [Fact]
        public void Evaluate_SkipsFalseCondition_PicksHighestPriorityTrueCondition()
        {
            var harness = BuildAndRegister();

            var request = harness.Host.Evaluate(Unit);

            Assert.NotNull(request);
            Assert.Equal(new Id("skill.rotation_b"), request!.Value.SkillId);
            Assert.Single(harness.Skills.Calls);
            Assert.Equal(new Id("skill.rotation_b"), harness.Skills.Calls[0].skillId);
        }

        [Fact]
        public void Evaluate_OnCooldownEntry_FallsBackToNextPriority()
        {
            var harness = BuildAndRegister();
            harness.Skills.Program(new Id("skill.rotation_b"), CastResult.Fail(CastFailureReason.OnCooldown));

            var request = harness.Host.Evaluate(Unit);

            Assert.NotNull(request);
            Assert.Equal(new Id("skill.rotation_c"), request!.Value.SkillId);
            Assert.Equal(2, harness.Skills.Calls.Count);
            Assert.Equal(new Id("skill.rotation_b"), harness.Skills.Calls[0].skillId);
            Assert.Equal(new Id("skill.rotation_c"), harness.Skills.Calls[1].skillId);
        }

        [Fact]
        public void Evaluate_AllEntriesFail_ReturnsNullAndNoDecisionEvent()
        {
            var harness = BuildAndRegister();
            harness.Skills.Program(new Id("skill.rotation_b"), CastResult.Fail(CastFailureReason.OnCooldown));
            harness.Skills.Program(new Id("skill.rotation_c"), CastResult.Fail(CastFailureReason.InsufficientPower));

            var request = harness.Host.Evaluate(Unit);

            Assert.Null(request);
            harness.Dispatch();
            Assert.Empty(harness.Decisions);
        }

        [Fact]
        public void Evaluate_Success_EnqueuesAiDecisionMadeEventWithSkillIdAsDecisionId()
        {
            var harness = BuildAndRegister();

            harness.Host.Evaluate(Unit);
            harness.Dispatch();

            var decision = Assert.Single(harness.Decisions);
            Assert.Equal(Unit, decision.UnitId);
            Assert.Equal(new Id("skill.rotation_b"), decision.DecisionId);
        }

        [Fact]
        public void Evaluate_UnitNotInCombatState_ReturnsNullWithoutCallingCastSkill()
        {
            var harness = AiTestHarness.Build(ProfilesJson, RotationsJson);
            harness.Units.Add(Unit, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Unit, ProfileId, Vec2.Zero); // 默认态 idle（无巡逻路径）

            var request = harness.Host.Evaluate(Unit);

            Assert.Null(request);
            Assert.Empty(harness.Skills.Calls);
        }
    }
}
