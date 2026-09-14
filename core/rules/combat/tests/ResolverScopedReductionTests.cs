using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// T-N1-7（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
    /// 决策 5；06_规则层_属性技能战斗AI.md 第 4.1 节 2026-09-14 修订段）："目标乘区"步骤按
    /// <c>stat.definition.scope</c> 匹配 <see cref="EffectContext.SourceKind"/> 遍历减免属性、
    /// 被暴击减免新介入点两组行为的回归——落地方案第 7 节验收标准 3"来源类别：scope: from_player
    /// 的减免属性在 sourceKind=creature 时不生效、sourceKind=player 时生效；被暴击减免介入暴击率
    /// （≥ 3 组用例）"。
    /// </summary>
    public sealed class ResolverScopedReductionTests
    {
        private static readonly Id Hero = new Id("unit.n17_hero");
        private static readonly Id Dummy = new Id("unit.n17_dummy");
        private static readonly Id SkillId = new Id("skill.n17_test_strike");

        private static CombatTestSupport.Fixture MakeFixture(string hitTableName)
        {
            var fx = CombatTestSupport.Build(o => o.HitTableConfigId = new Id($"combat.hit_table.{hitTableName}"));
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);
            return fx;
        }

        /// <summary>
        /// 供"被暴击减免"用例专用：<see cref="CombatOptions.DamageTakenCategory"/> 与
        /// <see cref="CombatOptions.CritTakenReductionCategory"/> 默认值都是 <c>"defense"</c>
        /// （见 <c>CombatOptions.CritTakenReductionCategory</c> 判断记录"契约疑点"一节——五值
        /// category 枚举里 <c>defense</c> 是唯一确认零占用既有内容的取值，两个新扫描类别目前只能
        /// 共享它），本测试夹具额外登记了 <c>category="defense"</c> 的"被暴击减免"测试属性
        /// （<see cref="CombatTestSupport.StatResilCritTakenFromPlayerPct"/>/
        /// <see cref="CombatTestSupport.StatResilCritTakenAnyPct"/>），若不隔离，步骤 6"目标乘区"的
        /// 新扫描机制会把这些属性的值也当作承伤百分比计入（同一属性被两种量纲各消费一次，正是该
        /// 判断记录描述的已知限制）——把本用例组的 <see cref="CombatOptions.DamageTakenCategory"/>
        /// 显式改成一个不对应任何已登记属性定义的占位类别字符串，使目标乘区新扫描机制在这组用例里
        /// 恒返回空集合，从而单独验证"被暴击减免"介入点本身的行为，不与目标乘区互相干扰（生产默认
        /// 配置本身的取舍不受本测试隔离手法影响，见上述判断记录）。
        /// </summary>
        private static CombatTestSupport.Fixture MakeCritReductionFixture(string hitTableName)
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id($"combat.hit_table.{hitTableName}");
                o.DamageTakenCategory = "n17_test_isolate_damage_taken_scan";
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);
            return fx;
        }

        /// <summary>17 参构造函数（携带 <see cref="SourceKind"/>），供本测试类全部用例构造带来源类别的
        /// <see cref="EffectContext"/>——不使用不带该参数的既有构造函数（那两个恒得到
        /// <see cref="SourceKind.Unknown"/>，见该字段判断记录），除非用例本身就是要验证 Unknown 的
        /// 行为（<see cref="TargetMultiplier_UnknownSourceKind_OnlyAnyScopeApplies"/>）。</summary>
        private static EffectContext DamageContext(SourceKind sourceKind, double baseValue = 100) =>
            new EffectContext(
                Hero, Dummy, SkillId, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0,
                @params: null, auraInstanceId: null, isPeriodic: false, canCrit: true, canMiss: true,
                tags: null, triggerChainDepth: 0, attackInstanceId: null, groundPoint: null,
                sourceKind: sourceKind);

        private static EffectContext DamageContextUnknownSourceKind(double baseValue = 100) =>
            new EffectContext(Hero, Dummy, SkillId, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0, canCrit: true, canMiss: true);

        // -----------------------------------------------------------------
        // 目标乘区：scope 匹配 sourceKind 遍历减免属性
        // -----------------------------------------------------------------

        [Fact]
        public void TargetMultiplier_FromPlayerScope_NotAppliedWhenSourceKindCreature()
        {
            var fx = MakeFixture("default"); // 全分支关闭，纯 Hit，无暴击/减免曲线干扰
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenFromPlayerPct, -30);

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Creature));

            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Equal(100.0, result.FinalAmount, 6);
            Assert.Contains(result.Steps!, s => s.Contains("scope=from_player") && s.Contains("不匹配"));
        }

        [Fact]
        public void TargetMultiplier_FromPlayerScope_AppliedWhenSourceKindPlayer()
        {
            var fx = MakeFixture("default");
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenFromPlayerPct, -30);

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Player));

            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Equal(70.0, result.FinalAmount, 6);
        }

        [Fact]
        public void TargetMultiplier_AnyScope_AppliedRegardlessOfSourceKind()
        {
            var fx = MakeFixture("default");
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenAnyPct, -10);

            var resultCreature = fx.Host.ResolveEffect(DamageContext(SourceKind.Creature));
            Assert.Equal(90.0, resultCreature.FinalAmount, 6);

            var fx2 = MakeFixture("default");
            fx2.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenAnyPct, -10);
            var resultPlayer = fx2.Host.ResolveEffect(DamageContext(SourceKind.Player));
            Assert.Equal(90.0, resultPlayer.FinalAmount, 6);
        }

        [Fact]
        public void TargetMultiplier_UnknownSourceKind_OnlyAnyScopeApplies()
        {
            var fx = MakeFixture("default");
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
            // 既有单属性配置项 DamageTakenPctStat 与新扫描机制求和，不互斥、不重复计入。
            var fx = MakeFixture("default");
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatDamageTakenPct, 20);
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilDamageTakenAnyPct, 5);

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Player));

            Assert.Equal(125.0, result.FinalAmount, 6);
        }

        // -----------------------------------------------------------------
        // 被暴击减免：暴击判定前扣减暴击率，下限 0
        // -----------------------------------------------------------------

        [Fact]
        public void CritTakenReduction_FromPlayerScope_NotAppliedWhenSourceKindCreature_StillCrits()
        {
            var fx = MakeCritReductionFixture("crit_forced"); // crit.base=1（100%）
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilCritTakenFromPlayerPct, 1.0);

            var result = fx.Host.ResolveEffect(DamageContext(SourceKind.Creature, baseValue: 100));

            // scope 不匹配 → 扣减不生效 → chance 仍是 1 → 恒暴击 → FinalAmount = 200（默认 ×2 倍率）。
            Assert.Equal(HitResult.Crit, result.Hit);
            Assert.Equal(200.0, result.FinalAmount, 6);
        }

        [Fact]
        public void CritTakenReduction_FromPlayerScope_AppliedWhenSourceKindPlayer_NeverCrits()
        {
            var fx = MakeCritReductionFixture("crit_forced"); // crit.base=1（100%）
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
            var fx = MakeCritReductionFixture("crit_forced");
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatResilCritTakenAnyPct, 1.0);

            var resultCreature = fx.Host.ResolveEffect(DamageContext(SourceKind.Creature, baseValue: 100));
            Assert.Equal(HitResult.Hit, resultCreature.Hit);
            Assert.Equal(100.0, resultCreature.FinalAmount, 6);

            var fx2 = MakeCritReductionFixture("crit_forced");
            fx2.Stats.SetBase(Dummy, CombatTestSupport.StatResilCritTakenAnyPct, 1.0);
            var resultPlayer = fx2.Host.ResolveEffect(DamageContext(SourceKind.Player, baseValue: 100));
            Assert.Equal(HitResult.Hit, resultPlayer.Hit);
            Assert.Equal(100.0, resultPlayer.FinalAmount, 6);
        }

        [Fact]
        public void CritTakenReduction_FlooredAtZero_WhenReductionExceedsBaseChance()
        {
            var fx = MakeCritReductionFixture("crit_forced"); // crit.base=1
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
            var fxWithout = MakeCritReductionFixture("crit_forced");
            var withoutResult = fxWithout.Host.ResolveEffect(DamageContext(SourceKind.Player, baseValue: 100));
            Assert.Equal(1, withoutResult.Steps!.Count(s => s.Contains("roll=")));

            var fxWith = MakeCritReductionFixture("crit_forced");
            fxWith.Stats.SetBase(Dummy, CombatTestSupport.StatResilCritTakenFromPlayerPct, 0.3);
            var withResult = fxWith.Host.ResolveEffect(DamageContext(SourceKind.Player, baseValue: 100));
            Assert.Equal(1, withResult.Steps!.Count(s => s.Contains("roll=")));
        }
    }
}
