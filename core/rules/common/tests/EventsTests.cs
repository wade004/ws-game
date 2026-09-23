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

        /// <summary>ADR-0073：旧的八参数构造（不带 skillId）是 ABI 兼容 façade，SkillId 恒为
        /// null，TryGetField("skillId") 因此查不到（同既有 attackInstanceId 字段惯例）。</summary>
        [Fact]
        public void CombatDamageDealtEvent_LegacyConstructor_SkillIdIsNullAndNotReadable()
        {
            var evt = new CombatDamageDealtEvent(
                new Id("unit.hero"), new Id("unit.wolf"), new Id("school.fire"), 10.0, false, HitResult.Hit);

            Assert.Null(evt.SkillId);
            Assert.False(evt.TryGetField("skillId", out _));
        }

        /// <summary>ADR-0073：新的九参数构造携带 skillId 时，TryGetField("skillId") 可读——供
        /// feedback.binding 的 event.skill_id 条件过滤与 from_display: skill 使用。</summary>
        [Fact]
        public void CombatDamageDealtEvent_NewConstructor_CarriesSkillId()
        {
            var skillId = new Id("skill.fireball");

            var evt = new CombatDamageDealtEvent(
                new Id("unit.hero"), new Id("unit.wolf"), new Id("school.fire"), 10.0, false, HitResult.Hit,
                triggerChainDepth: 0, attackInstanceId: null, skillId: skillId);

            Assert.Equal(skillId, evt.SkillId);
            Assert.True(evt.TryGetField("skillId", out var value));
            Assert.Equal(skillId, value.AsId);
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

        /// <summary>N19（外部审计 68c9bed，P2）：默认值（未显式传入 isInstant/castTimeSeconds 的
        /// 旧 3 参构造）应保持修复前的隐含语义——不假定瞬发。</summary>
        [Fact]
        public void SkillCastSuccessEvent_DefaultConstructor_IsNotInstant_ZeroCastTime()
        {
            var evt = new SkillCastSuccessEvent(new Id("unit.hero"), new Id("skill.fireball"), System.Array.Empty<Id>());

            Assert.False(evt.IsInstant);
            Assert.Equal(0.0, evt.CastTimeSeconds);
        }

        [Fact]
        public void SkillCastSuccessEvent_CarriesIsInstantAndCastTimeSeconds()
        {
            var evt = new SkillCastSuccessEvent(
                new Id("unit.hero"), new Id("skill.fireball"), System.Array.Empty<Id>(),
                isInstant: true, castTimeSeconds: 0);

            Assert.True(evt.IsInstant);
            Assert.Equal(0.0, evt.CastTimeSeconds);
            Assert.True(evt.TryGetField("isInstant", out var isInstantValue));
            Assert.True(isInstantValue.AsBool);
            Assert.True(evt.TryGetField("castTimeSeconds", out var castTimeValue));
            Assert.Equal(0.0, castTimeValue.AsNumber);

            var timed = new SkillCastSuccessEvent(
                new Id("unit.hero"), new Id("skill.firebolt"), System.Array.Empty<Id>(),
                isInstant: false, castTimeSeconds: 2.5);

            Assert.False(timed.IsInstant);
            Assert.Equal(2.5, timed.CastTimeSeconds);
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
