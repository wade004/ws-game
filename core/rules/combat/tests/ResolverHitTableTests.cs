using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// <see cref="Core.Rules.Combat.Resolver"/> 结算管线测试（见落地方案 T2-7 行"命中/闪避/暴击
    /// 三分支各有测试；招架/偏斜/格挡分支的开关配置项验证为关闭时不生效、开启时生效"）。
    /// </summary>
    public class ResolverHitTableTests
    {
        private static readonly Id Hero = new Id("unit.resolver_hero");
        private static readonly Id Dummy = new Id("unit.resolver_dummy");
        private static readonly Id SkillId = new Id("skill.resolver_test_strike");
        private static readonly Id HealSkillId = new Id("skill.resolver_test_heal");

        private static CombatTestSupport.Fixture MakeFixture(string hitTableName, int heroLevel = 1)
        {
            var fx = CombatTestSupport.Build(o => o.HitTableConfigId = new Id($"combat.hit_table.{hitTableName}"));
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty, level: heroLevel);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);
            return fx;
        }

        private static EffectContext DamageContext(double baseValue = 100, bool canCrit = true, bool canMiss = true) =>
            new EffectContext(Hero, Dummy, SkillId, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0, canCrit: canCrit, canMiss: canMiss);

        private static EffectContext HealContext(double baseValue = 100, bool canCrit = true) =>
            new EffectContext(Hero, Dummy, HealSkillId, EffectKind.Heal, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0, canCrit: canCrit);

        // -----------------------------------------------------------------
        // 命中/闪避/暴击三分支
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_MissBranch_ReturnsMissAndDealsNoDamage()
        {
            var fx = MakeFixture("miss_forced");
            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Miss, result.Hit);
            Assert.Equal(0.0, result.RequestedAmount);
            Assert.Equal(0.0, result.FinalAmount);
            Assert.False(result.Immune);
            Assert.Equal(1000.0, fx.Powers.GetPower(Dummy, WellKnownPowers.Health));

            // 即便未命中，仍构成一次战斗事件（见 README 判断记录 8）。
            Assert.True(fx.Host.IsInCombat(Hero));
            Assert.True(fx.Host.IsInCombat(Dummy));
        }

        [Fact]
        public void Resolve_DodgeBranch_ReturnsDodgeAndDealsNoDamage()
        {
            var fx = MakeFixture("dodge_forced");
            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Dodge, result.Hit);
            Assert.Equal(0.0, result.FinalAmount);
            Assert.Equal(1000.0, fx.Powers.GetPower(Dummy, WellKnownPowers.Health));
        }

        [Fact]
        public void Resolve_CritBranch_DoublesDamageByDefaultMultiplier()
        {
            var fx = MakeFixture("crit_forced");
            var result = fx.Host.ResolveEffect(DamageContext(baseValue: 100));

            Assert.Equal(HitResult.Crit, result.Hit);
            Assert.Equal(200.0, result.FinalAmount);
            Assert.Equal(800.0, fx.Powers.GetPower(Dummy, WellKnownPowers.Health));
        }

        // -----------------------------------------------------------------
        // 招架/偏斜/格挡：关闭时不生效，开启时生效
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_ParryDisabled_DoesNotTrigger_EvenWithProbabilityOne()
        {
            var fx = MakeFixture("parry_disabled");
            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Equal(100.0, result.FinalAmount);
        }

        [Fact]
        public void Resolve_ParryEnabled_TriggersAndDealsNoDamage()
        {
            var fx = MakeFixture("parry_enabled");
            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Parry, result.Hit);
            Assert.Equal(0.0, result.FinalAmount);
        }

        [Fact]
        public void Resolve_GlancingDisabled_DoesNotTrigger()
        {
            var fx = MakeFixture("glancing_disabled");
            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Equal(100.0, result.FinalAmount);
        }

        [Fact]
        public void Resolve_GlancingEnabled_AppliesDamagePercent()
        {
            var fx = MakeFixture("glancing_enabled"); // glancing_damage_pct = 0.5
            var result = fx.Host.ResolveEffect(DamageContext(baseValue: 100));

            Assert.Equal(HitResult.GlancingBlow, result.Hit);
            Assert.Equal(50.0, result.FinalAmount);
        }

        [Fact]
        public void Resolve_BlockDisabled_DoesNotTrigger()
        {
            var fx = MakeFixture("block_disabled");
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatBlockValue, 30);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Equal(100.0, result.FinalAmount);
        }

        [Fact]
        public void Resolve_BlockEnabled_SubtractsFlatBlockValue()
        {
            var fx = MakeFixture("block_enabled");
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatBlockValue, 30);

            var result = fx.Host.ResolveEffect(DamageContext(baseValue: 100));

            Assert.Equal(HitResult.Block, result.Hit);
            Assert.Equal(70.0, result.FinalAmount);
        }

        // -----------------------------------------------------------------
        // 手算全链
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_FullChain_MatchesHandCalculatedValue()
        {
            // base 100、crit ×2、施法者 +10%、护甲曲线 30% 减免（armor=300, level=10, k=70）、
            // 目标 +20% 承伤、吸收 50 → RequestedAmount=184.8, Absorbed=50, FinalAmount=134.8。
            var fx = MakeFixture("crit_forced", heroLevel: 10);
            fx.Stats.SetBase(Hero, CombatTestSupport.StatDamageDonePct, 10);
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatArmor, 300);
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatDamageTakenPct, 20);
            fx.Auras.SetAbsorb(Dummy, 50);

            var result = fx.Host.ResolveEffect(DamageContext(baseValue: 100));

            Assert.Equal(HitResult.Crit, result.Hit);
            Assert.Equal(184.8, result.RequestedAmount, 6);
            Assert.Equal(50.0, result.Absorbed, 6);
            Assert.Equal(134.8, result.FinalAmount, 6);
            Assert.NotNull(result.Steps);
            Assert.NotEmpty(result.Steps!);

            Assert.Equal(1000.0 - 134.8, fx.Powers.GetPower(Dummy, WellKnownPowers.Health), 6);
        }

        // -----------------------------------------------------------------
        // 免疫
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_Immune_NoDamageNoEvent()
        {
            var fx = MakeFixture("default");
            fx.Auras.SetImmune(Dummy, CombatTestSupport.SchoolPhysical, EffectKind.SchoolDamage);

            var result = fx.Host.ResolveEffect(DamageContext(baseValue: 100));

            Assert.True(result.Immune);
            Assert.Equal(0.0, result.FinalAmount);
            Assert.Equal(1000.0, fx.Powers.GetPower(Dummy, WellKnownPowers.Health));
            Assert.Empty(fx.Events);
        }

        [Fact]
        public void Resolve_StaticImmunity_NoDamageNoEvent()
        {
            // 阶段 3 整理"事项三"：免疫来源是 IStaticImmunityProvider（内容驱动，如
            // creature.template.immunities 声明的学派免疫），不是光环（FakeAuraQuery 本例未配置任何
            // 免疫）——Resolver 步骤 7 在光环免疫之外叠加查询该契约，效果应与光环免疫等价。
            var fx = MakeFixture("default");
            fx.StaticImmunity.SetImmune(Dummy, CombatTestSupport.SchoolPhysical, EffectKind.SchoolDamage);

            var result = fx.Host.ResolveEffect(DamageContext(baseValue: 100));

            Assert.True(result.Immune);
            Assert.Equal(0.0, result.FinalAmount);
            Assert.Equal(1000.0, fx.Powers.GetPower(Dummy, WellKnownPowers.Health));
            Assert.Empty(fx.Events);
        }

        // -----------------------------------------------------------------
        // 治疗分支：只掷 crit
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_Heal_Basic_LandsFullAmount()
        {
            var fx = MakeFixture("default"); // crit 关闭
            fx.Powers.ModifyPower(Dummy, WellKnownPowers.Health, -500, Hero); // 先扣到 500，留出空间

            var result = fx.Host.ResolveEffect(HealContext(baseValue: 100));

            Assert.True(result.IsHeal);
            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Equal(100.0, result.FinalAmount);
            Assert.Equal(600.0, fx.Powers.GetPower(Dummy, WellKnownPowers.Health));
        }

        [Fact]
        public void Resolve_Heal_CritBranch_DoublesHealAmount()
        {
            var fx = MakeFixture("crit_forced");
            fx.Powers.ModifyPower(Dummy, WellKnownPowers.Health, -500, Hero);

            var result = fx.Host.ResolveEffect(HealContext(baseValue: 100));

            Assert.Equal(HitResult.Crit, result.Hit);
            Assert.Equal(200.0, result.FinalAmount);
            Assert.Equal(700.0, fx.Powers.GetPower(Dummy, WellKnownPowers.Health));
        }

        [Fact]
        public void Resolve_Heal_DoesNotOverflowMaxHealth()
        {
            var fx = MakeFixture("default"); // 目标满血
            var result = fx.Host.ResolveEffect(HealContext(baseValue: 100));

            Assert.Equal(100.0, result.FinalAmount); // Resolver 报告的是计算量，不是落地后的夹取量
            Assert.Equal(1000.0, fx.Powers.GetPower(Dummy, WellKnownPowers.Health)); // PowerHost 夹取到上限
        }

        // -----------------------------------------------------------------
        // 死亡相关
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_AgainstAlreadyDeadTarget_ReturnsMissAndLogsDiagnostic()
        {
            var fx = MakeFixture("crit_forced");
            fx.Units.SetAlive(Dummy, false);

            var result = fx.Host.ResolveEffect(DamageContext(baseValue: 100));

            Assert.Equal(HitResult.Miss, result.Hit);
            Assert.Equal(0.0, result.FinalAmount);
            Assert.False(result.Immune);
            Assert.NotEmpty(fx.Diagnostics.Warnings);
        }

        [Fact]
        public void Resolve_LethalDamage_KillsTargetAndEmitsUnitDied()
        {
            var fx = MakeFixture("default");
            fx.Powers.ModifyPower(Dummy, WellKnownPowers.Health, -950, Hero); // 1000 -> 50

            var result = fx.Host.ResolveEffect(DamageContext(baseValue: 100));
            fx.Bus.DispatchPending(); // Resolver 只 Enqueue，需要显式派发才能被订阅者观察到

            Assert.Equal(100.0, result.FinalAmount);
            Assert.False(fx.Units.IsAlive(Dummy));

            UnitDiedEvent? died = null;
            foreach (var evt in fx.Events)
            {
                if (evt is UnitDiedEvent d) died = d;
            }
            Assert.NotNull(died);
            Assert.Equal(Dummy, died!.UnitId);
            Assert.Equal(Hero, died.KillerId);
        }

        // -----------------------------------------------------------------
        // 属性缺失按 0 处理
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_MissingStat_TreatedAsZero_AndWarns()
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id("combat.hit_table.default");
                o.DamageDonePctStat = new Id("stat.combat_test_undefined");
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            var result = fx.Host.ResolveEffect(DamageContext(baseValue: 100));

            Assert.Equal(100.0, result.FinalAmount); // 未登记的属性按 0 处理，不影响乘区
            Assert.NotEmpty(fx.Diagnostics.Warnings);
        }
    }
}
