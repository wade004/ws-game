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
        // 消费方反馈-2026-09-17（读档触发脱战回满）根治：RestoreInCombat 纯赋值，不触发回满。
        // -----------------------------------------------------------------

        /// <summary>
        /// 核心回归：与 <see cref="SetInCombat_LeavingCombat_RefillsWhenConfigured"/> 完全相同的
        /// true→false 转换、完全相同的 <c>refill_on_leave_combat=true</c> 配置——唯一区别是恢复入口
        /// 换成 <see cref="PowerHost.RestoreInCombat"/>。修复前 <c>CombatHost.RestoreCombatState</c>
        /// 复用 <see cref="PowerHost.SetInCombat"/> 会命中这条回满分支（真实探针复现：存档
        /// health=37 被回满成 100）；<see cref="PowerHost.RestoreInCombat"/> 必须只做纯赋值，不
        /// 触发回满、不发任何事件。
        /// </summary>
        [Fact]
        public void RestoreInCombat_TrueToFalse_DoesNotRefill_EvenWhenConfigured()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100, startFull: false, refillOnLeaveCombat: true);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));

            var changed = new List<PowerChangedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));

            host.SetInCombat(Hero, true); // 真实进战：运行期状态置为 true（同存档恢复前的运行期快照）。
            host.ModifyPower(Hero, Mana, 37, ModifySource); // 模拟"存档恢复当前值"这一步已经完成。
            bus.DispatchPending(); // 排空上面 ModifyPower 产生的事件，避免污染下方"不应发事件"的断言。
            Assert.Equal(37, host.GetPower(Hero, Mana));
            changed.Clear();

            host.RestoreInCombat(Hero, false); // 存档快照 in_combat=false：不是真实脱战。
            bus.DispatchPending();

            Assert.Equal(37, host.GetPower(Hero, Mana));
            Assert.False(host.IsInCombat(Hero));
            Assert.Empty(changed);
        }

        [Fact]
        public void RestoreInCombat_SetsFlag_InBothDirections()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));

            Assert.False(host.IsInCombat(Hero));

            host.RestoreInCombat(Hero, true);
            Assert.True(host.IsInCombat(Hero));

            host.RestoreInCombat(Hero, false);
            Assert.False(host.IsInCombat(Hero));
        }

        // -----------------------------------------------------------------
        // T-N4-5：RefillAll（升级回满）
        // -----------------------------------------------------------------

        [Fact]
        public void RefillAll_RefillsEachRegisteredResourceToMax_AndFiresOnePowerChangedPerPool()
        {
            var bus = PowerTestSupport.CreateBus();
            var health = PowerTestSupport.FixedType("arch.power.health", maxValue: 100);
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 50);
            var healthId = new Id("arch.power.health");
            var host = new PowerHost(new[] { health, mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.health", "arch.power.mana"));

            host.ModifyPower(Hero, healthId, -60, ModifySource); // 100 -> 40
            host.ModifyPower(Hero, Mana, -20, ModifySource); // 50 -> 30
            bus.DispatchPending(); // 冲掉上面两次 ModifyPower 排队的事件，只观察 RefillAll 自己发的。

            var changed = new List<PowerChangedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));

            host.RefillAll(Hero, new Id("progression.level_up"));
            bus.DispatchPending();

            Assert.Equal(100, host.GetPower(Hero, healthId));
            Assert.Equal(50, host.GetPower(Hero, Mana));

            var healthChanged = Assert.Single(changed, e => e.PowerType == healthId);
            Assert.Equal(40, healthChanged.OldValue);
            Assert.Equal(100, healthChanged.NewValue);

            var manaChanged = Assert.Single(changed, e => e.PowerType == Mana);
            Assert.Equal(30, manaChanged.OldValue);
            Assert.Equal(50, manaChanged.NewValue);
        }

        [Fact]
        public void RefillAll_AccumulationTypeResource_StartFullFalse_IsNotRefilled_NoEventFired()
        {
            // 积累型资源（如连击点，start_full=false）不在升级回满范围内——见 IPowerHost.RefillAll
            // 判断记录"契约疑点上报（积累型资源是否回满）"。
            var bus = PowerTestSupport.CreateBus();
            var combo = PowerTestSupport.FixedType("arch.power.combo_points", maxValue: 5, startFull: false);
            var host = new PowerHost(new[] { combo }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.combo_points"));

            host.ModifyPower(Hero, ComboPoints, 2, ModifySource); // 0 -> 2（部分累积，不是满也不是空）
            bus.DispatchPending(); // 冲掉上面这次 ModifyPower 排队的事件，只观察 RefillAll 自己发的。

            var changed = new List<PowerChangedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));

            host.RefillAll(Hero, new Id("progression.level_up"));
            bus.DispatchPending();

            Assert.Equal(2, host.GetPower(Hero, ComboPoints)); // 原样不动，没有被拉到上限 5。
            Assert.Empty(changed);
        }

        [Fact]
        public void RefillAll_MixedStartFullAndAccumulationResources_OnlyRefillsStartFullOnes()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var combo = PowerTestSupport.FixedType("arch.power.combo_points", maxValue: 5, startFull: false);
            var host = new PowerHost(new[] { mana, combo }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana", "arch.power.combo_points"));

            host.ModifyPower(Hero, Mana, -40, ModifySource); // 100 -> 60
            host.ModifyPower(Hero, ComboPoints, 3, ModifySource); // 0 -> 3

            host.RefillAll(Hero, new Id("progression.level_up"));

            Assert.Equal(100, host.GetPower(Hero, Mana)); // 回复型：回满。
            Assert.Equal(3, host.GetPower(Hero, ComboPoints)); // 积累型：原样不动。
        }

        [Fact]
        public void RefillAll_AlreadyAtMax_DoesNotFireEvent()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));

            var changed = new List<PowerChangedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));

            host.RefillAll(Hero, new Id("progression.level_up")); // 已经是满值（start_full 默认 true）。
            bus.DispatchPending();

            Assert.Equal(100, host.GetPower(Hero, Mana));
            Assert.Empty(changed);
        }

        [Fact]
        public void RefillAll_UnregisteredUnit_Throws()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var host = new PowerHost(new[] { mana }, bus);

            Assert.Throws<InvalidOperationException>(() => host.RefillAll(Hero, new Id("progression.level_up")));
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
            // （RegisterUnit 对已注册单位会抛异常，见本文件 RegisterUnit_DuplicateUnit_Throws 用例）。
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

        // -----------------------------------------------------------------
        // SetMinOverride（ADR-0106：单位级资源下限覆盖，消费方反馈第五十五批）
        // -----------------------------------------------------------------

        [Fact]
        public void SetMinOverride_BelowDefinitionMin_Throws()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, min: 5);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));

            Assert.Throws<ArgumentException>(() => host.SetMinOverride(Hero, Rage, 4));
        }

        [Fact]
        public void SetMinOverride_AboveCurrentMax_Throws()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));

            Assert.Throws<ArgumentException>(() => host.SetMinOverride(Hero, Rage, 101));
        }

        [Fact]
        public void SetMinOverride_UnregisteredUnit_Throws()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100);
            var host = new PowerHost(new[] { rage }, bus);

            Assert.Throws<InvalidOperationException>(() => host.SetMinOverride(new Id("unit.never_registered"), Rage, 1));
        }

        [Fact]
        public void SetMinOverride_UnregisteredPowerType_Throws()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100);
            var host = new PowerHost(new[] { mana, rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));

            // Hero 只注册了 mana，没有 rage。
            Assert.Throws<InvalidOperationException>(() => host.SetMinOverride(Hero, Rage, 1));
        }

        [Fact]
        public void SetMinOverride_ClampsFutureModifyPower_NeverBelowOverride_AndAmountEventUnaffected()
        {
            // 复现 ADR-0106 设计意图的最小单元版本：覆盖下限后，ModifyPower 无论传入多大的负增量，
            // 当前值都不会跌破覆盖值——呼应 core/rules/combat.Resolver 判断记录"选覆盖下限而非死亡
            // 判定特判"：Resolver 自己不需要改，落地入口本身已经把值夹在覆盖下限之上。
            var bus = PowerTestSupport.CreateBus();
            var health = PowerTestSupport.FixedType("arch.power.health", maxValue: 100);
            var host = new PowerHost(new[] { health }, bus);
            var healthId = new Id("arch.power.health");
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.health"));

            host.SetMinOverride(Hero, healthId, 1);

            host.ModifyPower(Hero, healthId, -10000, ModifySource);
            Assert.Equal(1, host.GetPower(Hero, healthId));

            host.ModifyPower(Hero, healthId, -1, ModifySource);
            Assert.Equal(1, host.GetPower(Hero, healthId));
        }

        [Fact]
        public void SetMinOverride_CurrentBelowNewFloor_ImmediatelyClampsUp_FiresChangedNotDepleted()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, startFull: false);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage")); // 当前值 0（startFull=false, min=0）

            var changed = new List<PowerChangedEvent>();
            var depleted = new List<PowerDepletedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));
            bus.Subscribe<PowerDepletedEvent>(PowerEventKeys.Depleted, e => depleted.Add(e));

            host.SetMinOverride(Hero, Rage, 10); // 当前值 0 < 新下限 10，应立即夹上去

            bus.DispatchPending();

            Assert.Equal(10, host.GetPower(Hero, Rage));
            Assert.Single(changed);
            Assert.Equal(0, changed[0].OldValue);
            Assert.Equal(10, changed[0].NewValue);
            // 夹的方向是从低于下限升到下限，不是从高于下限跌到下限，不应发 depleted。
            Assert.Empty(depleted);
        }

        [Fact]
        public void SetMinOverride_Null_ClearsOverride_FallsBackToDefinitionMin()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, min: 0);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));

            host.SetMinOverride(Hero, Rage, 20);
            host.ModifyPower(Hero, Rage, -1000, ModifySource);
            Assert.Equal(20, host.GetPower(Hero, Rage));

            host.SetMinOverride(Hero, Rage, null);
            host.ModifyPower(Hero, Rage, -1000, ModifySource);

            // 覆盖已清除，回落到资源类型定义的 min（本例为 0）。
            Assert.Equal(0, host.GetPower(Hero, Rage));
        }

        [Fact]
        public void PowerDepleted_FiresOnlyOnce_OnConsecutiveHitsToOverrideFloor()
        {
            // PowerDepleted_FiresOnlyOnce_OnConsecutiveHitsToMin 的覆盖版本："跌到下限"改按
            // EffectiveMin（覆盖值）判定，不再是资源类型定义的全局 min——从高于覆盖值跌到覆盖值仍应
            // 发一次 power.depleted（语义是"跌到这个单位当前生效的下限"，不是"永不再发 depleted"；
            // "受伤但不死"依赖的是 Resolver 判死用的 GetPower() <= 0 这条路径夹在覆盖值之上，不依赖
            // 本事件是否发出，见 core/rules/combat.Resolver 判断记录），反复打到覆盖值只发一次。
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, startFull: false);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));
            host.SetMinOverride(Hero, Rage, 5);
            host.ModifyPower(Hero, Rage, 50, ModifySource); // 5 -> 55（先垫高，避免 SetMinOverride 自己的夹取事件混进来）
            bus.DispatchPending();

            var depleted = new List<PowerDepletedEvent>();
            bus.Subscribe<PowerDepletedEvent>(PowerEventKeys.Depleted, e => depleted.Add(e));

            host.ModifyPower(Hero, Rage, -1000, ModifySource); // 55 -> 5（覆盖下限），跌到下限，发一次
            host.ModifyPower(Hero, Rage, -1000, ModifySource); // 已经是 5，不再变化，不再发
            bus.DispatchPending();

            Assert.Equal(5, host.GetPower(Hero, Rage));
            Assert.Single(depleted);
            Assert.Equal(Hero, depleted[0].UnitId);
            Assert.Equal(Rage, depleted[0].PowerType);
        }

        [Fact]
        public void UnregisterUnit_ThenRegisterAgain_DoesNotCarryOverPreviousOverride()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, startFull: false);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));
            host.SetMinOverride(Hero, Rage, 30);

            host.UnregisterUnit(Hero);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));

            // 覆盖随单位一起被清除：重新注册后 ModifyPower 应能打到资源类型定义的 min（0），不再
            // 停在旧覆盖值 30。
            host.ModifyPower(Hero, Rage, -1000, ModifySource);
            Assert.Equal(0, host.GetPower(Hero, Rage));
        }

        [Fact]
        public void Advance_WithMinOverride_RegenStillAppliesAboveFloor()
        {
            // 不变量⑥：覆盖下限只改变"能跌到多低"，不影响下限之上的正常回复（regen）行为。
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, startFull: false, regenOutOfCombat: 3);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));
            host.SetMinOverride(Hero, Rage, 5); // 当前值 0 < 5，立即夹到 5

            host.Advance(Hero, 2.0); // 脱战 regen 3/秒 × 2 秒 = +6

            Assert.Equal(11, host.GetPower(Hero, Rage));
        }

        // -----------------------------------------------------------------
        // ADR-0125 D17 / T-H5：非法入参路径
        // -----------------------------------------------------------------

        /// <summary>非有限数三取值（NaN、+Infinity、-Infinity），供 D17 各写入口共用。</summary>
        public static IEnumerable<object[]> NonFiniteValues => new[]
        {
            new object[] { double.NaN },
            new object[] { double.PositiveInfinity },
            new object[] { double.NegativeInfinity },
        };

        /// <summary>D17 复现/守护：修复前 <c>ModifyPower(NaN)</c> 会把当前值写成 NaN 并发 <c>power.changed</c>
        /// （夹取对 NaN 不生效）。修复后抛 <see cref="ArgumentOutOfRangeException"/>，不发事件，当前值不变。</summary>
        [Theory]
        [MemberData(nameof(NonFiniteValues))]
        public void ModifyPower_NonFiniteDelta_Throws_NoEvent_StateUnchanged(double bad)
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, startFull: false);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));
            host.ModifyPower(Hero, Rage, 30, ModifySource);
            bus.DispatchPending();

            var changed = new List<PowerChangedEvent>();
            var depleted = new List<PowerDepletedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));
            bus.Subscribe<PowerDepletedEvent>(PowerEventKeys.Depleted, e => depleted.Add(e));

            Assert.Throws<ArgumentOutOfRangeException>(() => host.ModifyPower(Hero, Rage, bad, ModifySource));
            Assert.Throws<ArgumentOutOfRangeException>(() => host.ModifyPower(Hero, Rage, bad, ModifySource));
            bus.DispatchPending();

            Assert.Empty(changed);
            Assert.Empty(depleted);
            Assert.Equal(30, host.GetPower(Hero, Rage));
        }

        /// <summary>D17 同类写入口：<c>Advance</c>/<c>AdvanceAll</c> 的时长非有限时抛出（修复前 NaN 会经
        /// <c>regenRate * NaN</c> 把当前值写成 NaN），状态不变、不发事件。</summary>
        [Theory]
        [MemberData(nameof(NonFiniteValues))]
        public void Advance_AndAdvanceAll_NonFiniteDuration_Throws_StateUnchanged(double bad)
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100, startFull: false, regenOutOfCombat: 2);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));
            host.Advance(Hero, 5); // 由规则：regen 2 * 5
            bus.DispatchPending();
            var expected = 2.0 * 5.0;
            Assert.Equal(expected, host.GetPower(Hero, Mana));

            var changed = new List<PowerChangedEvent>();
            bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, e => changed.Add(e));

            Assert.Throws<ArgumentOutOfRangeException>(() => host.Advance(Hero, bad));
            Assert.Throws<ArgumentOutOfRangeException>(() => host.AdvanceAll(bad));
            bus.DispatchPending();

            Assert.Empty(changed);
            Assert.Equal(expected, host.GetPower(Hero, Mana));
        }

        /// <summary>D17 同类写入口：<c>SetMinOverride</c> 的下限覆盖值为 NaN/±Infinity 时抛
        /// <see cref="ArgumentOutOfRangeException"/>（修复前 NaN 通过区间比较校验，写成 NaN 下限）；
        /// 已有的覆盖与当前值不变。</summary>
        [Theory]
        [MemberData(nameof(NonFiniteValues))]
        public void SetMinOverride_NonFinite_Throws_StateUnchanged(double bad)
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100, startFull: false);
            var host = new PowerHost(new[] { rage }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.rage"));
            host.SetMinOverride(Hero, Rage, 5); // 当前值 0 -> 夹到 5
            Assert.Equal(5, host.GetPower(Hero, Rage));

            Assert.Throws<ArgumentOutOfRangeException>(() => host.SetMinOverride(Hero, Rage, bad));

            // 覆盖仍为 5：继续向下打到下限，停在 5。
            host.ModifyPower(Hero, Rage, -1000, ModifySource);
            Assert.Equal(5, host.GetPower(Hero, Rage));
        }

        /// <summary>D17 同类写入口：<see cref="RegenModifier"/> 构造期对非有限的 multiplier/add 抛
        /// <see cref="ArgumentOutOfRangeException"/>（修复前 NaN 通过 <c>multiplier &lt; 0</c> 校验）。</summary>
        [Theory]
        [MemberData(nameof(NonFiniteValues))]
        public void RegenModifier_NonFiniteMultiplierOrAdd_Throws(double bad)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RegenModifier(RegenScope.Both, multiplier: bad));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RegenModifier(RegenScope.Both, add: bad));
        }

        /// <summary>T-H5：同一单位重复 <c>RegisterUnit</c> 抛 <see cref="InvalidOperationException"/>，
        /// 已有状态不被重置，<c>AdvanceAll</c> 也不会把该单位推进两次（<c>_unitOrder</c> 未被重复追加）。
        /// （<see cref="UnregisterUnit_ThenRegisterAgain_Succeeds"/> 注释引用本用例。）</summary>
        [Fact]
        public void RegisterUnit_DuplicateUnit_Throws()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100, startFull: false, regenOutOfCombat: 2);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));
            host.ModifyPower(Hero, Mana, 10, ModifySource);

            Assert.Throws<InvalidOperationException>(() =>
                host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana")));

            Assert.Equal(10, host.GetPower(Hero, Mana)); // 没有被重置回 min
            host.AdvanceAll(3);
            Assert.Equal(10 + 2.0 * 3, host.GetPower(Hero, Mana)); // 只推进一次
        }

        /// <summary>T-H5：<c>RegisterUnit</c> 的 <c>powerTypes</c> 参数校验——null 抛
        /// <see cref="ArgumentNullException"/>；空列表、同一次调用内重复的资源类型抛
        /// <see cref="ArgumentException"/>；失败后单位不会留下半注册状态。</summary>
        [Fact]
        public void RegisterUnit_InvalidPowerTypeList_Throws_AndLeavesNoResidue()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var rage = PowerTestSupport.FixedType("arch.power.rage", maxValue: 100);
            var host = new PowerHost(new[] { mana, rage }, bus);

            Assert.Throws<ArgumentNullException>(() => host.RegisterUnit(Hero, null!));
            Assert.Throws<ArgumentException>(() => host.RegisterUnit(Hero, PowerTestSupport.Ids()));
            // 重复项出现在列表后部：前面的合法项已经处理过，失败后仍不能留下单位。
            Assert.Throws<ArgumentException>(() =>
                host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana", "arch.power.rage", "arch.power.mana")));

            Assert.False(host.IsRegistered(Hero));
            // 随后用合法列表仍可正常注册（无残留）。
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana", "arch.power.rage"));
            Assert.Equal(2, host.GetRegisteredPowerTypes(Hero).Count);
        }

        /// <summary>T-H5：<c>Advance</c>/<c>AdvanceAll</c> 负时长抛 <see cref="ArgumentException"/>，当前值不变；
        /// 0 时长是合法 no-op。</summary>
        [Fact]
        public void Advance_AndAdvanceAll_NegativeDuration_Throws_ZeroIsNoOp()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100, startFull: false, regenOutOfCombat: 2);
            var host = new PowerHost(new[] { mana }, bus);
            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana"));
            host.Advance(Hero, 4);
            var expected = 2.0 * 4.0;

            Assert.Throws<ArgumentException>(() => host.Advance(Hero, -0.5));
            Assert.Throws<ArgumentException>(() => host.AdvanceAll(-0.5));
            Assert.Equal(expected, host.GetPower(Hero, Mana));

            host.Advance(Hero, 0);
            host.AdvanceAll(0);
            Assert.Equal(expected, host.GetPower(Hero, Mana));
        }

        /// <summary>T-H5：构造期资源类型定义列表校验——集合本身为 null 抛
        /// <see cref="ArgumentNullException"/>；含 null 元素、同 id 重复定义抛 <see cref="ArgumentException"/>；
        /// 总线为 null 抛 <see cref="ArgumentNullException"/>；空集合合法（之后注册任何资源类型都因未登记而抛
        /// <see cref="InvalidOperationException"/>）。</summary>
        [Fact]
        public void Constructor_InvalidPowerTypeDefinitions_Throw()
        {
            var bus = PowerTestSupport.CreateBus();
            var mana = PowerTestSupport.FixedType("arch.power.mana", maxValue: 100);
            var manaAgain = PowerTestSupport.FixedType("arch.power.mana", maxValue: 50);

            Assert.Throws<ArgumentNullException>(() => new PowerHost(null!, bus));
            Assert.Throws<ArgumentException>(() => new PowerHost(new PowerTypeDefinition[] { mana, null! }, bus));
            Assert.Throws<ArgumentException>(() => new PowerHost(new[] { mana, manaAgain }, bus));
            Assert.Throws<ArgumentNullException>(() => new PowerHost(new[] { mana }, null!));

            var empty = new PowerHost(Array.Empty<PowerTypeDefinition>(), bus);
            Assert.Throws<InvalidOperationException>(() =>
                empty.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.mana")));
        }
    }
}
