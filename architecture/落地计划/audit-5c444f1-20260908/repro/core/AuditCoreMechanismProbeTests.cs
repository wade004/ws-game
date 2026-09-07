using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;
using Core.Gameplay.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>Audit-only probes. This file exists only in the isolated archive copy.</summary>
    public sealed class AuditCoreMechanismProbeTests
    {
        private static readonly Id Unit = new Id("unit.audit_core");
        private static readonly Id Skill = new Id("skill.audit_charge");
        private static readonly Id Shape = new Id("target.chain.audit_core");

        private static JsonObject ChargeSkill() => J.O(
            ("id", J.S(Skill.Value)),
            ("school", J.S("skill.school_audit_core")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("charges", J.O(("max", J.N(2)), ("recharge_time", J.N(10)))),
            ("target_shape_ref", J.S(Shape.Value)),
            ("effects", J.A()));

        private static JsonObject RewardSkill() => J.O(
            ("id", J.S("skill.audit_reward")),
            ("school", J.S("skill.school_audit_core")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S(Shape.Value)),
            ("effects", J.A()));

        [Fact]
        public void RewardDispatcher_QuestSourceSkill_IsNotPersistedAsPermanent()
        {
            var saving = new SkillWorldBuilder().SkillDef(RewardSkill()).Build();
            saving.AddUnit(Unit);
            var questSource = new Id("quest.audit_skill_reward");
            var emptySnapshot = KnownSkillsPersistable.For(saving.Host, Unit).Save();
            var dispatcher = new RewardDispatcher(
                skillGranter: (unitId, skillId, sourceId, learn) =>
                {
                    if (learn) saving.Host.LearnSkill(unitId, skillId, sourceId);
                    else saving.Host.ForgetSkill(unitId, skillId, sourceId);
                });
            var bundle = new RewardBundle(
                items: Array.Empty<ItemStack>(), xp: 0,
                currency: Array.Empty<(Id, long)>(),
                skills: new[] { new Id("skill.audit_reward") },
                worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 0);

            Assert.True(dispatcher.Grant(Unit, bundle, questSource));
            Assert.True(saving.Host.Knows(Unit, new Id("skill.audit_reward")));
            Assert.Empty(saving.Host.GetPermanentlyKnownSkills(Unit));
            KnownSkillsPersistable.For(saving.Host, Unit).Load(emptySnapshot);
            Assert.True(saving.Host.Knows(Unit, new Id("skill.audit_reward")));
            var saved = KnownSkillsPersistable.For(saving.Host, Unit).Save();
            Assert.Empty(Assert.IsType<JsonArray>(saved));

            var loading = new SkillWorldBuilder().SkillDef(RewardSkill()).Build();
            loading.AddUnit(Unit);
            KnownSkillsPersistable.For(loading.Host, Unit).Load(saved);
            Assert.False(loading.Host.Knows(Unit, new Id("skill.audit_reward")));
            Console.WriteLine("reward_source=quest.audit_skill_reward known_before_save=1 permanent_snapshot=0 fresh_host_known_after_load=0");
        }

        [Fact]
        public void CooldownTracker_RescaledCharges_UsesUnscaledNextRecharge()
        {
            var world = new SkillWorldBuilder().SkillDef(ChargeSkill()).Build();
            world.AddUnit(Unit);
            world.Targets.SetChain(Shape, Unit);
            var def = new SkillDefCache(world.Registry).GetSkillDef(Skill);
            var tracker = new CooldownTracker();
            tracker.RescaleAll(0.2);
            tracker.StartCooldown(Unit, def);
            tracker.StartCooldown(Unit, def);
            Assert.Equal(0, tracker.GetCharges(Unit, def));
            Assert.Equal(2.0, tracker.GetCooldown(Unit, def), 9);

            tracker.AdvanceCharges(Unit, def, 2.0);
            var afterFirst = tracker.GetCharges(Unit, def);
            var afterFirstRemaining = tracker.GetCooldown(Unit, def);
            // Consume the newly returned charge so GetCooldown exposes the
            // second recharge window rather than the ready sentinel (0).
            tracker.StartCooldown(Unit, def);
            var afterSecondConsumedRemaining = tracker.GetCooldown(Unit, def);
            tracker.AdvanceCharges(Unit, def, 2.0);
            var afterSecond = tracker.GetCharges(Unit, def);
            var afterSecondRemaining = tracker.GetCooldown(Unit, def);

            // StartCooldown scales the first window (10 * .2 = 2), while the
            // AdvanceCharges refill below adds the raw 10 rather than 10 * .2.
            Assert.Equal(1, afterFirst);
            Assert.Equal(0.0, afterFirstRemaining, 9);
            Assert.Equal(10.0, afterSecondConsumedRemaining, 9);
            Assert.Equal(0, afterSecond);
            Assert.Equal(8.0, afterSecondRemaining, 9);
            Console.WriteLine($"factor=0.2 max=2 recharge_time=10 charges_after_casts=0 t=2 charges={afterFirst} ready_remaining={afterFirstRemaining:0.########} consume_returned_charge remaining={afterSecondConsumedRemaining:0.########} t=4 charges={afterSecond} ready_remaining={afterSecondRemaining:0.########} expected_refill_window=2 actual_refill_window=10");
        }

        [Fact]
        public void CastPipeline_ChannelHalfSecond_IntervalOne_UpdateOneHasOneTick()
        {
            var skill = J.O(
                ("id", J.S("skill.audit_channel")),
                ("school", J.S("skill.school_audit_core")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("channel_time", J.N(0.5)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(Shape.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("tick_interval", J.N(1)), ("base_value", J.N(1)), ("coefficient", J.N(0))))))));
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Unit);
            world.Targets.SetChain(Shape, Unit);

            Assert.True(world.Host.CastSkill(Unit, new Id("skill.audit_channel"), Array.Empty<Id>()).Success);
            Assert.True(world.Host.IsCasting(Unit));
            world.Host.Update(1.0);

            Assert.Single(world.Combat.ResolveCalls);
            Assert.False(world.Host.IsCasting(Unit));
            Console.WriteLine("channel_time=0.5 tick_interval=1 dt=1 resolve_calls=1 casting_after_update=0");
        }
    }
}
