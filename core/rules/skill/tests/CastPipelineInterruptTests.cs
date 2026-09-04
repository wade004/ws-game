using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>打断三类触发方式（06 第 3.1 节 <c>interrupt_flags</c>：movement/damage_taken/
    /// control）+ 引导（channel）中断（见落地方案 T2-4 行"读条被 Interrupt 打断""引导中断"）。</summary>
    public sealed class CastPipelineInterruptTests
    {
        private static Core.Foundation.Common.Json.JsonObject ChannelSkill(string id, double channelTime, params string[] interruptFlags)
        {
            return J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("channel_time", J.N(channelTime)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0)), ("tick_interval", J.N(1))))))),
                ("interrupt_flags", new Core.Foundation.Common.Json.JsonArray(
                    System.Linq.Enumerable.Select(interruptFlags, f => (Core.Foundation.Common.Json.JsonValue)J.S(f)))));
        }

        private static Core.Foundation.Common.Json.JsonObject CastSkillWithFlags(string id, double castTime, params string[] interruptFlags)
        {
            return J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(castTime)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()),
                ("interrupt_flags", new Core.Foundation.Common.Json.JsonArray(
                    System.Linq.Enumerable.Select(interruptFlags, f => (Core.Foundation.Common.Json.JsonValue)J.S(f)))));
        }

        [Fact]
        public void MovementFlag_InterruptsOngoingCast()
        {
            var skill = CastSkillWithFlags("skill.sample_cast_move", 2.0, "movement");
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var start = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_cast_move"), System.Array.Empty<Id>());
            Assert.True(start.Success);
            Assert.True(world.Host.IsCasting(new Id("unit.caster")));

            world.Host.NotifyMoved(new Id("unit.caster"));
            world.Flush();

            Assert.False(world.Host.IsCasting(new Id("unit.caster")));
            var interrupted = world.Of<SkillCastInterruptedEvent>().Single();
            Assert.Equal(new Id("unit.caster"), interrupted.CasterId);
            Assert.Equal(new Id("skill.sample_cast_move"), interrupted.SkillId);
        }

        [Fact]
        public void DamageTakenFlag_InterruptsOngoingCast()
        {
            var skill = CastSkillWithFlags("skill.sample_cast_fragile", 2.0, "damage_taken");
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var start = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_cast_fragile"), System.Array.Empty<Id>());
            Assert.True(start.Success);

            world.Bus.Enqueue(new CombatDamageDealtEvent(
                new Id("unit.attacker"), new Id("unit.caster"), new Id("skill.school_sample"), 5, false, HitResult.Hit));
            world.Flush();

            Assert.False(world.Host.IsCasting(new Id("unit.caster")));
        }

        [Fact]
        public void ChannelSkill_IsInterrupted_LikeAnOngoingCast()
        {
            var skill = ChannelSkill("skill.sample_channel_move", 3.0, "movement");
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.target"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            var start = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_channel_move"), System.Array.Empty<Id>());
            Assert.True(start.Success);
            Assert.True(world.Host.IsCasting(new Id("unit.caster")));

            world.Host.Update(1.0);
            Assert.True(world.Host.IsCasting(new Id("unit.caster")));
            Assert.True(world.Combat.ResolveCalls.Count >= 1);

            world.Host.NotifyMoved(new Id("unit.caster"));
            Assert.False(world.Host.IsCasting(new Id("unit.caster")));

            // 打断后不应再继续触发周期效果。
            var callsAtInterrupt = world.Combat.ResolveCalls.Count;
            world.Host.Update(5.0);
            Assert.Equal(callsAtInterrupt, world.Combat.ResolveCalls.Count);
        }

        [Fact]
        public void Interrupt_WithoutActiveCast_IsIdempotent()
        {
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(new Id("unit.caster"));

            // 未在读条/引导中调用 Interrupt 不应抛异常。
            var exception = Record.Exception(() => world.Host.Interrupt(new Id("unit.caster"), new Id("unit.other"), null, 0));
            Assert.Null(exception);
        }
    }
}
