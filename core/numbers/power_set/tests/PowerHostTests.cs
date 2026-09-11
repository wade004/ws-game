using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Numbers.PowerSet;
using Xunit;

namespace Tests.Numbers.PowerSet
{
    public class PowerHostTests
    {
        private static readonly Id Hero = new Id("unit.hero");
        private static readonly Id Mana = new Id("arch.power.mana");
        private static readonly Id Rage = new Id("arch.power.rage");
        private static readonly Id ComboPoints = new Id("arch.power.combo_points");
        private static readonly Id ManaCapStat = new Id("stat.mana_cap");
        private static readonly Id ModifySource = new Id("skill.fireball");

        // -----------------------------------------------------------------
        // 注册 / 初始化
        // -----------------------------------------------------------------

        [Fact]
        public void RegisterUnit_FixedAndStatMax_InitializesToFullByDefault()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.StatType("arch.power.mana", "stat.mana_cap");
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100);

            double StatLookup(Id unitId, Id stat) => stat == ManaCapStat ? 50 : throw new InvalidOperationException("未预期的属性查询");

            var host = new PowerHost(new[] { mana, rage }, bus, StatLookup);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana", "arch.power.rage"));

            Assert.Equal(50, host.GetPower(Hero, Mana));
            Assert.Equal(50, host.GetPowerMax(Hero, Mana));
            Assert.Equal(100, host.GetPower(Hero, Rage));
            Assert.Equal(100, host.GetPowerMax(Hero, Rage));
        }

        [Fact]
        public void RegisterUnit_StartFullFalse_InitializesToMin()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, startFull: false, min: 5);

            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));

            Assert.Equal(5, host.GetPower(Hero, Rage));
        }

        [Fact]
        public void RegisterUnit_ThreeResourceTypesSimultaneously_AllIndependentlyQueryable()
        {
            // 证明资源池数量不硬编码为 1（见落地方案 T2-2 行禁止事项）。
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, startFull: false);
            var combo = PowerTestSupport.FixedType("arch.power.combo_points", maxValue: 5, startFull: false);

            var host = new PowerHost(new[] { mana, rage, combo }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana", "arch.power.rage", "arch.power.combo_points"));

            Assert.True(host.HasPower(Hero, Mana));
            Assert.True(host.HasPower(Hero, Rage));
            Assert.True(host.HasPower(Hero, ComboPoints));
            Assert.Equal(100, host.GetPower(Hero, Mana));
            Assert.Equal(0, host.GetPower(Hero, Rage));
            Assert.Equal(0, host.GetPower(Hero, ComboPoints));
            Assert.Equal(5, host.GetPowerMax(Hero, ComboPoints));
        }

        // -----------------------------------------------------------------
        // ModifyPower：夹取与事件
        // -----------------------------------------------------------------

        [Fact]
        public void ModifyPower_ClampsToUpperBound_WhenOverflowNotAllowed()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100, startFull: false);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));

            host.ModifyPower(Hero, Mana, 150, ModifySource);

            Assert.Equal(100, host.GetPower(Hero, Mana));
        }

        [Fact]
        public void ModifyPower_ClampsToLowerBound()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));

            host.ModifyPower(Hero, Mana, -500, ModifySource);

            Assert.Equal(0, host.GetPower(Hero, Mana));
        }

        [Fact]
        public void ModifyPower_AllowOverflow_ExceedsMaxOnPositiveDelta()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, allowOverflow: true);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));

            host.ModifyPower(Hero, Rage, 50, ModifySource);

            Assert.Equal(150, host.GetPower(Hero, Rage));
        }

        [Fact]
        public void ModifyPower_AllowOverflow_StillClampsToLowerBound()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, allowOverflow: true);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));

            host.ModifyPower(Hero, Rage, -500, ModifySource);

            Assert.Equal(0, host.GetPower(Hero, Rage));
        }

        [Fact]
        public void ModifyPower_ChangedEvent_CarriesExpectedFields_AndOnlyFiresOnActualChange()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100, startFull: false);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));

            var changed = new List<PowerChangedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));

            host.ModifyPower(Hero, Mana, 10, ModifySource);
            bus.DispatchPending();

            Assert.Single(changed);
            Assert.Equal(Hero, changed[0].UnitId);
            Assert.Equal(Mana, changed[0].PowerType);
            Assert.Equal(0, changed[0].OldValue);
            Assert.Equal(10, changed[0].NewValue);

            // 已在上限的资源再尝试正向修改：目标值与旧值相同，不应再发事件。
            host.ModifyPower(Hero, Mana, 1000, ModifySource); // -> 100
            bus.DispatchPending();
            changed.Clear();
            host.ModifyPower(Hero, Mana, 1000, ModifySource); // 已经是 100，目标仍是 100
            bus.DispatchPending();

            Assert.Empty(changed);
        }

        [Fact]
        public void PowerDepleted_FiresOnlyOnce_OnConsecutiveHitsToMin()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, startFull: false);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));
            host.ModifyPower(Hero, Rage, 10, ModifySource); // 0 -> 10

            var depleted = new List<PowerDepletedEvent>();
            bus.Subscribe<PowerDepletedEvent>(PowerEventKeys.Depleted, e => depleted.Add(e));

            host.ModifyPower(Hero, Rage, -50, ModifySource); // 10 -> 0，触发一次
            host.ModifyPower(Hero, Rage, -20, ModifySource); // 已经是 0，不再触发
            bus.DispatchPending();

            Assert.Single(depleted);
            Assert.Equal(Hero, depleted[0].UnitId);
            Assert.Equal(Rage, depleted[0].PowerType);
        }

        // -----------------------------------------------------------------
        // TryGetPower（消费方反馈第 33 条）
        // -----------------------------------------------------------------

        [Fact]
        public void TryGetPower_RegisteredType_ReturnsTrueAndCurrentValue()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));

            var found = host.TryGetPower(Hero, Rage, out var value);

            Assert.True(found);
            Assert.Equal(100, value);
        }

        [Fact]
        public void TryGetPower_UnitNotRegistered_ReturnsFalseWithoutThrowing()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100);
            var host = new PowerHost(new[] { rage }, bus);

            var found = host.TryGetPower(Hero, Rage, out var value);

            Assert.False(found);
            Assert.Equal(0, value);
        }

        [Fact]
        public void TryGetPower_UnitRegisteredButPowerTypeNotHeld_ReturnsFalseWithoutThrowing()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100);
            var host = new PowerHost(new[] { mana, rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana")); // 未注册 rage

            var found = host.TryGetPower(Hero, Rage, out var value);

            Assert.False(found);
            Assert.Equal(0, value);
            Assert.Throws<InvalidOperationException>(() => host.GetPower(Hero, Rage));
        }

        /// <summary>消费方反馈第 33 条："转发/豁免其它实现"——<see cref="PowerHost"/> 之外的
        /// <see cref="IPowerHost"/> 实现若不自行覆盖 <see cref="IPowerHost.TryGetPower"/>，自动
        /// 继承接口默认实现（try/catch 精确异常，见该成员判断记录），不需要逐个实现类补代码。
        /// <see cref="MinimalPowerHostStub"/> 只实现契约必需的成员、刻意不覆盖 <c>TryGetPower</c>，
        /// 证明默认实现本身对"未注册单位/未持有资源类型" 与"已持有资源类型"两种场景均给出正确
        /// 结果——不依赖 <see cref="PowerHost"/> 的具体覆盖是否存在。</summary>
        [Fact]
        public void TryGetPower_OnImplementationWithoutOwnOverride_UsesInterfaceDefaultImplementation()
        {
            IPowerHost host = new MinimalPowerHostStub();

            var foundMissing = host.TryGetPower(Hero, Rage, out var missingValue);
            var foundKnown = host.TryGetPower(Hero, Mana, out var knownValue);

            Assert.False(foundMissing);
            Assert.Equal(0, missingValue);
            Assert.True(foundKnown);
            Assert.Equal(42, knownValue);
        }

        /// <summary>见 <see cref="TryGetPower_OnImplementationWithoutOwnOverride_UsesInterfaceDefaultImplementation"/>：
        /// 最小 <see cref="IPowerHost"/> 实现，只认得一个硬编码的 (Hero, Mana) 组合，其余查询按契约
        /// 抛 <see cref="InvalidOperationException"/>（与 <see cref="PowerHost.GetPower"/> 同一异常
        /// 类型约定），本用例范围外的成员均不实现（抛 <see cref="NotImplementedException"/>，不会被
        /// 本用例调用到）。</summary>
        private sealed class MinimalPowerHostStub : IPowerHost
        {
            public void RegisterUnit(Id unitId, IReadOnlyList<Id> powerTypes) => throw new NotImplementedException();
            public void UnregisterUnit(Id unitId) => throw new NotImplementedException();
            public bool HasPower(Id unitId, Id powerType) => unitId == Hero && powerType == Mana;
            public double GetPower(Id unitId, Id powerType) =>
                HasPower(unitId, powerType) ? 42 : throw new InvalidOperationException("未注册");
            public double GetPowerMax(Id unitId, Id powerType) => throw new NotImplementedException();
            public void ModifyPower(Id unitId, Id powerType, double delta, Id sourceId) => throw new NotImplementedException();
            public void SetInCombat(Id unitId, bool inCombat) => throw new NotImplementedException();
            public void Advance(Id unitId, double timeUnits) => throw new NotImplementedException();
            public void AdvanceAll(double timeUnits) => throw new NotImplementedException();
            public void RecomputeMax(Id unitId) => throw new NotImplementedException();
        }

        // -----------------------------------------------------------------
        // 时间推进：回复 / 衰减
        // -----------------------------------------------------------------

        [Fact]
        public void Advance_RegenInCombat_PreciseValueAfterTwoPointFiveUnits()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100, startFull: false, regenInCombat: 4);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));
            host.SetInCombat(Hero, true);

            host.Advance(Hero, 2.5);

            Assert.Equal(10, host.GetPower(Hero, Mana));
        }

        [Fact]
        public void Advance_RegenOutOfCombat_PreciseValueAfterTwoPointFiveUnits()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100, startFull: false, regenOutOfCombat: 6);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));

            host.Advance(Hero, 2.5);

            Assert.Equal(15, host.GetPower(Hero, Mana));
        }

        [Fact]
        public void Advance_DecayAppliesOnlyOutOfCombat()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, decayOutOfCombat: 8);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage")); // 满值 100，脱战状态

            host.Advance(Hero, 2.5); // 脱战：100 - 8*2.5 = 80
            Assert.Equal(80, host.GetPower(Hero, Rage));

            host.SetInCombat(Hero, true);
            host.Advance(Hero, 2.5); // 战斗内：衰减不生效，保持 80
            Assert.Equal(80, host.GetPower(Hero, Rage));
        }

        [Fact]
        public void AdvanceAll_AppliesToEveryRegisteredUnitInOrder()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100, startFull: false, regenOutOfCombat: 2);
            var host = new PowerHost(new[] { mana }, bus);
            var unitA = new Id("unit.a");
            var unitB = new Id("unit.b");
            host.RegisterUnit(unitA, PowerTestSupport.Ids("arch.power.mana"));
            host.RegisterUnit(unitB, PowerTestSupport.Ids("arch.power.mana"));

            host.AdvanceAll(3);

            Assert.Equal(6, host.GetPower(unitA, Mana));
            Assert.Equal(6, host.GetPower(unitB, Mana));
        }

        // -----------------------------------------------------------------
        // 进出战斗
        // -----------------------------------------------------------------

        [Fact]
        public void SetInCombat_LeavingCombat_RefillsWhenConfigured()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100, startFull: false, refillOnLeaveCombat: true);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));

            var changed = new List<PowerChangedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));

            host.SetInCombat(Hero, true);
            host.SetInCombat(Hero, false); // 脱战瞬间：回满
            bus.DispatchPending();

            Assert.Equal(100, host.GetPower(Hero, Mana));
            Assert.Contains(changed, e => e.OldValue == 0 && e.NewValue == 100);
        }

        [Fact]
        public void SetInCombat_LeavingCombat_DoesNotRefillWhenNotConfigured()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100, startFull: false, refillOnLeaveCombat: false);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));

            host.SetInCombat(Hero, true);
            host.SetInCombat(Hero, false);

            Assert.Equal(0, host.GetPower(Hero, Mana));
        }

        // -----------------------------------------------------------------
        // 上限重算
        // -----------------------------------------------------------------

        [Fact]
        public void RecomputeMax_LoweredStatMax_ClampsCurrentValue()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.StatType("arch.power.mana", "stat.mana_cap");
            double cap = 100;
            var host = new PowerHost(new[] { mana }, bus, (unitId, stat) => cap);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));
            Assert.Equal(100, host.GetPower(Hero, Mana));

            var changed = new List<PowerChangedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));

            cap = 40;
            host.RecomputeMax(Hero);
            bus.DispatchPending();

            Assert.Equal(40, host.GetPowerMax(Hero, Mana));
            Assert.Equal(40, host.GetPower(Hero, Mana));
            Assert.Contains(changed, e => e.OldValue == 100 && e.NewValue == 40);
        }

        [Fact]
        public void RecomputeMax_FixedSourceType_IsUnaffected()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));

            host.RecomputeMax(Hero);

            Assert.Equal(100, host.GetPowerMax(Hero, Rage));
        }

        // -----------------------------------------------------------------
        // 异常路径
        // -----------------------------------------------------------------

        [Fact]
        public void GetPower_UnregisteredUnit_Throws()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var host = new PowerHost(new[] { mana }, bus);

            Assert.Throws<InvalidOperationException>(() => host.GetPower(Hero, Mana));
        }

        [Fact]
        public void RegisterUnit_UndefinedPowerType_Throws()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var host = new PowerHost(new[] { mana }, bus);

            Assert.Throws<InvalidOperationException>(() =>
                host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage")));
        }

        [Fact]
        public void ModifyPower_StatSourceWithoutStatLookup_ThrowsOnFirstUse()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.StatType("arch.power.mana", "stat.mana_cap");
            var host = new PowerHost(new[] { mana }, bus); // 未注入 StatLookup

            Assert.Throws<InvalidOperationException>(() =>
                host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana")));
        }

        // -----------------------------------------------------------------
        // W1 收边补齐（A3 审计 #16）：UnregisterUnit 无直接测试
        // -----------------------------------------------------------------

        [Fact]
        public void UnregisterUnit_RemovesUnit_SubsequentQueriesThrow()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));

            Assert.True(host.HasPower(Hero, Rage));

            host.UnregisterUnit(Hero);

            Assert.False(host.HasPower(Hero, Rage));
            Assert.Throws<InvalidOperationException>(() => host.GetPower(Hero, Rage));
        }

        [Fact]
        public void UnregisterUnit_UnknownUnit_Throws()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100);
            var host = new PowerHost(new[] { rage }, bus);

            Assert.Throws<InvalidOperationException>(() => host.UnregisterUnit(new Id("unit.never_registered")));
        }

        [Fact]
        public void UnregisterUnit_ThenRegisterAgain_Succeeds()
        {
            // UnregisterUnit 应真正从内部索引移除该单位，允许之后用同一个 id 重新 RegisterUnit
            // （RegisterUnit 对已注册单位会抛异常，见 RegisterUnit_DuplicateUnit_Throws 一类既有用例）。
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, startFull: false, min: 0);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));
            host.ModifyPower(Hero, Rage, 40, ModifySource);

            host.UnregisterUnit(Hero);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));

            // 重新注册后应是全新状态（回到 min，而不是保留 Unregister 之前的 40）。
            Assert.Equal(0, host.GetPower(Hero, Rage));
        }
    }
}
