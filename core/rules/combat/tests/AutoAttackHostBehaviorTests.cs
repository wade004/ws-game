using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Combat;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// T-M17（测试覆盖剩余项 2026-10-01）：<see cref="AutoAttackHost"/> 计时/暂停/余数/结算顺序的直接用例。
    /// 与 <c>AutoAttackHostSwingEventTests</c>（挥击事件）、<c>core/sim/tests/AutoAttackHostIntegrationTests</c>
    /// （端到端）互补：这里只摆布 Fake 依赖，用"结算调用次数 + 调用方顺序"观测计时语义，期望值全部由
    /// 武器间隔推出，不写死秒数。
    /// </summary>
    public class AutoAttackHostBehaviorTests
    {
        private static readonly Id Faction = new Id("fac.aa_behavior");
        private static readonly Id PhysicalSchool = new Id("school.aa_behavior_physical");
        private static readonly Id Target = new Id("unit.aa_target");
        private static readonly Id Caster = new Id("unit.aa_caster");

        private sealed class RecordingEffectSink : IEffectSink
        {
            public readonly List<Id> Casters = new List<Id>();

            public ResolveResult ApplyEffect(EffectContext context)
            {
                Casters.Add(context.SourceId);
                return new ResolveResult(HitResult.Hit, 10, 10, 0, false, false, null);
            }

            public AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null) =>
                throw new NotSupportedException();

            public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef) =>
                throw new NotSupportedException();
        }

        private sealed class FixedWeaponDamageQuery : IWeaponDamageQuery
        {
            public double Interval = 1.5;

            public double GetWeaponBaseDamage(Id unitId) => 10.0;
            public double GetWeaponDps(Id unitId) => 10.0;
            public double? GetWeaponAttackIntervalSeconds(Id unitId) => Interval;
            public Id? GetWeaponSchool(Id unitId) => PhysicalSchool;
        }

        private sealed class Fixture
        {
            public FakeUnitAccess Units = new FakeUnitAccess();
            public RecordingEffectSink Sink = new RecordingEffectSink();
            public FixedWeaponDamageQuery Weapon = new FixedWeaponDamageQuery();
            public AutoAttackHost Host = null!;
            public double Interval => Weapon.Interval;
            public int Swings => Sink.Casters.Count;
        }

        private static Fixture Build(params Id[] casters)
        {
            var fx = new Fixture();
            fx.Units.Add(Target, Faction);
            foreach (var caster in casters)
            {
                fx.Units.Add(caster, Faction);
            }

            fx.Host = new AutoAttackHost(fx.Units, new FakeAuraQuery(), fx.Sink, fx.Weapon, PhysicalSchool);
            return fx;
        }

        [Fact]
        public void SetTarget_ResetsSwingTimer_NoSwingUntilFullIntervalFromTheSwitch()
        {
            var fx = Build(Caster);
            var other = new Id("unit.aa_other_target");
            fx.Units.Add(other, Faction);
            fx.Host.SetTarget(Caster, Target);
            fx.Host.SetEnabled(Caster, true);

            fx.Host.Update(fx.Interval * 0.6);
            Assert.Equal(0, fx.Swings);

            // 切目标：攒下的 0.6 个间隔作废。若不清零，再推 0.6 个间隔会累计到 1.2 而挥击。
            fx.Host.SetTarget(Caster, other);
            fx.Host.Update(fx.Interval * 0.6);
            Assert.Equal(0, fx.Swings);

            fx.Host.Update(fx.Interval * 0.4);
            Assert.Equal(1, fx.Swings);
        }

        [Fact]
        public void SetTarget_SameTargetAgain_AlsoResetsTimer()
        {
            var fx = Build(Caster);
            fx.Host.SetTarget(Caster, Target);
            fx.Host.SetEnabled(Caster, true);

            fx.Host.Update(fx.Interval * 0.6);
            fx.Host.SetTarget(Caster, Target);
            fx.Host.Update(fx.Interval * 0.6);

            Assert.Equal(0, fx.Swings);
        }

        [Fact]
        public void SetEnabled_FalseThenTrue_ResetsTimer_ButRepeatedTrueDoesNot()
        {
            var fx = Build(Caster);
            fx.Host.SetTarget(Caster, Target);
            fx.Host.SetEnabled(Caster, true);

            // 重复 SetEnabled(true) 不清零：0.6 + 0.6 个间隔累计越过一个间隔，挥击一次。
            fx.Host.Update(fx.Interval * 0.6);
            fx.Host.SetEnabled(Caster, true);
            fx.Host.Update(fx.Interval * 0.6);
            Assert.Equal(1, fx.Swings);

            // 关闭再开启：计时清零（余下 0.2 个间隔的余数也作废）。
            fx.Host.SetEnabled(Caster, false);
            fx.Host.SetEnabled(Caster, true);
            fx.Host.Update(fx.Interval * 0.9);
            Assert.Equal(1, fx.Swings);
            fx.Host.Update(fx.Interval * 0.1);
            Assert.Equal(2, fx.Swings);
        }

        [Fact]
        public void SetEnabled_False_KeepsTarget_AndStateIsOffUntilReenabled()
        {
            var fx = Build(Caster);
            fx.Host.SetTarget(Caster, Target);
            fx.Host.SetEnabled(Caster, true);
            Assert.Equal(AutoAttackState.Attacking, fx.Host.GetState(Caster));

            fx.Host.SetEnabled(Caster, false);
            Assert.Equal(AutoAttackState.Off, fx.Host.GetState(Caster));
            Assert.Equal(Target, fx.Host.GetTarget(Caster));

            fx.Host.Update(fx.Interval * 5);
            Assert.Equal(0, fx.Swings);

            fx.Host.SetEnabled(Caster, true);
            Assert.Equal(AutoAttackState.Attacking, fx.Host.GetState(Caster));
        }

        [Fact]
        public void CasterDead_PausesTimer_KeepsTarget_AndResumesWithBankedProgressAfterRevive()
        {
            var fx = Build(Caster);
            fx.Host.SetTarget(Caster, Target);
            fx.Host.SetEnabled(Caster, true);
            fx.Host.Update(fx.Interval * 0.5);

            fx.Units.SetAlive(Caster, false);
            fx.Host.Update(fx.Interval * 10);

            // 施法者死亡：不挥击、不清目标，状态仍是 Attacking（目标与开关是正交状态，死亡只暂停）。
            Assert.Equal(0, fx.Swings);
            Assert.Equal(Target, fx.Host.GetTarget(Caster));
            Assert.Equal(AutoAttackState.Attacking, fx.Host.GetState(Caster));

            // 复活：死亡期间不推进计时，原先攒下的 0.5 个间隔保留；再推 0.5 个间隔恰好够一拍。
            fx.Units.SetAlive(Caster, true);
            fx.Host.Update(fx.Interval * 0.4);
            Assert.Equal(0, fx.Swings);
            fx.Host.Update(fx.Interval * 0.1);
            Assert.Equal(1, fx.Swings);
        }

        [Fact]
        public void CasterDespawned_DoesNotThrow_KeepsTarget_NoSwing()
        {
            var fx = Build(Caster);
            fx.Host.SetTarget(Caster, Target);
            fx.Host.SetEnabled(Caster, true);
            fx.Units.Despawn(Caster);

            var ex = Record.Exception(() => fx.Host.Update(fx.Interval * 3));

            Assert.Null(ex);
            Assert.Equal(0, fx.Swings);
            Assert.Equal(Target, fx.Host.GetTarget(Caster));
        }

        [Fact]
        public void LargeDt_SwingsOnlyOncePerUpdate_AndRemainderAccumulatesAcrossUpdates()
        {
            var fx = Build(Caster);
            fx.Host.SetTarget(Caster, Target);
            fx.Host.SetEnabled(Caster, true);

            // 一次 dt = 2.5 个间隔：本次只结算一拍（不补发"积压"的第二拍），剩余 1.5 个间隔留作余数。
            fx.Host.Update(fx.Interval * 2.5);
            Assert.Equal(1, fx.Swings);

            // 余数 1.5 个间隔已超过一个间隔：下一次任意正 dt 的 Update 立刻再挥一拍，余数降到 0.5 个间隔以上。
            var tiny = fx.Interval * 0.01;
            fx.Host.Update(tiny);
            Assert.Equal(2, fx.Swings);

            // 余数 0.5 个间隔 + 2 个 tiny 不足一拍。
            fx.Host.Update(tiny);
            Assert.Equal(2, fx.Swings);

            // 再补 0.5 个间隔，余数越过一个间隔，第三拍。
            fx.Host.Update(fx.Interval * 0.5);
            Assert.Equal(3, fx.Swings);
        }

        [Fact]
        public void NonPositiveDt_IsIgnored()
        {
            var fx = Build(Caster);
            fx.Host.SetTarget(Caster, Target);
            fx.Host.SetEnabled(Caster, true);

            fx.Host.Update(0.0);
            fx.Host.Update(-fx.Interval * 5);
            Assert.Equal(0, fx.Swings);

            // 负 dt 不会倒扣计时：随后恰好一个间隔仍然挥击一次。
            fx.Host.Update(fx.Interval);
            Assert.Equal(1, fx.Swings);
        }

        [Fact]
        public void MultipleCasters_SettleInOrdinalIdOrder_RegardlessOfRegistrationOrder()
        {
            var b = new Id("unit.aa_order_b");
            var a = new Id("unit.aa_order_a");
            var c = new Id("unit.aa_order_c");
            var fx = Build(b, a, c);

            // 故意按 b、c、a 的顺序登记，结算顺序必须仍是序数升序 a、b、c。
            foreach (var id in new[] { b, c, a })
            {
                fx.Host.SetTarget(id, Target);
                fx.Host.SetEnabled(id, true);
            }

            fx.Host.Update(fx.Interval);

            var expected = new List<Id> { a, b, c };
            expected.Sort((x, y) => string.CompareOrdinal(x.Value, y.Value));
            Assert.Equal(expected, fx.Sink.Casters);
        }
    }
}
