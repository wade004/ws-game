using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// H4 补齐（离散模式"统一推进"，见 <c>SkillHost.AdvanceRoundTimers</c>/<c>AdvanceCastForActor</c>、
    /// <c>SkillTickHandler</c>、<c>CastPipeline.AdvanceOne</c> 判断记录）：验证冷却/光环按"轮"
    /// （<c>AdvanceRoundTimers</c>，对应 <c>sim.round_ended</c>，dt=1.0/轮）与读条/引导按"该行动者
    /// 自己的离散步"（<c>AdvanceCastForActor</c>，同样 dt=1.0/步）分别推进、互不干扰——04 第 3.1 节
    /// "以数据集声明的时间单位计"：离散作用域下 <c>cooldown_duration</c>/<c>cast_time</c>/光环
    /// <c>duration</c> 均已是整数回合，本测试直接把这些字段填成整数回合值，不经过
    /// <c>SimTimers.RescaleAll</c> 换算（那是 <c>TimeModelSwitch</c> 切模式时刻的关注点，见该类型）。
    /// </summary>
    public sealed class H4DiscreteAdvanceTests
    {
        [Fact]
        public void Cooldown_BecomesReady_AfterExactlyNRounds()
        {
            var skill = J.O(
                ("id", J.S("skill.sample_bolt")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("cooldown_duration", J.N(3)), // 3 回合
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            var caster = new Id("unit.caster");
            var target = new Id("unit.target");
            world.AddUnit(caster);
            world.AddUnit(target);
            world.Targets.SetChain(new Id("target.chain.sample"), target);

            var result = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(result.Success);
            Assert.Equal(3, world.Host.GetCooldown(caster, new Id("skill.sample_bolt")));

            // 每轮结束调用一次（对应 sim.round_ended，dt=1.0/轮，见 SkillTickHandler 判断记录）。
            world.Host.AdvanceRoundTimers(1.0);
            Assert.Equal(2, world.Host.GetCooldown(caster, new Id("skill.sample_bolt")));

            world.Host.AdvanceRoundTimers(1.0);
            Assert.Equal(1, world.Host.GetCooldown(caster, new Id("skill.sample_bolt")));

            world.Host.AdvanceRoundTimers(1.0);
            Assert.Equal(0, world.Host.GetCooldown(caster, new Id("skill.sample_bolt")));
        }

        [Fact]
        public void CastTime_TwoRounds_ResolvesOnCaster_SecondOwnStep_NotOnRoundEnd()
        {
            var skill = J.O(
                ("id", J.S("skill.sample_channel_bolt")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(2)), // 2 回合读条，不是引导（channel_time 缺省 0）
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            var caster = new Id("unit.caster");
            var target = new Id("unit.target");
            world.AddUnit(caster);
            world.AddUnit(target);
            world.Targets.SetChain(new Id("target.chain.sample"), target);

            // 施法者自己的第 1 个离散步：发起读条。
            var result = world.Host.CastSkill(caster, new Id("skill.sample_channel_bolt"), System.Array.Empty<Id>());
            Assert.True(result.Success);
            Assert.True(world.Host.IsCasting(caster));

            // 同一步内，SkillTickHandler 紧接着会调用一次 AdvanceCastForActor（见该类型 Execute
            // 判断记录：CurrentIntents 处理完之后、离散步末尾）——不是等到下一轮的全局
            // AdvanceRoundTimers。这里模拟"发起读条那一步自己也算一次推进"。
            world.Host.AdvanceCastForActor(caster, 1.0);
            Assert.True(world.Host.IsCasting(caster)); // 剩余 1 回合，尚未结算。

            // 轮结束（sim.round_ended）统一推进冷却/光环/Proc，不应影响读条剩余——即便走了很多轮，
            // 只要施法者自己没有再执行一次离散步，读条就不会继续推进。
            world.Host.AdvanceRoundTimers(1.0);
            world.Host.AdvanceRoundTimers(1.0);
            Assert.True(world.Host.IsCasting(caster)); // 仍未结算：round 级推进不碰读条管线。

            // 施法者自己的第 2 个离散步：读条剩余归零，结算。
            world.Host.AdvanceCastForActor(caster, 1.0);
            Assert.False(world.Host.IsCasting(caster));
            world.Flush();
            Assert.Single(world.Of<SkillCastSuccessEvent>());
        }

        [Fact]
        public void Aura_Duration_ExpiresAfterExactRounds_ViaAdvanceRoundTimers()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_dot")),
                ("duration", J.N(2)), // 2 回合
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(1)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_sample"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            var target = new Id("unit.target");
            world.AddUnit(target);

            world.Host.EffectSink.ApplyAura(target, new Id("skill.aura_def.sample_dot"), new Id("unit.source"));
            Assert.True(world.Host.AuraQuery.HasAura(target, new Id("skill.aura_def.sample_dot")));

            world.Host.AdvanceRoundTimers(1.0);
            Assert.True(world.Host.AuraQuery.HasAura(target, new Id("skill.aura_def.sample_dot")));

            world.Host.AdvanceRoundTimers(1.0);
            world.Flush();
            Assert.False(world.Host.AuraQuery.HasAura(target, new Id("skill.aura_def.sample_dot")));

            var removed = world.Of<AuraRemovedEvent>();
            Assert.Contains(removed, e => e.Reason == "expired");
        }
    }
}
