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
        // 深度复审 C-S1 修复回归测试（2026-09-16）：GetSkillReadiness 的 ActionLocked 判定补齐
        // CastSkill 顶部的排队窗口（QueueWindow）与反应类瞬发插入分支，使二者结论一致。
        // -----------------------------------------------------------------

        /// <summary>C-S1：施法者当前读条剩余时间落在 <see cref="SkillOptions.QueueWindow"/> 窗口内
        /// 时，<see cref="ISkillHost.CastSkill"/> 会接受新请求排队（不以 ActionLocked 拒绝）——修复前
        /// <see cref="ISkillHost.GetSkillReadiness"/> 只看 <c>IsCasting</c>（不区分剩余时间），会在
        /// 这个窄窗口内错误地汇报 ActionLocked/IsReady=false，与随后一次 <see cref="CastSkill"/> 实际
        /// 会成功排队的结论不一致。本用例验证修复后二者结论一致：窗口内 GetSkillReadiness 报
        /// IsReady=true、不置位 ActionLocked，随后一次 CastSkill 成功排队。</summary>
        [Fact]
        public void GetSkillReadiness_WithinQueueWindow_IsConsistentWithCastSkillQueueingSuccess()
        {
            var channel = J.O(
                ("id", J.S("skill.n3_4_readiness_queue_channel")),
                ("school", J.S("skill.school_n3_4")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(1.0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.n3_4")),
                ("effects", J.A()));
            // respects_gcd=true：与 GetSkillReadiness_ReflectsActionLocked_WhenGcdDisabled_AndCasterBusy
            // 用的"locked"技能同一形状——只是这次要落在排队窗口内而不是窗口外。
            var queued = J.O(
                ("id", J.S("skill.n3_4_readiness_queue_target")),
                ("school", J.S("skill.school_n3_4")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(true)),
                ("target_shape_ref", J.S("target.chain.n3_4")),
                ("effects", J.A()));

            var builder = new SkillWorldBuilder();
            builder.Options.QueueWindow = 0.3;
            var world = builder.SkillDef(channel).SkillDef(queued).Build();
            var caster = new Id("unit.n3_4_caster_queue_window");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.n3_4"), caster);

            Assert.True(world.Host.CastSkill(
                caster, new Id("skill.n3_4_readiness_queue_channel"), System.Array.Empty<Id>()).Success);

            // 推进到剩余 0.2（<= 0.3 队列窗口），仍处于 IsCasting=true 状态。
            world.Host.Update(0.8);
            Assert.True(world.Host.IsCasting(caster));

            var readiness = world.Host.GetSkillReadiness(caster, new Id("skill.n3_4_readiness_queue_target"));

            // 核心断言（C-S1）：修复前这里会被误判为 ActionLocked/IsReady=false。
            Assert.True(readiness.IsReady);
            Assert.Equal(SkillReadinessBlockers.None, readiness.BlockingSources);

            // 与随后一次 CastSkill 的裁决一致：窗口内应成功排队（Ok），不是失败。
            var castResult = world.Host.CastSkill(
                caster, new Id("skill.n3_4_readiness_queue_target"), System.Array.Empty<Id>());
            Assert.True(castResult.Success);
        }

        /// <summary>C-S1 对照组（报告分析：反应类瞬发插入分支与本判定的 <c>def.RespectsGcd</c> 前提
        /// 互斥，天然一致，不需要额外代码改动）：respects_gcd=false 且瞬发的技能，本判定恒不置位
        /// ActionLocked（前提 <c>def.RespectsGcd</c> 为假），与 CastSkill 的 SafeInstantInsert 分支
        /// 总是成功这一结论一致——即便施法者当前正处于他技能的非瞬发动作时长内（远超队列窗口）。</summary>
        [Fact]
        public void GetSkillReadiness_ReactiveInstantSkill_IsConsistentWithCastSkillSafeInstantInsert()
        {
            var channel = J.O(
                ("id", J.S("skill.n3_4_readiness_reactive_channel")),
                ("school", J.S("skill.school_n3_4")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(2.0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.n3_4")),
                ("effects", J.A()));
            var reactive = J.O(
                ("id", J.S("skill.n3_4_readiness_reactive_instant")),
                ("school", J.S("skill.school_n3_4")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)), // 反应类：瞬发 + 不受节拍锁——SafeInstantInsert 前提。
                ("target_shape_ref", J.S("target.chain.n3_4")),
                ("effects", J.A()));

            var builder = new SkillWorldBuilder();
            builder.Options.QueueWindow = 0.3;
            var world = builder.SkillDef(channel).SkillDef(reactive).Build();
            var caster = new Id("unit.n3_4_caster_reactive_readiness");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.n3_4"), caster);

            Assert.True(world.Host.CastSkill(
                caster, new Id("skill.n3_4_readiness_reactive_channel"), System.Array.Empty<Id>()).Success);

            // 未推进任何时间：剩余读条时间 = 2.0，远大于队列窗口 0.3——若按窗口逻辑判断本该
            // ActionLocked，但反应类瞬发插入分支不吃这一套（前提 respects_gcd=false）。
            Assert.True(world.Host.IsCasting(caster));

            var readiness = world.Host.GetSkillReadiness(caster, new Id("skill.n3_4_readiness_reactive_instant"));
            Assert.True(readiness.IsReady);
            Assert.Equal(SkillReadinessBlockers.None, readiness.BlockingSources);

            var castResult = world.Host.CastSkill(
                caster, new Id("skill.n3_4_readiness_reactive_instant"), System.Array.Empty<Id>());
            Assert.True(castResult.Success);
            // 反应类插入不打断/不覆盖原有读条状态。
            Assert.True(world.Host.IsCasting(caster));
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
