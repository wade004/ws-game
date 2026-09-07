using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>施法管线九步固定顺序（06 第 3.6 节）各失败原因码的最小复现用例（见落地方案 T2-4 行
    /// "九步各失败码至少一例"）。</summary>
    public sealed class CastPipelineFailureTests
    {

        private static Core.Foundation.Common.Json.JsonObject InstantDamageSkill(
            string id = "skill.sample_bolt",
            double range = 30,
            bool respectsGcd = true,
            string? cooldownCategory = null,
            double cooldownDuration = 0,
            Core.Foundation.Common.Json.JsonValue? charges = null)
        {
            var fields = new System.Collections.Generic.List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(range)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(respectsGcd)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(10)), ("coefficient", J.N(0))))))),
            };

            if (cooldownCategory != null) fields.Add(("cooldown_category", J.S(cooldownCategory)));
            if (cooldownDuration != 0) fields.Add(("cooldown_duration", J.N(cooldownDuration)));
            if (charges != null) fields.Add(("charges", charges));

            return J.O(fields.ToArray());
        }

        [Fact]
        public void UnknownSkill_Fails()
        {
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(new Id("unit.caster"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_missing"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.UnknownSkill, result.Reason);
        }

        [Fact]
        public void PassiveSkill_CannotBeCast()
        {
            var passive = J.O(
                ("id", J.S("skill.sample_passive")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("passive")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var world = new SkillWorldBuilder().SkillDef(passive).Build();
            world.AddUnit(new Id("unit.caster"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_passive"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.PassiveSkill, result.Reason);
        }

        [Fact]
        public void DeadCaster_Fails()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill()).Build();
            world.AddUnit(new Id("unit.caster"), alive: false);

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.Dead, result.Reason);
        }

        [Fact]
        public void FullyIncapacitated_ReturnsStunned()
        {
            var control = J.O(
                ("id", J.S("skill.aura_def.sample_stun")),
                ("effects", J.A(
                    J.O(("kind", J.S("control")),
                        ("params", J.O(("flags", J.A(
                            J.S("no_move"), J.S("no_cast"), J.S("no_attack")))))))));

            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill()).AuraDef(control).Build();
            world.AddUnit(new Id("unit.caster"));

            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_stun"), new Id("unit.caster"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.Stunned, result.Reason);
        }

        [Fact]
        public void SilencedOnly_ReturnsSilenced()
        {
            var silence = J.O(
                ("id", J.S("skill.aura_def.sample_silence")),
                ("effects", J.A(
                    J.O(("kind", J.S("control")),
                        ("params", J.O(("flags", J.A(J.S("no_cast")))))))));

            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill()).AuraDef(silence).Build();
            world.AddUnit(new Id("unit.caster"));

            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_silence"), new Id("unit.caster"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.Silenced, result.Reason);
        }

        [Fact]
        public void OnCooldown_Fails()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill(cooldownDuration: 5)).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var first = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(first.Success);

            var second = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(second.Success);
            Assert.Equal(CastFailureReason.OnCooldown, second.Reason);
        }

        [Fact]
        public void NoCharges_Fails()
        {
            var charges = J.O(("max", J.N(1)), ("recharge_time", J.N(10)));
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill(charges: charges)).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var first = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(first.Success);

            var second = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(second.Success);
            Assert.Equal(CastFailureReason.NoCharges, second.Reason);
        }

        [Fact]
        public void GcdActive_FailsWhenEnabled()
        {
            var builder = new SkillWorldBuilder();
            builder.Options.GcdEnabled = true;
            builder.Options.GcdDuration = 1.5;
            var world = builder.SkillDef(InstantDamageSkill(cooldownDuration: 0)).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var first = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(first.Success);

            // 立即再次施放：技能自身无冷却，但公共冷却应挡下。
            var second = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(second.Success);
            Assert.Equal(CastFailureReason.GcdActive, second.Reason);
        }

        [Fact]
        public void Gcd_AlwaysPassesWhenDisabled()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill(cooldownDuration: 0)).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var first = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            var second = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.True(first.Success);
            Assert.True(second.Success);
        }

        [Fact]
        public void InsufficientPower_Fails()
        {
            var withCost = J.O(
                ("id", J.S("skill.sample_costly")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("cost", J.A(J.O(("power_type", J.S("arch.power.sample_mana")), ("amount", J.N(50))))),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var builder = new SkillWorldBuilder().SkillDef(withCost).Power("arch.power.sample_mana", 10);
            var world = builder.Build();
            world.AddUnit(new Id("unit.caster"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_costly"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.InsufficientPower, result.Reason);
        }

        [Fact]
        public void NoValidTarget_Fails()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill()).Build();
            world.AddUnit(new Id("unit.caster"));
            // 链未登记任何目标 -> Resolve 返回空。

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.NoValidTarget, result.Reason);
        }

        [Fact]
        public void OutOfRange_Fails()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill(range: 5)).Build();
            world.AddUnit(new Id("unit.caster"), new Vec2(0, 0));
            world.AddUnit(new Id("unit.target"), new Vec2(100, 0));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.OutOfRange, result.Reason);
        }

        // -----------------------------------------------------------------
        // N10（外部审计 68c9bed，P2）：显式 targets 此前绕过目标链的额外 filters（tag/expr），
        // 见 CastPipeline 步骤 6 判断记录、ITargetHost.FilterExplicitTargets 类型注释。
        // -----------------------------------------------------------------

        /// <summary>技能配置要求目标带 <c>tag:undead</c>（用 FakeTargetHost.SetFilter 模拟真实
        /// TargetChainDef.Filters 里的 <c>"tag:xxx"</c> 简写，见 FakeTargetHost 判断记录）；调用方
        /// 显式指定一个不满足条件的目标，修复前会绕过链的 filters 直接成功命中，修复后应在步骤 6
        /// 失败（复用 <see cref="CastFailureReason.NoValidTarget"/>，见 ITargetHost.FilterExplicitTargets
        /// 判断记录"06 未单独为显式目标不满足额外条件定义原因码"）。</summary>
        [Fact]
        public void ExplicitTarget_FailingChainFilter_Fails_NotBypassed()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill()).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.non_undead_target"));
            // 模拟"要求 tag:undead"：显式目标没有这个标签，谓词返回 false。
            world.Targets.SetFilter(new Id("target.chain.sample"), id => id.Equals(new Id("unit.undead_target")));

            var result = world.Host.CastSkill(
                new Id("unit.caster"), new Id("skill.sample_bolt"), new[] { new Id("unit.non_undead_target") });

            // 修复前该断言会失败：显式 targets 非空时直接跳过 ITargetHost 整条解析/过滤管线，
            // resolvedTargets 就是调用方传入的原始列表，不满足 undead 条件的目标也会施法成功。
            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.NoValidTarget, result.Reason);
        }

        /// <summary>对照组：显式目标满足链的 filters 时应正常施法成功——确认修复没有把"显式目标"
        /// 这条路径整体堵死，只是补上了原本被绕过的额外条件校验。</summary>
        [Fact]
        public void ExplicitTarget_PassingChainFilter_Succeeds()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill()).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.undead_target"));
            world.Targets.SetFilter(new Id("target.chain.sample"), id => id.Equals(new Id("unit.undead_target")));

            var result = world.Host.CastSkill(
                new Id("unit.caster"), new Id("skill.sample_bolt"), new[] { new Id("unit.undead_target") });

            Assert.True(result.Success);
        }

        /// <summary>链未配置任何 filter（<c>SetFilter</c> 未调用）时，显式目标不应受影响——
        /// 与真实 <c>TargetChainDef.Filters</c> 为空数组时的语义一致（无额外条件，全部通过）。</summary>
        [Fact]
        public void ExplicitTarget_NoChainFilterConfigured_StillSucceeds()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill()).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.any_target"));

            var result = world.Host.CastSkill(
                new Id("unit.caster"), new Id("skill.sample_bolt"), new[] { new Id("unit.any_target") });

            Assert.True(result.Success);
        }

        [Fact]
        public void SchoolLocked_BlocksSameSchoolAfterInterrupt()
        {
            // 用一个带读条的技能制造"打断 + 学派锁定"的场景。
            var channelSkill = J.O(
                ("id", J.S("skill.sample_channel_lockable")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(3)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var world2 = new SkillWorldBuilder().SkillDef(channelSkill).SkillDef(InstantDamageSkill()).Build();
            world2.AddUnit(new Id("unit.caster"));
            world2.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var start = world2.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_channel_lockable"), System.Array.Empty<Id>());
            Assert.True(start.Success);
            Assert.True(world2.Host.IsCasting(new Id("unit.caster")));

            world2.Host.Interrupt(new Id("unit.caster"), new Id("unit.interrupter"), new Id("skill.school_sample"), 5);
            Assert.False(world2.Host.IsCasting(new Id("unit.caster")));

            var recast = world2.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(recast.Success);
            Assert.Equal(CastFailureReason.SchoolLocked, recast.Reason);
        }

        // -----------------------------------------------------------------
        // RC-07（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-07）：
        // 学派锁定推进（CastPipeline.AdvanceSchoolLocks）此前只挂在 CastPipeline.Update（连续模式
        // 每 tick 调用）内部——离散模式的生产路径（见 core/rules/skill/core/SkillTickHandler.cs
        // Execute 判断记录）从不调用 CastPipeline.Update，只调用 SkillHost.AdvanceCastForActor（每
        // 行动者自己的读条）与 SkillHost.AdvanceRoundTimers（经 sim.round_ended，每轮一次），学派
        // 锁定因此在离散模式下永远不衰减。本用例只调用 AdvanceRoundTimers（不调用 CastPipeline.
        // Update），精确复现离散模式的真实调用面。
        // -----------------------------------------------------------------

        [Fact]
        public void SchoolLock_DecaysViaAdvanceRoundTimers_NotViaContinuousUpdate_ForDiscreteMode()
        {
            var lockableSkill = J.O(
                ("id", J.S("skill.sample_channel_lock_decay")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(3)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var world = new SkillWorldBuilder().SkillDef(lockableSkill).SkillDef(InstantDamageSkill()).Build();
            var caster = new Id("unit.caster");
            var school = new Id("skill.school_sample");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var start = world.Host.CastSkill(caster, new Id("skill.sample_channel_lock_decay"), System.Array.Empty<Id>());
            Assert.True(start.Success);
            world.Host.Interrupt(caster, new Id("unit.interrupter"), school, lockDuration: 2.0);

            var stillLocked = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(stillLocked.Success);
            Assert.Equal(CastFailureReason.SchoolLocked, stillLocked.Reason);

            // 离散模式真实调用面：只有 AdvanceRoundTimers（经 sim.round_ended，见
            // SkillTickHandler 构造函数），从不调用 CastPipeline.Update/SkillHost.Update。
            world.Host.AdvanceRoundTimers(1.0); // 剩余 1.0
            var stillLockedAfterOneRound = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(stillLockedAfterOneRound.Success);
            Assert.Equal(CastFailureReason.SchoolLocked, stillLockedAfterOneRound.Reason);

            world.Host.AdvanceRoundTimers(1.0); // 剩余 0.0 -> 解锁

            // 修复前：AdvanceRoundTimers 完全不推进学派锁定，本次施法仍会被 SchoolLocked 拒绝。
            var unlocked = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(unlocked.Success);
        }

        [Fact]
        public void SchoolLock_ContinuousMode_StillDecaysViaHostUpdate_NotDoubleCounted()
        {
            // 对照组：连续模式（SkillHost.Update，内部转调 CastPipeline.Update + AdvanceRoundTimers）
            // 行为不变，且不会因为两者都可能触碰学派锁定而产生"同一个 dt 衰减两次"的双倍速度——
            // 见 CastPipeline.Update/SkillHost.AdvanceRoundTimers 判断记录"不会重复推进"。
            var lockableSkill = J.O(
                ("id", J.S("skill.sample_channel_lock_decay")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(3)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var world = new SkillWorldBuilder().SkillDef(lockableSkill).SkillDef(InstantDamageSkill()).Build();
            var caster = new Id("unit.caster");
            var school = new Id("skill.school_sample");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var start = world.Host.CastSkill(caster, new Id("skill.sample_channel_lock_decay"), System.Array.Empty<Id>());
            Assert.True(start.Success);
            world.Host.Interrupt(caster, new Id("unit.interrupter"), school, lockDuration: 2.0);

            // 恰好推进 1.9 秒：若被双重计数会变成 3.8（超过 2.0），错误地提前解锁。
            world.Host.Update(1.9);
            var stillLocked = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(stillLocked.Success);
            Assert.Equal(CastFailureReason.SchoolLocked, stillLocked.Reason);

            world.Host.Update(0.2); // 累计 2.1，单倍计数应已解锁。
            var unlocked = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(unlocked.Success);
        }
    }
}
