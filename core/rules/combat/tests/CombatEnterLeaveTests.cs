using Core.Foundation.Common;
using Core.Foundation.SimLoop;
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

        // 收边任务补齐：combat.entered.hostileId（首个敌对目标）——伤害结算对双方各触发一次
        // NotifyCombatEvent(自己, 对方)，因此 Hero 一侧的 hostileId 应为 Dummy，Dummy 一侧的
        // hostileId 应为 Hero（见 CombatEnteredEvent.HostileId 判断记录）。
        [Fact]
        public void Damage_CombatEnteredEvent_CarriesHostileIdAsCounterpart()
        {
            var fx = CombatTestSupport.Build(o => o.HitTableConfigId = new Id("combat.hit_table.default"));
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            fx.Host.ResolveEffect(DamageContext(Hero, Dummy));
            fx.Bus.DispatchPending();

            CombatEnteredEvent? heroEntered = null;
            CombatEnteredEvent? dummyEntered = null;
            foreach (var evt in fx.Events)
            {
                if (evt is CombatEnteredEvent entered)
                {
                    if (entered.UnitId.Equals(Hero)) heroEntered = entered;
                    if (entered.UnitId.Equals(Dummy)) dummyEntered = entered;
                }
            }

            Assert.NotNull(heroEntered);
            Assert.NotNull(dummyEntered);
            Assert.Equal(Dummy, heroEntered!.HostileId);
            Assert.Equal(Hero, dummyEntered!.HostileId);
        }

        // 已在战中的单位再次调用 NotifyCombatEvent 不会重新触发 combat.entered（hostileId 也不会
        // 被后续调用覆盖——事件只在"首次"进战这一次触发）。
        [Fact]
        public void NotifyCombatEvent_AlreadyInCombat_DoesNotRefireWithNewHostileId()
        {
            var fx = CombatTestSupport.Build(o => o.HitTableConfigId = new Id("combat.hit_table.default"));
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);
            var thirdParty = new Id("unit.enter_leave_third");
            CombatTestSupport.RegisterUnit(fx, thirdParty, CombatTestSupport.FactionHorde);

            fx.Host.NotifyCombatEvent(Hero, Dummy);
            fx.Host.NotifyCombatEvent(Hero, thirdParty); // Hero 已在战中，这次调用应是空操作
            fx.Bus.DispatchPending();

            var heroEnteredCount = 0;
            CombatEnteredEvent? heroEntered = null;
            foreach (var evt in fx.Events)
            {
                if (evt is CombatEnteredEvent entered && entered.UnitId.Equals(Hero))
                {
                    heroEnteredCount++;
                    heroEntered = entered;
                }
            }

            Assert.Equal(1, heroEnteredCount);
            Assert.Equal(Dummy, heroEntered!.HostileId);
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

        // -----------------------------------------------------------------
        // RC-02：生物销毁后 CombatHost 残留访问已注销资源（见外部审计
        // architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-02、validation-repros.txt
        // R7）——CreatureFactory.Despawn 同步注销 IPowerHost/IStatHost 对该单位的注册，但
        // CombatHost 的 _inCombat/_timeSinceLastEvent/ThreatTable 此前完全不感知，脱战延迟到期后
        // Update 仍会对它调用 Powers.SetInCombat 而抛异常。
        // -----------------------------------------------------------------

        [Fact]
        public void EntityDestroyed_ThenUpdateAfterLeaveCombatDelay_DoesNotThrow_AndClearsCombatState()
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id("combat.hit_table.default");
                o.LeaveCombatDelay = 5.0;
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            // 精确复现外部审计 R7 的进战方式：只调用 NotifyCombatEvent（不经 ResolveEffect/
            // AddThreat）——ThreatTable 对 Dummy 完全空白，HasLivingHostileThreatSource(Dummy) 两个
            // 方向都查不到任何条目，天然为 false，不会绕开下面即将暴露的崩溃路径（若改用真实伤害
            // 结算相互攻击，仇恨表里彼此互为"存活敌对来源"，Update 会在到达 Powers.SetInCombat 之前
            // 就因为 HasLivingHostileThreatSource 返回 true 而提前 continue，掩盖本条缺陷——
            // 见 CombatHost.HasLivingHostileThreatSource 判断记录，两个方向任一命中都会跳过）。
            fx.Host.NotifyCombatEvent(Dummy, Hero);
            fx.Bus.DispatchPending();
            Assert.True(fx.Host.IsInCombat(Dummy));
            Assert.Empty(fx.Host.GetThreatTable(Dummy).GetAll(Dummy));

            // 生物销毁：真实生产路径（core/carriers/creature/core/CreatureFactory.Despawn）先同步
            // UnregisterUnit(Dummy) 于 IPowerHost/IStatHost，随后经 WorldSim 生命周期清理阶段发出
            // entity.destroyed（见该方法源码判断记录）。本测试不依赖真实 WorldSim，直接模拟这条
            // 事件到达顺序：先把 Dummy 从（真实）PowerHost 注销，让"之后任何访问都会抛异常"这个
            // 前提在测试里同样成立，再派发 entity.destroyed。
            fx.Powers.UnregisterUnit(Dummy);
            fx.Bus.Enqueue(new EntityDestroyedEvent(Dummy));
            fx.Bus.DispatchPending();

            // 修复前：CombatHost 完全不知道 Dummy 已被销毁，_inCombat[Dummy] 仍为 true；脱战延迟
            // 到期后 Update 会对 Dummy 调用 Powers.SetInCombat，命中已注销单位抛出
            // InvalidOperationException（见外部审计 R7「PASS_FOR_REPRO」）。
            var ex = Record.Exception(() => fx.Host.Update(5.0));
            Assert.Null(ex);

            Assert.False(fx.Host.IsInCombat(Dummy));

            // 幂等：同一个（理论上不会重复派发的）entity.destroyed 再来一次、或再次 Update，均不抛异常。
            fx.Bus.Enqueue(new EntityDestroyedEvent(Dummy));
            fx.Bus.DispatchPending();
            var ex2 = Record.Exception(() => fx.Host.Update(1.0));
            Assert.Null(ex2);
        }

        [Fact]
        public void EntityDestroyed_RemovesUnitAsThreatSourceFromOtherUnitsTables()
        {
            var fx = CombatTestSupport.Build(o => o.HitTableConfigId = new Id("combat.hit_table.default"));
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            // 互殴：Dummy 攻击 Hero，使 Hero 的仇恨表里挂着"来源=Dummy"的条目——这条条目记在
            // Hero 名下，Dummy 自己的 Clear(Dummy) 并不会触碰它，必须靠 RemoveSourceEverywhere
            // 按"来源"整体摘除（见 ThreatTable.RemoveSourceEverywhere 判断记录"双向仇恨"）。
            fx.Host.ResolveEffect(DamageContext(Dummy, Hero));
            fx.Bus.DispatchPending();
            Assert.True(fx.Host.GetThreatTable(Hero).GetThreat(Hero, Dummy) > 0.0);

            fx.Powers.UnregisterUnit(Dummy);
            fx.Bus.Enqueue(new EntityDestroyedEvent(Dummy));
            fx.Bus.DispatchPending();

            // 修复前：CombatHost 完全不订阅 entity.destroyed，Hero 表上这条"来源=Dummy"的僵尸
            // 条目会无限期残留（Dummy 已经不存在，却仍然能被 GetTopThreat 一类查询选中）。
            Assert.Equal(0.0, fx.Host.GetThreatTable(Hero).GetThreat(Hero, Dummy));
            foreach (var (source, _) in fx.Host.GetThreatTable(Hero).GetAll(Hero))
            {
                Assert.NotEqual(Dummy, source);
            }
        }
    }
}
