using Core.Foundation.Common;
using Core.Rules.Ai;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>
    /// T-N3-10（ADR-0031 决策 12"一键智能释放"；ADR-0035 决策 2）验收标准 6：优先级表独立求值组件
    /// <see cref="RotationEvaluator"/> 不依赖 <c>ai.behavior_profile</c>、不要求 <see
    /// cref="BehaviorState.Combat"/> 态——本文件全程不调用 <c>AiHost.RegisterUnit</c>/
    /// <c>AiHost.ForceState</c>，甚至不构造 <see cref="AiHost"/>，只直接
    /// <c>new RotationEvaluator(registry, skills, exprFactory)</c> 驱动一个"玩家单位"，验证给定一张
    /// <c>ai.rotation</c> 表即可选出技能并经 <see cref="ISkillHost.CastSkill"/> 施放。
    /// <see cref="AiRotationTests"/> 验证的是同一条选择逻辑经 <see cref="AiHost.Evaluate"/> 委托后行为
    /// 不变（Combat 态门控 + ai.decision_made 事件），两个文件互补覆盖 T-N3-10 验收标准 6/AiHost 既有
    /// 测试不变两条要求。
    /// </summary>
    public sealed class RotationEvaluatorTests
    {
        private static readonly Id PlayerUnit = new Id("unit.test_player_driven");
        private static readonly Id RotationId = new Id("ai.rotation.three_entries");

        // 与 AiRotationTests 同一份三条目表：第一条条件恒假，第二条/第三条恒真，按优先级 b 先于 c。
        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.three_entries"", ""entries"": [
                { ""priority"": 30, ""condition"": ""false"", ""skill_id"": ""skill.rotation_a"" },
                { ""priority"": 20, ""condition"": ""true"", ""skill_id"": ""skill.rotation_b"" },
                { ""priority"": 10, ""condition"": ""true"", ""skill_id"": ""skill.rotation_c"" }
            ] }
        ]";

        private static RotationEvaluator BuildEvaluator(FakeSkillHost skills, FakeExprHostFactory exprFactory)
        {
            var bus = AiTestSupport.CreateBus();
            // profilesJson 传 "[]"：验收标准要求"不注册 ai.behavior_profile"，registry 里这张表本就
            // 是空的——RotationEvaluator 构造时也确实从不读取它（只读 ai.rotation/skill.def/
            // target.chain_def，见该类型 LoadRotations）。
            var registry = AiTestSupport.MakeRegistry(bus, profilesJson: "[]", rotationsJson: RotationsJson, patrolsJson: "[]");
            return new RotationEvaluator(registry, skills, exprFactory);
        }

        /// <summary>验收标准 6 第一组："一组条件筛选选中第二条"——首条 condition 恒假被跳过，第二条
        /// （最高优先级里第一个条件为真的）被选中并真的调用一次 <c>CastSkill</c>。全程未注册
        /// <c>ai.behavior_profile</c>、未构造/进入任何 <see cref="BehaviorState"/>。</summary>
        [Fact]
        public void Evaluate_NoProfileNoCombatState_ConditionFiltering_PicksSecondEntry()
        {
            var skills = new FakeSkillHost();
            var exprFactory = new FakeExprHostFactory();
            var evaluator = BuildEvaluator(skills, exprFactory);

            var request = evaluator.Evaluate(PlayerUnit, RotationId, targetId: null);

            Assert.NotNull(request);
            Assert.Equal(PlayerUnit, request!.Value.CasterId);
            Assert.Equal(new Id("skill.rotation_b"), request.Value.SkillId);
            Assert.Single(skills.Calls);
            Assert.Equal(new Id("skill.rotation_b"), skills.Calls[0].skillId);
        }

        /// <summary>验收标准 6 第二组："一组首条不就绪（冷却/ConditionNotMet/ActionLocked）跳过选下
        /// 一条"——就绪判定复用 <see cref="ISkillHost.GetSkillReadiness"/>（不自行推断，见
        /// <see cref="RotationEvaluator"/> 判断记录），三种阻塞位各验证一次：第二条被判定不就绪时，
        /// 直接跳过、不产生一次注定失败的 <c>CastSkill</c> 调用，改选第三条并真的施放成功。</summary>
        [Theory]
        [InlineData(SkillReadinessBlockers.SkillCooldown)]
        [InlineData(SkillReadinessBlockers.ConditionNotMet)]
        [InlineData(SkillReadinessBlockers.ActionLocked)]
        public void Evaluate_FirstCandidateNotReady_SkipsWithoutCastAttempt_PicksNextEntry(SkillReadinessBlockers blocker)
        {
            var skills = new FakeSkillHost();
            var notReadySkillId = new Id("skill.rotation_b");
            skills.ProgramReadiness(notReadySkillId, new SkillReadiness(
                notReadySkillId, isReady: false, blockingSources: blocker,
                skillCooldownRemaining: null, categoryCooldownRemaining: null, globalCooldownRemaining: null,
                maxCharges: null, currentCharges: null, nextChargeRemaining: null, effectiveCooldownDuration: null));
            var exprFactory = new FakeExprHostFactory();
            var evaluator = BuildEvaluator(skills, exprFactory);

            var request = evaluator.Evaluate(PlayerUnit, RotationId, targetId: null);

            Assert.NotNull(request);
            Assert.Equal(new Id("skill.rotation_c"), request!.Value.SkillId);
            // 与 AiRotationTests.Evaluate_OnCooldownEntry_FallsBackToNextPriority（经 CastSkill 返回
            // 值判定"不就绪"）不同：这里第二条被 GetSkillReadiness 直接挡下，从未进入 CastSkill 调用
            // 记录，Calls 里只有真正被尝试施放（且成功）的第三条这一次调用。
            Assert.Single(skills.Calls);
            Assert.Equal(new Id("skill.rotation_c"), skills.Calls[0].skillId);
        }

        [Fact]
        public void Evaluate_UnknownRotationId_ReturnsNullWithoutCallingCastSkill()
        {
            var skills = new FakeSkillHost();
            var exprFactory = new FakeExprHostFactory();
            var evaluator = BuildEvaluator(skills, exprFactory);

            var request = evaluator.Evaluate(PlayerUnit, new Id("ai.rotation.does_not_exist"), targetId: null);

            Assert.Null(request);
            Assert.Empty(skills.Calls);
        }

        [Fact]
        public void HasRotation_KnownAndUnknownId_ReflectsCompiledCache()
        {
            var skills = new FakeSkillHost();
            var exprFactory = new FakeExprHostFactory();
            var evaluator = BuildEvaluator(skills, exprFactory);

            Assert.True(evaluator.HasRotation(RotationId));
            Assert.False(evaluator.HasRotation(new Id("ai.rotation.does_not_exist")));
        }
    }
}
