using System;
using Core.Foundation.Common;
using Core.Numbers.PowerSet;
using Xunit;

namespace Tests.Numbers.PowerSet
{
    /// <summary>
    /// ADR-0108（消费方反馈第五十七批"静息回复"）决策 1：单位级运行期回复速率修饰器
    /// （<see cref="RegenModifier"/>/<see cref="IPowerHost.AddRegenModifier"/>/
    /// <see cref="IPowerHost.RemoveRegenModifier"/>）。见 <see cref="Core.Numbers.PowerSet.PowerHost"/>
    /// <c>ComputeEffectiveRegenRate</c> 判断记录"有效速率 = (定义速率 + ΣAdd) × ΠMultiplier"。
    /// </summary>
    public sealed class Adr0108_RegenModifierTests
    {
        private static readonly Id Hero = new Id("unit.adr0108_hero");
        private static readonly Id Energy = new Id("arch.power.adr0108_energy");
        private static readonly Id AuraA = new Id("skill.aura_inst_a");
        private static readonly Id AuraB = new Id("skill.aura_inst_b");

        private static PowerHost BuildHost(double regenOutOfCombat = 0, double regenInCombat = 0, double decayOutOfCombat = 0)
        {
            var bus = PowerTestSupport.CreateBus();
            var energy = PowerTestSupport.FixedType(
                "arch.power.adr0108_energy", maxValue: 1000, startFull: false,
                regenInCombat: regenInCombat, regenOutOfCombat: regenOutOfCombat, decayOutOfCombat: decayOutOfCombat);
            var host = new PowerHost(new[] { energy }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.adr0108_energy"));
            return host;
        }

        [Fact]
        public void OutOfCombatMultiplier_ScalesRegenRate_OnlyOutOfCombat()
        {
            var host = BuildHost(regenOutOfCombat: 6, regenInCombat: 4);
            host.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat, multiplier: 2.0));

            host.Advance(Hero, 2.5);
            Assert.Equal(6 * 2.0 * 2.5, host.GetPower(Hero, Energy));

            host.SetInCombat(Hero, true);
            var before = host.GetPower(Hero, Energy);
            host.Advance(Hero, 2.5);
            // 战斗内 scope=OutOfCombat 的修饰器不生效，按定义的 regen_in_combat=4 原样推进。
            Assert.Equal(before + 4 * 2.5, host.GetPower(Hero, Energy));
        }

