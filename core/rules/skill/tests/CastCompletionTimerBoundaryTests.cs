using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 消费方反馈 2026-09-10（读条完成当帧新冷却被提前推进问题，证据 c08-new-cooldown，见
    /// architecture/落地计划/消费方反馈-2026-09-10-施法时序与实例标识.md）复现与根治：读条/引导恰好
    /// 在本次 <c>Update(dt)</c> 内完成时，<c>CastPipeline.FinishCast</c>/<c>ExecuteEffectsOnly</c>
    /// 新创建的冷却/公共冷却/充能恢复窗口/光环实例不应该被同一次调用里紧接着执行的
    /// <see cref="Core.Rules.Skill.SkillHost.AdvanceRoundTimers"/> 用同一个 <c>dt</c> 再扣一遍
    /// （见 <see cref="Core.Rules.Skill.SkillHost.Update"/> 判断记录）。本文件覆盖：
    /// <list type="bullet">
    /// <item>冷却本身——原始复现的三种 Tick 分段 + 瞬发同帧对照 + 已有冷却正常推进对照。</item>
    /// <item>GCD——同一调用点 <c>StartCooldownAndGcd</c> 写入，同类缺口。</item>
    /// <item>充能恢复——耗尽充能那一刻写入 <c>RechargeRemaining</c>，同类缺口。</item>
    /// <item>光环持续时间/周期累加器——<c>apply_aura</c> 效果原语落地的新实例，同类缺口。</item>
    /// <item>Proc 内部冷却——控制组：ICD 只在 <c>DispatchPending</c> 批处理时才写入，不与本次调换
    /// 顺序竞争同一个 <c>dt</c>，预期不受影响，本文件用一条用例钉住"不受影响"这个事实本身。</item>
    /// <item>离散模式（ADR-0013）——控制组：<c>AdvanceCastForActor</c>/<c>AdvanceRoundTimers</c> 的
    /// <c>dt</c> 恒为 1.0（不可再分），不存在"同一完成时刻不同分段结果不一致"的可观测条件；round-end
    /// 立即扣减 mid-round 新建冷却 1 轮是现状行为，本文件钉住现状、不在本次改动范围内变更（是否需要
    /// 改成"ready next-next round"需要新的 ADR 明确离散冷却起算语义，见回复文档判断记录）。</item>
    /// </list>
    /// </summary>
    public sealed class CastCompletionTimerBoundaryTests
    {
        private static readonly Id Caster = new Id("unit.wu17_caster");
        private static readonly Id Target = new Id("unit.wu17_target");
        private static readonly Id ChainId = new Id("target.chain.wu17_self");

        private static Core.Foundation.Common.Json.JsonObject CastSkillJson(
            string id, double castTime, double cooldownDuration, bool respectsGcd = false,
            (int Max, double RechargeTime)? charges = null)
        {
            var fields = new System.Collections.Generic.List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("id", J.S(id)),
                ("school", J.S("skill.school_wu17")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(castTime)),
                ("respects_gcd", J.B(respectsGcd)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0))))))),
            };

            if (charges.HasValue)
            {
                fields.Add(("charges", J.O(("max", J.N(charges.Value.Max)), ("recharge_time", J.N(charges.Value.RechargeTime)))));
            }
            else
            {
                fields.Add(("cooldown_duration", J.N(cooldownDuration)));
            }

            return J.O(fields.ToArray());
        }

        private static SkillWorld BuildWorld(Core.Foundation.Common.Json.JsonObject skill)
        {
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            return world;
        }

        // -----------------------------------------------------------------
        // 冷却：原始复现的三种 Tick 分段 + 瞬发同帧对照
        // -----------------------------------------------------------------

        [Fact]
        public void Cooldown_SingleStepCompletion_ShowsFullDuration_NotHalved()
        {
            var skillId = new Id("skill.wu17_cast_single");
            var world = BuildWorld(CastSkillJson(skillId.Value, castTime: 0.5, cooldownDuration: 1));

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            world.Host.Update(0.5);

            Assert.False(world.Host.IsCasting(Caster));
            Assert.Equal(1.0, world.Host.GetCooldown(Caster, skillId));
        }

        [Fact]
        public void Cooldown_TwoStepCompletion_ShowsFullDuration_NotDotSevenFive()
        {
            var skillId = new Id("skill.wu17_cast_two_step");
            var world = BuildWorld(CastSkillJson(skillId.Value, castTime: 0.5, cooldownDuration: 1));

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            world.Host.Update(0.25);
            world.Host.Update(0.25);

            Assert.False(world.Host.IsCasting(Caster));
            Assert.Equal(1.0, world.Host.GetCooldown(Caster, skillId));
        }

        [Fact]
        public void Cooldown_ThreeStepCompletion_ShowsFullDuration_NotDotEightSevenFive()
        {
            var skillId = new Id("skill.wu17_cast_three_step");
            var world = BuildWorld(CastSkillJson(skillId.Value, castTime: 0.5, cooldownDuration: 1));

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            world.Host.Update(0.25);
            world.Host.Update(0.125);
            world.Host.Update(0.125);

            Assert.False(world.Host.IsCasting(Caster));
            Assert.Equal(1.0, world.Host.GetCooldown(Caster, skillId));
        }

        [Fact]
        public void Cooldown_InstantCast_SameCallNoUpdate_ShowsFullDuration_Control()
        {
            var skillId = new Id("skill.wu17_instant");
            var world = BuildWorld(CastSkillJson(skillId.Value, castTime: 0, cooldownDuration: 1));

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);

            Assert.Equal(1.0, world.Host.GetCooldown(Caster, skillId));
        }

        /// <summary>任务要求"增加原本已有冷却正常推进的对照"：瞬发施放后连续两次 <c>Update</c>（均
        /// 不再触发新的施法完成），冷却应按每次调用的 <c>dt</c> 正常线性递减，调换
        /// <see cref="Core.Rules.Skill.SkillHost.Update"/> 内部顺序不应该影响既有冷却的推进节奏。</summary>
        [Fact]
        public void Cooldown_PreExisting_StillDecrementsNormally_Control()
        {
            var skillId = new Id("skill.wu17_instant_existing");
            var world = BuildWorld(CastSkillJson(skillId.Value, castTime: 0, cooldownDuration: 2));

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            Assert.Equal(2.0, world.Host.GetCooldown(Caster, skillId));

            world.Host.Update(0.5);
            Assert.Equal(1.5, world.Host.GetCooldown(Caster, skillId));

            world.Host.Update(0.5);
            Assert.Equal(1.0, world.Host.GetCooldown(Caster, skillId));
        }

        // -----------------------------------------------------------------
        // GCD：同一调用点写入，同类缺口
        // -----------------------------------------------------------------

        /// <summary>读条在 <c>Update(0.5)</c> 内完成、开启 GCD（<c>GcdDuration=1.0</c>）；紧接着再
        /// 推进 0.6（此时 GCD 已经是"既有状态"，理应被正常推进）。若 GCD 在创建当帧被错误地先扣了
        /// 0.5（同冷却缺口），第二次 <c>Update(0.6)</c> 会把 GCD 推到 0（就绪），另一个同样受 GCD
        /// 约束的技能会被放行；修复后 GCD 创建当帧不受扣减，第二次调用后仍剩 0.4，另一个技能应继续
        /// 被 <c>CastFailureReason.GcdActive</c> 拒绝。</summary>
        [Fact]
        public void Gcd_CompletesAtTickBoundary_NotPreDecremented()
        {
            var castId = new Id("skill.wu17_gcd_cast");
            var probeId = new Id("skill.wu17_gcd_probe");

            var builder = new SkillWorldBuilder()
                .SkillDef(CastSkillJson(castId.Value, castTime: 0.5, cooldownDuration: 0, respectsGcd: true))
                .SkillDef(CastSkillJson(probeId.Value, castTime: 0, cooldownDuration: 0, respectsGcd: true));
            builder.Options.GcdEnabled = true;
            builder.Options.GcdDuration = 1.0;

            var world = builder.Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            Assert.True(world.Host.CastSkill(Caster, castId, System.Array.Empty<Id>()).Success);
            world.Host.Update(0.5); // 读条完成，开启 GCD=1.0。
            Assert.False(world.Host.IsCasting(Caster));

            // 再推进 0.6——若 GCD 创建当帧已经被误扣 0.5，这里会把 GCD 推到 0。
            world.Host.Update(0.6);

            var probe = world.Host.CastSkill(Caster, probeId, System.Array.Empty<Id>());
            Assert.False(probe.Success);
            Assert.Equal(CastFailureReason.GcdActive, probe.Reason);
        }

        // -----------------------------------------------------------------
        // 充能恢复：耗尽充能那一刻写入 RechargeRemaining，同类缺口
        // -----------------------------------------------------------------

        [Fact]
        public void Charges_LastChargeConsumedAtTickBoundary_RechargeShowsFullDuration()
        {
            var skillId = new Id("skill.wu17_charges_cast");
            var world = BuildWorld(CastSkillJson(skillId.Value, castTime: 0.5, cooldownDuration: 0, charges: (1, 1.0)));

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            world.Host.Update(0.5); // 读条完成，消耗最后一点充能，开启 recharge_time=1.0 的恢复窗口。

            Assert.False(world.Host.IsCasting(Caster));
            // GetCooldown 在充能耗尽（Current==0）时返回 RechargeRemaining（见 CooldownTracker.
            // GetCooldown 判断记录）。
            Assert.Equal(1.0, world.Host.GetCooldown(Caster, skillId));
        }

        // -----------------------------------------------------------------
        // 光环持续时间 / 周期累加器：apply_aura 效果原语落地的新实例，同类缺口
        // -----------------------------------------------------------------

        private static Core.Foundation.Common.Json.JsonObject AuraCastSkillJson(string id, string auraDefId, double castTime) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_wu17")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(castTime)),
            ("respects_gcd", J.B(false)),
            ("cooldown_duration", J.N(0)),
            ("target_shape_ref", J.S(ChainId.Value)),
            ("effects", J.A(
                J.O(("kind", J.S("apply_aura")),
                    ("params", J.O(("aura_def", J.S(auraDefId))))))));

        [Fact]
        public void Aura_AppliedAtTickBoundary_DurationNotPreDecremented()
        {
            var auraId = new Id("skill.aura_def.wu17_boundary");
            var skillId = new Id("skill.wu17_aura_cast");
            var aura = J.O(("id", J.S(auraId.Value)), ("duration", J.N(1.0)), ("effects", J.A()));

            var world = new SkillWorldBuilder()
                .SkillDef(AuraCastSkillJson(skillId.Value, auraId.Value, 0.5))
                .AuraDef(aura)
                .Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            world.Host.Update(0.5); // 读条完成，施加 duration=1.0 的光环。
            world.Flush();
            Assert.True(world.Host.AuraQuery.HasAura(Target, auraId));

            // 累计真实经过的时间只有 0.9（< 1.0）——若光环创建当帧已经被误扣 0.5（同冷却缺口），
            // 这里会提前到期。
            world.Host.Update(0.9);
            Assert.True(world.Host.AuraQuery.HasAura(Target, auraId));

            // 再推进 0.2，累计 1.1 (> 1.0)，此时才应该真正到期。
            world.Host.Update(0.2);
            world.Flush();
            Assert.False(world.Host.AuraQuery.HasAura(Target, auraId));
        }

        [Fact]
        public void Aura_AppliedAtTickBoundary_PeriodicAccumulatorNotPreAdvanced()
        {
            var auraId = new Id("skill.aura_def.wu17_periodic_boundary");
            var skillId = new Id("skill.wu17_periodic_aura_cast");
            var aura = J.O(
                ("id", J.S(auraId.Value)),
                ("duration", J.N(10)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(0.5)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_wu17"))))))));

            var world = new SkillWorldBuilder()
                .SkillDef(AuraCastSkillJson(skillId.Value, auraId.Value, 0.5))
                .AuraDef(aura)
                .Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            world.Host.Update(0.5); // 读条完成，施加光环；本次调用不应该产生任何周期结算。
            Assert.Empty(world.Combat.ResolveCalls);

            // 累计有效周期时长 0.4（< 0.5 的 interval）——若累加器创建当帧已经被误计入 0.5，
            // 这里会提前触发一跳。
            world.Host.Update(0.4);
            Assert.Empty(world.Combat.ResolveCalls);

            // 再推进 0.15，累计 0.55 (>= 0.5)，此时才应该恰好触发第一跳。
            world.Host.Update(0.15);
            Assert.Single(world.Combat.ResolveCalls);
        }

        // -----------------------------------------------------------------
        // Proc 内部冷却：控制组——ICD 只在 DispatchPending 批处理时写入，不受本次调换顺序影响
        // -----------------------------------------------------------------

        /// <summary>Proc 的内部冷却写入点在 <c>ProcHost.OnEvent</c>——只在事件总线批处理
        /// （<c>DispatchPending</c>）派发触发事件时才会被调用，不在 <c>SkillHost.Update</c> 内部同步
        /// 发生（本模块的规则事件一律 <c>Enqueue</c>，见 <c>CastPipeline</c> 类型注释 RC-01 判断
        /// 记录）。本用例钉住"内部冷却与 <c>SkillHost.Update</c> 内部调用顺序无关，按调用批次正常
        /// 推进"这个事实：Proc 触发一次后立即在同一个 <c>DispatchPending</c> 批次内再喂一次同样的
        /// 触发事件，ICD 生效应拦下第二次；随后按 <c>internal_cooldown</c> 推进足够的 <c>Update</c>
        /// 后，第三次触发应重新生效。</summary>
        [Fact]
        public void ProcInternalCooldown_UnaffectedByUpdateOrderSwap_Control()
        {
            var triggerSkill = J.O(
                ("id", J.S("skill.wu17_proc_effect")),
                ("school", J.S("skill.school_wu17")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A()));
            var proc = J.O(
                ("id", J.S("skill.proc_def.wu17_boundary")),
                ("trigger_event", J.S("combat.damage_dealt")),
                ("trigger_skill", J.S("skill.wu17_proc_effect")),
                ("proc_chance", J.N(1.0)),
                ("internal_cooldown", J.N(1.0)));
            var procAura = J.O(
                ("id", J.S("skill.aura_def.wu17_proc_holder")),
                ("duration", J.N(600)),
                ("effects", J.A(
                    J.O(("kind", J.S("proc_trigger")), ("params", J.O(("proc_ref", J.S("skill.proc_def.wu17_boundary"))))))));

            var world = new SkillWorldBuilder().SkillDef(triggerSkill).ProcDef(proc).AuraDef(procAura).Build();
            world.AddUnit(Caster);
            world.Host.EffectSink.ApplyAura(Caster, new Id("skill.aura_def.wu17_proc_holder"), Caster);

            // 事件按生产路径一律 Enqueue，批处理时才真正派发到 ProcHost.OnEvent。
            world.Bus.Enqueue(new CombatDamageDealtEvent(Caster, Target, new Id("skill.school_wu17"), 1, false, HitResult.Hit));
            world.Bus.Enqueue(new CombatDamageDealtEvent(Caster, Target, new Id("skill.school_wu17"), 1, false, HitResult.Hit));
            world.Flush();

            // 同一批 DispatchPending 内两次触发事件——第一次命中并写入 ICD=1.0，第二次应被 ICD 拦下。
            Assert.Single(world.Of<ProcTriggeredEvent>());

            world.Host.Update(0.9);
            world.Bus.Enqueue(new CombatDamageDealtEvent(Caster, Target, new Id("skill.school_wu17"), 1, false, HitResult.Hit));
            world.Flush();
            Assert.Single(world.Of<ProcTriggeredEvent>()); // 0.9 < 1.0，ICD 仍未就绪。

            world.Host.Update(0.2);
            world.Bus.Enqueue(new CombatDamageDealtEvent(Caster, Target, new Id("skill.school_wu17"), 1, false, HitResult.Hit));
            world.Flush();
            Assert.Equal(2, world.Of<ProcTriggeredEvent>().Count()); // 累计 1.1 >= 1.0，ICD 就绪，第二次命中。
        }

        // -----------------------------------------------------------------
        // 离散模式（ADR-0013）：控制组——dt 恒为 1.0，不存在"分段不一致"的可观测条件
        // -----------------------------------------------------------------

        /// <summary>离散步/轮的 <c>dt</c> 恒为 1.0（<see cref="Core.Rules.Skill.SkillHost.
        /// AdvanceCastForActor"/>/<see cref="Core.Rules.Skill.SkillHost.AdvanceRoundTimers"/> 均
        /// 固定传 1.0，不可再分），因此不存在连续模式那种"同一完成时刻因为 Tick 分段方式不同产生
        /// 不同冷却剩余"的缺陷条件——本用例钉住现状：技能在某个行动者自己的离散步内完成读条并开启
        /// 冷却，同一轮的round-end（<c>AdvanceRoundTimers(1.0)</c>）会把这个刚创建的冷却按 1 轮扣减，
        /// 下一轮即可再次施放。是否应该改成"新建冷却在创建它的那一轮 round-end 不计入、要等再下一轮
        /// round-end 才开始扣减"（与连续模式的处理方式类比）是一个需要新 ADR 拍板的离散冷却起算语义
        /// 问题，不属于本次消费方反馈复现的缺陷范围（回复文档已记录该判断），本次改动不在此变更现状
        /// 行为。</summary>
        [Fact]
        public void DiscreteMode_CooldownStartedMidRound_DecrementsAtSameRoundEnd_CurrentBehaviorControl()
        {
            var skillId = new Id("skill.wu17_discrete_cast");
            var skill = J.O(
                ("id", J.S(skillId.Value)),
                ("school", J.S("skill.school_wu17")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(1)), // 1 回合读条
                ("cooldown_duration", J.N(1)), // 1 回合冷却
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            // 施法者自己的离散步：读条（1 回合）在这一步内恰好推满，完成并开启冷却=1。
            world.Host.AdvanceCastForActor(Caster, 1.0);
            Assert.False(world.Host.IsCasting(Caster));
            Assert.Equal(1.0, world.Host.GetCooldown(Caster, skillId));

            // 同一轮的 round-end——现状行为：立即把这个刚创建的冷却扣掉 1 轮，变为就绪。
            world.Host.AdvanceRoundTimers(1.0);
            Assert.Equal(0.0, world.Host.GetCooldown(Caster, skillId));
        }
    }
}
