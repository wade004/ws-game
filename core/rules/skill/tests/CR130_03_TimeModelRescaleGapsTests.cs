using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// CR130-03（外部审计 audit-5c444f1-20260908，P2）复现与根治：混合时间模式只折算了"首次写入"的
    /// 那一刻，三处"后续推进/重新写入"的相邻缺口未被 <see cref="TimeModelRescaleTests"/>（R05 既有
    /// 覆盖）纳入：
    /// <list type="number">
    /// <item><see cref="Core.Rules.Skill.CooldownTracker.AdvanceCharges"/> 消耗掉的充能恢复后，紧接着
    /// 开始的"下一个恢复窗口"用未折算的原始 <c>recharge_time</c>（探针复现：factor=0.2、
    /// recharge_time=10，期望折算为 2，实际残留 10）。</item>
    /// <item><see cref="Core.Rules.Skill.ProcHost"/> 的内部冷却（ICD）完全没有接入 R05 那一批时间
    /// 模式广播——写入时不折算，模式切换时既有倒计时也不换算。</item>
    /// <item><see cref="Core.Rules.Skill.CastPipeline"/> 的施法学派锁（school lock，<see
    /// cref="Core.Rules.Skill.CastPipeline.Interrupt"/> 写入）同样未折算、切换时也不换算既有存量。</item>
    /// </list>
    /// </summary>
    public sealed class CR130_03_TimeModelRescaleGapsTests
    {
        private static readonly Id Caster = new Id("unit.cr130_03_caster");
        private static readonly Id Target = new Id("unit.cr130_03_target");
        private static readonly Id ChainId = new Id("target.chain.cr130_03_sample");

        // ==== 1) 充能：第二个恢复窗口须按当前系数折算，不是原始 authoring 秒数 ====

        [Fact]
        public void ChargeRecharge_SecondWindow_UsesScaledRechargeTime_NotRawAuthoringValue()
        {
            var charges = J.O(("max", J.N(2)), ("recharge_time", J.N(10))); // 连续模式：10 秒恢复一次。
            var skill = J.O(
                ("id", J.S("skill.cr130_03_charge")),
                ("school", J.S("skill.school_cr130_03_charge")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("charges", charges),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            var skillId = new Id("skill.cr130_03_charge");

            // 连续 → 离散，factor=0.2：10 秒的恢复时长应折算成 2 个离散单位（同 R05
            // TimeModelRescaleTests.ChargeRecharge_RescaledOnModelSwitch 的首窗口口径）。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(0.2));

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            // 两次施放耗尽 2 个充能，首个恢复窗口是折算值（R05 既有覆盖，不是本条复现目标）。
            Assert.Equal(2.0, world.Host.GetCooldown(Caster, skillId), 9);

            world.Host.AdvanceRoundTimers(2.0); // 首窗口耗尽：充能 0→1，紧接着开始第二个恢复窗口。

            // 立刻把刚恢复的这枚充能消耗掉，让 GetCooldown 暴露"第二个恢复窗口"的剩余值，而不是
            // 充能>0 时的就绪哨兵值 0（同 CooldownTracker.GetCooldown 判断记录/审计探针手法）。
            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);

            // 修复前第二个窗口用未折算的原始 10 秒（外部审计复现：期望 2 实际 10）；修复后应同样按
            // 当前模式的折算系数处理。
            Assert.Equal(2.0, world.Host.GetCooldown(Caster, skillId), 9);
        }

        // ==== 2) ProcHost ICD：写入时折算 + 模式切换时既有存量换算 ====

        private static Core.Foundation.Common.Json.JsonObject NoOpSkill(string id) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_cr130_03_proc")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S(ChainId.Value)),
            ("effects", J.A()));

        private static Core.Foundation.Common.Json.JsonObject ProcDef(string id, string triggerSkill, double internalCooldown) => J.O(
            ("id", J.S(id)),
            ("trigger_event", J.S("combat.damage_dealt")),
            ("trigger_skill", J.S(triggerSkill)),
            ("proc_chance", J.N(1.0)),
            ("internal_cooldown", J.N(internalCooldown)));

        private static Core.Foundation.Common.Json.JsonObject ProcAura(string id, string procRef) => J.O(
            ("id", J.S(id)),
            ("duration", J.N(600)),
            ("effects", J.A(
                J.O(("kind", J.S("proc_trigger")), ("params", J.O(("proc_ref", J.S(procRef))))))));

        [Fact]
        public void ProcInternalCooldown_WrittenAfterModelSwitch_IsScaledByCurrentFactor()
        {
            var trigger = NoOpSkill("skill.cr130_03_proc_effect_a");
            var proc = ProcDef("skill.proc_def.cr130_03_icd_a", "skill.cr130_03_proc_effect_a", 10); // 连续模式：10 秒 ICD。
            var aura = ProcAura("skill.aura_def.cr130_03_icd_a", "skill.proc_def.cr130_03_icd_a");

            var world = new SkillWorldBuilder().SkillDef(trigger).ProcDef(proc).AuraDef(aura).Build();
            var unit = new Id("unit.cr130_03_proc_caster_a");
            world.AddUnit(unit);
            world.Host.EffectSink.ApplyAura(unit, new Id("skill.aura_def.cr130_03_icd_a"), unit);

            // 连续 → 离散，factor=0.1：10 秒的 ICD 应折算成 1 个离散单位再写入。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(0.1));

            var dmg = new CombatDamageDealtEvent(unit, new Id("unit.cr130_03_proc_enemy_a"), new Id("skill.school_cr130_03_proc"), 1, false, HitResult.Hit);
            world.Bus.PublishImmediate(dmg);
            world.Flush();
            Assert.Single(world.Of<ProcTriggeredEvent>());

            // 修复前 IcdRemaining 写入未折算的原始 10；本次只推进 1.0（离散一轮）远不足以清零，
            // 第二次命中仍会被抑制。修复后应已折算为 1.0，一轮后归零，第二次命中应触发。
            world.Host.Update(1.0);
            world.Bus.PublishImmediate(dmg);
            world.Flush();
            Assert.Equal(2, world.Of<ProcTriggeredEvent>().Count());
        }

        [Fact]
        public void ProcInternalCooldown_ExistingCountdown_RescaledOnModelSwitch()
        {
            var trigger = NoOpSkill("skill.cr130_03_proc_effect_b");
            var proc = ProcDef("skill.proc_def.cr130_03_icd_b", "skill.cr130_03_proc_effect_b", 10); // 连续模式：10 秒 ICD。
            var aura = ProcAura("skill.aura_def.cr130_03_icd_b", "skill.proc_def.cr130_03_icd_b");

            var world = new SkillWorldBuilder().SkillDef(trigger).ProcDef(proc).AuraDef(aura).Build();
            var unit = new Id("unit.cr130_03_proc_caster_b");
            world.AddUnit(unit);
            world.Host.EffectSink.ApplyAura(unit, new Id("skill.aura_def.cr130_03_icd_b"), unit);

            var dmg = new CombatDamageDealtEvent(unit, new Id("unit.cr130_03_proc_enemy_b"), new Id("skill.school_cr130_03_proc"), 1, false, HitResult.Hit);
            // 连续模式下先触发一次：ICD 写入未折算的原始 10（factor 恒为 1，这一步本身没有问题）。
            world.Bus.PublishImmediate(dmg);
            world.Flush();
            Assert.Single(world.Of<ProcTriggeredEvent>());

            // 连续 → 离散，factor=0.1：正在倒计时的 10 秒 ICD 存量应换算成 1.0。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(0.1));

            // 修复前既有 ICD 存量从未随模式切换换算，仍是 10；本次只推进 1.0 远不足以清零。修复后
            // 应已换算为 1.0，一轮后归零。
            world.Host.Update(1.0);
            world.Bus.PublishImmediate(dmg);
            world.Flush();
            Assert.Equal(2, world.Of<ProcTriggeredEvent>().Count());
        }

        // ==== 3) 施法学派锁：写入时折算 + 模式切换时既有存量换算 ====

        private static Core.Foundation.Common.Json.JsonObject LockableSkill(string id, string school) => J.O(
            ("id", J.S(id)),
            ("school", J.S(school)),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(1)), // 非瞬发，进入读条状态才能被 Interrupt 加学派锁。
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S("target.chain.cr130_03_lock")),
            ("effects", J.A(
                J.O(("kind", J.S("school_damage")),
                    ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

        [Fact]
        public void SchoolLock_WrittenAfterModelSwitch_IsScaledByCurrentFactor()
        {
            var schoolId = "skill.school_cr130_03_lock_a";
            var skillId = new Id("skill.cr130_03_lock_skill_a");
            var world = new SkillWorldBuilder().SkillDef(LockableSkill(skillId.Value, schoolId)).Build();
            var caster = new Id("unit.cr130_03_lock_caster_a");
            var target = new Id("unit.cr130_03_lock_target_a");
            world.AddUnit(caster);
            world.AddUnit(target);
            world.Targets.SetChain(new Id("target.chain.cr130_03_lock"), target);

            Assert.True(world.Host.CastSkill(caster, skillId, System.Array.Empty<Id>()).Success);
            Assert.True(world.Host.IsCasting(caster));

            // 连续 → 离散，factor=0.1：10 秒的学派锁应折算成 1 个离散单位再写入。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(0.1));

            world.Host.Interrupt(caster, caster, new Id(schoolId), 10);

            var blocked = world.Host.CastSkill(caster, skillId, System.Array.Empty<Id>());
            Assert.False(blocked.Success);
            Assert.Equal(CastFailureReason.SchoolLocked, blocked.Reason);

            // 修复前学派锁写入未折算的原始 10；本次只推进 1.0（离散一轮）远不足以清零，再次施法
            // 仍会被拒绝。修复后应已折算为 1.0，一轮后归零。
            world.Host.AdvanceRoundTimers(1.0);

            var allowed = world.Host.CastSkill(caster, skillId, System.Array.Empty<Id>());
            Assert.True(allowed.Success);
        }

        [Fact]
        public void SchoolLock_ExistingCountdown_RescaledOnModelSwitch()
        {
            var schoolId = "skill.school_cr130_03_lock_b";
            var skillId = new Id("skill.cr130_03_lock_skill_b");
            var world = new SkillWorldBuilder().SkillDef(LockableSkill(skillId.Value, schoolId)).Build();
            var caster = new Id("unit.cr130_03_lock_caster_b");
            var target = new Id("unit.cr130_03_lock_target_b");
            world.AddUnit(caster);
            world.AddUnit(target);
            world.Targets.SetChain(new Id("target.chain.cr130_03_lock"), target);

            Assert.True(world.Host.CastSkill(caster, skillId, System.Array.Empty<Id>()).Success);
            // 连续模式下先加锁：写入未折算的原始 10（factor 恒为 1，这一步本身没有问题）。
            world.Host.Interrupt(caster, caster, new Id(schoolId), 10);

            // 连续 → 离散，factor=0.1：正在倒计时的 10 秒学派锁存量应换算成 1.0。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(0.1));

            var blocked = world.Host.CastSkill(caster, skillId, System.Array.Empty<Id>());
            Assert.False(blocked.Success);

            // 修复前既有学派锁存量从未随模式切换换算，仍是 10；本次只推进 1.0 远不足以清零。修复后
            // 应已换算为 1.0，一轮后归零。
            world.Host.AdvanceRoundTimers(1.0);

            var allowed = world.Host.CastSkill(caster, skillId, System.Array.Empty<Id>());
            Assert.True(allowed.Success);
        }
    }
}