        [Fact]
        public void Add_IncreasesBaseRateBeforeMultiplier()
        {
            var host = BuildHost(regenOutOfCombat: 6);
            host.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat, multiplier: 1.0, add: 2.0));

            host.Advance(Hero, 2.0);

            Assert.Equal((6 + 2) * 1.0 * 2.0, host.GetPower(Hero, Energy));
        }

        [Fact]
        public void AddAndMultiplier_CombineAsAddThenMultiplyFormula()
        {
            var host = BuildHost(regenOutOfCombat: 6);
            host.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat, multiplier: 3.0, add: 2.0));

            host.Advance(Hero, 1.0);

            Assert.Equal((6 + 2) * 3.0, host.GetPower(Hero, Energy));
        }

        [Fact]
        public void SameKey_RepeatedAdd_ReplacesRatherThanStacks()
        {
            var host = BuildHost(regenOutOfCombat: 6);
            host.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat, multiplier: 2.0));
            host.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat, multiplier: 3.0));

            host.Advance(Hero, 1.0);

            // 若发生"叠加"会是 6*2*3=36；替换语义下只有最后一次的 multiplier=3 生效。
            Assert.Equal(6 * 3.0, host.GetPower(Hero, Energy));
        }

        [Fact]
        public void MultipleKeys_CombineAddBySumAndMultiplierByProduct()
        {
            var host = BuildHost(regenOutOfCombat: 6);
            host.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat, multiplier: 2.0, add: 1.0));
            host.AddRegenModifier(Hero, Energy, AuraB, new RegenModifier(RegenScope.OutOfCombat, multiplier: 1.5, add: 3.0));

            host.Advance(Hero, 1.0);

            // (6 + 1 + 3) * (2.0 * 1.5) = 10 * 3 = 30。
            Assert.Equal((6 + 1 + 3) * (2.0 * 1.5), host.GetPower(Hero, Energy));
        }

        [Fact]
        public void RemoveRegenModifier_RevertsToDefinitionRate()
        {
            var host = BuildHost(regenOutOfCombat: 6);
            host.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat, multiplier: 5.0));
            host.RemoveRegenModifier(Hero, Energy, AuraA);

            host.Advance(Hero, 1.0);

            Assert.Equal(6, host.GetPower(Hero, Energy));
        }

        [Fact]
        public void RemoveRegenModifier_UnknownKeyOrUnregisteredTarget_IsSilentNoop()
        {
            var host = BuildHost(regenOutOfCombat: 6);

            // key 从未登记过。
            host.RemoveRegenModifier(Hero, Energy, new Id("skill.aura_inst_never_added"));
            // 单位/资源类型未注册。
            host.RemoveRegenModifier(new Id("unit.never_registered"), Energy, AuraA);
            host.RemoveRegenModifier(Hero, new Id("arch.power.never_registered"), AuraA);

            host.Advance(Hero, 1.0);
            Assert.Equal(6, host.GetPower(Hero, Energy)); // 未抛异常，行为不受影响。
        }

        [Fact]
        public void NegativeEffectiveRate_ClampsToZero_DoesNotDrainResource()
        {
            var host = BuildHost(regenOutOfCombat: 2);
            host.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat, multiplier: 1.0, add: -10.0));

            var before = host.GetPower(Hero, Energy);
            host.Advance(Hero, 3.0);

            Assert.Equal(before, host.GetPower(Hero, Energy)); // 有效速率被夹到 0，不产生负增量。
        }

        [Fact]
        public void ScopeBoth_AppliesRegardlessOfCombatState()
        {
            var host = BuildHost(regenOutOfCombat: 6, regenInCombat: 4);
            host.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.Both, multiplier: 2.0));

            host.Advance(Hero, 1.0);
            Assert.Equal(6 * 2.0, host.GetPower(Hero, Energy));

            host.SetInCombat(Hero, true);
            var before = host.GetPower(Hero, Energy);
            host.Advance(Hero, 1.0);
            Assert.Equal(before + 4 * 2.0, host.GetPower(Hero, Energy));
        }

        [Fact]
        public void UnregisterUnit_ClearsRegenModifiers_ReregisteredUnitStartsClean()
        {
            var bus = PowerTestSupport.CreateBus();
            var energy = PowerTestSupport.FixedType("arch.power.adr0108_energy", maxValue: 1000, startFull: false, regenOutOfCombat: 6);
            var host = new PowerHost(new[] { energy }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.adr0108_energy"));
            host.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat, multiplier: 10.0));

            host.UnregisterUnit(Hero);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.adr0108_energy"));

            host.Advance(Hero, 1.0);
            Assert.Equal(6, host.GetPower(Hero, Energy)); // 旧修饰器随注销一并清除，不残留到重新注册后。
        }

        [Fact]
        public void DecayOutOfCombat_UnaffectedByRegenModifiers()
        {
            // regen_out_of_combat 恒为 0：即使挂一个很大的 multiplier，(0 + add) × multiplier 只要
            // add 也是 0 就仍然是 0——本用例只登记 multiplier，不登记 add，专门验证"衰减速率"这个
            // 独立字段不会被回复修饰器公式波及（PowerHost.ComputeEffectiveRegenRate 只读取点在
            // regen 分支，AdvanceUnit 的 decay 分支读 definition.DecayOutOfCombat 原始值，未经过
            // 本方法）。
            var bus = PowerTestSupport.CreateBus();
            var energy = PowerTestSupport.FixedType(
                "arch.power.adr0108_energy", maxValue: 1000, startFull: true, regenOutOfCombat: 0, decayOutOfCombat: 8);
            var host = new PowerHost(new[] { energy }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.adr0108_energy"));
            host.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat, multiplier: 100.0));

            host.Advance(Hero, 2.0);

            Assert.Equal(1000 - 8 * 2.0, host.GetPower(Hero, Energy));
        }

        [Fact]
        public void AddRegenModifier_UnregisteredUnit_ThrowsInvalidOperationException()
        {
            var host = BuildHost();
            Assert.Throws<InvalidOperationException>(() =>
                host.AddRegenModifier(new Id("unit.never_registered"), Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat)));
        }

        [Fact]
        public void AddRegenModifier_UnknownPowerType_ThrowsInvalidOperationException()
        {
            var host = BuildHost();
            Assert.Throws<InvalidOperationException>(() =>
                host.AddRegenModifier(Hero, new Id("arch.power.never_registered"), AuraA, new RegenModifier(RegenScope.OutOfCombat)));
        }

        [Fact]
        public void RegenModifier_NegativeMultiplier_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new RegenModifier(RegenScope.OutOfCombat, multiplier: -1.0));
        }

        [Fact]
        public void DefaultInterfaceImplementation_ThrowsNotSupportedException()
        {
            IPowerHost fake = new BareFakePowerHost();
            Assert.Throws<NotSupportedException>(() =>
                fake.AddRegenModifier(Hero, Energy, AuraA, new RegenModifier(RegenScope.OutOfCombat)));
            Assert.Throws<NotSupportedException>(() =>
                fake.RemoveRegenModifier(Hero, Energy, AuraA));
        }

        /// <summary>只实现 <see cref="IPowerHost"/> 必须成员、不覆盖 <see
        /// cref="IPowerHost.AddRegenModifier"/>/<see cref="IPowerHost.RemoveRegenModifier"/> 默认接口
        /// 成员的最小假实现——验证默认体确实会抛出，不是悄悄空操作。</summary>
        private sealed class BareFakePowerHost : IPowerHost
        {
            public void RegisterUnit(Id unitId, System.Collections.Generic.IReadOnlyList<Id> powerTypes) => throw new NotImplementedException();
            public void UnregisterUnit(Id unitId) => throw new NotImplementedException();
            public bool HasPower(Id unitId, Id powerType) => throw new NotImplementedException();
            public double GetPower(Id unitId, Id powerType) => throw new NotImplementedException();
            public double GetPowerMax(Id unitId, Id powerType) => throw new NotImplementedException();
            public void ModifyPower(Id unitId, Id powerType, double delta, Id sourceId) => throw new NotImplementedException();
            public void SetInCombat(Id unitId, bool inCombat) => throw new NotImplementedException();
            public void Advance(Id unitId, double timeUnits) => throw new NotImplementedException();
            public void AdvanceAll(double timeUnits) => throw new NotImplementedException();
            public void RecomputeMax(Id unitId) => throw new NotImplementedException();
        }
    }
}
