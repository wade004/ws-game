using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// <see cref="Core.Rules.Combat.CombatHost"/> 进出战斗测试（见 06 第 4.5 节、落地方案 T2-8
    /// 行"仇恨值增减、进出战斗事件触发条件均有测试"）。
    /// </summary>
    public class CombatEnterLeaveTests
    {
        private static readonly Id Hero = new Id("unit.enter_leave_hero");
        private static readonly Id Dummy = new Id("unit.enter_leave_dummy");
        private static readonly Id SkillId = new Id("skill.enter_leave_strike");

        private static EffectContext DamageContext(Id source, Id target, double baseValue = 100) =>
            new EffectContext(source, target, SkillId, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0);

        [Fact]
        public void Damage_TriggersCombatEnteredForBothSides()
        {
            var fx = CombatTestSupport.Build(o => o.HitTableConfigId = new Id("combat.hit_table.default"));
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            Assert.False(fx.Host.IsInCombat(Hero));
            Assert.False(fx.Host.IsInCombat(Dummy));

            fx.Host.ResolveEffect(DamageContext(Hero, Dummy));
            fx.Bus.DispatchPending(); // Resolver 只 Enqueue，需要显式派发才能被订阅者观察到

            Assert.True(fx.Host.IsInCombat(Hero));
            Assert.True(fx.Host.IsInCombat(Dummy));

            var enteredCount = 0;
            foreach (var evt in fx.Events)
            {
                if (evt is CombatEnteredEvent) enteredCount++;
            }
            Assert.Equal(2, enteredCount);
        }

        [Fact]
        public void Update_AfterDelay_NoLivingHostileSource_LeavesCombatAndClearsThreat()
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id("combat.hit_table.default");
                o.LeaveCombatDelay = 5.0;
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            fx.Host.ResolveEffect(DamageContext(Hero, Dummy));
            Assert.True(fx.Host.IsInCombat(Dummy));
            Assert.NotEmpty(fx.Host.GetThreatTable(Dummy).GetAll(Dummy));

            // 攻击者死亡：仇恨表里不再有存活的敌对来源。
            fx.Units.SetAlive(Hero, false);

            fx.Host.Update(5.0);
            fx.Bus.DispatchPending();

            Assert.False(fx.Host.IsInCombat(Dummy));
            Assert.Empty(fx.Host.GetThreatTable(Dummy).GetAll(Dummy));

            var leftCount = 0;
            foreach (var evt in fx.Events)
            {
                if (evt is CombatLeftEvent left && left.UnitId == Dummy) leftCount++;
            }
            Assert.Equal(1, leftCount);
        }

        [Fact]
        public void Update_AfterDelay_WithLivingHostileSource_StaysInCombat()
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id("combat.hit_table.default");
                o.LeaveCombatDelay = 5.0;
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            fx.Host.ResolveEffect(DamageContext(Hero, Dummy));
            Assert.True(fx.Host.IsInCombat(Dummy));

            fx.Host.Update(5.0); // Hero 仍存活且敌对，Dummy 不应脱战

            Assert.True(fx.Host.IsInCombat(Dummy));
            Assert.NotEmpty(fx.Host.GetThreatTable(Dummy).GetAll(Dummy));
        }

        [Fact]
        public void Update_BeforeDelayElapsed_StaysInCombat()
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id("combat.hit_table.default");
                o.LeaveCombatDelay = 5.0;
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            fx.Host.ResolveEffect(DamageContext(Hero, Dummy));
            fx.Units.SetAlive(Hero, false);

            fx.Host.Update(4.9); // 未到脱战时长

            Assert.True(fx.Host.IsInCombat(Dummy));
        }

        [Fact]
        public void HealThreat_AddsCoefficientScaledThreatToHostileTrackerOfHealedUnit()
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id("combat.hit_table.default"); // 无暴击/招架等，伤害=基础值
                o.HealThreatCoefficient = 0.5;
            });
            var enemy = new Id("unit.enter_leave_enemy");
            var healer = new Id("unit.enter_leave_healer");
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, enemy, CombatTestSupport.FactionHorde);
            CombatTestSupport.RegisterUnit(fx, healer, CombatTestSupport.FactionParty);

            // Hero 先攻击 enemy：enemy 的仇恨表里记下 Hero，金额 = 100（无加成）。
            fx.Host.ResolveEffect(DamageContext(Hero, enemy, baseValue: 100));
            Assert.Equal(100.0, fx.Host.GetThreatTable(enemy).GetThreat(enemy, Hero));

            // healer 治疗 Hero：enemy 已经在跟踪 Hero，应追加 50*0.5=25 仇恨到 enemy 对 Hero 的记录上。
            var healContext = new EffectContext(healer, Hero, new Id("skill.enter_leave_heal"),
                EffectKind.Heal, CombatTestSupport.SchoolPhysical, 50.0, coefficient: 1.0);
            fx.Host.ResolveEffect(healContext);

            Assert.Equal(125.0, fx.Host.GetThreatTable(enemy).GetThreat(enemy, Hero));
        }
    }
}
