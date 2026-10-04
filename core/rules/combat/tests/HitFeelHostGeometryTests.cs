// 手感落地 M5-S2a（手感设计/03 第 2.3、2.4 节）：结算第 0 步的无敌前置门，以及受击裁决收到的"分段/技能手感覆盖"与"蓄力缩放"。
// 同属 HitFeelHostTests 夹具（partial 拆分，避免与并行切片同改一个文件尾部）。期望值由档案毫秒、标定 tick 与缩放规则算出。
using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Rules.Combat;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    public partial class HitFeelHostTests
    {
        // ------------------------------------------------------------------ 结算第 0 步：无敌前置门

        [Fact]
        public void InvulnerabilityGate_Closed_JudgesDamageAsInvulnerable_WithoutTouchingHealthHitTableOrCombatState()
        {
            var fx = Build();
            fx.Combat.InvulnerabilityGate = ctx => ctx.TargetId.Equals(Target);
            var health = fx.C.Powers.GetPower(Target, WellKnownPowers.Health);
            var avoidedEvents = new System.Collections.Generic.List<CombatAttackAvoidedEvent>();
            var damageEvents = new System.Collections.Generic.List<CombatDamageDealtEvent>();
            fx.Bus.Subscribe<CombatAttackAvoidedEvent>(RulesEventKeys.CombatAttackAvoided, avoidedEvents.Add);
            fx.Bus.Subscribe<CombatDamageDealtEvent>(RulesEventKeys.CombatDamageDealt, damageEvents.Add);

            var result = fx.Combat.ResolveEffect(new EffectContext(
                Attacker, Target, Skill, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical, 10, 1.0, canCrit: true, canMiss: true));
            fx.Bus.DispatchPending();

            Assert.Equal(HitResult.Invulnerable, result.Hit);
            Assert.Equal(0.0, result.FinalAmount);
            Assert.Equal(health, fx.C.Powers.GetPower(Target, WellKnownPowers.Health)); // 不落地
            Assert.False(fx.Combat.IsInCombat(Attacker)); // 不进战（被无敌挡住不算一次战斗事件）
            var avoided = Assert.Single(avoidedEvents);
            Assert.Equal(HitResult.Invulnerable, avoided.HitResult);
            Assert.Equal(Target, avoided.TargetId);
            Assert.Equal(Skill, avoided.SkillId);
            Assert.Empty(damageEvents);
        }

        [Fact]
        public void InvulnerabilityGate_DoesNotApplyToHeals_AndAnOpenGateOrNoGateChangesNothing()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.Combat.InvulnerabilityGate = _ => true;
            // 治疗类结算不经门（无敌窗口不拦治疗）。
            var heal = fx.Combat.ResolveEffect(new EffectContext(
                Attacker, Target, Skill, EffectKind.Heal, CombatTestSupport.SchoolPhysical, 10, 1.0, canCrit: false));
            Assert.NotEqual(HitResult.Invulnerable, heal.Hit);

            // 门恒为假与没有接门的结果逐位相同（零影响证明的单元级形式；全库基线零影响见实验室 suite）。
            var withGate = Build();
            withGate.Combat.InvulnerabilityGate = _ => false;
            var without = Build();
            var a = withGate.Combat.ResolveEffect(new EffectContext(
                Attacker, Target, Skill, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical, 10, 1.0, canCrit: false, canMiss: false));
            var b = without.Combat.ResolveEffect(new EffectContext(
                Attacker, Target, Skill, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical, 10, 1.0, canCrit: false, canMiss: false));
            Assert.Equal(b.Hit, a.Hit);
            Assert.Equal(b.FinalAmount, a.FinalAmount);
            Assert.Equal(b.Steps?.Count, a.Steps?.Count);
        }

        // ------------------------------------------------------------------ 受击裁决：蓄力缩放与手感覆盖

        private static CombatHitConfirmedEvent ConfirmedWith(Fx fx, Id attackId, HitFeelOutcome outcome) =>
            new CombatHitConfirmedEvent(
                attackId, 0, Attacker, Target, Skill, HitResult.Hit, 10.0, 0.01, false, false, fx.C.Units.GetPosition(Target),
                new Vec2(-1, 0), new Vec2(1, 0), outcome.ImpactClass, outcome.AttackerHitStopTicks, outcome.TargetHitStopTicks, outcome.Reaction);

        [Theory]
        [InlineData(1.0)]
        [InlineData(2.0)]
        [InlineData(3.0)]
        public void ChargeScale_MultipliesTheHitstopMillisecondsBeforeTheCaps(double hitstopScale)
        {
            var fx = Build();
            var atkMs = Ms(fx, Attacker, FeelFieldNames.AttackerHitstopMs);
            var tgtMs = Ms(fx, Target, FeelFieldNames.TargetHitstopMs);
            var scale = new HitFeelScale(hitstopScale, hitstopScale, 1.0, 1.0);

            var outcome = fx.Host.Evaluate(new HitFeelInput(
                Attacker, Target, HitResult.Hit, 10, false, null, new Id("attack.hf.scale"), false, scale));

            // 规则：毫秒 × 缩放，换算成 tick 后各自取所属单位的上限。
            Assert.Equal(
                Math.Min(Ticks(atkMs * hitstopScale), Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopCapMs))), outcome.AttackerHitStopTicks);
            Assert.Equal(Math.Min(Ticks(tgtMs * hitstopScale), Ticks(Ms(fx, Target, FeelFieldNames.HitstopCapMs))), outcome.TargetHitStopTicks);
        }

        [Fact]
        public void ChargeScale_Identity_IsBitIdenticalToAnInputWithoutScale()
        {
            var fx = Build();
            var plain = fx.Host.Evaluate(new HitFeelInput(Attacker, Target, HitResult.Hit, 10, false));
            var identity = fx.Host.Evaluate(new HitFeelInput(
                Attacker, Target, HitResult.Hit, 10, false, null, new Id("attack.hf.identity"), false, HitFeelScale.Identity));
            Assert.Equal(plain.AttackerHitStopTicks, identity.AttackerHitStopTicks);
            Assert.Equal(plain.TargetHitStopTicks, identity.TargetHitStopTicks);
            Assert.Equal(plain.Reaction, identity.Reaction);
            Assert.True(HitFeelScale.Identity.IsIdentity);
            Assert.False(default(HitFeelScale).IsIdentity); // default 是全 0（不是恒等）：只有经构造的输入才携带恒等缩放
        }

        /// <summary>
        /// 在下一个 tick 的开头（受击裁决宿主清理逐 tick 暂存之后）做一次裁决并发出对应的 <c>combat.hit_confirmed</c>——与生产里"同一 tick 内裁决、同一 tick 派发"的次序一致。
        /// </summary>
        private static void EvaluateAndConfirmInNextTick(Fx fx, Id attackId, HitFeelInput input)
        {
            var done = false;
            fx.Bus.Subscribe<Core.Foundation.SimLoop.SimTickStartedEvent>(Core.Foundation.SimLoop.SimEventKeys.TickStarted, _ =>
            {
                if (done) return;
                done = true;
                var outcome = fx.Host.Evaluate(input);
                fx.Bus.Enqueue(ConfirmedWith(fx, attackId, outcome));
            });
        }

        private static double? KnockbackFor(Fx fx, Id attackId, HitFeelInput input)
        {
            EvaluateAndConfirmInNextTick(fx, attackId, input);
            fx.Run(1 + Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs)) + 3);
            return fx.Knock.Calls.Count == 0 ? (double?)null : fx.Knock.Calls.Single().Distance;
        }

        private static Fx KnockbackFx()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "heavy");
            fx.Set(FeelFieldNames.KnockbackDistance, 0.5);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            return fx;
        }

        [Fact]
        public void ChargeScale_ScalesTheKnockbackDistanceAtLanding_ByTheSameFactor()
        {
            var attackId = new Id("attack.hf.kb_scale");
            var baseline = KnockbackFor(KnockbackFx(), attackId, new HitFeelInput(Attacker, Target, HitResult.Hit, 10, false));

            var fx = KnockbackFx();
            var factor = 2.5;
            var scaled = KnockbackFor(fx, attackId, new HitFeelInput(
                Attacker, Target, HitResult.Hit, 10, false, null, attackId, false, new HitFeelScale(1.0, 1.0, factor, 1.0)));

            Assert.True(baseline > 0.0);
            Assert.Equal(baseline!.Value * factor, scaled!.Value, 9);
        }

        private static Fx WithActionRow(Fx fx, string row, string field, double value)
        {
            var p = fx.Feel.Resolver.Profiles;
            var rows = p.Presets.Concat(p.Archetypes).Concat(p.Weapons).Concat(p.Characters).Concat(p.Actions).Concat(p.TagMaps)
                .Append(FeelRow.Overlay(FeelTables.Action, row, new[] { new FeelWrite(field, FeelOp.Set, FeelValue.Of(value)) }));
            fx.Feel.Resolver.Reload(new FeelProfileSet(p.Fields, rows, p.MotionModeRules, p.Calibrations));
            return fx;
        }

        [Fact]
        public void AttackerFeelOverride_ReplacesTheViewTheLandingReads_OnlyWhenFlagged()
        {
            const string row = "feel.action.hf_override";
            var attackId = new Id("attack.hf.override");
            const double overrideDistance = 1.2;

            Fx Prepared()
            {
                // 攻击方当前解析结果不带击退（预设击退为 0，不写调试覆盖——调试层高于动作层，会盖住分段行）。
                var fx = Build();
                fx.Set(FeelFieldNames.ImpactClass, "heavy");
                fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
                return WithActionRow(fx, row, FeelFieldNames.KnockbackDistance, overrideDistance);
            }

            // 带标志：落地读覆盖视图（分段行把击退距离改写成 overrideDistance 个体高）。
            var flagged = Prepared();
            var view = flagged.Feel.Resolver.ResolveJudgingWithAction(Attacker, null, row);
            var viewDistance = view.GetNumber(FeelFieldNames.KnockbackDistance);
            Assert.Equal(0.0, flagged.Feel.Resolver.ResolveJudging(Attacker).GetNumber(FeelFieldNames.KnockbackDistance));
            Assert.True(viewDistance > 0.0);
            var overridden = KnockbackFor(flagged, attackId, new HitFeelInput(
                Attacker, Target, HitResult.Hit, 10, false, view, attackId, true, HitFeelScale.Identity));
            var expected = viewDistance * flagged.Options.KnockbackImpactMultipliers["heavy"]; // 目标没有击退抗性
            Assert.Equal(expected, overridden!.Value, 9);

            // 不带标志（时间线动作开始快照的口径）：落地仍读攻击方当前解析结果——击退为 0，不提交击退，与此前一致。
            var unflagged = Prepared();
            Assert.Null(KnockbackFor(unflagged, attackId, new HitFeelInput(
                Attacker, Target, HitResult.Hit, 10, false, view, attackId, false, HitFeelScale.Identity)));
        }

        // ------------------------------------------------------------------ instant 路径：技能行 feel_ref

        [Fact]
        public void InstantSkillFeelRef_RebuildsTheAttackerViewFromTheSkillRow_AndUndeclaredKeepsTheCurrentView()
        {
            const string row = "feel.action.hf_spell";
            const double spellHitstopMs = 90.0;

            Fx Prepared(bool declared)
            {
                var fx = Build(configure: o => o.SkillFeelRef = id => declared && id.HasValue && id.Value.Equals(Skill) ? row : null);
                fx.Feel.DebugOverrides!.ClearGlobal(FeelFieldNames.AttackerHitstopMs); // 调试层高于动作层，先撤掉夹具默认的 50 ms
                WithActionRow(fx, row, FeelFieldNames.AttackerHitstopMs, spellHitstopMs);
                fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
                return fx;
            }

            var declaredFx = Prepared(true);
            CastHit(declaredFx);
            declaredFx.Tick();
            var cap = Ticks(Ms(declaredFx, Attacker, FeelFieldNames.AttackerHitstopCapMs));
            Assert.Equal(Math.Min(Ticks(spellHitstopMs), cap), declaredFx.FrozenOver(Attacker, 40));

            var undeclaredFx = Prepared(false);
            CastHit(undeclaredFx);
            undeclaredFx.Tick();
            var baseTicks = Ticks(Ms(undeclaredFx, Attacker, FeelFieldNames.AttackerHitstopMs));
            Assert.Equal(Math.Min(baseTicks, cap), undeclaredFx.FrozenOver(Attacker, 40));
            Assert.NotEqual(Ticks(spellHitstopMs), baseTicks);
            Assert.Equal(0, baseTicks); // 预设攻击方顿帧为 0：未声明时没有顿帧，声明了才有
        }
    }
}
