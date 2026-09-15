using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// T-N3-4（ADR-0031 决策 9/10，06 第 3.1/3.6 节 2026-09-14 修订）验收：施法管线新插入的步骤
    /// 1.5"使用条件"（<see cref="CastFailureReason.ConditionNotMet"/>）与步骤 4"节拍锁"泛化
    /// （<see cref="CastFailureReason.ActionLocked"/>）在 <c>GetSkillReadiness</c> 只读查询里的同步
    /// 反映。节拍锁分支（含反应类插入、队列+反应类回归）见 <c>CastPipelineFlowTests</c> 新增用例。
    /// </summary>
    public sealed class T_N3_4_UseConditionAndActionLockTests
    {
        private static Core.Foundation.Common.Json.JsonObject SkillWithUseCondition(
            string id, string useCondition, bool respectsGcd = true, double castTime = 0)
        {
            return J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_n3_4")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(castTime)),
                ("respects_gcd", J.B(respectsGcd)),
                ("target_shape_ref", J.S("target.chain.n3_4")),
                ("use_condition", J.S(useCondition)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0))))))));
        }

        // -----------------------------------------------------------------
        // 使用条件（ADR-0031 决策 9）：脱战限定
        // -----------------------------------------------------------------

        /// <summary>use_condition = "not combat.in_combat"（脱战限定）：施法者处于战斗中时施法应
        /// 返回 ConditionNotMet。</summary>
        [Fact]
        public void UseCondition_CombatRestriction_FailsWithConditionNotMet_WhenInCombat()
        {
            var skill = SkillWithUseCondition("skill.n3_4_out_of_combat_only", "not combat.in_combat");
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            var caster = new Id("unit.n3_4_caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.n3_4"), caster);

            world.Exprs.DefaultQuery = (group, key, args) =>
                group == "combat" && key == "in_combat"
                    ? ExprValue.OfBool(world.Combat.IsInCombat(world.Exprs.LastSelfId!.Value))
                    : ExprValue.OfBool(true);

            world.Combat.NotifyCombatEvent(caster); // 进入战斗。

            var result = world.Host.CastSkill(caster, new Id("skill.n3_4_out_of_combat_only"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.ConditionNotMet, result.Reason);
        }

        /// <summary>对照组：脱战时同一条件应放行，正常施法成功。</summary>
        [Fact]
        public void UseCondition_CombatRestriction_Succeeds_WhenNotInCombat()
        {
            var skill = SkillWithUseCondition("skill.n3_4_out_of_combat_only2", "not combat.in_combat");
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            var caster = new Id("unit.n3_4_caster2");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.n3_4"), caster);

            world.Exprs.DefaultQuery = (group, key, args) =>
                group == "combat" && key == "in_combat"
                    ? ExprValue.OfBool(world.Combat.IsInCombat(world.Exprs.LastSelfId!.Value))
                    : ExprValue.OfBool(true);

            // 未调用 NotifyCombatEvent——保持脱战状态。
            var result = world.Host.CastSkill(caster, new Id("skill.n3_4_out_of_combat_only2"), System.Array.Empty<Id>());

            Assert.True(result.Success);
        }

        // -----------------------------------------------------------------
        // 使用条件：目标类型限定（早于步骤 6 目标解析，绑定调用方显式传入的候选目标）
        // -----------------------------------------------------------------

        /// <summary>use_condition 引用 target.* 时，步骤 1.5 位于步骤 6（目标解析）之前——本实现的
        /// 判断记录（见 CastPipeline.EvaluateUseCondition）：绑定调用方显式传入的 targets[0] 作为
        /// 候选目标。本用例验证该绑定确实生效（宿主收到的 targetId 等于显式目标），且条件为假时
        /// 返回 ConditionNotMet、为真时正常施法成功——模拟"目标是物件"一类目标类型限定。</summary>
        [Fact]
        public void UseCondition_TargetTypeRestriction_BindsExplicitTarget_FailsWhenConditionFalse()
        {
            var skill = SkillWithUseCondition("skill.n3_4_target_gobj_only", "target.has_tag(tag.n3_4_gobj_marker)");
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            var caster = new Id("unit.n3_4_caster3");
            var target = new Id("unit.n3_4_non_gobj_target");
            world.AddUnit(caster);
            world.AddUnit(target);

            world.Exprs.DefaultQuery = (group, key, args) =>
                group == "target" && key == "has_tag" ? ExprValue.OfBool(false) : ExprValue.OfBool(true);

            var result = world.Host.CastSkill(
                caster, new Id("skill.n3_4_target_gobj_only"), new[] { target });

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.ConditionNotMet, result.Reason);
            // 判断记录验证：宿主确实收到了显式目标（不是 null），条件求值不是"没有目标可绑定"的
            // 默认降级分支。
            Assert.Equal(target, world.Exprs.LastTargetId);
        }

        /// <summary>对照组：目标满足条件时正常施法成功。</summary>
        [Fact]
        public void UseCondition_TargetTypeRestriction_Succeeds_WhenConditionTrue()
        {
            var skill = SkillWithUseCondition("skill.n3_4_target_gobj_only2", "target.has_tag(tag.n3_4_gobj_marker)");
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            var caster = new Id("unit.n3_4_caster4");
            var target = new Id("unit.n3_4_gobj_target");
            world.AddUnit(caster);
            world.AddUnit(target);

            world.Exprs.DefaultQuery = (group, key, args) =>
                group == "target" && key == "has_tag" ? ExprValue.OfBool(true) : ExprValue.OfBool(true);

            var result = world.Host.CastSkill(
                caster, new Id("skill.n3_4_target_gobj_only2"), new[] { target });

            Assert.True(result.Success);
            Assert.Equal(target, world.Exprs.LastTargetId);
        }

        // -----------------------------------------------------------------
        // GetSkillReadiness 同步反映（06 §3.6 修订"readiness 只读查询的裁决口径同步纳入本步"）
        // -----------------------------------------------------------------

        [Fact]
        public void GetSkillReadiness_ReflectsConditionNotMet()
        {
            var skill = SkillWithUseCondition("skill.n3_4_readiness_condition", "not combat.in_combat");
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            var caster = new Id("unit.n3_4_caster5");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.n3_4"), caster);

            world.Exprs.DefaultQuery = (group, key, args) =>
                group == "combat" && key == "in_combat"
                    ? ExprValue.OfBool(world.Combat.IsInCombat(world.Exprs.LastSelfId!.Value))
                    : ExprValue.OfBool(true);

            world.Combat.NotifyCombatEvent(caster);

            var readiness = world.Host.GetSkillReadiness(caster, new Id("skill.n3_4_readiness_condition"));

            Assert.False(readiness.IsReady);
            Assert.True((readiness.BlockingSources & SkillReadinessBlockers.ConditionNotMet) != 0);

            // 查询本身不推进时间/不修改状态：紧随其后一次 CastSkill 应得到一致的裁决。
            var castResult = world.Host.CastSkill(caster, new Id("skill.n3_4_readiness_condition"), System.Array.Empty<Id>());
            Assert.False(castResult.Success);
            Assert.Equal(CastFailureReason.ConditionNotMet, castResult.Reason);
        }

        [Fact]
        public void GetSkillReadiness_ReflectsActionLocked_WhenGcdDisabled_AndCasterBusy()
        {
            var channel = J.O(
                ("id", J.S("skill.n3_4_readiness_channel")),
                ("school", J.S("skill.school_n3_4")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(2.0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.n3_4")),
                ("effects", J.A()));
            var locked = J.O(
                ("id", J.S("skill.n3_4_readiness_locked")),
                ("school", J.S("skill.school_n3_4")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(true)),
                ("target_shape_ref", J.S("target.chain.n3_4")),
                ("effects", J.A()));

            var world = new SkillWorldBuilder().SkillDef(channel).SkillDef(locked).Build();
            var caster = new Id("unit.n3_4_caster6");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.n3_4"), caster);

            // 施法前：未处于任何动作中，不应被节拍锁阻塞。
            var before = world.Host.GetSkillReadiness(caster, new Id("skill.n3_4_readiness_locked"));
            Assert.True(before.IsReady);
            Assert.Equal(SkillReadinessBlockers.None, before.BlockingSources);

            Assert.True(world.Host.CastSkill(caster, new Id("skill.n3_4_readiness_channel"), System.Array.Empty<Id>()).Success);

            var during = world.Host.GetSkillReadiness(caster, new Id("skill.n3_4_readiness_locked"));
            Assert.False(during.IsReady);
            Assert.True((during.BlockingSources & SkillReadinessBlockers.ActionLocked) != 0);

            // 与随后一次 CastSkill 的裁决一致。
            var castResult = world.Host.CastSkill(caster, new Id("skill.n3_4_readiness_locked"), System.Array.Empty<Id>());
            Assert.False(castResult.Success);
            Assert.Equal(CastFailureReason.ActionLocked, castResult.Reason);

            // 只读查询本身不修改状态：再次查询得到同一结论。
            var again = world.Host.GetSkillReadiness(caster, new Id("skill.n3_4_readiness_locked"));
            Assert.False(again.IsReady);
            Assert.True((again.BlockingSources & SkillReadinessBlockers.ActionLocked) != 0);
        }

        // -----------------------------------------------------------------
        // GcdEnabled=true 回归：ConditionNotMet 与既有 GCD 裁决互不干扰（禁止改 GcdEnabled=true 行为）
        // -----------------------------------------------------------------

        [Fact]
        public void UseCondition_StillEnforced_WhenGcdEnabled_ExistingGcdBehaviorUnchanged()
        {
            var skill = SkillWithUseCondition("skill.n3_4_gcd_and_condition", "not combat.in_combat", respectsGcd: true);

            var builder = new SkillWorldBuilder();
            builder.Options.GcdEnabled = true;
            builder.Options.GcdDuration = 1.5;
            var world = builder.SkillDef(skill).Build();
            var caster = new Id("unit.n3_4_caster7");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.n3_4"), caster);

            world.Exprs.DefaultQuery = (group, key, args) =>
                group == "combat" && key == "in_combat"
                    ? ExprValue.OfBool(world.Combat.IsInCombat(world.Exprs.LastSelfId!.Value))
                    : ExprValue.OfBool(true);

            world.Combat.NotifyCombatEvent(caster);

            // 使用条件在 GcdEnabled=true 时同样生效——步骤 1.5 早于步骤 4，不受 GCD 开关影响。
            var result = world.Host.CastSkill(caster, new Id("skill.n3_4_gcd_and_condition"), System.Array.Empty<Id>());
            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.ConditionNotMet, result.Reason);
        }
    }
}
