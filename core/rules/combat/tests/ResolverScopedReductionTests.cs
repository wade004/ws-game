using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Combat;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// T-N1-7（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
    /// 决策 5；06_规则层_属性技能战斗AI.md 第 4.1 节 2026-09-14 修订段）："目标乘区"步骤按
    /// <see cref="CombatOptions.DamageTakenPctStats"/> 显式清单遍历 <c>scope</c> 匹配
    /// <see cref="EffectContext.SourceKind"/> 的减免属性、被暴击减免新介入点两组行为的回归——
    /// 落地方案第 7 节验收标准 3"来源类别：scope: from_player 的减免属性在 sourceKind=creature 时
    /// 不生效、sourceKind=player 时生效；被暴击减免介入暴击率（≥ 3 组用例）"。
    /// <para>
    /// 复核返工记录：首版实现按 <c>stat.definition.category</c> 批量扫描识别"减免属性"，被复核
    /// 指出会把 ADR-0030 决策 9 推荐归入 <c>defense</c> 类别的护甲值误当百分比计入目标乘区——
    /// 已改为 <see cref="CombatOptions.DamageTakenPctStats"/>/<see cref="CombatOptions.CritTakenReductionStats"/>
    /// 显式 id 清单，本文件全部用例相应改为通过 <c>configureOptions</c> 配置清单，不再配置类别；
    /// 新增 <see cref="TargetMultiplier_ArmorWithDefenseCategory_NotInList_NotAppliedToTargetMultiplier"/>
    /// 与 <see cref="TargetMultiplier_SameIdInBothDamageTakenPctStatAndStats_CountedOnce"/> 两条回归。
    /// </para>
    /// </summary>
    public sealed class ResolverScopedReductionTests
    {
        private static readonly Id Hero = new Id("unit.n17_hero");
        private static readonly Id Dummy = new Id("unit.n17_dummy");
        private static readonly Id SkillId = new Id("skill.n17_test_strike");

        /// <summary>未在 <c>combat.resist_curve</c> 登记的学派——供
        /// <see cref="TargetMultiplier_ArmorWithDefenseCategory_NotInList_NotAppliedToTargetMultiplier"/>
        /// 隔离步骤 5"减免"（<c>ComputeMitigation</c> 对未登记学派直接返回 0，不查询
        /// <c>ArmorStat</c>，见该方法判断记录），使该用例只观测步骤 6"目标乘区"是否误把护甲值计入，
        /// 不与护甲同时驱动的减免曲线互相干扰。</summary>
        private static readonly Id SchoolWithoutCurve = new Id("school.n17_no_curve");

        private static CombatTestSupport.Fixture MakeFixture(
            string hitTableName,
            System.Action<CombatOptions>? configureOptions = null)
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id($"combat.hit_table.{hitTableName}");
                configureOptions?.Invoke(o);
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);
            return fx;
        }

        /// <summary>17 参构造函数（携带 <see cref="SourceKind"/>），供本测试类全部用例构造带来源类别的
        /// <see cref="EffectContext"/>——不使用不带该参数的既有构造函数（那两个恒得到
        /// <see cref="SourceKind.Unknown"/>，见该字段判断记录），除非用例本身就是要验证 Unknown 的
        /// 行为（<see cref="TargetMultiplier_UnknownSourceKind_OnlyAnyScopeApplies"/>）。</summary>
        private static EffectContext DamageContext(SourceKind sourceKind, double baseValue = 100, Id? school = null) =>
            new EffectContext(
                Hero, Dummy, SkillId, EffectKind.SchoolDamage, school ?? CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0,
                @params: null, auraInstanceId: null, isPeriodic: false, canCrit: true, canMiss: true,
                tags: null, triggerChainDepth: 0, attackInstanceId: null, groundPoint: null,
                sourceKind: sourceKind);

        private static EffectContext DamageContextUnknownSourceKind(double baseValue = 100) =>
            new EffectContext(Hero, Dummy, SkillId, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0, canCrit: true, canMiss: true);

        // -----------------------------------------------------------------
        // 目标乘区：DamageTakenPctStats 显式清单，逐条按 scope 匹配 sourceKind
        // -----------------------------------------------------------------

        [Fact]
        public void TargetMultiplier_FromPlayerScope_NotAppliedWhenSourceKindCreature()
        {
            var fx = MakeFixture("default", // 全分支关闭，纯 Hit，无暴击/减免曲线干扰
                o => o.DamageTakenPctStats = new[] { CombatTestSupport.StatResilDamageTakenFromPlayerPct });
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenFromPlayerPct, -30);

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Creature));

            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Equal(100.0, result.FinalAmount, 6);
            Assert.Contains(result.Steps!, s => s.Contains("scope=from_player") && s.Contains("不匹配"));
        }

        [Fact]
        public void TargetMultiplier_FromPlayerScope_AppliedWhenSourceKindPlayer()
        {
            var fx = MakeFixture("default",
                o => o.DamageTakenPctStats = new[] { CombatTestSupport.StatResilDamageTakenFromPlayerPct });
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenFromPlayerPct, -30);

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Player));

            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Equal(70.0, result.FinalAmount, 6);
        }

        [Fact]
        public void TargetMultiplier_AnyScope_AppliedRegardlessOfSourceKind()
        {
            System.Action<CombatOptions> configure = o =>
                o.DamageTakenPctStats = new[] { CombatTestSupport.StatResilDamageTakenAnyPct };

            var fx = MakeFixture("default", configure);
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenAnyPct, -10);
            var resultCreature = fx.Host.ResolveEffect(DamageContext(SourceKind.Creature));
            Assert.Equal(90.0, resultCreature.FinalAmount, 6);

            var fx2 = MakeFixture("default", configure);
            fx2.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenAnyPct, -10);
            var resultPlayer = fx2.Host.ResolveEffect(DamageContext(SourceKind.Player));
            Assert.Equal(90.0, resultPlayer.FinalAmount, 6);
        }

        [Fact]
        public void TargetMultiplier_UnknownSourceKind_OnlyAnyScopeApplies()
        {
            var fx = MakeFixture("default", o => o.DamageTakenPctStats = new[]
            {
                CombatTestSupport.StatResilDamageTakenFromPlayerPct,
                CombatTestSupport.StatResilDamageTakenAnyPct,
            });
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenFromPlayerPct, -30);
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenAnyPct, -10);

            var result = fx.Host.ResolveEffect(DamageContextUnknownSourceKind());

            // from_player 一条对 Unknown 不匹配（-30 不生效），any 一条恒匹配（-10 生效）：
            // 100 × (1 + (-10)/100) = 90，不是 100 × (1 - 0.4) = 60。
            Assert.Equal(90.0, result.FinalAmount, 6);
        }

        [Fact]
        public void TargetMultiplier_ScopedSum_CombinesWithLegacyDamageTakenPctStat()
        {
            // 既有单属性配置项 DamageTakenPctStat 与 DamageTakenPctStats 清单求和，不互斥、不重复计入
            // （两者是不同 id，天然只各计一次；同一 id 出现在两处的去重见
            // TargetMultiplier_SameIdInBothDamageTakenPctStatAndStats_CountedOnce）。
            var fx = MakeFixture("default",
                o => o.DamageTakenPctStats = new[] { CombatTestSupport.StatResilDamageTakenAnyPct });
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatDamageTakenPct, 20);
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenAnyPct, 5);

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Player));

            Assert.Equal(125.0, result.FinalAmount, 6);
        }

        [Fact]
        public void TargetMultiplier_SameIdInBothDamageTakenPctStatAndStats_CountedOnce()
        {
            // CombatOptions.DamageTakenPctStat 默认引用 stat.damage_taken_pct；若游戏层把同一 id
            // 也显式列进 DamageTakenPctStats（比如内容配置疏忽、或刻意想"确保它一定在清单里"），
            // Resolver.BuildDamageTakenStatIds 按去重集合处理，只计入一次——不是 40%（20 算两次）。
            var fx = MakeFixture("default",
                o => o.DamageTakenPctStats = new[] { o.DamageTakenPctStat });
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatDamageTakenPct, 20);

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Player));

            Assert.Equal(120.0, result.FinalAmount, 6);
            Assert.DoesNotContain(result.Steps!, s => s.Contains("sum=40"));
        }

        [Fact]
        public void TargetMultiplier_ArmorWithDefenseCategory_NotInList_NotAppliedToTargetMultiplier()
        {
            // 复核返工的核心回归：护甲（stat.armor，category=defense，符合 ADR-0030 决策 9 推荐
            // 分类）未被显式列进 DamageTakenPctStats/DamageTakenPctStat，即便数值很大（300）、
            // scope 缺省 any（恒匹配一切来源），也不应该被目标乘区计入——首版"按类别扫描"的实现
            // 会把它误当"目标承伤 +300%"计入，本用例钉住这一点不再发生。用不登记减免曲线的学派
            // 隔离步骤 5，确保观测到的 FinalAmount 只反映步骤 6 是否误计入护甲，不与步骤 5 本身
            // 消费 ArmorStat 的既有行为混淆。
            var fx = MakeFixture("default"); // 不配置 DamageTakenPctStats，沿用默认空列表
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatArmor, 300);

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Player, baseValue: 100, school: SchoolWithoutCurve));

            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Equal(100.0, result.FinalAmount, 6);
            Assert.Contains(result.Steps!, s => s.Contains("mitigation") && s.Contains("无对应"));
        }

        // -----------------------------------------------------------------
        // 被暴击减免：CritTakenReductionStats 显式清单，暴击判定前扣减暴击率，下限 0
        // -----------------------------------------------------------------

        [Fact]
        public void CritTakenReduction_FromPlayerScope_NotAppliedWhenSourceKindCreature_StillCrits()
        {
            var fx = MakeFixture("crit_forced", // crit.base=1（100%）
                o => o.CritTakenReductionStats = new[] { CombatTestSupport.StatResilCritTakenFromPlayerPct });
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilCritTakenFromPlayerPct, 1.0);

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Creature, baseValue: 100));

            // scope 不匹配 → 扣减不生效 → chance 仍是 1 → 恒暴击 → FinalAmount = 200（默认 ×2 倍率）。
            Assert.Equal(HitResult.Crit, result.Hit);
            Assert.Equal(200.0, result.FinalAmount, 6);
        }

        [Fact]
        public void CritTakenReduction_FromPlayerScope_AppliedWhenSourceKindPlayer_NeverCrits()
        {
            var fx = MakeFixture("crit_forced", // crit.base=1（100%）
                o => o.CritTakenReductionStats = new[] { CombatTestSupport.StatResilCritTakenFromPlayerPct });
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilCritTakenFromPlayerPct, 1.0);

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Player, baseValue: 100));

            // scope 匹配 → chance = max(0, 1 - 1.0) = 0 → roll ∈ [0,1) 恒不小于 0 → 不暴击。
            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Equal(100.0, result.FinalAmount, 6);
            Assert.Contains(result.Steps!, s => s.Contains("chance=0") && s.Contains("taken_reduction=1"));
        }

        [Fact]
        public void CritTakenReduction_AnyScope_AppliedRegardlessOfSourceKind()
        {
            System.Action<CombatOptions> configure = o =>
                o.CritTakenReductionStats = new[] { CombatTestSupport.StatResilCritTakenAnyPct };

            var fx = MakeFixture("crit_forced", configure);
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilCritTakenAnyPct, 1.0);
            var resultCreature = fx.Host.ResolveEffect(DamageContext(SourceKind.Creature, baseValue: 100));
            Assert.Equal(HitResult.Hit, resultCreature.Hit);
            Assert.Equal(100.0, resultCreature.FinalAmount, 6);

            var fx2 = MakeFixture("crit_forced", configure);
            fx2.Stats.SetBase(Dummy, CombatTestSupport.StatResilCritTakenAnyPct, 1.0);
            var resultPlayer = fx2.Host.ResolveEffect(DamageContext(SourceKind.Player, baseValue: 100));
            Assert.Equal(HitResult.Hit, resultPlayer.Hit);
            Assert.Equal(100.0, resultPlayer.FinalAmount, 6);
        }

        [Fact]
        public void CritTakenReduction_FlooredAtZero_WhenReductionExceedsBaseChance()
        {
            var fx = MakeFixture("crit_forced", // crit.base=1
                o => o.CritTakenReductionStats = new[] { CombatTestSupport.StatResilCritTakenFromPlayerPct });
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilCritTakenFromPlayerPct, 2.0); // 远超 1

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Player, baseValue: 100));

            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Equal(100.0, result.FinalAmount, 6);
            // 1 - 2.0 = -1.0，下限夹取到 0，不是负数。
            Assert.Contains(result.Steps!, s => s.Contains("chance=0(base=1") && s.Contains("taken_reduction=2"));
        }

        [Fact]
        public void CritTakenReduction_InterventionPoint_DoesNotChangeRollCount()
        {
            // 介入点在取样（IRngHost.Next）之前，只改变取样前的 chance 数值本身，不改变取样次数：
            // 本表其余分支全部关闭（miss/dodge/parry/glancing/block 均 disabled，RollBranch 对
            // disabled 分支直接返回、不掷骰，见 Resolver.RollBranch），暴击判定是唯一一次掷骰——
            // 有/无被暴击减免属性时，Steps 里 "roll=" 出现次数应恒为 1，不多不少。
            var fxWithout = MakeFixture("crit_forced");
            var withoutResult = fxWithout.Host.ResolveEffect(DamageContext(SourceKind.Player, baseValue: 100));
            Assert.Equal(1, withoutResult.Steps!.Count(s => s.Contains("roll=")));

            var fxWith = MakeFixture("crit_forced",
                o => o.CritTakenReductionStats = new[] { CombatTestSupport.StatResilCritTakenFromPlayerPct });
            fxWith.Stats.SetBase(Dummy, CombatTestSupport.StatResilCritTakenFromPlayerPct, 0.3);
            var withResult = fxWith.Host.ResolveEffect(DamageContext(SourceKind.Player, baseValue: 100));
            Assert.Equal(1, withResult.Steps!.Count(s => s.Contains("roll=")));
        }

        /// <summary>深度复审 A-S1（2026-09-16）：<see cref="Resolver.Resolve"/> 步骤 6"目标乘区"每次
        /// 调用一次的 <c>BuildDamageTakenStatIds</c> 是私有方法，本类型没有公开出口暴露其返回的集合
        /// 实例是否被复用——按报告建议的"白盒断言"取样，经反射直接调用该私有方法验证缓存行为，不
        /// 借道公开 API 曲线拐弯验证（<c>Steps</c> 里的 <c>stats=[...]</c> 只能证明内容相同，证明不了
        /// 是否为同一个集合实例）。</summary>
        private static Core.Rules.Combat.Resolver GetResolver(Core.Rules.Combat.CombatHost host)
        {
            var field = typeof(Core.Rules.Combat.CombatHost).GetField(
                "_resolver", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            return (Core.Rules.Combat.Resolver)field!.GetValue(host)!;
        }

        private static System.Collections.Generic.IReadOnlyList<Id> InvokeBuildDamageTakenStatIds(
            Core.Rules.Combat.Resolver resolver)
        {
            var method = typeof(Core.Rules.Combat.Resolver).GetMethod(
                "BuildDamageTakenStatIds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(method);
            return (System.Collections.Generic.IReadOnlyList<Id>)method!.Invoke(resolver, null)!;
        }

        [Fact]
        public void BuildDamageTakenStatIds_RepeatedCallsWithUnchangedOptions_ReturnSameCachedInstance()
        {
            var fx = MakeFixture("crit_forced",
                o => o.DamageTakenPctStats = new[] { new Id("stat.n17_a_s1_extra_taken_pct") });

            var resolver = GetResolver(fx.Host);

            var first = InvokeBuildDamageTakenStatIds(resolver);
            var second = InvokeBuildDamageTakenStatIds(resolver);
            var third = InvokeBuildDamageTakenStatIds(resolver);

            // A-S1：CombatOptions.DamageTakenPctStat/DamageTakenPctStats 均未变化时，三次调用
            // 应该返回同一个集合实例，不重新分配 HashSet/List。
            Assert.Same(first, second);
            Assert.Same(second, third);
            Assert.Equal(new[] { CombatTestSupport.StatDamageTakenPct, new Id("stat.n17_a_s1_extra_taken_pct") }, first);
        }

        [Fact]
        public void BuildDamageTakenStatIds_AfterDamageTakenPctStatsReassigned_InvalidatesCacheAndRecomputes()
        {
            Core.Rules.Combat.CombatOptions? capturedOptions = null;
            var fx = MakeFixture("crit_forced", o =>
            {
                o.DamageTakenPctStats = new[] { new Id("stat.n17_a_s1_before") };
                capturedOptions = o;
            });
            Assert.NotNull(capturedOptions);

            var resolver = GetResolver(fx.Host);
            var before = InvokeBuildDamageTakenStatIds(resolver);
            Assert.Contains(new Id("stat.n17_a_s1_before"), before);

            // 模拟装配阶段之后 CombatOptions.DamageTakenPctStats 被整体重新赋值（引用变化）——
            // A-S1 缓存必须能感知到，不能永久卡在装配期第一次算出的旧集合上。
            capturedOptions!.DamageTakenPctStats = new[] { new Id("stat.n17_a_s1_after") };
            var after = InvokeBuildDamageTakenStatIds(resolver);

            Assert.NotSame(before, after);
            Assert.Contains(new Id("stat.n17_a_s1_after"), after);
            Assert.DoesNotContain(new Id("stat.n17_a_s1_before"), after);

            // 重新赋值之后再次调用应该重新开始复用新的缓存实例。
            var afterAgain = InvokeBuildDamageTakenStatIds(resolver);
            Assert.Same(after, afterAgain);
        }
    }
}
