using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    public class EventsTests
    {
        [Fact]
        public void SkillCastFailedEvent_CarriesKeyAndFields()
        {
            var caster = new Id("unit.hero");
            var skill = new Id("skill.fireball");

            var evt = new SkillCastFailedEvent(caster, skill, CastFailureReason.OutOfRange);

            Assert.Equal(RulesEventKeys.SkillCastFailed, evt.Key);
            Assert.Equal(caster, evt.CasterId);
            Assert.Equal(skill, evt.SkillId);
            Assert.Equal(CastFailureReason.OutOfRange, evt.ReasonCode);
        }

        [Fact]
        public void CombatDamageDealtEvent_CarriesKeyAndFields()
        {
            var source = new Id("unit.hero");
            var target = new Id("unit.wolf");
            var school = new Id("school.fire");

            var evt = new CombatDamageDealtEvent(source, target, school, 42.5, true, HitResult.Crit);

            Assert.Equal(RulesEventKeys.CombatDamageDealt, evt.Key);
            Assert.Equal(42.5, evt.Amount);
            Assert.True(evt.IsCrit);
            Assert.Equal(HitResult.Crit, evt.HitResult);
        }

        [Fact]
        public void UnitDiedEvent_AllowsNullKiller()
        {
            var unit = new Id("unit.wolf");

            var evt = new UnitDiedEvent(unit, null);

            Assert.Equal(RulesEventKeys.UnitDied, evt.Key);
            Assert.Null(evt.KillerId);
        }

        [Fact]
        public void SkillCastSuccessEvent_DefensivelyCopiesTargets()
        {
            var caster = new Id("unit.hero");
            var skill = new Id("skill.fireball");
            var targets = new System.Collections.Generic.List<Id> { new Id("unit.wolf") };

            var evt = new SkillCastSuccessEvent(caster, skill, targets);
            targets.Add(new Id("unit.bear"));

            Assert.Single(evt.Targets);
        }

        [Fact]
        public void RulesEventKeys_MatchEventCatalogNaming()
        {
            Assert.Equal("skill.cast_start", RulesEventKeys.SkillCastStart.Value);
            Assert.Equal("combat.damage_dealt", RulesEventKeys.CombatDamageDealt.Value);
            Assert.Equal("unit.respawned", RulesEventKeys.UnitRespawned.Value);
            Assert.Equal("ai.state_changed", RulesEventKeys.AiStateChanged.Value);
        }
    }
}
