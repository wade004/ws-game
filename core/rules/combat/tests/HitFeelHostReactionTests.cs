// HitFeelHostReactionTests：手感设计/03（ADR-0145）受击反应的运行时冒烟——硬直时长公式、击退时长、硬直保护期、倒地/起身阶段与事件、
// 起身无敌、命中类别（暴击/格挡/偏斜/弹反）、格挡裁决（真实 Resolver）。
// 每组是"复现 + 不变量"：复现给出具体数值，不变量覆盖同一类的其它取值；期望值全部由档案毫秒与标定 tick 率算出，不写死裸数；
// 档案里没声明新字段 = 与此前逐位一致（每组都有一条"缺省不变"的对照）。
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Numbers.PowerSet;
using Core.Rules.Combat;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    public partial class HitFeelHostTests
    {
        // ------------------------------------------------------------------ 夹具

        private sealed class PhaseProbe
        {
            public readonly List<(long Tick, UnitKnockedDownEvent E)> Down = new List<(long, UnitKnockedDownEvent)>();
            public readonly List<(long Tick, UnitGetupStartedEvent E)> GetupStarted = new List<(long, UnitGetupStartedEvent)>();
            public readonly List<(long Tick, UnitGetupFinishedEvent E)> GetupFinished = new List<(long, UnitGetupFinishedEvent)>();
        }

        private static PhaseProbe WatchPhases(Fx fx)
        {
            var probe = new PhaseProbe();
            fx.Bus.Subscribe<UnitKnockedDownEvent>(RulesEventKeys.UnitKnockedDown, e => probe.Down.Add((fx.TickNo, e)));
            fx.Bus.Subscribe<UnitGetupStartedEvent>(RulesEventKeys.UnitGetupStarted, e => probe.GetupStarted.Add((fx.TickNo, e)));
            fx.Bus.Subscribe<UnitGetupFinishedEvent>(RulesEventKeys.UnitGetupFinished, e => probe.GetupFinished.Add((fx.TickNo, e)));
            return probe;
        }

        /// <summary>massive 冲击（→ knockdown）、目标无顿帧：硬直/倒地/起身三段都能直接数 tick。</summary>
        private static Fx KnockdownFx(double getupMs = 200.0, double invulnMs = 0.0)
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "massive");
            fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            fx.Set(FeelFieldNames.GetupMs, getupMs);
            if (invulnMs > 0.0) fx.Set(FeelFieldNames.GetupInvulnMs, invulnMs);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            return fx;
        }

        /// <summary>目标在 <paramref name="fx"/> 里连续处于硬直的最长 tick 数。</summary>
        private static int LongestStaggerRun(Fx fx, Id unit)
        {
            var best = 0;
            var run = 0;
            foreach (var s in fx.Staggered[unit])
            {
                run = s ? run + 1 : 0;
                if (run > best) best = run;
            }

            return best;
        }

        // ------------------------------------------------------------------ 硬直时长公式（hit_stun_ms × hit_stun_scale × 反应倍率表）

        [Theory]
        [InlineData(0.5)]
        [InlineData(1.0)]
        [InlineData(1.5)]
        [InlineData(2.0)]
        public void HitStunScale_AttackerScalesTheTargetsBaseStun(double scale)
        {
            var fx = Build();
            fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            SetUnitField(fx, Attacker, FeelFieldNames.HitStunScale, scale);
            var expected = Ticks(Ms(fx, Target, FeelFieldNames.HitStunMs) * Ms(fx, Attacker, FeelFieldNames.HitStunScale));

            fx.Hit(Attacker, Target);
            fx.Run(1 + expected + 4);

            Assert.Equal(HitReaction.Stagger, ReactionOf(fx));
            Assert.Equal(expected, fx.Reactions.Single().E.DurationTicks);
            Assert.Equal(expected, fx.Reactions.Single().E.StunTicks);
            Assert.Equal(expected, fx.StaggeredCount(Target));
        }

        [Fact]
        public void HitStunScale_Undeclared_IsBitIdenticalToTheBaseStun()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var expected = Ticks(Ms(fx, Target, FeelFieldNames.HitStunMs));
            fx.Hit(Attacker, Target);
            fx.Run(1 + expected + 4);
            Assert.Equal(expected, fx.StaggeredCount(Target));
        }

        [Fact]
        public void HitStunReactionMultipliers_ScaleOnlyTheStunSegmentOfTheDeclaredReaction()
        {
            // 复现：表里只有 knockdown 一项 ×1.5：stagger 不变，knockdown 的硬直段 ×1.5，倒地段（downed_ms）不乘。
            var table = new Dictionary<HitReaction, double> { [HitReaction.Knockdown] = 1.5 };
            var stagger = Build(configure: o => o.HitStunReactionMultipliers = table);
            stagger.Set(FeelFieldNames.TargetHitstopMs, 0);
            stagger.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var baseStun = Ticks(Ms(stagger, Target, FeelFieldNames.HitStunMs));
            stagger.Hit(Attacker, Target);
            stagger.Run(1 + baseStun + 3);
            Assert.Equal(baseStun, stagger.Reactions.Single().E.StunTicks);

            var down = Build(configure: o => o.HitStunReactionMultipliers = table);
            down.Set(FeelFieldNames.ImpactClass, "massive");
            down.Set(FeelFieldNames.TargetHitstopMs, 0);
            down.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var scaledStun = Ticks(Ms(down, Target, FeelFieldNames.HitStunMs) * table[HitReaction.Knockdown]);
            var downed = Ticks(Ms(down, Target, FeelFieldNames.DownedMs));
            down.Hit(Attacker, Target);
            down.Run(1 + scaledStun + downed + 3);
            var applied = down.Reactions.Single().E;
            Assert.Equal(scaledStun, applied.StunTicks);
            Assert.Equal(downed, applied.DownedTicks);
            Assert.Equal(scaledStun + downed, applied.DurationTicks);
        }

        // ------------------------------------------------------------------ 击退时长（knockback_duration_ms）

        [Fact]
        public void KnockbackDuration_AttackerDeclaresIt_TheSinkGetsThatDuration_OtherwiseTheGameOption()
        {
            var declared = Build(configure: o => o.KnockbackDurationSeconds = 0.3);
            declared.Set(FeelFieldNames.ImpactClass, "heavy");
            declared.Set(FeelFieldNames.TargetHitstopMs, 0);
            declared.Set(FeelFieldNames.KnockbackDistance, 0.5);
            declared.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            SetUnitField(declared, Attacker, FeelFieldNames.KnockbackDurationMs, 450);
            declared.Hit(Attacker, Target);
            declared.Run(3);
            Assert.Equal(Ms(declared, Attacker, FeelFieldNames.KnockbackDurationMs) / 1000.0, declared.Knock.Calls.Single().Duration, 9);

            var fallback = Build(configure: o => o.KnockbackDurationSeconds = 0.3);
            fallback.Set(FeelFieldNames.ImpactClass, "heavy");
            fallback.Set(FeelFieldNames.TargetHitstopMs, 0);
            fallback.Set(FeelFieldNames.KnockbackDistance, 0.5);
            fallback.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fallback.Hit(Attacker, Target);
            fallback.Run(3);
            Assert.Equal(fallback.Options.KnockbackDurationSeconds, fallback.Knock.Calls.Single().Duration, 9);
        }

        // ------------------------------------------------------------------ 硬直保护期（stagger_grace_ms）：堵死"反复命中 = 永久控制"

        [Fact]
        public void StaggerGrace_RepeatedHitsInsideStagger_CannotChainTheStunForever()
        {
            // 复现：目标无顿帧，每 4 tick 一击（< 硬直时长）。不声明保护期：每击都刷新硬直，一直锁到停手；
            // 声明保护期：第一段硬直之后的击打只剩 flinch，硬直恰好走完一个 hit_stun（不变量：连续硬直 tick 数 == 反应时长）。
            const int hits = 8;
            const int every = 4;

            var free = Build();
            free.Set(FeelFieldNames.TargetHitstopMs, 0);
            free.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var stun = Ticks(Ms(free, Target, FeelFieldNames.HitStunMs));
            Assert.True(stun > every);
            for (var i = 0; i < hits; i++)
            {
                free.Hit(Attacker, Target);
                free.Run(every);
            }

            free.Run(stun + 4);
            Assert.True(LongestStaggerRun(free, Target) > stun); // 无保护期：被锁超过一次硬直

            var graced = Build();
            graced.Set(FeelFieldNames.TargetHitstopMs, 0);
            graced.Set(FeelFieldNames.StaggerGraceMs, 100);
            graced.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            for (var i = 0; i < hits; i++)
            {
                graced.Hit(Attacker, Target);
                graced.Run(every);
            }

            graced.Run(stun + 4);
            Assert.Equal(stun, LongestStaggerRun(graced, Target));
            Assert.Contains(graced.Reactions, r => r.E.Reaction == HitReaction.Flinch);
        }

        [Fact]
        public void StaggerGrace_ProtectsExactlyGraceTicksAfterTheStaggerEnds_ThenTheNextHitIsFull()
        {
            // 不变量：硬直结束的 tick 起共保护 stagger_grace_ms 折算的 tick 数；窗口内 flinch，窗口外完整硬直。
            foreach (var graceMs in new[] { 50.0, 100.0, 200.0 })
            {
                var grace = Ticks(graceMs);
                // 替身发射器在调用时（上一个 tick 末）裁决：硬直结束的 tick 末起共 grace 个 tick 末在保护期内。
                var inside = GraceProbe(graceMs, offsetFromEnd: grace);
                var outside = GraceProbe(graceMs, offsetFromEnd: grace + 1);
                Assert.Equal(HitReaction.Flinch, inside);
                Assert.Equal(HitReaction.Stagger, outside);
            }
        }

        private static HitReaction GraceProbe(double graceMs, int offsetFromEnd)
        {
            var fx = Build();
            fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            fx.Set(FeelFieldNames.StaggerGraceMs, graceMs);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var stun = Ticks(Ms(fx, Target, FeelFieldNames.HitStunMs));
            fx.Hit(Attacker, Target);
            fx.Run(1 + stun + 2);
            var first = fx.FirstStaggeredTick(Target);
            var end = first + stun; // 第一个不在硬直里的 tick 下标
            Assert.False(fx.Staggered[Target][end]);
            fx.Run(end + offsetFromEnd - (int)fx.TickNo);
            Assert.Equal(end + offsetFromEnd, (int)fx.TickNo);
            var before = fx.Reactions.Count;
            fx.Hit(Attacker, Target);
            fx.Run(1);
            return fx.Reactions[before].E.Reaction;
        }

        [Fact]
        public void StaggerGrace_NotDeclared_AllowsTheSecondFullStaggerRightAfterTheFirst()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var stun = Ticks(Ms(fx, Target, FeelFieldNames.HitStunMs));
            fx.Hit(Attacker, Target);
            fx.Run(1 + stun + 1);
            fx.Hit(Attacker, Target);
            fx.Run(2);
            Assert.All(fx.Reactions, r => Assert.Equal(HitReaction.Stagger, r.E.Reaction));
            Assert.Equal(2, fx.Reactions.Count);
        }

        [Fact]
        public void StaggerGrace_NeverTouchesTheKillingBlow()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            fx.Set(FeelFieldNames.StaggerGraceMs, 200);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.Hit(Attacker, Target);
            fx.Run(2);
            fx.Hit(Attacker, Target, kill: true);
            fx.Run(2);
            Assert.Equal(HitReaction.Death, fx.Reactions.Last().E.Reaction);
        }

        [Fact]
        public void StaggerGrace_CapIsConfigurable()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            fx.Set(FeelFieldNames.StaggerGraceMs, 200);
            fx.Set(FeelFieldNames.StaggerGraceCap, "stagger_light");
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.Hit(Attacker, Target);
            fx.Run(2);
            var outcome = fx.Host.Evaluate(new HitFeelInput(Attacker, Target, HitResult.Hit, 10, false));
            Assert.Equal(HitReaction.StaggerLight, outcome.Reaction);
        }

        // ------------------------------------------------------------------ 倒地 → 起身阶段与事件

        [Fact]
        public void KnockdownPhases_EventsMarkTheSegmentBoundaries_AndQueriesCoverEachSegment()
        {
            var fx = KnockdownFx(getupMs: 200);
            var probe = WatchPhases(fx);
            var stun = Ticks(Ms(fx, Target, FeelFieldNames.HitStunMs));
            var down = Ticks(Ms(fx, Target, FeelFieldNames.DownedMs));
            var getup = Ticks(Ms(fx, Target, FeelFieldNames.GetupMs));
            Assert.True(stun > 0 && down > 0 && getup > 0);

            fx.Hit(Attacker, Target);
            var downedTicks = 0;
            var gettingUpTicks = 0;
            for (var i = 0; i < 1 + stun + down + getup + 6; i++)
            {
                fx.Tick();
                if (fx.Host.IsDowned(Target)) downedTicks++;
                if (fx.Host.IsGettingUp(Target)) gettingUpTicks++;
            }

            var first = fx.FirstStaggeredTick(Target);
            var kd = Assert.Single(probe.Down);
            var gs = Assert.Single(probe.GetupStarted);
            var gf = Assert.Single(probe.GetupFinished);
            Assert.Equal(Target, kd.E.UnitId);
            Assert.Equal(stun, kd.Tick - first);
            Assert.Equal(down, gs.Tick - kd.Tick);
            Assert.Equal(getup, gf.Tick - gs.Tick);
            Assert.Equal(down, downedTicks);
            Assert.Equal(getup, gettingUpTicks);
            Assert.Equal(stun + down + getup, fx.StaggeredCount(Target));

            var applied = fx.Reactions.Single().E;
            Assert.Equal(stun, applied.StunTicks);
            Assert.Equal(down, applied.DownedTicks);
            Assert.Equal(getup, applied.GetupTicks);
            Assert.Equal(stun + down + getup, applied.DurationTicks);
            Assert.Equal(down, kd.E.DownedTicks);
            Assert.Equal(getup, kd.E.GetupTicks);
            Assert.Equal(getup, gs.E.GetupTicks);
        }

        [Fact]
        public void KnockdownPhases_NoGetupDeclared_BehavesAsBefore_NoGetupEvents()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "massive");
            fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var probe = WatchPhases(fx);
            var stun = Ticks(Ms(fx, Target, FeelFieldNames.HitStunMs));
            var down = Ticks(Ms(fx, Target, FeelFieldNames.DownedMs));
            fx.Hit(Attacker, Target);
            fx.Run(1 + stun + down + 4);
            Assert.Equal(stun + down, fx.StaggeredCount(Target));
            Assert.Equal(down, fx.Reactions.Single().E.DownedTicks);
            Assert.Equal(0, fx.Reactions.Single().E.GetupTicks);
            Assert.Single(probe.Down);
            Assert.Empty(probe.GetupStarted);
            Assert.Empty(probe.GetupFinished);
        }

        [Fact]
        public void NonKnockdownReactions_NeverEmitPhaseEvents()
        {
            foreach (var impact in new[] { "light", "medium", "heavy" })
            {
                var fx = Build();
                fx.Set(FeelFieldNames.ImpactClass, impact);
                fx.Set(FeelFieldNames.TargetHitstopMs, 0);
                fx.Set(FeelFieldNames.GetupMs, 200);
                fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
                var probe = WatchPhases(fx);
                fx.Hit(Attacker, Target);
                fx.Run(60);
                Assert.Empty(probe.Down);
                Assert.Empty(probe.GetupStarted);
                Assert.Empty(probe.GetupFinished);
            }
        }

        // ------------------------------------------------------------------ 起身无敌（getup_invuln_ms）：真实结算管线

        private double HealthOf(Fx fx) => fx.C.Powers.GetPower(Target, WellKnownPowers.Health);

        [Fact]
        public void GetupInvulnerability_AvoidsDamageForExactlyTheDeclaredTicks_ThenDamageLandsAgain()
        {
            var fx = KnockdownFx(getupMs: 200, invulnMs: 100);
            var invuln = Ticks(Ms(fx, Target, FeelFieldNames.GetupInvulnMs));
            var getup = Ticks(Ms(fx, Target, FeelFieldNames.GetupMs));
            Assert.True(invuln > 0 && invuln < getup);

            fx.Hit(Attacker, Target);
            var protectedTicks = 0;
            var unprotectedGetupTicks = 0;
            var guardedHealthIntact = true;
            var damagedOutside = false;
            for (var i = 0; i < 400 && !damagedOutside; i++)
            {
                fx.Tick();
                var inWindow = fx.Host.IsGetupInvulnerable(Target);
                if (inWindow)
                {
                    protectedTicks++;
                    var before = HealthOf(fx);
                    CastHit(fx);
                    guardedHealthIntact &= HealthOf(fx) == before;
                }
                else if (fx.Host.IsGettingUp(Target))
                {
                    unprotectedGetupTicks++;
                    var before = HealthOf(fx);
                    CastHit(fx);
                    damagedOutside = HealthOf(fx) < before;
                }
            }

            Assert.Equal(invuln, protectedTicks);
            Assert.True(guardedHealthIntact);
            Assert.True(unprotectedGetupTicks >= 1 && damagedOutside);
        }

        [Fact]
        public void GetupInvulnerability_NotDeclared_NothingIsAvoided()
        {
            var fx = KnockdownFx(getupMs: 200);
            fx.Hit(Attacker, Target);
            var sawGetup = false;
            for (var i = 0; i < 400 && !sawGetup; i++)
            {
                fx.Tick();
                if (!fx.Host.IsGettingUp(Target)) continue;
                sawGetup = true;
                Assert.False(fx.Host.IsGetupInvulnerable(Target));
                var before = HealthOf(fx);
                CastHit(fx);
                Assert.True(HealthOf(fx) < before);
            }

            Assert.True(sawGetup);
        }

        // ------------------------------------------------------------------ 命中类别：暴击/格挡/偏斜/弹反

        private static HitFeelOutcome Eval(Fx fx, HitResult result, bool kill = false) =>
            fx.Host.Evaluate(new HitFeelInput(Attacker, Target, result, 10, kill));

        [Theory]
        [InlineData(HitResult.Crit)]
        [InlineData(HitResult.Block)]
        [InlineData(HitResult.GlancingBlow)]
        public void HitCategories_NothingDeclared_AreIdenticalToANormalHit(HitResult result)
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var normal = Eval(fx, HitResult.Hit);
            var other = Eval(fx, result);
            Assert.Equal(normal.ImpactClass, other.ImpactClass);
            Assert.Equal(normal.AttackerHitStopTicks, other.AttackerHitStopTicks);
            Assert.Equal(normal.TargetHitStopTicks, other.TargetHitStopTicks);
            Assert.Equal(normal.Reaction, other.Reaction);
            Assert.Equal(HitReaction.None, other.AttackerReaction);
        }

        [Fact]
        public void CritCategory_AttackerDeclaredImpactAndHitstopScale_ReplaceTheNormalHitsOnly()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            SetUnitField(fx, Attacker, FeelFieldNames.CritImpactClass, "massive");
            SetUnitField(fx, Attacker, FeelFieldNames.CritHitstopScale, 1.5);
            var scale = Ms(fx, Attacker, FeelFieldNames.CritHitstopScale);
            var expectedAttacker = Math.Min(
                Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopMs) * scale), Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopCapMs)));
            var expectedTarget = Math.Min(
                Ticks(Ms(fx, Attacker, FeelFieldNames.TargetHitstopMs) * scale), Ticks(Ms(fx, Target, FeelFieldNames.HitstopCapMs)));

            var crit = Eval(fx, HitResult.Crit);
            Assert.Equal("massive", crit.ImpactClass);
            Assert.Equal(HitReaction.Knockdown, crit.Reaction);
            Assert.Equal(expectedAttacker, crit.AttackerHitStopTicks);
            Assert.Equal(expectedTarget, crit.TargetHitStopTicks);

            var normal = Eval(fx, HitResult.Hit);
            Assert.Equal("medium", normal.ImpactClass);
            Assert.Equal(HitReaction.Stagger, normal.Reaction);
            Assert.Equal(Ticks(Ms(fx, Attacker, FeelFieldNames.TargetHitstopMs)), normal.TargetHitStopTicks);
        }

        [Fact]
        public void BlockCategory_DefenderDeclaredImpactScaleAndReactionCap_Apply()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            SetUnitField(fx, Target, FeelFieldNames.BlockImpactClass, "heavy");
            SetUnitField(fx, Target, FeelFieldNames.BlockHitstopScale, 0.5);
            SetUnitField(fx, Target, FeelFieldNames.BlockReactionCap, "flinch");
            var scale = Ms(fx, Target, FeelFieldNames.BlockHitstopScale);

            var blocked = Eval(fx, HitResult.Block);
            Assert.Equal("heavy", blocked.ImpactClass);
            Assert.Equal(HitReaction.Flinch, blocked.Reaction); // heavy 本应 knockback，被格挡上限压成 flinch
            Assert.Equal(Math.Min(
                Ticks(Ms(fx, Attacker, FeelFieldNames.TargetHitstopMs) * scale), Ticks(Ms(fx, Target, FeelFieldNames.HitstopCapMs))), blocked.TargetHitStopTicks);

            Assert.Equal(HitReaction.Stagger, Eval(fx, HitResult.Hit).Reaction); // 同样的档案，普通命中不受格挡声明影响
        }

        [Fact]
        public void GlancingCategory_DefenderDeclaredImpactAndScale_Apply()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            SetUnitField(fx, Target, FeelFieldNames.GlancingImpactClass, "light");
            SetUnitField(fx, Target, FeelFieldNames.GlancingHitstopScale, 0.5);
            var scale = Ms(fx, Target, FeelFieldNames.GlancingHitstopScale);
            var glancing = Eval(fx, HitResult.GlancingBlow);
            Assert.Equal("light", glancing.ImpactClass);
            Assert.Equal(HitReaction.StaggerLight, glancing.Reaction);
            Assert.Equal(Math.Min(
                Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopMs) * scale), Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopCapMs))), glancing.AttackerHitStopTicks);
        }

        [Fact]
        public void BlockCategory_AttackerBounce_AppliesTheDeclaredReactionToTheAttacker_UnlessSuperArmor()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.C.Stats.SetBase(Attacker, CombatTestSupport.StatArmor, 0);
            fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            SetUnitField(fx, Target, FeelFieldNames.BlockAttackerReaction, "stagger_light");
            var attackerStun = Ticks(Ms(fx, Attacker, FeelFieldNames.HitStunMs));

            var outcome = Eval(fx, HitResult.Block);
            Assert.Equal(HitReaction.StaggerLight, outcome.AttackerReaction);
            Assert.Equal(attackerStun, outcome.Detail.AttackerStunTicks);

            fx.Hit(Attacker, Target, result: HitResult.Block);
            fx.Run(1 + attackerStun + 4);
            Assert.Contains(fx.Reactions, r => r.E.TargetId == Attacker && r.E.Reaction == HitReaction.StaggerLight && r.E.SourceId == Target);
            Assert.Equal(attackerStun, fx.StaggeredCount(Attacker));

            var armored = Build(useActions: true);
            armored.Actions.SuperArmor = true;
            SetUnitField(armored, Target, FeelFieldNames.BlockAttackerReaction, "stagger_light");
            Assert.Equal(HitReaction.None, Eval(armored, HitResult.Block).AttackerReaction);
        }

        [Fact]
        public void ParryCategory_DeclaredHitstopAndAttackerReaction_Apply_UndeclaredDoesNothing()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var none = Eval(fx, HitResult.Parry);
            Assert.Equal(0, none.AttackerHitStopTicks);
            Assert.Equal(0, none.TargetHitStopTicks);
            Assert.Equal(HitReaction.None, none.AttackerReaction);

            SetUnitField(fx, Target, FeelFieldNames.ParryHitstopScale, 2.0);
            SetUnitField(fx, Target, FeelFieldNames.ParryAttackerReaction, "knockback");
            var scale = Ms(fx, Target, FeelFieldNames.ParryHitstopScale);
            var parry = Eval(fx, HitResult.Parry);
            Assert.Equal(Math.Min(
                Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopMs) * scale), Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopCapMs))), parry.AttackerHitStopTicks);
            Assert.Equal(HitReaction.Knockback, parry.AttackerReaction);
            Assert.Equal(HitReaction.None, parry.Reaction); // 弹反：防御方不受击
        }

        // ------------------------------------------------------------------ 格挡裁决：真实 Resolver + 防御状态查询

        /// <summary>在给定攻击方位置、防御状态下，对目标打一击（canMiss 可选），返回目标掉的血。</summary>
        private static double DamageOf(Action<Fx> setup, bool canMiss, Vec2 attackerAt)
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.C.Units.SetPosition(Attacker, attackerAt);
            setup(fx);
            var before = fx.C.Powers.GetPower(Target, WellKnownPowers.Health);
            var context = new EffectContext(Attacker, Target, Skill, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                100, coefficient: 1.0, isPeriodic: false, canCrit: false, canMiss: canMiss);
            fx.Combat.ResolveEffect(context);
            fx.Tick();
            return before - fx.C.Powers.GetPower(Target, WellKnownPowers.Health);
        }

        private static readonly Vec2 InFront = new Vec2(2, 0);   // 目标在 (1,0) 朝 +x：攻击方在 (2,0) 即正面
        private static readonly Vec2 Behind = new Vec2(0, 0);    // 攻击方在 (0,0) 即背后

        [Fact]
        public void Guard_FrontalHit_TakesGuardDamageScale_BackstabIsUnaffected()
        {
            var baseline = DamageOf(_ => { }, canMiss: false, InFront);
            Assert.True(baseline > 0);

            const double scale = 0.25;
            Action<Fx> guarding = fx =>
            {
                fx.GuardState.Elapsed[Target] = 1000;
                SetUnitField(fx, Target, FeelFieldNames.GuardDamageScale, scale);
            };

            Assert.Equal(baseline * scale, DamageOf(guarding, canMiss: true, InFront), 6);
            Assert.Equal(baseline, DamageOf(guarding, canMiss: true, Behind), 6); // 背后不吃格挡
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(1.0)]
        public void Guard_DamageScaleInvariant_FrontalDamageEqualsBaselineTimesScale(double scale)
        {
            var baseline = DamageOf(_ => { }, canMiss: false, InFront);
            var guarded = DamageOf(fx =>
            {
                fx.GuardState.Elapsed[Target] = 1000;
                SetUnitField(fx, Target, FeelFieldNames.GuardDamageScale, scale);
            }, canMiss: true, InFront);
            Assert.Equal(baseline * scale, guarded, 6);
        }

        [Fact]
        public void Guard_NotGuarding_OrNoDeclarations_DamageIsUnchanged()
        {
            var baseline = DamageOf(_ => { }, canMiss: false, InFront);
            Assert.Equal(baseline, DamageOf(fx => SetUnitField(fx, Target, FeelFieldNames.GuardDamageScale, 0.1), canMiss: true, InFront), 6); // 没在格挡
            Assert.Equal(baseline, DamageOf(fx => fx.GuardState.Elapsed[Target] = 1000, canMiss: true, InFront), 6); // 在格挡但没声明减伤倍率（缺省 1）
        }

        [Fact]
        public void Guard_CanMissFalseEffects_IgnoreTheGuard()
        {
            var baseline = DamageOf(_ => { }, canMiss: false, InFront);
            var guarded = DamageOf(fx =>
            {
                fx.GuardState.Elapsed[Target] = 1000;
                SetUnitField(fx, Target, FeelFieldNames.GuardDamageScale, 0.0);
            }, canMiss: false, InFront);
            Assert.Equal(baseline, guarded, 6);
        }

        [Fact]
        public void Guard_ParryWindowAfterGuardStart_AvoidsTheHitEntirely_ThenBecomesABlock()
        {
            const double scale = 0.5;
            var baseline = DamageOf(_ => { }, canMiss: false, InFront);
            const double windowMs = 100;
            var windowTicks = Ticks(windowMs);
            Assert.True(windowTicks > 1);
            double Dmg(int elapsed) => DamageOf(fx =>
            {
                fx.GuardState.Elapsed[Target] = elapsed;
                SetUnitField(fx, Target, FeelFieldNames.GuardParryWindowMs, windowMs);
                SetUnitField(fx, Target, FeelFieldNames.GuardDamageScale, scale);
            }, canMiss: true, InFront);

            Assert.Equal(0.0, Dmg(0), 6);
            Assert.Equal(0.0, Dmg(windowTicks - 1), 6);           // 窗口最后一个 tick 仍是弹反
            Assert.Equal(baseline * scale, Dmg(windowTicks), 6);  // 窗口之后是普通格挡
        }

        [Fact]
        public void Guard_AngleLimit_NarrowArcRejectsGlancingAttacks()
        {
            const double scale = 0.5;
            var baseline = DamageOf(_ => { }, canMiss: false, InFront);
            // 攻击方在目标侧面（(1,2)：来向与朝向夹 90°）：缺省 180° 弧（半角 90°）仍格挡，90° 弧（半角 45°）不格挡。
            var side = new Vec2(1, 2);
            Assert.Equal(baseline * scale, DamageOf(fx =>
            {
                fx.GuardState.Elapsed[Target] = 1000;
                SetUnitField(fx, Target, FeelFieldNames.GuardDamageScale, scale);
            }, canMiss: true, side), 6);
            Assert.Equal(baseline, DamageOf(fx =>
            {
                fx.GuardState.Elapsed[Target] = 1000;
                SetUnitField(fx, Target, FeelFieldNames.GuardDamageScale, scale);
                SetUnitField(fx, Target, FeelFieldNames.GuardArcDeg, 90);
            }, canMiss: true, side), 6);
            // 360° 全向：背后也格挡。
            Assert.Equal(baseline * scale, DamageOf(fx =>
            {
                fx.GuardState.Elapsed[Target] = 1000;
                SetUnitField(fx, Target, FeelFieldNames.GuardDamageScale, scale);
                SetUnitField(fx, Target, FeelFieldNames.GuardArcDeg, 360);
            }, canMiss: true, Behind), 6);
        }
    }
}
