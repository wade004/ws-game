using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// ADR-0108（消费方反馈第五十七批"静息回复"）决策 3：<see cref="Core.Rules.Combat.Resolver"/>
    /// 的治疗分支不再无条件对结算双方各调用一次 <c>NotifyCombatEvent</c>——两个不在战的单位之间
    /// 互相治疗（含自我治疗）此前会被无条件拉进战斗，让脱战回复永远没有机会启动。修复前后行为对照
    /// 见各用例注释；伤害路径（含免疫命中）不变，由既有 <c>CombatEnterLeaveTests</c> 覆盖。
    /// </summary>
    public sealed class Adr0108_HealCombatEntryTests
    {
        private static readonly Id Healer = new Id("unit.adr0108_healer");
        private static readonly Id Patient = new Id("unit.adr0108_patient");
        private static readonly Id Enemy = new Id("unit.adr0108_enemy");
        private static readonly Id SkillId = new Id("skill.adr0108_heal");

        private static EffectContext HealContext(Id source, Id target, double baseValue = 50) =>
            new EffectContext(source, target, SkillId, EffectKind.Heal, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0);

        /// <summary>修复前：两个互不相识、都不在战的单位之间一次治疗，会无条件把双方都拉进战斗
        /// （Resolver.Resolve 收尾无条件对结算双方各调用一次 NotifyCombatEvent）。本用例修复前会
        /// 断言失败——修复前 <c>Assert.False(fx.Host.IsInCombat(Healer))</c> 这一行会因为
        /// <c>IsInCombat(Healer)</c> 变成 <c>true</c> 而失败。</summary>
        [Fact]
        public void Heal_BetweenTwoOutOfCombatUnits_WithNoHostileTracking_NeitherEntersCombat()
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id("combat.hit_table.default");
                o.HealThreatCoefficient = 0.5;
            });
            CombatTestSupport.RegisterUnit(fx, Healer, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Patient, CombatTestSupport.FactionParty);

            Assert.False(fx.Host.IsInCombat(Healer));
            Assert.False(fx.Host.IsInCombat(Patient));

            fx.Host.ResolveEffect(HealContext(Healer, Patient));
            fx.Bus.DispatchPending();

            Assert.False(fx.Host.IsInCombat(Healer)); // 修复前：true（缺陷复现点）。
            Assert.False(fx.Host.IsInCombat(Patient));
        }

        /// <summary>自我治疗同样不应进战——sourceId == targetId，任何一方都不在任何敌对仇恨表里。
        /// 修复前会和上一用例一样被无条件拉进战。</summary>
        [Fact]
        public void SelfHeal_OutOfCombat_WithNoHostileTracking_DoesNotEnterCombat()
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id("combat.hit_table.default");
                o.HealThreatCoefficient = 0.5;
            });
            CombatTestSupport.RegisterUnit(fx, Patient, CombatTestSupport.FactionParty);

            fx.Host.ResolveEffect(HealContext(Patient, Patient));
            fx.Bus.DispatchPending();

            Assert.False(fx.Host.IsInCombat(Patient)); // 修复前：true。
        }

        /// <summary>
        /// 对照：被治疗者已经被某个存活敌对单位记在仇恨表里（直接摆布 <see cref="IThreatTable"/>，
        /// 不经过一次真实伤害结算——避免连带把 Healer/Patient 拉进战，保持"进战前置条件只来自这条
        /// 仇恨记录"这一单一变量）。治疗落地后 <see cref="ApplyHealThreat"/> 确实对 Enemy 追加了
        /// 仇恨（<c>applied &gt; 0</c>），因此治疗者（Healer）应当进战；被治疗者（Patient）本次不
        /// 应被通知（它若本来就在战，状态已经由仇恨表维持；这里它本来就不在战，治疗本身不构成"正在
        /// 参战"的信号）。
        /// <para>
        /// 已核实并需要向派单方更正的一点：<see cref="Resolver"/> 现有 <c>ApplyHealThreat</c> 把追加
        /// 的仇恨记在"被治疗者"已有的仇恨条目上（<c>_threatTable.AddThreat(ownerId, targetId, delta)</c>，
        /// 见既有 <c>CombatEnterLeaveTests.HealThreat_AddsCoefficientScaledThreatToHostileTrackerOfHealedUnit</c>
        /// 同一断言口径），不会在 Enemy 的仇恨表里另外创建一条以 Healer 为来源的独立条目——因此本
        /// 用例断言的是 Enemy 对 Patient 的仇恨条目增加，不是"出现一条治疗者条目"。这一点不影响
        /// 决策 3 本身（决策 3 只改"治疗是否让治疗者进战"这一独立信号，不改仇恨记账对象），已在任务
        /// 汇报里说明。
        /// </para>
        /// </summary>
        [Fact]
        public void Heal_PatientAlreadyTrackedByHostile_HealerEntersCombat_PatientUnaffected()
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id("combat.hit_table.default");
                o.HealThreatCoefficient = 0.5;
            });
            CombatTestSupport.RegisterUnit(fx, Healer, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Patient, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Enemy, CombatTestSupport.FactionHorde);

            // 直接摆布仇恨表：Enemy 已经跟踪 Patient（不经伤害结算，避免连带触发进战）。
            fx.Host.GetThreatTable(Enemy).AddThreat(Enemy, Patient, 10.0);

            Assert.False(fx.Host.IsInCombat(Healer));
            Assert.False(fx.Host.IsInCombat(Patient));

            fx.Host.ResolveEffect(HealContext(Healer, Patient, baseValue: 50));
            fx.Bus.DispatchPending();

            Assert.True(fx.Host.IsInCombat(Healer)); // 治疗仇恨确实追加成功，治疗者进战。
            Assert.False(fx.Host.IsInCombat(Patient)); // 被治疗者不因这次治疗改变状态。
            // 10（种子仇恨） + 50*0.5（本次治疗仇恨） = 35，记在 Enemy 对 Patient 的条目上。
            Assert.Equal(10.0 + 50.0 * 0.5, fx.Host.GetThreatTable(Enemy).GetThreat(Enemy, Patient));
        }

        /// <summary>免疫治疗：immune 分支从不调用 <see cref="Resolver"/> 私有的
        /// <c>ApplyHealThreat</c>（该调用点在 <c>if (!immune)</c> 内），因此
        /// <c>healThreatApplied</c> 恒为 false，治疗者也不应进战——回归保护"免疫的治疗不会绕开
        /// healThreatApplied 判定直接进战"。</summary>
        [Fact]
        public void ImmuneHeal_DoesNotEnterCombat_EvenIfHostileAlreadyTracksPatient()
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id("combat.hit_table.default");
                o.HealThreatCoefficient = 0.5;
            });
            CombatTestSupport.RegisterUnit(fx, Healer, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Patient, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Enemy, CombatTestSupport.FactionHorde);
            fx.Host.GetThreatTable(Enemy).AddThreat(Enemy, Patient, 10.0);
            fx.Auras.SetImmune(Patient, CombatTestSupport.SchoolPhysical, EffectKind.Heal);

            fx.Host.ResolveEffect(HealContext(Healer, Patient, baseValue: 50));
            fx.Bus.DispatchPending();

            Assert.False(fx.Host.IsInCombat(Healer));
            Assert.False(fx.Host.IsInCombat(Patient));
            Assert.Equal(10.0, fx.Host.GetThreatTable(Enemy).GetThreat(Enemy, Patient)); // 未追加。
        }
    }
}
