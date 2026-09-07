using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// R05 收口（外部审计 5e779c6，P2；见 <see cref="TimeModelRescaledEvent"/>、
    /// <c>Core.Rules.Skill.CooldownTracker.RescaleAll</c>、<c>Core.Rules.Skill.AuraHost.RescaleAll</c>、
    /// <c>SkillHost.OnTimeModelRescaled</c> 判断记录）：<c>core/gameplay/assembly.TimeModelSwitch</c>
    /// 连续/离散模式切换时会在同一个 <see cref="Core.Foundation.EventBus.IEventBus"/> 上
    /// <c>PublishImmediate</c> 一条 <see cref="TimeModelRescaledEvent"/>——本文件直接在
    /// <c>core/rules/skill</c> 这一层验证接收端（<c>SkillHost</c> 构造期订阅、转发给
    /// <c>CooldownTracker</c>/<c>AuraHost</c>）确实按同一系数换算了技能冷却/充能恢复进度/光环
    /// 剩余时间；跨层"是否真的在模式切换那一刻发出了这条事件"由
    /// <c>core/gameplay/tests/Discrete/TimeModelSwitchTests.cs</c> 覆盖（该文件同属本条允许改动的
    /// 范围）。用 <see cref="SkillWorldBuilder"/>（真实 <see cref="Core.Rules.Skill.SkillHost"/>）
    /// 而不是假实现——换算逻辑本身就在 <c>SkillHost</c>/<c>CooldownTracker</c>/<c>AuraHost</c>
    /// 内部，没有可替代的简化模拟。
    /// </summary>
    public sealed class TimeModelRescaleTests
    {
        private static readonly Id Caster = new Id("unit.tmr_caster");
        private static readonly Id Target = new Id("unit.tmr_target");
        private static readonly Id ChainId = new Id("target.chain.tmr_sample");

        [Fact]
        public void SkillCooldown_RescaledDownOnContinuousToDiscreteSwitch_ThenBackUp()
        {
            var skill = J.O(
                ("id", J.S("skill.tmr_bolt")),
                ("school", J.S("skill.school_tmr")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("cooldown_duration", J.N(12)), // 连续模式下按秒计：12 秒。
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            var result = world.Host.CastSkill(Caster, new Id("skill.tmr_bolt"), System.Array.Empty<Id>());
            Assert.True(result.Success);
            Assert.Equal(12, world.Host.GetCooldown(Caster, new Id("skill.tmr_bolt")));

            // 连续 → 离散：seconds_per_turn = 6，12 秒 → 2 回合（TimeModelSwitch.SwitchToDiscrete
            // 传入 factor = 1 / seconds_per_turn，见该方法源码）。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0));
            Assert.Equal(2.0, world.Host.GetCooldown(Caster, new Id("skill.tmr_bolt")), 9);

            // 离散模式下按轮推进：2 轮后应恰好就绪（H4DiscreteAdvanceTests 同款惯例）。
            world.Host.AdvanceRoundTimers(1.0);
            Assert.Equal(1.0, world.Host.GetCooldown(Caster, new Id("skill.tmr_bolt")), 9);
            world.Host.AdvanceRoundTimers(1.0);
            Assert.Equal(0.0, world.Host.GetCooldown(Caster, new Id("skill.tmr_bolt")), 9);
        }

        [Fact]
        public void SkillCooldown_RescaledUpOnDiscreteToContinuousSwitch()
        {
            var skill = J.O(
                ("id", J.S("skill.tmr_bolt2")),
                ("school", J.S("skill.school_tmr")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("cooldown_duration", J.N(12)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            world.Host.CastSkill(Caster, new Id("skill.tmr_bolt2"), System.Array.Empty<Id>());
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0)); // → 2 回合。
            Assert.Equal(2.0, world.Host.GetCooldown(Caster, new Id("skill.tmr_bolt2")), 9);

            // 离散 → 连续：factor = seconds_per_turn（TimeModelSwitch.SwitchToContinuous 源码），
            // 2 回合 → 12 秒，精确恢复到切换前的原始值（不是"当前值 × 6"意外撞对，是同一次线性
            // 换算的可逆性）。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(6.0));
            Assert.Equal(12.0, world.Host.GetCooldown(Caster, new Id("skill.tmr_bolt2")), 9);
        }

        [Fact]
        public void ChargeRecharge_RescaledOnModelSwitch()
        {
            var charges = J.O(("max", J.N(1)), ("recharge_time", J.N(12))); // 连续模式：12 秒恢复一次。
            var skill = J.O(
                ("id", J.S("skill.tmr_charge")),
                ("school", J.S("skill.school_tmr")),
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

            // 唯一一次充能耗尽：GetCooldown 折算成"恢复进度"（见 CooldownTracker.GetCooldown
            // HasCharges 分支）。
            world.Host.CastSkill(Caster, new Id("skill.tmr_charge"), System.Array.Empty<Id>());
            Assert.Equal(12, world.Host.GetCooldown(Caster, new Id("skill.tmr_charge")));

            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0)); // → 2 回合。
            Assert.Equal(2.0, world.Host.GetCooldown(Caster, new Id("skill.tmr_charge")), 9);

            world.Host.AdvanceRoundTimers(1.0);
            world.Host.AdvanceRoundTimers(1.0);
            Assert.Equal(0.0, world.Host.GetCooldown(Caster, new Id("skill.tmr_charge")), 9);
        }

        [Fact]
        public void AuraRemaining_RescaledOnContinuousToDiscreteSwitch_ExpiresAfterExactRounds()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.tmr_buff")),
                ("duration", J.N(12)), // 连续模式：12 秒。
                ("effects", J.A(
                    J.O(("kind", J.S("mod_stat")),
                        ("params", J.O(("stat", J.S("stat.tmr_power")), ("op", J.S("flat")), ("value", J.N(100))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Stat("stat.tmr_power").Build();
            world.AddUnit(Target);

            world.Host.EffectSink.ApplyAura(Target, new Id("skill.aura_def.tmr_buff"), new Id("unit.tmr_source"));
            Assert.True(world.Host.AuraQuery.HasAura(Target, new Id("skill.aura_def.tmr_buff")));

            // 连续 → 离散：12 秒 → 2 回合。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0));

            // 未换算前（修复前）12 这个数值会被离散 dt=1.0/轮重新解读成 12 轮，1 轮后远未到期；
            // 换算后应恰好在第 2 轮到期（同 H4DiscreteAdvanceTests.Aura_Duration_ExpiresAfterExactRounds_ViaAdvanceRoundTimers
            // 的断言写法）。
            world.Host.AdvanceRoundTimers(1.0);
            Assert.True(world.Host.AuraQuery.HasAura(Target, new Id("skill.aura_def.tmr_buff")));

            world.Host.AdvanceRoundTimers(1.0);
            world.Flush();
            Assert.False(world.Host.AuraQuery.HasAura(Target, new Id("skill.aura_def.tmr_buff")));

            var removed = world.Of<AuraRemovedEvent>();
            Assert.Contains(removed, e => e.Reason == "expired");
        }

        [Fact]
        public void AuraRemaining_RescaledOnDiscreteToContinuousSwitch_BackToOriginalSeconds()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.tmr_buff2")),
                ("duration", J.N(12)),
                ("effects", J.A(
                    J.O(("kind", J.S("mod_stat")),
                        ("params", J.O(("stat", J.S("stat.tmr_power2")), ("op", J.S("flat")), ("value", J.N(100))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Stat("stat.tmr_power2").Build();
            world.AddUnit(Target);

            world.Host.EffectSink.ApplyAura(Target, new Id("skill.aura_def.tmr_buff2"), new Id("unit.tmr_source"));
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0)); // → 2 回合。

            // 离散 → 连续之前先推进 1 轮（剩 1 轮），换算回秒应变成 6 秒（1 × 6），不是重新变回 12——
            // 验证换算作用于"当前剩余值"，不是重置回某个固定基准。
            world.Host.AdvanceRoundTimers(1.0);
            Assert.True(world.Host.AuraQuery.HasAura(Target, new Id("skill.aura_def.tmr_buff2")));

            world.Bus.PublishImmediate(new TimeModelRescaledEvent(6.0));

            // 连续模式下按秒推进：5.9 秒后仍应存在，6.1 秒后应已到期。
            world.Host.AdvanceRoundTimers(5.9);
            Assert.True(world.Host.AuraQuery.HasAura(Target, new Id("skill.aura_def.tmr_buff2")));

            world.Host.AdvanceRoundTimers(0.2);
            world.Flush();
            Assert.False(world.Host.AuraQuery.HasAura(Target, new Id("skill.aura_def.tmr_buff2")));
        }

        [Fact]
        public void RescaleFactor_MustBePositive_Throws()
        {
            var world = new SkillWorldBuilder().Build();
            Assert.Throws<System.ArgumentException>(() => new TimeModelRescaledEvent(0));
            Assert.Throws<System.ArgumentException>(() => new TimeModelRescaledEvent(-1));
        }
    }
}
