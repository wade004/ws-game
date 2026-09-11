using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// 消费方反馈 2026-09-11"编辑器第 31 条：结算中间步骤经真实施法路径不可观测"验收（见
    /// architecture/落地计划/消费方反馈-2026-09-11-编辑器-第31条.md"方案 1"，命名前缀 C10 延续本
    /// 仓库既有验收测试惯例——见 <c>core/rules/skill/tests/C07_MultipleProcTriggersTests.cs</c>/
    /// <c>C09_SkillReadinessTests.cs</c>）。
    /// <para>
    /// 本文件在 <see cref="Core.Rules.Combat.Resolver"/> 这一层（经 <see
    /// cref="CombatTestSupport.Fixture.Host"/>，真实 <c>CombatHost</c>/<c>Resolver</c>，
    /// <c>IUnitAccess</c>/<c>IAuraQuery</c> 用 Fake，与本模块既有测试惯例一致，见
    /// <see cref="ResolverHitTableTests"/>）穷举 <see cref="Core.Rules.Combat.Resolver.Resolve"/>
    /// 的全部三条返回路径（目标已死亡短路 / miss-dodge-parry 判定终止短路 / 完整九步落地）逐一验证
    /// <c>CombatOptions.ResolveTrace</c> 被调用一次、<c>Steps</c> 非空；覆盖"周期光环/Proc/治疗"三类
    /// 路径与"未设置时行为不变""回调抛异常不影响结算"两条跨路径不变量的集成级验收见
    /// <c>core/rules/skill/tests/C10_ResolveTraceTests.cs</c>（经真实 <c>RulesAssembly</c> +
    /// <c>CastSkill</c>）。
    /// </para>
    /// </summary>
    public sealed class C10_ResolveTraceTests
    {
        private static readonly Id Hero = new Id("unit.c10_hero");
        private static readonly Id Dummy = new Id("unit.c10_dummy");
        private static readonly Id SkillId = new Id("skill.c10_test_strike");
        private static readonly Id HealSkillId = new Id("skill.c10_test_heal");

        private static CombatTestSupport.Fixture MakeFixture(
            string hitTableName, Action<EffectContext, ResolveResult>? resolveTrace = null, bool targetAlive = true)
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.HitTableConfigId = new Id($"combat.hit_table.{hitTableName}");
                o.ResolveTrace = resolveTrace;
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde, alive: targetAlive);
            return fx;
        }

        private static EffectContext DamageContext(double baseValue = 100, bool canCrit = true, bool canMiss = true) =>
            new EffectContext(Hero, Dummy, SkillId, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0, canCrit: canCrit, canMiss: canMiss);

        private static EffectContext HealContext(double baseValue = 100) =>
            new EffectContext(Hero, Dummy, HealSkillId, EffectKind.Heal, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0);

        // -----------------------------------------------------------------
        // 三条返回路径各一例：回调恰好调用一次，Steps 非空，与最终落地量一致
        // -----------------------------------------------------------------

        [Fact]
        public void ResolveTrace_FullCompletionPath_InvokedOnce_WithNonEmptyStepsAndMatchingFinalAmount()
        {
            var calls = new List<(EffectContext Context, ResolveResult Result)>();
            var fx = MakeFixture("crit_forced", (ctx, result) => calls.Add((ctx, result)));

            var result = fx.Host.ResolveEffect(DamageContext(baseValue: 100));

            Assert.Equal(HitResult.Crit, result.Hit);
            Assert.Equal(200.0, result.FinalAmount);

            var call = Assert.Single(calls);
            Assert.Same(result, call.Result); // 回调收到的正是本次 Resolve 返回的同一个 ResolveResult 实例。
            Assert.Equal(Hero, call.Context.SourceId);
            Assert.Equal(Dummy, call.Context.TargetId);
            Assert.NotNull(call.Result.Steps);
            Assert.NotEmpty(call.Result.Steps!);
            // 与最终落地量一致：回调里看到的 FinalAmount 与结算真正扣掉的生命值相符。
            Assert.Equal(1000.0 - call.Result.FinalAmount, fx.Powers.GetPower(Dummy, WellKnownPowers.Health));
        }

        [Fact]
        public void ResolveTrace_DeadTargetPrecheckPath_InvokedOnce_WithMissResultAndPrecheckStep()
        {
            var calls = new List<(EffectContext Context, ResolveResult Result)>();
            var fx = MakeFixture("default", (ctx, result) => calls.Add((ctx, result)), targetAlive: false);

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Miss, result.Hit);
            Assert.Equal(0.0, result.FinalAmount);

            var call = Assert.Single(calls);
            Assert.Same(result, call.Result);
            Assert.NotNull(call.Result.Steps);
            Assert.Contains(call.Result.Steps!, s => s.Contains("precheck"));
        }

        [Fact]
        public void ResolveTrace_TerminalMissDodgeParryPath_InvokedOnce_WithZeroAmountsAndTerminalStep()
        {
            var calls = new List<(EffectContext Context, ResolveResult Result)>();
            var fx = MakeFixture("miss_forced", (ctx, result) => calls.Add((ctx, result)));

            var result = fx.Host.ResolveEffect(DamageContext());

            Assert.Equal(HitResult.Miss, result.Hit);
            Assert.Equal(0.0, result.FinalAmount);

            var call = Assert.Single(calls);
            Assert.Same(result, call.Result);
            Assert.NotNull(call.Result.Steps);
            Assert.Contains(call.Result.Steps!, s => s.Contains("terminal"));
        }

        [Fact]
        public void ResolveTrace_HealPath_InvokedOnce_WithHealResultMatchingLandedAmount()
        {
            var calls = new List<(EffectContext Context, ResolveResult Result)>();
            var fx = MakeFixture("default", (ctx, result) => calls.Add((ctx, result)));
            fx.Powers.ModifyPower(Dummy, WellKnownPowers.Health, -500, sourceId: new Id("system.c10_setup"));

            var result = fx.Host.ResolveEffect(HealContext(baseValue: 100));

            Assert.True(result.IsHeal);
            Assert.Equal(100.0, result.FinalAmount);

            var call = Assert.Single(calls);
            Assert.True(call.Result.IsHeal);
            Assert.Equal(500.0 + call.Result.FinalAmount, fx.Powers.GetPower(Dummy, WellKnownPowers.Health));
        }

        // -----------------------------------------------------------------
        // 未设置时零开销：行为与结果逐字段不变（对照同一场景两次独立结算）
        // -----------------------------------------------------------------

        [Fact]
        public void ResolveTrace_Unset_ResultAndEventsAndLandedAmount_IdenticalToBaseline()
        {
            var baseline = MakeFixture("crit_forced", resolveTrace: null);
            var baselineResult = baseline.Host.ResolveEffect(DamageContext(baseValue: 100));
            baseline.Bus.DispatchPending();

            var withCallback = MakeFixture("crit_forced", (ctx, result) => { /* 观测但不改变任何状态 */ });
            var withCallbackResult = withCallback.Host.ResolveEffect(DamageContext(baseValue: 100));
            withCallback.Bus.DispatchPending();

            Assert.Equal(baselineResult.Hit, withCallbackResult.Hit);
            Assert.Equal(baselineResult.RequestedAmount, withCallbackResult.RequestedAmount);
            Assert.Equal(baselineResult.FinalAmount, withCallbackResult.FinalAmount);
            Assert.Equal(baselineResult.Absorbed, withCallbackResult.Absorbed);
            Assert.Equal(baselineResult.Immune, withCallbackResult.Immune);
            Assert.Equal(baselineResult.IsHeal, withCallbackResult.IsHeal);
            Assert.Equal(baselineResult.Steps, withCallbackResult.Steps);
            Assert.Equal(
                baseline.Powers.GetPower(Dummy, WellKnownPowers.Health),
                withCallback.Powers.GetPower(Dummy, WellKnownPowers.Health));
            Assert.Equal(baseline.Events.Count, withCallback.Events.Count);

            CombatDamageDealtEvent? baselineDealt = null;
            foreach (var evt in baseline.Events) { if (evt is CombatDamageDealtEvent d) baselineDealt = d; }
            CombatDamageDealtEvent? withCallbackDealt = null;
            foreach (var evt in withCallback.Events) { if (evt is CombatDamageDealtEvent d) withCallbackDealt = d; }

            Assert.NotNull(baselineDealt);
            Assert.NotNull(withCallbackDealt);
            Assert.Equal(baselineDealt!.Amount, withCallbackDealt!.Amount);
            Assert.Equal(baselineDealt.IsCrit, withCallbackDealt.IsCrit);
            Assert.Equal(baselineDealt.HitResult, withCallbackDealt.HitResult);
        }

        // -----------------------------------------------------------------
        // 回调抛异常：不中断结算，不阻断落地事件，经 ICombatDiagnostics 记一次警告
        // -----------------------------------------------------------------

        [Fact]
        public void ResolveTrace_CallbackThrows_DoesNotInterruptResolution_LandsDamageAndFiresEvent_AndWarns()
        {
            var fx = MakeFixture("crit_forced", (ctx, result) => throw new InvalidOperationException("boom (仅测试用异常，不代表真实诊断代码缺陷)"));

            var result = fx.Host.ResolveEffect(DamageContext(baseValue: 100));
            fx.Bus.DispatchPending();

            Assert.Equal(HitResult.Crit, result.Hit);
            Assert.Equal(200.0, result.FinalAmount);
            Assert.Equal(800.0, fx.Powers.GetPower(Dummy, WellKnownPowers.Health));

            var dealt = Assert.Single(fx.Events, e => e is CombatDamageDealtEvent);
            Assert.Equal(200.0, ((CombatDamageDealtEvent)dealt).Amount);

            Assert.Contains(fx.Diagnostics.Warnings, w => w.Contains("ResolveTrace"));
        }
    }
}
