using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// 消费方反馈第 5 条根治：<see cref="Core.Rules.Combat.Resolver"/> 步骤 5"减免"
    /// （<c>ComputeMitigation</c>）对未在 <c>combat.resist_curve</c> 登记的学派，此前只写 steps
    /// 追踪日志（<see cref="Core.Rules.Combat.CombatOptions.ResolveTrace"/> 才看得到）、从未经
    /// <see cref="Core.Rules.Combat.ICombatDiagnostics"/> 报出——数据漏配时结算仍得到一个合法的
    /// "零减免"结果，接入方完全看不出这是配置缺失还是设计如此（AGENTS.md §3"运行时路径不静默
    /// 降级"）。本文件验证：(1) 数据齐全时不产生诊断噪音；(2) 数据缺失时诊断消息含可定位信息
    /// （具体缺失的学派 id），且行为本身不变（减免仍按 0 处理，不阻断结算）。
    /// </summary>
    public sealed class ResolverMitigationDiagnosticsTests
    {
        private static readonly Id Hero = new Id("unit.mitigation_diag_hero");
        private static readonly Id Dummy = new Id("unit.mitigation_diag_dummy");
        private static readonly Id SkillId = new Id("skill.mitigation_diag_strike");

        /// <summary>未在 <c>combat.resist_curve</c> 登记的学派（惯例同
        /// <c>ResolverScopedReductionTests.SchoolWithoutCurve</c>，另建一份避免跨测试类耦合）。</summary>
        private static readonly Id SchoolWithoutCurve = new Id("school.mitigation_diag_no_curve");

        private static CombatTestSupport.Fixture MakeFixture()
        {
            var fx = CombatTestSupport.Build(o => o.HitTableConfigId = new Id("combat.hit_table.default"));
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);
            return fx;
        }

        /// <summary>canCrit/canMiss 均为 false：跳过步骤 1 的全部掷骰分支，确保结算无条件推进到
        /// 步骤 5"减免"（不受具体 hit_table 掷骰结果影响，保持用例确定性）。</summary>
        private static EffectContext DamageContext(Id school) =>
            new EffectContext(Hero, Dummy, SkillId, EffectKind.SchoolDamage, school,
                baseValue: 100, coefficient: 1.0, canCrit: false, canMiss: false);

        [Fact]
        public void Resolve_SchoolHasResistCurve_NoDiagnosticNoise()
        {
            var fx = MakeFixture();

            var result = fx.Host.ResolveEffect(DamageContext(CombatTestSupport.SchoolPhysical));

            // 未显式配置护甲属性时按 0 处理，走真实减免曲线仍算出 reduction=0（手算见
            // CombatTestSupport.ResistCurveJson 判断记录），FinalAmount 与"学派缺表"分支数值上
            // 恰好相同——本用例只关心"学派已登记时不产生诊断噪音"，数值相同只是这份最小夹具的
            // 副产物，不是本用例的断言重点。
            Assert.Equal(100.0, result.FinalAmount);
            Assert.Empty(fx.Diagnostics.Warnings);
        }

        [Fact]
        public void Resolve_SchoolMissingResistCurve_ReductionZero_AndLogsLocatableDiagnostic()
        {
            var fx = MakeFixture();

            var result = fx.Host.ResolveEffect(DamageContext(SchoolWithoutCurve));

            // 行为不变：未登记学派仍然按 0 减免处理，不阻断结算——本用例只验证"不再静默"，不改变
            // 既有数值行为（同 T-N1-7 ResolverScopedReductionTests 里同一分支的既有断言口径）。
            Assert.Equal(100.0, result.FinalAmount);

            var warning = Assert.Single(fx.Diagnostics.Warnings);
            Assert.Contains("combat.resist_curve", warning);
            Assert.Contains(SchoolWithoutCurve.Value, warning);
        }

        [Fact]
        public void Resolve_SchoolMissingResistCurve_RepeatedHits_WarnsOnlyOnce()
        {
            var fx = MakeFixture();

            fx.Host.ResolveEffect(DamageContext(SchoolWithoutCurve));
            fx.Host.ResolveEffect(DamageContext(SchoolWithoutCurve));
            fx.Host.ResolveEffect(DamageContext(SchoolWithoutCurve));

            // 同 WarnMissingStatOnce 一贯惯例：按学派 id 只警告一次，不随每次结算重复入账、刷爆
            // 诊断消息列表（见 ComputeMitigation/WarnMissingResistCurveOnce 判断记录）。
            Assert.Single(fx.Diagnostics.Warnings);
        }
    }
}
