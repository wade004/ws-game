using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 消费方反馈 2026-09-10"同一光环多个 Proc 触发器静默忽略问题"验收（见
    /// architecture/落地计划/消费方反馈-2026-09-10-多Proc触发器.md，消费方证据目录
    /// <c>ws-game-wow/docs/框架反馈/证据/c07-multiple-proc-1.15.0-20260910/</c>，命名前缀 C07
    /// 取自该证据目录）。
    /// <para>
    /// 修复前：<see cref="AuraHost"/> 的 <c>AuraInstanceState.ProcDefRef</c> 是单值字段，
    /// <c>ApplyStaticEffects</c> 遍历同一光环定义的多个 <c>proc_trigger</c> 效果条目时后一个覆盖
    /// 前一个，只有最后登记的触发器真正挂载到 <see cref="ProcHost"/>（其 <c>_attachments</c> 同样
    /// 以 <c>instanceId</c> 为单值键，第二次 <c>Attach</c> 直接覆盖第一次）；前面的条目加载校验
    /// 通过但运行期被静默丢弃。修复后：<c>AuraInstanceState.ProcDefRefs</c> 保存完整有序集合，
    /// <c>ProcHost</c> 按 <c>instanceId</c> 分桶存多个 <c>Attachment</c>，同一光环内多个
    /// <c>proc_trigger</c> 各自独立挂载/结算条件-概率-内部冷却，光环整体移除时（到期/RemoveAura/
    /// Dispel/吸收耗尽/叠加溢出替换）一次 <c>Detach(instanceId)</c> 完整注销全部触发器；同一光环内
    /// 重复引用同一个 <c>proc_def</c> 改在加载期由 <see cref="AuraProcTriggerDuplicateRule"/> 阻断。
    /// </para>
    /// </summary>
    public sealed class C07_MultipleProcTriggersTests
    {
        private static Core.Foundation.Common.Json.JsonObject NoOpSkill(string id)
        {
            return J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));
        }

        private static Core.Foundation.Common.Json.JsonObject ProcDef(
            string id, string triggerEvent, string triggerSkill, double procChance, double? internalCooldown = null)
        {
            var fields = new List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("id", J.S(id)),
                ("trigger_event", J.S(triggerEvent)),
                ("trigger_skill", J.S(triggerSkill)),
                ("proc_chance", J.N(procChance)),
            };

            if (internalCooldown.HasValue) fields.Add(("internal_cooldown", J.N(internalCooldown.Value)));

            return J.O(fields.ToArray());
        }

        /// <summary>登记 <paramref name="procRefs"/> 数量个 <c>proc_trigger</c> 效果条目，按传入顺序
        /// 原样落进 <c>effects</c> 数组——顺序本身是本次修复要验证的维度之一（AB/BA 两种登记顺序都要
        /// 各自独立生效，不能只有"数组最后一个"生效）。</summary>
        private static Core.Foundation.Common.Json.JsonObject MultiProcAura(string id, double? duration, params string[] procRefs)
        {
            var effects = procRefs
                .Select(r => J.O(("kind", J.S("proc_trigger")), ("params", J.O(("proc_ref", J.S(r))))))
                .Cast<Core.Foundation.Common.Json.JsonValue>()
                .ToArray();

            var fields = new List<(string, Core.Foundation.Common.Json.JsonValue)> { ("id", J.S(id)) };
            if (duration.HasValue) fields.Add(("duration", J.N(duration.Value)));
            fields.Add(("effects", J.A(effects)));
            return J.O(fields.ToArray());
        }

        private static CombatDamageDealtEvent SelfDamageEvent(Id unit) =>
            new CombatDamageDealtEvent(unit, new Id("unit.enemy"), new Id("skill.school_sample"), 1, false, HitResult.Hit);

        // -----------------------------------------------------------------
        // 核心缺口：同一光环多个 proc_trigger 必须各自独立生效，与登记顺序无关
        // -----------------------------------------------------------------

        [Theory]
        [InlineData("A", "B")]
        [InlineData("B", "A")]
        public void SameAura_TwoProcTriggers_BothFireIndependently_RegardlessOfDeclarationOrder(string first, string second)
        {
            var trigger = NoOpSkill("skill.sample_proc_extra");
            var procA = ProcDef("skill.proc_def.sample_c07_a", "combat.damage_dealt", "skill.sample_proc_extra", 1.0, internalCooldown: 5);
            var procB = ProcDef("skill.proc_def.sample_c07_b", "combat.damage_dealt", "skill.sample_proc_extra", 1.0, internalCooldown: 5);

            var refs = new[] { first, second }
                .Select(x => x == "A" ? "skill.proc_def.sample_c07_a" : "skill.proc_def.sample_c07_b")
                .ToArray();
            var aura = MultiProcAura("skill.aura_def.sample_c07_multi", 60, refs);

            var world = new SkillWorldBuilder().SkillDef(trigger).ProcDef(procA).ProcDef(procB).AuraDef(aura).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_c07_multi"), new Id("unit.caster"));
            world.Flush();

            world.Bus.PublishImmediate(SelfDamageEvent(new Id("unit.caster")));
            world.Flush();

            var procDefIds = world.Of<ProcTriggeredEvent>().Select(e => e.ProcDefId.Value).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "skill.proc_def.sample_c07_a", "skill.proc_def.sample_c07_b" }, procDefIds);
        }

        /// <summary>回归对照：两个独立光环各挂一个触发器（消费方反馈"两个独立光环对照"行）—— 修复前
        /// 后行为都应是两个都触发，本用例确认本次改动没有影响这条既有正确路径。</summary>
        [Fact]
        public void SeparateAuras_EachWithOneProcTrigger_BothFire_Unaffected()
        {
            var trigger = NoOpSkill("skill.sample_proc_extra");
            var procA = ProcDef("skill.proc_def.sample_c07_sep_a", "combat.damage_dealt", "skill.sample_proc_extra", 1.0, internalCooldown: 5);
            var procB = ProcDef("skill.proc_def.sample_c07_sep_b", "combat.damage_dealt", "skill.sample_proc_extra", 1.0, internalCooldown: 5);
            var auraA = MultiProcAura("skill.aura_def.sample_c07_sep_a", 60, "skill.proc_def.sample_c07_sep_a");
            var auraB = MultiProcAura("skill.aura_def.sample_c07_sep_b", 60, "skill.proc_def.sample_c07_sep_b");

            var world = new SkillWorldBuilder().SkillDef(trigger).ProcDef(procA).ProcDef(procB).AuraDef(auraA).AuraDef(auraB).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_c07_sep_a"), new Id("unit.caster"));
            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_c07_sep_b"), new Id("unit.caster"));
            world.Flush();

            world.Bus.PublishImmediate(SelfDamageEvent(new Id("unit.caster")));
            world.Flush();

            var procDefIds = world.Of<ProcTriggeredEvent>().Select(e => e.ProcDefId.Value).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "skill.proc_def.sample_c07_sep_a", "skill.proc_def.sample_c07_sep_b" }, procDefIds);
        }

        // -----------------------------------------------------------------
        // 独立内部冷却：多触发器共用同一实例，但各自 ICD 互不干扰
        // -----------------------------------------------------------------

        [Fact]
        public void SameAura_TwoProcTriggers_IndependentInternalCooldowns()
        {
            var trigger = NoOpSkill("skill.sample_proc_extra");
            // A 冷却 5，B 冷却 1——用不同的 ICD 让两者的"是否仍在冷却"在时间推进过程中出现分叉，
            // 修复前两者共享同一个 Attachment.IcdRemaining（本就只会剩一个 Attachment），无法体现
            // "各自独立计时"。
            var procA = ProcDef("skill.proc_def.sample_c07_icd_a", "combat.damage_dealt", "skill.sample_proc_extra", 1.0, internalCooldown: 5);
            var procB = ProcDef("skill.proc_def.sample_c07_icd_b", "combat.damage_dealt", "skill.sample_proc_extra", 1.0, internalCooldown: 1);
            var aura = MultiProcAura("skill.aura_def.sample_c07_icd", 60,
                "skill.proc_def.sample_c07_icd_a", "skill.proc_def.sample_c07_icd_b");

            var world = new SkillWorldBuilder().SkillDef(trigger).ProcDef(procA).ProcDef(procB).AuraDef(aura).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_c07_icd"), new Id("unit.caster"));
            world.Flush();

            world.Bus.PublishImmediate(SelfDamageEvent(new Id("unit.caster")));
            world.Flush();
            Assert.Equal(2, world.Of<ProcTriggeredEvent>().Count());

            // dt=1：B 的 ICD 已耗尽，A 仍差 4 秒——立即再派发一次事件，只有 B 应该再次触发。
            world.Host.Update(1.0);
            world.Bus.PublishImmediate(SelfDamageEvent(new Id("unit.caster")));
            world.Flush();
            var afterOneSecond = world.Of<ProcTriggeredEvent>().Select(e => e.ProcDefId.Value).ToArray();
            Assert.Equal(3, afterOneSecond.Length);
            Assert.Equal(2, afterOneSecond.Count(x => x == "skill.proc_def.sample_c07_icd_b"));
            Assert.Single(afterOneSecond, x => x == "skill.proc_def.sample_c07_icd_a");

            // 再推进 4 秒（累计 5），A 的 ICD 也耗尽——两者应各自再触发一次。
            world.Host.Update(4.0);
            world.Bus.PublishImmediate(SelfDamageEvent(new Id("unit.caster")));
            world.Flush();
            var afterFiveSeconds = world.Of<ProcTriggeredEvent>().Select(e => e.ProcDefId.Value).ToArray();
            Assert.Equal(5, afterFiveSeconds.Length);
            Assert.Equal(2, afterFiveSeconds.Count(x => x == "skill.proc_def.sample_c07_icd_a"));
            Assert.Equal(3, afterFiveSeconds.Count(x => x == "skill.proc_def.sample_c07_icd_b"));
        }

        // -----------------------------------------------------------------
        // 移除路径：整个光环摘除时全部触发器一起注销，不残留孤儿订阅
        // -----------------------------------------------------------------

        [Fact]
        public void RemoveAura_DetachesBothProcTriggers_NoResidualSubscription()
        {
            var trigger = NoOpSkill("skill.sample_proc_extra");
            var procA = ProcDef("skill.proc_def.sample_c07_rm_a", "combat.damage_dealt", "skill.sample_proc_extra", 1.0);
            var procB = ProcDef("skill.proc_def.sample_c07_rm_b", "combat.damage_dealt", "skill.sample_proc_extra", 1.0);
            var aura = MultiProcAura("skill.aura_def.sample_c07_rm", 60,
                "skill.proc_def.sample_c07_rm_a", "skill.proc_def.sample_c07_rm_b");

            var world = new SkillWorldBuilder().SkillDef(trigger).ProcDef(procA).ProcDef(procB).AuraDef(aura).Build();
            world.AddUnit(new Id("unit.caster"));
            var handle = world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_c07_rm"), new Id("unit.caster"));
            world.Flush();

            world.Host.EffectSink.RemoveAura(new Id("unit.caster"), handle);
            world.Flush();
            Assert.False(world.Host.AuraQuery.HasAura(new Id("unit.caster"), new Id("skill.aura_def.sample_c07_rm")));

            world.Bus.PublishImmediate(SelfDamageEvent(new Id("unit.caster")));
            world.Flush();
            Assert.Empty(world.Of<ProcTriggeredEvent>());
        }

        [Fact]
        public void AuraExpires_DetachesBothProcTriggers_NoResidualSubscription()
        {
            var trigger = NoOpSkill("skill.sample_proc_extra");
            var procA = ProcDef("skill.proc_def.sample_c07_exp_a", "combat.damage_dealt", "skill.sample_proc_extra", 1.0);
            var procB = ProcDef("skill.proc_def.sample_c07_exp_b", "combat.damage_dealt", "skill.sample_proc_extra", 1.0);
            var aura = MultiProcAura("skill.aura_def.sample_c07_exp", 2,
                "skill.proc_def.sample_c07_exp_a", "skill.proc_def.sample_c07_exp_b");

            var world = new SkillWorldBuilder().SkillDef(trigger).ProcDef(procA).ProcDef(procB).AuraDef(aura).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_c07_exp"), new Id("unit.caster"));
            world.Flush();

            world.Host.Update(3.0); // duration=2，dt=3 跨过到期点
            world.Flush();
            Assert.False(world.Host.AuraQuery.HasAura(new Id("unit.caster"), new Id("skill.aura_def.sample_c07_exp")));

            world.Bus.PublishImmediate(SelfDamageEvent(new Id("unit.caster")));
            world.Flush();
            Assert.Empty(world.Of<ProcTriggeredEvent>());
        }

        /// <summary>叠加溢出 Replace 策略：旧实例被摘除、新实例重新挂载——修复前旧实例只有"最后一个"
        /// 触发器真正挂载，本用例同时验证替换后不会因为旧订阅未摘除而导致同一次事件触发两份。</summary>
        [Fact]
        public void StackOverflowReplace_DetachesOldInstanceTriggers_NewInstanceStillFiresOncePerTrigger()
        {
            var trigger = NoOpSkill("skill.sample_proc_extra");
            var procA = ProcDef("skill.proc_def.sample_c07_rep_a", "combat.damage_dealt", "skill.sample_proc_extra", 1.0);
            var procB = ProcDef("skill.proc_def.sample_c07_rep_b", "combat.damage_dealt", "skill.sample_proc_extra", 1.0);
            var aura = MultiProcAura("skill.aura_def.sample_c07_rep", 60,
                "skill.proc_def.sample_c07_rep_a", "skill.proc_def.sample_c07_rep_b");

            var builder = new SkillWorldBuilder().SkillDef(trigger).ProcDef(procA).ProcDef(procB).AuraDef(aura);
            builder.Options.StackOverflowPolicy = StackOverflowPolicy.Replace;
            var world = builder.Build();
            world.AddUnit(new Id("unit.caster"));

            // max_stacks 缺省 1，第二次施加即触发 Replace：旧实例摘除，新实例创建。
            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_c07_rep"), new Id("unit.caster"));
            world.Flush();
            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_c07_rep"), new Id("unit.caster"));
            world.Flush();

            world.Bus.PublishImmediate(SelfDamageEvent(new Id("unit.caster")));
            world.Flush();

            // 若旧实例的订阅没有被摘除，这里会看到每个 proc_def 各触发两次（旧+新）。
            var procDefIds = world.Of<ProcTriggeredEvent>().Select(e => e.ProcDefId.Value).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "skill.proc_def.sample_c07_rep_a", "skill.proc_def.sample_c07_rep_b" }, procDefIds);
        }

        // -----------------------------------------------------------------
        // 加载期契约：同一光环内重复引用同一个 proc_def 被拒绝
        // -----------------------------------------------------------------

        [Fact]
        public void DuplicateProcRefInSameAura_RejectedAtLoad_WithFieldLocation()
        {
            var procA = ProcDef("skill.proc_def.sample_c07_dup", "combat.damage_dealt", "skill.sample_proc_extra", 1.0);
            var aura = MultiProcAura("skill.aura_def.sample_c07_dup", 60,
                "skill.proc_def.sample_c07_dup", "skill.proc_def.sample_c07_dup");

            var builder = new SkillWorldBuilder().ProcDef(procA).AuraDef(aura)
                .ValidationRule(new AuraProcTriggerDuplicateRule());

            var report = builder.Validate();
            Assert.True(report.IsBlocking);
            var issue = Assert.Single(report.Issues, i => i.Check == "aura_proc_trigger_duplicate");
            Assert.Equal("skill.aura_def", issue.Table);
            Assert.Equal("skill.aura_def.sample_c07_dup", issue.RecordKey);
            Assert.Equal("effects[1].params.proc_ref", issue.Field);
        }

        /// <summary>正例对照：同一光环内两个不同的 proc_ref 不触发本规则（这正是本次修复要支持的
        /// 合法配置形态）。</summary>
        [Fact]
        public void DistinctProcRefsInSameAura_PassesLoad()
        {
            var procA = ProcDef("skill.proc_def.sample_c07_ok_a", "combat.damage_dealt", "skill.sample_proc_extra", 1.0);
            var procB = ProcDef("skill.proc_def.sample_c07_ok_b", "combat.damage_dealt", "skill.sample_proc_extra", 1.0);
            var aura = MultiProcAura("skill.aura_def.sample_c07_ok", 60,
                "skill.proc_def.sample_c07_ok_a", "skill.proc_def.sample_c07_ok_b");

            var builder = new SkillWorldBuilder().ProcDef(procA).ProcDef(procB).AuraDef(aura)
                .ValidationRule(new AuraProcTriggerDuplicateRule());

            var report = builder.Validate();
            Assert.DoesNotContain(report.Issues, i => i.Check == "aura_proc_trigger_duplicate");
        }
    }
}
