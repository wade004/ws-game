using System;
using Core.Foundation.Common;
using Core.Rules.Combat;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// T-N1-8（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
    /// 决策 6；06_规则层_属性技能战斗AI.md 第 4.2 节修订段）：<c>combat.level_diff_table</c> 接入
    /// <see cref="Resolver.DetermineHit"/> 的回归——落地方案 T-N1-8 验收标准"Δ 矩阵用例 ≥ 11 组
    /// （Δ = −5～+5 各一组），证明双向生效与两段斜率；有效等级是否计入装备等级偏移 关闭时装备不影响
    /// Δ（1 组）、开启时按偏移曲线计入（1 组）"。
    /// <para>
    /// 用到的 <c>combat.level_diff_table</c> 测试数据（<see cref="CombatTestSupport"/>
    /// <c>combat.level_diff.test_matrix</c>）：<c>miss_bonus</c>/<c>crit_suppression</c> 断点
    /// [-5,-3,-2,-1,0,1,2,3,5]，|Δ|&lt;=2 每级加得少（miss 0.02/0.04、crit 0.01/0.02），|Δ|&gt;=3
    /// 陡增并封顶到 ±1.0（双向：Δ&lt;0 侧为负值）——数值只服务测试，不进框架默认。
    /// </para>
    /// </summary>
    public sealed class ResolverLevelDiffTests
    {
        private static readonly Id Hero = new Id("unit.n18_hero");
        private static readonly Id Dummy = new Id("unit.n18_dummy");
        private static readonly Id SkillId = new Id("skill.n18_test_strike");
        private static readonly Id LevelDiffTableId = new Id("combat.level_diff.test_matrix");

        /// <summary>与 <c>CombatTestSupport.LevelDiffTableJson</c>（记录
        /// <c>combat.level_diff.test_matrix</c>）的 <c>miss_bonus</c> 断点逐点一致，供
        /// <see cref="LevelDiff_ElevenDeltaPoints_MatchesCurveAndIsBidirectional"/> 独立算出"应得值"
        /// 去匹配 Resolver 的 steps 追踪文本（见该用例判断记录）。两处若后续改动请保持同步。</summary>
        private static readonly PiecewiseCurve MissBonusCurve = new PiecewiseCurve(new[]
        {
            new CurvePoint(-5, -1.0), new CurvePoint(-3, -1.0), new CurvePoint(-2, -0.04),
            new CurvePoint(-1, -0.02), new CurvePoint(0, 0.0), new CurvePoint(1, 0.02),
            new CurvePoint(2, 0.04), new CurvePoint(3, 1.0), new CurvePoint(5, 1.0),
        });

        /// <summary>同上，对应 <c>crit_suppression</c> 断点。</summary>
        private static readonly PiecewiseCurve CritSuppressionCurve = new PiecewiseCurve(new[]
        {
            new CurvePoint(-5, -1.0), new CurvePoint(-3, -1.0), new CurvePoint(-2, -0.02),
            new CurvePoint(-1, -0.01), new CurvePoint(0, 0.0), new CurvePoint(1, 0.01),
            new CurvePoint(2, 0.02), new CurvePoint(3, 1.0), new CurvePoint(5, 1.0),
        });

        private static CombatTestSupport.Fixture MakeFixture(
            string hitTableName, int heroLevel, int dummyLevel,
            Action<CombatOptions>? configureOptions = null)
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id($"combat.hit_table.{hitTableName}");
                configureOptions?.Invoke(o);
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty, level: heroLevel);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde, level: dummyLevel);
            return fx;
        }

        private static EffectContext DamageContext(double baseValue = 100) =>
            new EffectContext(Hero, Dummy, SkillId, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0, canCrit: true, canMiss: true);

        // -----------------------------------------------------------------
        // Δ 矩阵：11 组（-5～+5），双向生效、两段斜率——直接断言 Resolver 计算出的曲线求值结果。
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(-5, -1.0, -1.0)]
        [InlineData(-4, -1.0, -1.0)]
        [InlineData(-3, -1.0, -1.0)]
        [InlineData(-2, -0.04, -0.02)]
        [InlineData(-1, -0.02, -0.01)]
        [InlineData(0, 0.0, 0.0)]
        [InlineData(1, 0.02, 0.01)]
        [InlineData(2, 0.04, 0.02)]
        [InlineData(3, 1.0, 1.0)]
        [InlineData(4, 1.0, 1.0)]
        [InlineData(5, 1.0, 1.0)]
        public void LevelDiff_ElevenDeltaPoints_MatchesCurveAndIsBidirectional(
            int delta, double expectedMissBonus, double expectedCritSuppression)
        {
            // "default" 命中表六分支全部关闭——不触发任何掷骰，纯粹验证 Δ 加成/压制的曲线求值本身
            // （Resolver.ResolveLevelDiffAdjustments 在 DetermineHit 开头无条件计算一次，不依赖任何
            // 分支是否启用）。攻击者固定 20 级，目标 = 20 + delta，Δ = 目标有效等级 − 攻击者有效等级
            // 因此恰好等于 delta。
            var fx = MakeFixture("default", heroLevel: 20, dummyLevel: 20 + delta,
                o => o.LevelDiffTableId = LevelDiffTableId);

            // 断点表在"精确落在某个非平台段端点"时，插值式子 lo.Y + t×(hi.Y − lo.Y)（t 精确等于
            // 1.0）与直接返回 hi.Y 在浮点上不逐位相等（同 IEEE754 舍入的既有事实，不是缺陷）——本
            // 用例因此不直接对 Resolver 内部算出的字符串做硬编码字面量比对，而是用与
            // LevelDiffTableJson 完全相同的断点（见 MissBonusCurve/CritSuppressionCurve）独立经
            // PiecewiseCurve.Evaluate 算出"这次应该得到的值"，再拿这个值（而不是 InlineData 字面量）
            // 去匹配 steps 追踪文本——PiecewiseCurve.Evaluate 是纯函数，同样的断点序列 + 同样的 x
            // 必然逐位算出同一个结果，与 Resolver 内部经 CombatDataLoader 从同一段 JSON 加载出的
            // 曲线天然一致（CombatDataLoader 是 internal，测试工程不跨程序集直接访问，故在此重建
            // 一份等价断点，不是重复造轮子）。InlineData 里的字面量改用带容差的数值比较核对"设计
            // 意图与曲线数据一致"（两段斜率、双向生效）。
            var curveMissBonus = MissBonusCurve.Evaluate(delta);
            var curveCritSuppression = CritSuppressionCurve.Evaluate(delta);
            Assert.Equal(expectedMissBonus, curveMissBonus, 9);
            Assert.Equal(expectedCritSuppression, curveCritSuppression, 9);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Contains(result.Steps!, s => s.Contains($"delta={delta} miss_bonus={curveMissBonus} crit_suppression={curveCritSuppression}"));
        }

        // -----------------------------------------------------------------
        // 未命中率：Δ 双向影响真实结算结果（不只是曲线求值），封顶到 [0,1] 的边界用例。
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_MissChance_TargetMuchHigherLevel_MissBonusPushesChanceToOne_AlwaysMisses()
        {
            // level_diff_probe：miss base=0.5；Δ=+5 时 miss_bonus=+1.0 -> chance=clamp01(1.5)=1.0，
            // roll∈[0,1) 恒 < 1.0，必定 Miss（确定性边界，不依赖具体 roll 值）。
            var fx = MakeFixture("level_diff_probe", heroLevel: 20, dummyLevel: 25,
                o => o.LevelDiffTableId = LevelDiffTableId);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Miss, result.Hit);
            Assert.Contains(result.Steps!, s => s.Contains("hit_check: miss chance=1"));
        }

        [Fact]
        public void Resolve_MissChance_TargetMuchLowerLevel_NegativeMissBonusPushesChanceToZero_NeverMisses()
        {
            // 同上表；Δ=-5 时 miss_bonus=-1.0 -> chance=clamp01(-0.5)=0.0，必定 Hit——证明双向生效
            // （目标等级更低时命中更容易，不只是"更难命中"单向）。
            var fx = MakeFixture("level_diff_probe", heroLevel: 20, dummyLevel: 15,
                o => o.LevelDiffTableId = LevelDiffTableId);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Contains(result.Steps!, s => s.Contains("hit_check: miss chance=0"));
        }

        [Fact]
        public void Resolve_MissChance_LevelDiffTableIdNotConfigured_NoDeltaEffect_ExistingBehaviorPreserved()
        {
            // 回归：CombatOptions.LevelDiffTableId 缺省 null——即便攻击者/目标等级差达到 Δ=+5（若
            // 接表会被夹到 chance=1.0 必 Miss），不接表时行为退化为 T-N1-8 之前：miss base=0.5，无
            // Δ 项，chance 恒为 0.5（trace 里不出现 level_diff: 前缀，见 Resolver 判断记录"未配置时
            // 不写 steps 追踪日志"）。
            var fx = MakeFixture("level_diff_probe", heroLevel: 20, dummyLevel: 25);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.DoesNotContain(result.Steps!, s => s.StartsWith("level_diff:", StringComparison.Ordinal));
            Assert.Contains(result.Steps!, s => s.Contains("hit_check: miss chance=0.5"));
        }

        // -----------------------------------------------------------------
        // 暴击率：Δ 双向影响暴击判定，封顶边界用例。
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_CritChance_TargetMuchHigherLevel_SuppressionPushesForcedCritToZero_NeverCrits()
        {
            // level_diff_crit_zero_base：crit base=0；无 Δ 时恒不暴击，用来确认"Δ=-5 能把 0 推到 1"
            // 这一方向；本用例反向验证"Δ=+5 对本就是 0 的 chance 没有负向影响"（钳制在 0，不会变负）。
            var fx = MakeFixture("level_diff_crit_zero_base", heroLevel: 20, dummyLevel: 25,
                o => o.LevelDiffTableId = LevelDiffTableId);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.NotEqual(HitResult.Crit, result.Hit);
            Assert.Contains(result.Steps!, s => s.Contains("hit_check: crit chance=0"));
        }

        [Fact]
        public void Resolve_CritChance_TargetMuchLowerLevel_NegativeSuppressionIncreasesChance_ForcedNoCritBecomesCrit()
        {
            // level_diff_crit_zero_base：crit base=0（未接 Δ 表时恒不暴击）；Δ=-5 时
            // crit_suppression=-1.0 -> chance=max(0, 0 - 0 - (-1.0))=1.0，roll<1.0 恒真，必定暴击——
            // 证明双向生效：目标更低级时暴击率被"负压制"（即加成）推到必暴击，而不是保持不动。
            var fx = MakeFixture("level_diff_crit_zero_base", heroLevel: 20, dummyLevel: 15,
                o => o.LevelDiffTableId = LevelDiffTableId);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Crit, result.Hit);
            Assert.Contains(result.Steps!, s => s.Contains("hit_check: crit chance=1"));
        }

        // -----------------------------------------------------------------
        // miss 分支 hit_stat（攻击者命中属性）。
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_MissChance_AttackerHitStat_SubtractsFromMissChance_ForcedMissBecomesHit()
        {
            // miss_forced_with_hit_stat：base=1、hit_stat=stat.hit_rating；不设置该属性时
            // chance=1-0=1，必 Miss（下一条用例覆盖）。本用例把 Hero 的 stat.hit_rating 设为 1，
            // chance=clamp01(1-1)=0，必 Hit——证明 hit_stat 确实从未命中率里被减去。
            var fx = MakeFixture("miss_forced_with_hit_stat", heroLevel: 1, dummyLevel: 1);
            fx.Stats.SetBase(Hero, CombatTestSupport.StatHitRating, 1.0);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.Contains(result.Steps!, s => s.Contains("hit_check: miss chance=0(base=1, hit_stat=1"));
        }

        [Fact]
        public void Resolve_MissChance_HitStatNotConfigured_NoSubtraction_ExistingBehaviorPreserved()
        {
            // 回归：既有 miss_forced 表（无 hit_stat 字段）——即便 Hero 身上有 stat.hit_rating，
            // HitTableBranch.HitStat 为 null，本条不读取该属性，chance 仍恒为 1，必 Miss，与
            // T-N1-8 之前逐位一致。
            var fx = MakeFixture("miss_forced", heroLevel: 1, dummyLevel: 1);
            fx.Stats.SetBase(Hero, CombatTestSupport.StatHitRating, 999.0);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Miss, result.Hit);
        }

        // -----------------------------------------------------------------
        // 有效等级是否计入装备等级偏移（策略项，默认关闭）。
        // -----------------------------------------------------------------

        [Fact]
        public void EffectiveLevel_GearOffsetDisabled_IgnoresProviderOffset_DeltaFromRawLevelsOnly()
        {
            // Hero/Dummy 同级（Δ 本应为 0）；即便注入的 FakeGearLevelOffsetProvider 给 Dummy 配置了
            // +5 的装备等级偏移，EffectiveLevelIncludesGearOffset 默认关闭时该偏移必须被完全忽略，
            // Δ 仍是 0（miss_bonus/crit_suppression 均为 0）。
            var fx = MakeFixture("default", heroLevel: 20, dummyLevel: 20,
                o => o.LevelDiffTableId = LevelDiffTableId);
            fx.GearLevelOffset.SetOffset(Dummy, 5.0);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Contains(result.Steps!, s => s.Contains("delta=0 miss_bonus=0 crit_suppression=0"));
        }

        [Fact]
        public void EffectiveLevel_GearOffsetEnabled_AddsProviderOffsetToDelta()
        {
            // 同上等级设置，但开启策略项——Dummy 的有效等级 = 20 + 5 = 25，Δ = 25 − 20 = 5，落在
            // miss_bonus/crit_suppression 断点表 Δ=5 那一点（均为 1.0，见 11 点矩阵用例）。
            var fx = MakeFixture("default", heroLevel: 20, dummyLevel: 20,
                o =>
                {
                    o.LevelDiffTableId = LevelDiffTableId;
                    o.EffectiveLevelIncludesGearOffset = true;
                });
            fx.GearLevelOffset.SetOffset(Dummy, 5.0);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Contains(result.Steps!, s => s.Contains("delta=5 miss_bonus=1 crit_suppression=1"));
        }

        [Fact]
        public void EffectiveLevel_GearOffsetEnabled_NoOffsetConfiguredForUnit_DefaultsToZero_NoThrow()
        {
            // 策略项开启，但两个单位都没有调用 FakeGearLevelOffsetProvider.SetOffset——
            // GetGearLevelOffset 缺省返回 0（与 NullGearLevelOffsetProvider 语义等价），不抛异常，
            // 退化为与关闭时相同的 Δ=0。覆盖"开启但未接入真实装备等级偏移来源"这一防御路径。
            var fx = MakeFixture("default", heroLevel: 20, dummyLevel: 20,
                o =>
                {
                    o.LevelDiffTableId = LevelDiffTableId;
                    o.EffectiveLevelIncludesGearOffset = true;
                });

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Contains(result.Steps!, s => s.Contains("delta=0 miss_bonus=0 crit_suppression=0"));
        }
    }
}
