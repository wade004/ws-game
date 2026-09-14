using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// T-N1-6（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
    /// 决策 5；06 第 4.1 节 2026-09-14 修订段；任务表"风险"段——"EffectContext 构造点分散
    /// （EffectDispatcher、AuraHost.FirePeriodic、投射物、测试夹具），漏一处 sourceKind 默认值会让
    /// 作用域属性静默失效"）：端到端验证两条生产结算路径（<see cref="Core.Rules.Skill.CastPipeline.ExecuteEffectsOnly"/>
    /// 经 <see cref="Core.Rules.Skill.EffectDispatcher.ApplyDamageOrHeal"/> 转发、
    /// <see cref="Core.Rules.Skill.AuraHost.FirePeriodic"/> 经新增 <see cref="Core.Rules.Skill.AuraHost.Units"/>
    /// 属性查询）确实把 <see cref="IUnitAccess.GetSourceKind"/> 的查询结果一路带到
    /// <see cref="ICombatHost.ResolveEffect"/> 收到的 <see cref="EffectContext.SourceKind"/> 上，
    /// 不是"字段存在但没人真正填"。
    /// </summary>
    public sealed class EffectContextSourceKindPlumbingTests
    {
        private static readonly Id Caster = new Id("unit.n16_caster");
        private static readonly Id Target = new Id("unit.n16_target");
        private static readonly Id ChainId = new Id("target.chain.n16_self");

        private static Core.Foundation.Common.Json.JsonObject SkillJson(string id) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_n16")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("cooldown_duration", J.N(0)),
            ("target_shape_ref", J.S(ChainId.Value)),
            ("effects", J.A(
                J.O(("kind", J.S("school_damage")),
                    ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0))))))));

        [Fact]
        public void CastSkill_ForPlayerCaster_EffectContext_SourceKind_IsPlayer()
        {
            var skillId = new Id("skill.n16_player_cast");
            var world = new SkillWorldBuilder().SkillDef(SkillJson(skillId.Value)).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            world.Units.SetSourceKind(Caster, SourceKind.Player);

            var result = world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            var call = Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(SourceKind.Player, call.SourceKind);
        }

        [Fact]
        public void CastSkill_ForCreatureCaster_EffectContext_SourceKind_IsCreature()
        {
            var skillId = new Id("skill.n16_creature_cast");
            var world = new SkillWorldBuilder().SkillDef(SkillJson(skillId.Value)).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            world.Units.SetSourceKind(Caster, SourceKind.Creature);

            var result = world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            var call = Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(SourceKind.Creature, call.SourceKind);
        }

        [Fact]
        public void CastSkill_CasterSourceKindNotRegistered_DefaultsToUnknown()
        {
            // 未调用 SetSourceKind 时 FakeUnitAccess.GetSourceKind 沿用其自身实现（未登记的单位
            // 返回 SourceKind.Unknown）——核对生产路径在"没有更多信息"时不会凭空产生 Player/Creature。
            var skillId = new Id("skill.n16_unregistered_cast");
            var world = new SkillWorldBuilder().SkillDef(SkillJson(skillId.Value)).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            var result = world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            var call = Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(SourceKind.Unknown, call.SourceKind);
        }

        [Fact]
        public void PeriodicAuraEffect_SourceKind_QueriedFromAuraHostUnitsProperty()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.n16_periodic")),
                ("duration", J.N(4)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(2)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_n16"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Units.SetSourceKind(Caster, SourceKind.Creature);

            world.Host.EffectSink.ApplyAura(Target, new Id("skill.aura_def.n16_periodic"), Caster);
            world.Host.Update(2.0);

            var call = Assert.Single(world.Combat.ResolveCalls);
            Assert.True(call.IsPeriodic);
            Assert.Equal(SourceKind.Creature, call.SourceKind);
        }
    }
}
