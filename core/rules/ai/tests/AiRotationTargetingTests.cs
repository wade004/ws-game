using Core.Foundation.Common;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>
    /// RC-10（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-10）：
    /// <see cref="AiHost.Evaluate"/> 此前对 Rotation 里每个技能都无条件把 <c>AiState.Target</c>
    /// （当前追踪的敌人）强塞给 <c>CastSkill</c>，跳过了技能自己的目标链解析——自疗会被作用于敌人，
    /// 友疗/AOE 的过滤/多目标解析被完全绕开。本测试用一份真实的 <c>skill.def</c>/<c>target.chain_def</c>
    /// 夹具（见 <see cref="AiTestSupport.MakeRegistry(Core.Foundation.EventBus.IEventBus, string, string, string, string, string)"/>
    /// 六参数重载）验证：只有"敌对单体"类技能才会收到当前敌人，其余（自疗/友疗/AOE）一律收到空
    /// 目标数组，交由 <c>CastPipeline</c> 步骤 6 自行解析。
    /// </summary>
    public sealed class AiRotationTargetingTests
    {
        private static readonly Id Mob = new Id("unit.rc10_mob");
        private static readonly Id Enemy = new Id("unit.rc10_enemy");
        private static readonly Id ProfileId = new Id("ai.profile.rc10_targeting");

        private const string SkillDefJson = @"[
            { ""id"": ""skill.rc10_hostile_single"", ""school"": ""school.physical"", ""kind"": ""active"",
              ""range"": 0, ""cast_time"": 0, ""respects_gcd"": false,
              ""target_shape_ref"": ""target.chain.rc10_enemy_single"", ""effects"": [] },
            { ""id"": ""skill.rc10_self_heal"", ""school"": ""school.physical"", ""kind"": ""active"",
              ""range"": 0, ""cast_time"": 0, ""respects_gcd"": false,
              ""target_shape_ref"": ""target.chain.rc10_self"", ""effects"": [] },
            { ""id"": ""skill.rc10_friendly_heal"", ""school"": ""school.physical"", ""kind"": ""active"",
              ""range"": 0, ""cast_time"": 0, ""respects_gcd"": false,
              ""target_shape_ref"": ""target.chain.rc10_friendly"", ""effects"": [] },
            { ""id"": ""skill.rc10_aoe_hostile"", ""school"": ""school.physical"", ""kind"": ""active"",
              ""range"": 0, ""cast_time"": 0, ""respects_gcd"": false,
              ""target_shape_ref"": ""target.chain.rc10_aoe"", ""effects"": [] }
        ]";

        private const string TargetChainDefJson = @"[
            { ""id"": ""target.chain.rc10_enemy_single"", ""source"": ""nearest_in_shape"",
              ""filters"": [""relation:hostile"", ""alive""], ""max_targets"": 1 },
            { ""id"": ""target.chain.rc10_self"", ""source"": ""self"", ""max_targets"": 1 },
            { ""id"": ""target.chain.rc10_friendly"", ""source"": ""party_lowest_hp_pct"",
              ""filters"": [""relation:friendly""], ""max_targets"": 1 },
            { ""id"": ""target.chain.rc10_aoe"", ""source"": ""all_in_shape"",
              ""filters"": [""relation:hostile""], ""max_targets"": 0 }
        ]";

        private static string RotationJsonFor(string rotationId, string skillId) =>
            "[{ \"id\": \"" + rotationId + "\", \"entries\": [" +
            "{ \"priority\": 1, \"condition\": \"true\", \"skill_id\": \"" + skillId + "\" }" +
            "] }]";

        private static string ProfileJsonFor(string rotationId) =>
            "[{ \"id\": \"" + ProfileId.Value + "\", \"perception_radius\": 15, \"leash_range\": 20," +
            " \"combat_return_policy\": \"stay\", \"rotation_ref\": \"" + rotationId + "\" }]";

        /// <summary>登记 Mob + 一个近战距离内的敌对单位，Step 一次让状态机自然进入 Combat 并设置
        /// <c>AiState.Target</c>，随后直接调用 <see cref="AiHost.Evaluate"/>。</summary>
        private static AiTestHarness BuildInCombatWithTarget(string rotationId, string skillId)
        {
            var harness = AiTestHarness.Build(
                ProfileJsonFor(rotationId), RotationJsonFor(rotationId, skillId),
                skillDefJson: SkillDefJson, targetChainDefJson: TargetChainDefJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(Enemy, new Vec2(1, 0), AiTestSupport.FactionPlayer); // 近战距离内直接进战。
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);

            // Step 每次调用只按当前状态处理一段转移（见 AiStateMachineTests.cs FullSequence 用例
            // 判断记录）：idle -> chase 先设置 Target，chase -> combat 再检查是否已在 AttackRange
            // 内（本例敌人从一开始就在距离 1 <= 默认 AttackRange 2 内，第二次 Step 即可转移）。
            harness.Host.Step(Mob, 0.1); // idle -> chase
            Assert.Equal(BehaviorState.Chase, harness.Host.GetBehaviorState(Mob));
            harness.Host.Step(Mob, 0.1); // chase -> combat
            Assert.Equal(BehaviorState.Combat, harness.Host.GetBehaviorState(Mob));
            Assert.Equal(Enemy, harness.Host.GetTarget(Mob));

            return harness;
        }

        [Fact]
        public void HostileSingleTargetSkill_ReceivesCurrentEnemyAsTarget()
        {
            var harness = BuildInCombatWithTarget("ai.rotation.rc10_hostile", "skill.rc10_hostile_single");

            harness.Host.Evaluate(Mob);

            var call = Assert.Single(harness.Skills.Calls);
            Assert.Equal(new Id("skill.rc10_hostile_single"), call.skillId);
            Assert.Equal(new[] { Enemy }, call.targets);
        }

        [Fact]
        public void SelfHealSkill_DoesNotReceiveCurrentEnemyAsTarget()
        {
            var harness = BuildInCombatWithTarget("ai.rotation.rc10_self", "skill.rc10_self_heal");

            harness.Host.Evaluate(Mob);

            var call = Assert.Single(harness.Skills.Calls);
            Assert.Equal(new Id("skill.rc10_self_heal"), call.skillId);
            // 修复前：targets == [Enemy]，自疗会被错误地作用于敌人（见外部审计 RC-10）。
            Assert.Empty(call.targets);
        }

        [Fact]
        public void FriendlyHealSkill_DoesNotReceiveCurrentEnemyAsTarget()
        {
            var harness = BuildInCombatWithTarget("ai.rotation.rc10_friendly", "skill.rc10_friendly_heal");

            harness.Host.Evaluate(Mob);

            var call = Assert.Single(harness.Skills.Calls);
            Assert.Equal(new Id("skill.rc10_friendly_heal"), call.skillId);
            Assert.Empty(call.targets);
        }

        [Fact]
        public void AoeHostileSkill_DoesNotReceiveCurrentEnemyAsSoleTarget()
        {
            var harness = BuildInCombatWithTarget("ai.rotation.rc10_aoe", "skill.rc10_aoe_hostile");

            harness.Host.Evaluate(Mob);

            var call = Assert.Single(harness.Skills.Calls);
            Assert.Equal(new Id("skill.rc10_aoe_hostile"), call.skillId);
            // 修复前：targets == [Enemy]，AOE 技能自己的多目标/形状解析被完全跳过。
            Assert.Empty(call.targets);
        }

        [Fact]
        public void UnknownSkill_NotRegisteredInSkillDef_DoesNotReceiveCurrentEnemyAsTarget()
        {
            // 保守兜底：技能未在 skill.def 登记（或未装配 skill.def/target.chain_def 夹具，见既有
            // AiRotationTests.cs 等测试）时，分类一律按"非敌对单体"处理，不强塞——见
            // AiHost.ClassifyIsHostileSingleTarget 判断记录。
            var harness = BuildInCombatWithTarget("ai.rotation.rc10_unknown", "skill.rc10_never_registered");

            harness.Host.Evaluate(Mob);

            var call = Assert.Single(harness.Skills.Calls);
            Assert.Empty(call.targets);
        }
    }
}
