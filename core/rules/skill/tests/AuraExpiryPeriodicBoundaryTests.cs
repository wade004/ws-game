using Core.Foundation.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// R07 收口（外部审计 5e779c6，P2；见 <c>Core.Rules.Skill.AuraHost.Update</c> 判断记录）：一次
    /// <c>dt</c> 超过光环剩余持续时间时（主循环追帧/大步长跨过到期点），周期效果只应按"到期前"那
    /// 一段时长结算，不能把到期之后本不该存在的时间余量也计入周期累加器。
    /// </summary>
    public sealed class AuraExpiryPeriodicBoundaryTests
    {
        private static readonly Id Target = new Id("unit.aepb_target");
        private static readonly Id AuraDefId = new Id("skill.aura_def.aepb_dot");

        [Fact]
        public void LargeDtCrossingExpiry_OnlyTicksForRemainingDurationBeforeExpiry_NotFullDt()
        {
            // duration=1、interval=0.3：到期前理论上只应结算 floor(1 / 0.3) = 3 次。
            var aura = J.O(
                ("id", J.S(AuraDefId.Value)),
                ("duration", J.N(1)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(0.3)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_aepb"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(Target);
            world.Host.EffectSink.ApplyAura(Target, AuraDefId, new Id("unit.aepb_source"));

            // 一次大步长（dt=5）远超剩余持续时间（1）——修复前会按 floor(5/0.3)=16 次结算，
            // 修复后应恰好是到期前的 floor(1/0.3)=3 次。
            world.Host.Update(5.0);

            Assert.Equal(3, world.Combat.ResolveCalls.Count);
            Assert.False(world.Host.AuraQuery.HasAura(Target, AuraDefId), "剩余持续时间已耗尽，光环应当已到期移除");
        }

        [Fact]
        public void DtExactlyEqualToRemaining_TicksBoundaryOnce_ThenExpires()
        {
            // duration=2、interval=1：到期点恰好落在一次 interval 边界上，应结算 2 次（t=1、t=2）后到期。
            var aura = J.O(
                ("id", J.S(AuraDefId.Value)),
                ("duration", J.N(2)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(1)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_aepb"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(Target);
            world.Host.EffectSink.ApplyAura(Target, AuraDefId, new Id("unit.aepb_source"));

            world.Host.Update(2.0); // 一次 tick 恰好覆盖整个持续时间。

            Assert.Equal(2, world.Combat.ResolveCalls.Count);
            Assert.False(world.Host.AuraQuery.HasAura(Target, AuraDefId));
        }

        [Fact]
        public void SmallDtsNotCrossingExpiry_BehavesIdenticallyToBeforeFix_Regression()
        {
            // 回归：dt 均未跨越到期点时，行为应与逐步推进完全一致（AuraEffectTests.
            // PeriodicDamage_TicksExpectedNumberOfTimes 同款惯例）。
            var aura = J.O(
                ("id", J.S(AuraDefId.Value)),
                ("duration", J.N(6)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(2)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_aepb"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(Target);
            world.Host.EffectSink.ApplyAura(Target, AuraDefId, new Id("unit.aepb_source"));

            world.Host.Update(2.0);
            world.Host.Update(2.0);
            world.Host.Update(2.0);

            Assert.Equal(3, world.Combat.ResolveCalls.Count);
        }

        [Fact]
        public void PermanentAura_NoExpiry_LargeDtStillTicksFullAmount_Regression()
        {
            // 永久光环（duration 未声明，Remaining 恒为 null）没有到期点，本次修复不应改变其行为——
            // 仍用完整 dt 结算。
            var aura = J.O(
                ("id", J.S(AuraDefId.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(0.3)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_aepb"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(Target);
            world.Host.EffectSink.ApplyAura(Target, AuraDefId, new Id("unit.aepb_source"));

            world.Host.Update(5.0);

            Assert.Equal(16, world.Combat.ResolveCalls.Count); // floor(5 / 0.3) = 16。
            Assert.True(world.Host.AuraQuery.HasAura(Target, AuraDefId), "永久光环不应到期");
        }
    }
}
