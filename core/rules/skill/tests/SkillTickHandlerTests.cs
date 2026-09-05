using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary><see cref="SkillTickHandler"/> 消费 <c>cast</c> 意图（见 03 第 4.2 节步骤 3、
    /// 落地方案 T2-4/T2-6 行）。用真实 <see cref="WorldSim"/> 驱动完整 tick，验证意图真正从
    /// <see cref="IWorldSim.CurrentIntents"/> 走到 <see cref="ISkillHost.CastSkill"/>。</summary>
    public sealed class SkillTickHandlerTests
    {
        [Fact]
        public void CastIntent_IsConsumed_AndTriggersCast()
        {
            var skill = J.O(
                ("id", J.S("skill.sample_bolt")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.target"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            var sim = new WorldSim(world.Bus);
            var handler = new SkillTickHandler(world.Host, world.Diagnostics);
            sim.RegisterPhaseHandler(TickPhase.SkillPipeline, handler);

            var args = new Core.Foundation.Common.Json.JsonObjectBuilder()
                .Add("skill_id", new Core.Foundation.Common.Json.JsonString("skill.sample_bolt"))
                .Add("targets", new Core.Foundation.Common.Json.JsonArray(new Core.Foundation.Common.Json.JsonValue[]
                {
                    new Core.Foundation.Common.Json.JsonString("unit.target"),
                }))
                .Build();

            sim.SubmitIntent(new Intent(new Id("unit.caster"), "cast", args));
            sim.Tick(SimStep.Continuous(0.016));
            world.Flush();

            Assert.Single(world.Combat.ResolveCalls);
            Assert.Single(world.Of<SkillCastSuccessEvent>());
        }

        [Fact]
        public void NonCastIntent_IsIgnored()
        {
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(new Id("unit.caster"));

            var sim = new WorldSim(world.Bus);
            var handler = new SkillTickHandler(world.Host, world.Diagnostics);
            sim.RegisterPhaseHandler(TickPhase.SkillPipeline, handler);

            sim.SubmitIntent(new Intent(new Id("unit.caster"), "move"));

            var exception = Record.Exception(() => sim.Tick(SimStep.Continuous(0.016)));
            Assert.Null(exception);
        }

        // -----------------------------------------------------------------
        // H4 补齐：离散步只推进当前行动者自己的读条（AdvanceCastForActor），
        // 冷却/光环由构造期订阅的 sim.round_ended 驱动（AdvanceRoundTimers）。
        // -----------------------------------------------------------------

        [Fact]
        public void DiscreteStep_OnlyAdvancesActingActorsOwnCast_NotOthers()
        {
            var skill = J.O(
                ("id", J.S("skill.sample_channel_bolt")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(2)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            var caster = new Id("unit.caster");
            var bystander = new Id("unit.bystander"); // 同一时刻也在读条，但不是本步的行动者。
            var target = new Id("unit.target");
            world.AddUnit(caster);
            world.AddUnit(bystander);
            world.AddUnit(target);
            world.Targets.SetChain(new Id("target.chain.sample"), target);

            world.Host.CastSkill(caster, new Id("skill.sample_channel_bolt"), System.Array.Empty<Id>());
            world.Host.CastSkill(bystander, new Id("skill.sample_channel_bolt"), System.Array.Empty<Id>());
            Assert.True(world.Host.IsCasting(caster));
            Assert.True(world.Host.IsCasting(bystander));

            var sim = new WorldSim(world.Bus);
            var handler = new SkillTickHandler(world.Host, world.Diagnostics);
            sim.RegisterPhaseHandler(TickPhase.SkillPipeline, handler);

            // 离散步：只有 caster 是本步行动者。
            sim.Tick(SimStep.Discrete(caster, StepPhase.Act));
            sim.Tick(SimStep.Discrete(caster, StepPhase.Act));

            // caster 读条 2 回合已推进 2 次，应已结算；bystander 一次都没被推进，仍在读条中。
            Assert.False(world.Host.IsCasting(caster));
            Assert.True(world.Host.IsCasting(bystander));
        }

        [Fact]
        public void RoundEnded_Bus_AdvancesCooldown_ButNotActingActorsCast()
        {
            var skill = J.O(
                ("id", J.S("skill.sample_bolt")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("cooldown_duration", J.N(2)),
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

            // bus 非空：构造期订阅 sim.round_ended（见构造函数判断记录），与
            // core/rules/combat.CombatTickHandler 的既有惯例一致。
            var handler = new SkillTickHandler(world.Host, world.Diagnostics, bus: world.Bus);
            Assert.NotNull(handler);

            world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.Equal(2, world.Host.GetCooldown(caster, new Id("skill.sample_bolt")));

            world.Bus.PublishImmediate(new SimRoundEndedEvent(0));
            Assert.Equal(1, world.Host.GetCooldown(caster, new Id("skill.sample_bolt")));

            world.Bus.PublishImmediate(new SimRoundEndedEvent(1));
            Assert.Equal(0, world.Host.GetCooldown(caster, new Id("skill.sample_bolt")));
        }
    }
}
