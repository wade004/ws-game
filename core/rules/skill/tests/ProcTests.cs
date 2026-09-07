using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>Proc 触发链（见落地方案 T2-6 行、06 第 3.4 节）：命中率、内部冷却、条件 Expr、
    /// 递归防护、<c>proc.triggered</c> 字段。</summary>
    public sealed class ProcTests
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
            string id, string triggerEvent, string triggerSkill, double procChance,
            double? internalCooldown = null, string? condition = null)
        {
            var fields = new System.Collections.Generic.List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("id", J.S(id)),
                ("trigger_event", J.S(triggerEvent)),
                ("trigger_skill", J.S(triggerSkill)),
                ("proc_chance", J.N(procChance)),
            };

            if (internalCooldown.HasValue) fields.Add(("internal_cooldown", J.N(internalCooldown.Value)));
            if (condition != null) fields.Add(("condition", J.S(condition)));

            return J.O(fields.ToArray());
        }

        private static Core.Foundation.Common.Json.JsonObject ProcAura(string id, string procRef)
        {
            return J.O(
                ("id", J.S(id)),
                ("duration", J.N(600)),
                ("effects", J.A(
                    J.O(("kind", J.S("proc_trigger")), ("params", J.O(("proc_ref", J.S(procRef))))))));
        }

        [Fact]
        public void ProcChance_HitRate_FallsWithinExpectedRange()
        {
            var trigger = NoOpSkill("skill.sample_proc_effect");
            var proc = ProcDef("skill.proc_def.sample_chance", "combat.damage_dealt", "skill.sample_proc_effect", 0.5);
            var aura = ProcAura("skill.aura_def.sample_proc_holder", "skill.proc_def.sample_chance");

            var builder = new SkillWorldBuilder();
            builder.RngSeed = 12345;
            var world = builder.SkillDef(trigger).ProcDef(proc).AuraDef(aura).Build();
            world.AddUnit(new Id("unit.caster"));

            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_proc_holder"), new Id("unit.caster"));

            for (var i = 0; i < 1000; i++)
            {
                world.Bus.PublishImmediate(new CombatDamageDealtEvent(
                    new Id("unit.caster"), new Id("unit.enemy"), new Id("skill.school_sample"), 1, false, HitResult.Hit));
            }

            world.Flush();

            var hits = world.Of<ProcTriggeredEvent>().Count();
            Assert.InRange(hits, 430, 570);
        }

        [Fact]
        public void ProcTriggeredEvent_CarriesExpectedFields()
        {
            var trigger = NoOpSkill("skill.sample_proc_effect");
            var proc = ProcDef("skill.proc_def.sample_fields", "combat.damage_dealt", "skill.sample_proc_effect", 1.0);
            var aura = ProcAura("skill.aura_def.sample_proc_fields", "skill.proc_def.sample_fields");

            var world = new SkillWorldBuilder().SkillDef(trigger).ProcDef(proc).AuraDef(aura).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_proc_fields"), new Id("unit.caster"));

            world.Bus.PublishImmediate(new CombatDamageDealtEvent(
                new Id("unit.caster"), new Id("unit.enemy"), new Id("skill.school_sample"), 1, false, HitResult.Hit));
            world.Flush();

            var evt = world.Of<ProcTriggeredEvent>().Single();
            Assert.Equal(new Id("unit.caster"), evt.UnitId);
            Assert.Equal(new Id("skill.proc_def.sample_fields"), evt.ProcDefId);
            Assert.Equal(new Id("skill.sample_proc_effect"), evt.TriggerSkillId);
        }

        [Fact]
        public void InternalCooldown_SuppressesRepeatedTriggers_UntilElapsed()
        {
            var trigger = NoOpSkill("skill.sample_proc_effect");
            var proc = ProcDef("skill.proc_def.sample_icd", "combat.damage_dealt", "skill.sample_proc_effect", 1.0, internalCooldown: 5);
            var aura = ProcAura("skill.aura_def.sample_proc_icd", "skill.proc_def.sample_icd");

            var world = new SkillWorldBuilder().SkillDef(trigger).ProcDef(proc).AuraDef(aura).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_proc_icd"), new Id("unit.caster"));

            var dmg = new CombatDamageDealtEvent(new Id("unit.caster"), new Id("unit.enemy"), new Id("skill.school_sample"), 1, false, HitResult.Hit);

            world.Bus.PublishImmediate(dmg);
            world.Bus.PublishImmediate(dmg); // 内部冷却未到，应被抑制
            world.Flush();
            Assert.Single(world.Of<ProcTriggeredEvent>());

            world.Host.Update(5.0);
            world.Bus.PublishImmediate(dmg);
            world.Flush();
            Assert.Equal(2, world.Of<ProcTriggeredEvent>().Count());
        }

        [Fact]
        public void ConditionExpr_False_PreventsTrigger()
        {
            var trigger = NoOpSkill("skill.sample_proc_effect");
            var proc = ProcDef("skill.proc_def.sample_cond", "combat.damage_dealt", "skill.sample_proc_effect", 1.0, condition: "self.sample_flag");
            var aura = ProcAura("skill.aura_def.sample_proc_cond", "skill.proc_def.sample_cond");

            var builder = new SkillWorldBuilder().SkillDef(trigger).ProcDef(proc).AuraDef(aura);
            var world = builder.Build();
            world.Exprs.DefaultQuery = (group, key, args) => Core.Foundation.Expr.ExprValue.OfBool(false);
            world.AddUnit(new Id("unit.caster"));
            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_proc_cond"), new Id("unit.caster"));

            world.Bus.PublishImmediate(new CombatDamageDealtEvent(
                new Id("unit.caster"), new Id("unit.enemy"), new Id("skill.school_sample"), 1, false, HitResult.Hit));
            world.Flush();

            Assert.Empty(world.Of<ProcTriggeredEvent>());
        }

        [Fact]
        public void TriggerChain_AcrossEventBusDispatch_IsBoundedByMaxTriggerDepth_NotByGlobalDispatchPasses()
        {
            const string healSkillId = "skill.sample_heal_loop";
            var heal = J.O(
                ("id", J.S(healSkillId)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(J.O(("kind", J.S("heal")), ("params", J.O(("base_value", J.N(1))))))));

            var proc = ProcDef("skill.proc_def.sample_heal_loop", "combat.heal_done", healSkillId, 1.0);
            var aura = ProcAura("skill.aura_def.sample_heal_loop", "skill.proc_def.sample_heal_loop");

            var builder = new SkillWorldBuilder().SkillDef(heal).ProcDef(proc).AuraDef(aura);
            builder.Options.MaxTriggerDepth = 3;

            var world = builder.Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));
            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_heal_loop"), new Id("unit.caster"));

            var healResolveCount = 0;
            world.Combat.ResolveFunc = context =>
            {
                healResolveCount++;

                // 模拟真实 core/rules/combat/core/Resolver.cs 步骤 9 heal 分支：经 _bus.Enqueue
                // 异步落地 combat.heal_done（不是 PublishImmediate 同步派发），并把当前效果上下文的
                // TriggerChainDepth 戳到事件上——这正是 RC-01 修复后 Resolver 的真实行为（见该文件
                // 改动），本假实现在这里复刻它，使这条经 EventBus 真正入队/派发的路径被完整覆盖
                // （PublishImmediate 是同步调用，不会触发原缺陷，见下方断言注释）。
                world.Bus.Enqueue(new CombatHealDoneEvent(context.SourceId, context.TargetId, context.BaseValue, false, context.TriggerChainDepth));
                return new ResolveResult(HitResult.Hit, context.BaseValue, context.BaseValue, 0, immune: false, isHeal: true);
            };

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id(healSkillId), System.Array.Empty<Id>());
            Assert.True(result.Success);

            world.Flush();

            // 无 ICD 的 heal_done → trigger_skill(heal) 自循环：正确实现应由 MaxTriggerDepth（=3）
            // 在数个 EventBus pass 内截断，而不是退化到只靠 EventBusOptions.MaxDispatchPasses
            // （默认 16）兜底——上界 5（= 1 次根施法 + MaxTriggerDepth 3 层触发 + 1 冗余）远小于
            // 16，足以区分两种截断来源；修复前本断言会失败（healResolveCount 会一路顶到 16 左右）。
            Assert.InRange(healResolveCount, 2, 5);
            Assert.NotEmpty(world.Diagnostics.Errors);

            // 下一 tick 正常：链上限拒绝后事件队列已排空（不给下一次 Flush 留下未处理的残留触发），
            // 施法管线此后仍可正常施法并再次经由 ProcHost 触发。
            healResolveCount = 0;
            var again = world.Host.CastSkill(new Id("unit.caster"), new Id(healSkillId), System.Array.Empty<Id>());
            Assert.True(again.Success);
            world.Flush();
            Assert.True(healResolveCount >= 1);
        }

        [Fact]
        public void TriggerChain_RecursionIsBoundedByMaxTriggerDepth()
        {
            var skillA = J.O(
                ("id", J.S("skill.sample_chain_a")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("energize")), ("params", J.O(("power_type", J.S("arch.power.sample_counter")), ("amount", J.N(1))))),
                    J.O(("kind", J.S("trigger_spell")), ("params", J.O(("skill_id", J.S("skill.sample_chain_b"))))))));

            var skillB = J.O(
                ("id", J.S("skill.sample_chain_b")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("energize")), ("params", J.O(("power_type", J.S("arch.power.sample_counter")), ("amount", J.N(1))))),
                    J.O(("kind", J.S("trigger_spell")), ("params", J.O(("skill_id", J.S("skill.sample_chain_a"))))))));

            var builder = new SkillWorldBuilder().SkillDef(skillA).SkillDef(skillB)
                .Power("arch.power.sample_counter", 1_000_000, startFull: false);
            builder.Options.MaxTriggerDepth = 5;

            var world = builder.Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_chain_a"), System.Array.Empty<Id>());

            Assert.True(result.Success);

            var energizeCount = world.Powers.GetPower(new Id("unit.caster"), new Id("arch.power.sample_counter"));
            Assert.InRange(energizeCount, 2, builder.Options.MaxTriggerDepth + 2);
            Assert.NotEmpty(world.Diagnostics.Errors);
        }
    }
}
