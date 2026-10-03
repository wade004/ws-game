using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Ai;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>
    /// AI 施法路由钩子（ADR-0143，<see cref="IAiCastRouter"/>）：缺省（没有路由）与此前逐位一致；设置路由后，就绪（或只被动作锁/公共冷却挡住）的条目改为提交给路由，
    /// 路由接管即视为决策成功、不再直接施法；路由不接管则回落到直接施法。
    /// </summary>
    public sealed class RotationCastRouterTests
    {
        private static readonly Id Unit = new Id("unit.test_router");
        private static readonly Id RotationId = new Id("ai.rotation.three_entries");

        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.three_entries"", ""entries"": [
                { ""priority"": 30, ""condition"": ""false"", ""skill_id"": ""skill.rotation_a"" },
                { ""priority"": 20, ""condition"": ""true"", ""skill_id"": ""skill.rotation_b"" },
                { ""priority"": 10, ""condition"": ""true"", ""skill_id"": ""skill.rotation_c"" }
            ] }
        ]";

        private sealed class RecordingRouter : IAiCastRouter
        {
            public readonly List<(Id Unit, Id Skill)> Submitted = new List<(Id, Id)>();
            public bool Accept = true;

            public bool TrySubmit(Id unitId, Id skillId, IReadOnlyList<Id> targets)
            {
                Submitted.Add((unitId, skillId));
                return Accept;
            }
        }

        private static RotationEvaluator Build(FakeSkillHost skills)
        {
            var bus = AiTestSupport.CreateBus();
            var registry = AiTestSupport.MakeRegistry(bus, profilesJson: "[]", rotationsJson: RotationsJson, patrolsJson: "[]");
            return new RotationEvaluator(registry, skills, new FakeExprHostFactory());
        }

        private static SkillReadiness Blocked(Id skill, SkillReadinessBlockers blockers) => new SkillReadiness(
            skill, isReady: false, blockingSources: blockers,
            skillCooldownRemaining: null, categoryCooldownRemaining: null, globalCooldownRemaining: null,
            maxCharges: null, currentCharges: null, nextChargeRemaining: null, effectiveCooldownDuration: null);

        [Fact]
        public void NoRouter_CastsDirectly_AsBefore()
        {
            var skills = new FakeSkillHost();
            var evaluator = Build(skills);
            var request = evaluator.Evaluate(Unit, RotationId, null);
            Assert.Equal(new Id("skill.rotation_b"), request!.Value.SkillId);
            Assert.Single(skills.Calls);
        }

        [Fact]
        public void Router_TakesOverTheDecision_AndNoDirectCastHappens()
        {
            var skills = new FakeSkillHost();
            var evaluator = Build(skills);
            var router = new RecordingRouter();
            evaluator.CastRouter = router;

            var request = evaluator.Evaluate(Unit, RotationId, null);

            Assert.Equal(new Id("skill.rotation_b"), request!.Value.SkillId);
            Assert.Equal(new[] { (Unit, new Id("skill.rotation_b")) }, router.Submitted.ToArray());
            Assert.Empty(skills.Calls);
        }

        [Fact]
        public void Router_Declining_FallsBackToTheDirectCast()
        {
            var skills = new FakeSkillHost();
            var evaluator = Build(skills);
            evaluator.CastRouter = new RecordingRouter { Accept = false };

            var request = evaluator.Evaluate(Unit, RotationId, null);

            Assert.Equal(new Id("skill.rotation_b"), request!.Value.SkillId);
            Assert.Single(skills.Calls);
        }

        [Fact]
        public void Router_ActionLockedOrGcdEntriesAreSubmitted_ButCooldownEntriesStillSkipped()
        {
            var b = new Id("skill.rotation_b");

            // 只被动作锁挡住：有路由时照样提交（缓冲会等到能放）；没有路由时跳过（既有行为）。
            var locked = new FakeSkillHost();
            locked.ProgramReadiness(b, Blocked(b, SkillReadinessBlockers.ActionLocked));
            var routed = Build(locked);
            var router = new RecordingRouter();
            routed.CastRouter = router;
            Assert.Equal(b, routed.Evaluate(Unit, RotationId, null)!.Value.SkillId);
            Assert.Equal(new[] { (Unit, b) }, router.Submitted.ToArray());
            Assert.Empty(locked.Calls);

            var legacySkills = new FakeSkillHost();
            legacySkills.ProgramReadiness(b, Blocked(b, SkillReadinessBlockers.ActionLocked));
            var legacy = Build(legacySkills);
            Assert.Equal(new Id("skill.rotation_c"), legacy.Evaluate(Unit, RotationId, null)!.Value.SkillId);

            // 冷却中：有路由时也跳过（不是时间可解的小等待），改选下一条。
            var cooling = new FakeSkillHost();
            cooling.ProgramReadiness(b, Blocked(b, SkillReadinessBlockers.SkillCooldown));
            var coolRouted = Build(cooling);
            var coolRouter = new RecordingRouter();
            coolRouted.CastRouter = coolRouter;
            Assert.Equal(new Id("skill.rotation_c"), coolRouted.Evaluate(Unit, RotationId, null)!.Value.SkillId);
            Assert.Equal(new[] { (Unit, new Id("skill.rotation_c")) }, coolRouter.Submitted.ToArray());
        }
    }
}
