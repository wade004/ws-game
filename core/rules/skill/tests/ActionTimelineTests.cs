using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;
using static Tests.Rules.Skill.TimelineHarness;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 动作时间线状态机运行时冒烟（手感设计/01 第 3 节、03 第 2 节；ADR-0115）。期望值一律由分相毫秒、固定步长（1/60 秒）、
    /// 速率系数经 <see cref="FeelCalibration.MillisecondsToTicks"/> 算出，不写死裸 tick 数；事件发生的 tick 以"施法所在 tick + 动作时间"计。
    /// </summary>
    public sealed class ActionTimelineTests
    {
        private const double S = 200;
        private const double A = 100;
        private const double R = 300;

        private static JsonObject Slash(string id, string? next = null, double comboOpen = 300, double comboClose = 600) =>
            TlSkill(id, S, A, R, markers: new[] { Hit(250) }, combo: next == null ? null : ComboBlock(next, comboOpen, comboClose));

        private static JsonObject Dodge(string id = "skill.sample_dodge", double cooldown = 0) =>
            TlSkill(id, 50, 100, 100, cooldown: cooldown);

        private static TimelineHarness Make(IEnumerable<JsonObject> skills, string? preset = "feel.preset.arpg_responsive", bool hitResolver = true) =>
            Create(skills, b => b.Options.GcdEnabled = false, preset, hitResolver);

        private static void Cast(TimelineHarness h, string skill)
        {
            var r = h.World.Host.CastSkill(Actor, new Id(skill), Array.Empty<Id>());
            Assert.True(r.Success, "施法失败：" + r.Reason);
        }

        // ------------------------------------------------------------------ 1. 事件序列与相位 tick

        [Fact]
        public void Sequence_StartedPhaseChangedMarkerFinished_AtTicksDerivedFromPhaseMilliseconds()
        {
            var h = Make(new[] { Slash("skill.sample_slash") });
            var c = h.CastInTick("skill.sample_slash");
            var s = Ticks(S);
            var a = Ticks(A);
            var r = Ticks(R);
            var total = s + a + r;

            // 每个 tick 的动作经过时间 = 当前 tick - 开始 tick。
            for (var i = 1; i <= total - 1; i++)
            {
                h.Tick();
                Assert.Equal(i, h.ElapsedTicks);
            }

            Assert.Equal(ActionPhase.Recovery, h.Query.Current(Actor)!.Value.Phase);
            h.Tick();
            Assert.Null(h.Query.Current(Actor));

            var started = Assert.Single(h.Of<ActionStartedEvent>());
            Assert.Equal(c, started.Tick);
            Assert.Equal(total, started.Event.DurationTicks);
            Assert.Equal(0, started.Event.ComboIndex);
            Assert.Equal(new Id("skill.sample_slash"), started.Event.SkillId);

            var phases = h.Of<ActionPhaseChangedEvent>().Select(p => (p.Tick, p.Event.Phase)).ToList();
            Assert.Equal(
                new[] { (c, ActionPhase.Startup), (c + s, ActionPhase.Active), (c + s + a, ActionPhase.Recovery) }, phases);

            var hit = Assert.Single(h.Of<ActionMarkerEvent>().Where(m => m.Event.Name == "hit"));
            Assert.Equal(c + Ticks(250), hit.Tick);
            Assert.Equal("0", hit.Event.Args["segment"]);

            var finished = Assert.Single(h.Of<ActionFinishedEvent>());
            Assert.Equal(c + total, finished.Tick);
            Assert.Equal(started.Event.CastInstanceId, finished.Event.CastInstanceId);

            // 与既有施法事件同批：cast_start 与 action.started 同 tick，cast_success 与 action.finished 同 tick。
            Assert.Equal(c, h.Of<SkillCastStartEvent>().Single().Tick);
            Assert.Equal(c + total, h.Of<SkillCastSuccessEvent>().Single().Tick);
            Assert.Equal(total * Step, h.Of<SkillCastStartEvent>().Single().Event.CastTime, 9);
        }

        [Fact]
        public void Sequence_HitMarker_CallsHitResolverOncePerSegmentAtItsTick()
        {
            var h = Make(
                new[]
                {
                    TlSkill("skill.sample_multi", S, A, R, markers: new[] { Hit(220), Hit(260) }),
                });
            h.Hits.Settle = false;
            var c = h.CastInTick("skill.sample_multi");
            h.TickN(Ticks(S) + Ticks(A) + Ticks(R));

            Assert.Equal(
                new[] { (new Id("skill.sample_multi"), 0, 0), (new Id("skill.sample_multi"), 0, 1) }, h.Hits.Hits);
            var ticks = h.Of<ActionMarkerEvent>().Where(m => m.Event.Name == "hit").Select(m => m.Tick).ToList();
            Assert.Equal(new[] { c + Ticks(220), c + Ticks(260) }, ticks);
            // 未结算：缺省实现不介入时命中钩子替身没有结算，战斗宿主没有收到效果。
            Assert.Empty(h.World.Combat.ResolveCalls);
        }

        [Fact]
        public void DefaultHitResolver_ReusesInstantSettlement_OncePerHitMarker()
        {
            var h = Make(
                new[]
                {
                    TlSkill("skill.sample_multi", S, A, R, markers: new[] { Hit(220), Hit(260) }),
                },
                hitResolver: false);
            h.CastInTick("skill.sample_multi");
            Assert.Empty(h.World.Combat.ResolveCalls);
            h.TickN(Ticks(S) + Ticks(A) + Ticks(R));

            // 两个 hit 标记 → 两次 instant 模式结算，目标由目标选择链解析为对手。
            Assert.Equal(2, h.World.Combat.ResolveCalls.Count);
            Assert.All(h.World.Combat.ResolveCalls, call => Assert.Equal(Foe, call.TargetId));
        }

        // ------------------------------------------------------------------ 2. 速率重映射

        [Fact]
        public void Haste_ShortensEachPhaseByFactor_TickCountsFromCalibration()
        {
            const double hastePct = 100;
            var h = Create(
                new[] { Slash("skill.sample_slash") },
                b =>
                {
                    b.Options.GcdEnabled = false;
                    b.Options.HasteAffectsActionTime = true;
                    b.Options.HasteStat = HasteStat;
                });
            h.World.Stats.SetBase(Actor, HasteStat, hastePct);

            var factor = 1.0 / (1.0 + hastePct / 100.0);
            var s = Ticks(S * factor);
            var a = Ticks(A * factor);
            var r = Ticks(R * factor);

            var c = h.CastInTick("skill.sample_slash");
            h.TickN(s + a + r);

            var started = Assert.Single(h.Of<ActionStartedEvent>());
            Assert.Equal(s + a + r, started.Event.DurationTicks);
            Assert.True(started.Event.DurationTicks < Ticks(S) + Ticks(A) + Ticks(R));
            var phases = h.Of<ActionPhaseChangedEvent>().Select(p => (p.Tick, p.Event.Phase)).ToList();
            Assert.Equal(
                new[] { (c, ActionPhase.Startup), (c + s, ActionPhase.Active), (c + s + a, ActionPhase.Recovery) }, phases);
            // 判定标记按分相分段线性映射：250 毫秒 = 判定相的一半，落在重映射后判定相的同一比例处。
            var hitTick = Assert.Single(h.Of<ActionMarkerEvent>().Where(m => m.Event.Name == "hit")).Tick;
            Assert.Equal(c + Ticks((S + (250 - S) / A * A) * factor), hitTick);
            Assert.Equal(c + s + a + r, Assert.Single(h.Of<ActionFinishedEvent>()).Tick);
        }

        [Fact]
        public void Haste_ShortenedBelowMinActionMs_IsClampedToTheFloor()
        {
            var h = Create(
                new[] { TlSkill("skill.sample_quick", 160, 80, 0) },
                b =>
                {
                    b.Options.GcdEnabled = false;
                    b.Options.HasteAffectsActionTime = true;
                    b.Options.HasteStat = HasteStat;
                });
            h.World.Stats.SetBase(Actor, HasteStat, 100);

            Assert.True(h.Feel!.Resolve(Actor).Judging.TryGetNumber(FeelFieldNames.MinActionMs, out var floorMs));
            // 总 240 毫秒、系数 0.5 → 120 毫秒 < 下限 → 总时长取下限，三相按同一系数等比。
            var factor = floorMs / 240.0;
            Assert.True(factor > 0.5 && factor < 1.0);

            h.CastInTick("skill.sample_quick");
            var duration = Assert.Single(h.Of<ActionStartedEvent>()).Event.DurationTicks;
            Assert.Equal(Ticks(160 * factor) + Ticks(80 * factor), duration);
            Assert.NotEqual(Ticks(160 * 0.5) + Ticks(80 * 0.5), duration);
        }

        // ------------------------------------------------------------------ 3. 取消窗口

        private static IEnumerable<JsonObject> CancelSkills() => new[]
        {
            TlSkill("skill.sample_slash", S, A, R, markers: new[] { Hit(250) },
                cancelWindows: new[] { CancelWindow("dodge", 100, 250), CancelWindow("move", 200) }),
            Dodge(),
        };

        private static TimelineHarness MakeCancel()
        {
            var h = Make(CancelSkills());
            h.Binding.Map("input.dodge", "skill.sample_dodge");
            return h;
        }

        [Theory]
        [InlineData(3, 6)] // 窗口开启前入缓冲：窗口一开（动作时间 6）当 tick 取消。
        [InlineData(6, 6)] // 恰在窗口打开 tick。
        [InlineData(14, 14)] // 窗口最后一个打开的 tick（关闭点 15 不含）。
        public void BufferedDodge_InsideWindow_CancelsWithReasonAndStartsNewActionSameTick(int pushAtElapsed, int expectCancelAtElapsed)
        {
            var h = MakeCancel();
            var open = Ticks(100);
            var close = open + Ticks(250 - 100);
            Assert.Equal(6, open);
            Assert.Equal(15, close);

            var c = h.CastInTick("skill.sample_slash");
            while (h.TickIndex - c < pushAtElapsed - 1) h.Tick();
            h.Tick(() => h.Input.Push(Actor, new Id("input.dodge"), ActionClass.Dodge));
            while (h.Query.Current(Actor)?.SkillId == new Id("skill.sample_slash") && h.TickIndex - c < close + 2) h.Tick();

            var cancelled = Assert.Single(h.Of<ActionCancelledEvent>());
            Assert.Equal(c + expectCancelAtElapsed, cancelled.Tick);
            Assert.Equal(ActionCancelReason.CancelInto, cancelled.Event.Reason);
            Assert.Equal(new Id("skill.sample_dodge"), cancelled.Event.NextSkillId);

            var interrupted = Assert.Single(h.Of<SkillCastInterruptedEvent>());
            Assert.Equal(cancelled.Tick, interrupted.Tick);
            Assert.Equal("CANCELLED", interrupted.Event.Reason);
            Assert.Equal(new Id("skill.sample_slash"), interrupted.Event.SkillId);

            var started = h.Of<ActionStartedEvent>().Last();
            Assert.Equal(cancelled.Tick, started.Tick);
            Assert.Equal(new Id("skill.sample_dodge"), started.Event.SkillId);
            var now = h.Query.Current(Actor)!.Value;
            Assert.Equal(new Id("skill.sample_dodge"), now.SkillId);
            Assert.Equal(0, now.ElapsedTicks);
            Assert.Equal(ActionPhase.Startup, now.Phase);

            // 被取消的动作不再发 finished/cast_success；取消的缓冲记录已被消费。
            Assert.Empty(h.Of<ActionFinishedEvent>());
            Assert.Equal(0, h.Input.Pending);
            // 取消那一刻旧动作后续标记不再触发：重新累计到新动作结束前不应再出现旧技能的 hit。
            Assert.DoesNotContain(h.Of<ActionMarkerEvent>(), m => m.Event.Name == "hit" && m.Tick > cancelled.Tick);
        }

        [Theory]
        [InlineData(15)] // 关闭点（半开区间，不含）。
        [InlineData(16)]
        [InlineData(30)]
        public void BufferedDodge_OutsideWindow_DoesNotCancel(int pushAtElapsed)
        {
            var h = MakeCancel();
            var c = h.CastInTick("skill.sample_slash");
            while (h.TickIndex - c < pushAtElapsed - 1) h.Tick();
            h.Tick(() => h.Input.Push(Actor, new Id("input.dodge"), ActionClass.Dodge));

            Assert.Equal(new Id("skill.sample_slash"), h.Query.Current(Actor)!.Value.SkillId);
            Assert.False(h.Query.IsCancelOpen(Actor, ActionClass.Dodge));
            h.TickN(Ticks(S) + Ticks(A) + Ticks(R) - (pushAtElapsed));
            Assert.Empty(h.Of<ActionCancelledEvent>());
            Assert.Empty(h.Of<SkillCastInterruptedEvent>());
            Assert.Single(h.Of<ActionFinishedEvent>());
            Assert.Equal(1, h.Input.Pending); // 记录留在缓冲里（过期由输入缓冲自己负责）。
        }

        [Fact]
        public void IsCancelOpen_FollowsWindowTicks()
        {
            var h = MakeCancel();
            var c = h.CastInTick("skill.sample_slash");
            var seen = new List<(int Elapsed, bool Open)>();
            for (var i = 1; i <= 20; i++)
            {
                h.Tick();
                seen.Add((h.ElapsedTicks, h.Query.IsCancelOpen(Actor, ActionClass.Dodge)));
            }

            var open = Ticks(100);
            var close = open + Ticks(150);
            Assert.All(seen, e => Assert.Equal(e.Elapsed >= open && e.Elapsed < close, e.Open));
            Assert.False(h.Query.IsCancelOpen(Actor, ActionClass.Attack));
        }

        [Fact]
        public void CancelInto_RejectedByPipeline_KeepsCurrentActionAndReportsReason()
        {
            var skills = CancelSkills().Select(s => s).ToList();
            skills[1] = Dodge(cooldown: 5);
            var h = Make(skills);
            h.Binding.Map("input.dodge", "skill.sample_dodge");

            // 先放一次闪避并让它结束：冷却（5 秒）已起算。
            h.CastInTick("skill.sample_dodge");
            h.TickN(Ticks(50) + Ticks(100) + Ticks(100));
            Assert.Null(h.Query.Current(Actor));
            Assert.True(h.World.Host.GetCooldown(Actor, new Id("skill.sample_dodge")) > 0);

            var c = h.CastInTick("skill.sample_slash");
            while (h.TickIndex - c < 5) h.Tick();
            h.Tick(() => h.Input.Push(Actor, new Id("input.dodge"), ActionClass.Dodge, validTicks: 1000));
            h.TickN(8);

            Assert.Empty(h.Of<ActionCancelledEvent>());
            Assert.Equal(new Id("skill.sample_slash"), h.Query.Current(Actor)!.Value.SkillId);
            Assert.NotEmpty(h.Input.Rejections);
            Assert.All(h.Input.Rejections, x =>
            {
                Assert.Equal(CastFailureReason.OnCooldown.ToString(), x.Reason);
                Assert.True(x.TimeSolvable); // 剩余冷却（≈4.8 秒）短于记录剩余缓冲（1000 tick）。
            });
            // 探测验证不产生 cast_failed 事件。
            Assert.Empty(h.Of<SkillCastFailedEvent>());
        }

        [Fact]
        public void CancelInto_RejectedWithShortRemainingBuffer_IsNotTimeSolvable()
        {
            var skills = CancelSkills().ToList();
            skills[1] = Dodge(cooldown: 5);
            var h = Make(skills);
            h.Binding.Map("input.dodge", "skill.sample_dodge");
            h.CastInTick("skill.sample_dodge");
            h.TickN(Ticks(50) + Ticks(100) + Ticks(100));

            var c = h.CastInTick("skill.sample_slash");
            while (h.TickIndex - c < 5) h.Tick();
            h.Tick(() => h.Input.Push(Actor, new Id("input.dodge"), ActionClass.Dodge, validTicks: 2));
            h.TickN(4);

            var rejection = Assert.Single(h.Input.Rejections);
            Assert.False(rejection.TimeSolvable);
            Assert.Equal(0, h.Input.Pending); // 不可解的记录被丢弃。
        }

        [Fact]
        public void MoveIntent_CancelsOnlyInsideMoveWindow()
        {
            var h = MakeCancel();
            var c = h.CastInTick("skill.sample_slash");
            var moveOpen = Ticks(200);
            while (h.TickIndex - c < moveOpen - 2) h.Tick();
            h.World.Host.NotifyMoveIntent(Actor);
            h.World.Flush();
            Assert.NotNull(h.Query.Current(Actor));
            Assert.Empty(h.Of<ActionCancelledEvent>());

            h.TickN(2); // 到达窗口打开点。
            Assert.Equal(moveOpen, h.ElapsedTicks);
            h.World.Host.NotifyMoveIntent(Actor);
            h.World.Flush();
            var cancelled = Assert.Single(h.Of<ActionCancelledEvent>());
            Assert.Equal(ActionCancelReason.CancelInto, cancelled.Event.Reason);
            Assert.Null(cancelled.Event.NextSkillId);
            Assert.Null(h.Query.Current(Actor));
            Assert.Equal("CANCELLED", Assert.Single(h.Of<SkillCastInterruptedEvent>()).Event.Reason);
        }

        [Fact]
        public void CancelAction_Stagger_EmitsCancelledAndInterruptedWithoutReasonCode()
        {
            var h = MakeCancel();
            h.CastInTick("skill.sample_slash");
            h.TickN(3);
            h.World.Host.CancelAction(Actor, ActionCancelReason.Stagger);
            h.World.Flush();

            Assert.Equal(ActionCancelReason.Stagger, Assert.Single(h.Of<ActionCancelledEvent>()).Event.Reason);
            Assert.Null(Assert.Single(h.Of<SkillCastInterruptedEvent>()).Event.Reason);
            Assert.Null(h.Query.Current(Actor));
        }

        [Fact]
        public void ScaleZeroPreset_ClassicTurnBased_NeverOpensCancelOrComboWindows()
        {
            var skills = new[]
            {
                TlSkill("skill.sample_slash", S, A, R, cancelWindows: new[] { CancelWindow("dodge", 100, 250) },
                    combo: ComboBlock("skill.sample_slash2", 300, 600)),
                Slash("skill.sample_slash2"),
                Dodge(),
            };
            var h = Make(skills, preset: "feel.preset.rpg_classic");
            h.Binding.Map("input.dodge", "skill.sample_dodge");
            var c = h.CastInTick("skill.sample_slash");
            h.Input.Push(Actor, new Id("input.dodge"), ActionClass.Dodge);
            h.Input.Push(Actor, new Id("input.attack"), ActionClass.Attack);
            for (var i = 0; i < 20; i++)
            {
                h.Tick();
                Assert.False(h.Query.IsCancelOpen(Actor, ActionClass.Dodge));
            }

            h.TickN(Ticks(S) + Ticks(A) + Ticks(R) - 20);
            Assert.Empty(h.Of<ActionCancelledEvent>());
            Assert.Single(h.Of<ActionFinishedEvent>());
        }

        // ------------------------------------------------------------------ 4. 连招

        private static IEnumerable<JsonObject> ComboSkills() => new[]
        {
            Slash("skill.sample_slash1", "skill.sample_slash2"),
            Slash("skill.sample_slash2", "skill.sample_slash3"),
            Slash("skill.sample_slash3"),
        };

        [Fact]
        public void Combo_ThreeStages_AdvanceOnBufferedInputInsideWindow()
        {
            var h = Make(ComboSkills());
            var comboOpen = Ticks(300);
            var comboClose = Math.Min(Ticks(S) + Ticks(A) + Ticks(R), comboOpen + Ticks(600 - 300));

            // 第一段：连招窗口打开前入缓冲的 attack 记录，窗口一开当 tick 接续第二段。
            var c1 = h.CastInTick("skill.sample_slash1");
            while (h.TickIndex - c1 < 9) h.Tick();
            h.Tick(() => h.Input.Push(Actor, new Id("input.attack"), ActionClass.Attack));
            Assert.Equal(new Id("skill.sample_slash1"), h.Query.Current(Actor)!.Value.SkillId);
            while (h.Query.Current(Actor)?.SkillId == new Id("skill.sample_slash1")) h.Tick();

            var c2 = h.TickIndex;
            Assert.Equal(c1 + comboOpen, c2);
            var cancel1 = Assert.Single(h.Of<ActionCancelledEvent>());
            Assert.Equal(c2, cancel1.Tick);
            Assert.Equal(ActionCancelReason.CancelInto, cancel1.Event.Reason);
            Assert.Equal(new Id("skill.sample_slash2"), cancel1.Event.NextSkillId);
            var st2 = h.Of<ActionStartedEvent>().Last();
            Assert.Equal((c2, 1, new Id("skill.sample_slash2")), (st2.Tick, st2.Event.ComboIndex, st2.Event.SkillId));

            // 第二段：窗口内（动作时间 20）再输入一次 attack，接续第三段。
            while (h.TickIndex - c2 < 19) h.Tick();
            h.Tick(() => h.Input.Push(Actor, new Id("input.attack"), ActionClass.Attack));
            Assert.InRange(h.ElapsedTicks, 0, 0);
            var c3 = h.TickIndex;
            var st3 = h.Of<ActionStartedEvent>().Last();
            Assert.Equal((c3, 2, new Id("skill.sample_slash3")), (st3.Tick, st3.Event.ComboIndex, st3.Event.SkillId));
            Assert.Equal(c2 + 20, c3);
            Assert.InRange(20, comboOpen, comboClose - 1);

            // 第三段无 combo 块：跑到自然结束，不再接续，各段的 hit 按段号结算一次。
            h.TickN(Ticks(S) + Ticks(A) + Ticks(R));
            Assert.Null(h.Query.Current(Actor));
            Assert.Single(h.Of<ActionFinishedEvent>());
            Assert.Equal(
                new[]
                {
                    (new Id("skill.sample_slash1"), 0, 0), (new Id("skill.sample_slash2"), 1, 0), (new Id("skill.sample_slash3"), 2, 0),
                },
                h.Hits.Hits);
        }

        [Fact]
        public void Combo_InputOutsideWindow_DoesNotAdvance()
        {
            var h = Make(ComboSkills());
            var c = h.CastInTick("skill.sample_slash1");
            var open = Ticks(300);
            // 在动作时间 5（窗口外）入缓冲，但记录很快过期——窗口打开时已不可用，不接续。
            while (h.TickIndex - c < 4) h.Tick();
            h.Tick(() => h.Input.Push(Actor, new Id("input.attack"), ActionClass.Attack, validTicks: 3));
            while (h.TickIndex - c < open + 2) h.Tick();
            Assert.Equal(new Id("skill.sample_slash1"), h.Query.Current(Actor)!.Value.SkillId);
            Assert.Empty(h.Of<ActionCancelledEvent>());
        }

        [Theory]
        [InlineData(10, true)] // 重置时间之内：对根技能的再次施法接续链上第二段。
        [InlineData(36, true)] // 恰为 combo_reset_ms（600 毫秒 = 36 tick）：仍在之内。
        [InlineData(37, false)] // 超时：回到第一段。
        public void Combo_AfterNaturalFinish_ResetsAfterComboResetMs(int idleTicks, bool continues)
        {
            var h = Make(ComboSkills());
            Assert.True(h.Feel!.Resolve(Actor).Judging.TryGetNumber(FeelFieldNames.ComboResetMs, out var resetMs));
            var resetTicks = Ticks(resetMs);
            Assert.Equal(36, resetTicks);

            h.CastInTick("skill.sample_slash1");
            h.TickN(Ticks(S) + Ticks(A) + Ticks(R));
            Assert.Null(h.Query.Current(Actor));

            h.TickN(idleTicks - 1);
            h.CastInTick("skill.sample_slash1");
            var started = h.Of<ActionStartedEvent>().Last();
            var continued = idleTicks <= resetTicks;
            Assert.Equal(continues, continued);
            Assert.Equal(continued ? new Id("skill.sample_slash2") : new Id("skill.sample_slash1"), started.Event.SkillId);
            Assert.Equal(continued ? 1 : 0, started.Event.ComboIndex);
        }

        [Fact]
        public void Combo_ChainDoesNotSurviveStaggerOrDeath()
        {
            var h = Make(ComboSkills());
            h.CastInTick("skill.sample_slash1");
            h.TickN(4);
            h.World.Host.CancelAction(Actor, ActionCancelReason.Stagger);
            h.World.Flush();
            h.CastInTick("skill.sample_slash1");
            var started = h.Of<ActionStartedEvent>().Last();
            Assert.Equal(new Id("skill.sample_slash1"), started.Event.SkillId);
            Assert.Equal(0, started.Event.ComboIndex);
        }

        // ------------------------------------------------------------------ 5. 资源与冷却时刻

        [Theory]
        [InlineData(null, 0)] // 缺省 commit：施法 tick 当场扣。
        [InlineData("active", 1)]
        [InlineData("first_hit", 2)]
        public void CostAt_DeductsOnTheTickOfTheConfiguredMoment(string? costAt, int moment)
        {
            const double cost = 20;
            var h = Make(new[] { TlSkill("skill.sample_slash", S, A, R, markers: new[] { Hit(250) }, costAt: costAt, cost: cost) });
            var before = h.World.Powers.GetPower(Actor, Energy);
            var c = h.CastInTick("skill.sample_slash");
            var expectTick = c + (moment == 0 ? 0 : moment == 1 ? Ticks(S) : Ticks(250));

            var seen = new List<(int Tick, double Power)> { (c, h.World.Powers.GetPower(Actor, Energy)) };
            for (var i = 0; i < Ticks(S) + Ticks(A) + Ticks(R); i++)
            {
                h.Tick();
                seen.Add((h.TickIndex, h.World.Powers.GetPower(Actor, Energy)));
            }

            Assert.Equal(before, seen.First(e => e.Tick < expectTick || expectTick == c).Power + (expectTick == c ? cost : 0));
            Assert.All(seen.Where(e => e.Tick < expectTick), e => Assert.Equal(before, e.Power));
            Assert.All(seen.Where(e => e.Tick >= expectTick), e => Assert.Equal(before - cost, e.Power));
            if (moment == 2)
            {
                // 派生 cost 标记在首个 hit 之前到达。
                var names = h.Of<ActionMarkerEvent>().Select(m => m.Event.Name).ToList();
                Assert.True(names.IndexOf("cost") < names.IndexOf("hit"));
            }
        }

        [Fact]
        public void CostAtActive_NotDeductedWhenCancelledBeforeActive()
        {
            var skills = new[]
            {
                TlSkill("skill.sample_slash", S, A, R, costAt: "active", cost: 20,
                    cancelWindows: new[] { CancelWindow("dodge", 50, 150) }),
                Dodge(),
            };
            var h = Make(skills);
            h.Binding.Map("input.dodge", "skill.sample_dodge");
            var before = h.World.Powers.GetPower(Actor, Energy);
            h.CastInTick("skill.sample_slash");
            h.Tick(() => h.Input.Push(Actor, new Id("input.dodge"), ActionClass.Dodge));
            h.TickN(Ticks(100));
            Assert.Single(h.Of<ActionCancelledEvent>());
            Assert.Equal(before, h.World.Powers.GetPower(Actor, Energy));
        }

        [Theory]
        [InlineData("active")]
        [InlineData("finish")]
        [InlineData(null)]
        public void CooldownAt_StartsOnTheConfiguredMoment(string? cooldownAt)
        {
            var h = Make(new[] { TlSkill("skill.sample_slash", S, A, R, cooldownAt: cooldownAt, cooldown: 5) });
            var id = new Id("skill.sample_slash");
            h.CastInTick("skill.sample_slash");
            var total = Ticks(S) + Ticks(A) + Ticks(R);
            var startedAt = cooldownAt == null ? 0 : cooldownAt == "active" ? Ticks(S) : total;
            // 动作时间 1..total：冷却在 startedAt 之前不存在，之后有剩余。
            for (var e = 1; e <= total; e++)
            {
                h.Tick();
                var remaining = h.World.Host.GetCooldown(Actor, id);
                if (cooldownAt == null || e >= startedAt) Assert.True(remaining > 0, $"动作时间 {e} 应已有冷却");
                else Assert.Equal(0, remaining);
            }
        }

        // ------------------------------------------------------------------ 6. 顿帧与无敌/位移窗口

        [Fact]
        public void PausedActionClock_FreezesTimelineAndBufferPulls()
        {
            var h = MakeCancel();
            var c = h.CastInTick("skill.sample_slash");
            h.TickN(3);
            Assert.Equal(3, h.ElapsedTicks);

            h.Clock.SetPaused(Actor, true);
            Assert.True(h.Query.IsActionClockPaused(Actor));
            // 顿帧 20 tick：动作时钟不走，动作时间不增长；窗口内的记录也不被拉取。
            for (var i = 0; i < 20; i++)
            {
                h.Tick(advanceClock: false);
                Assert.Equal(3, h.ElapsedTicks);
            }

            h.Clock.SetPaused(Actor, false);
            Assert.False(h.Query.IsActionClockPaused(Actor));
            h.TickN(Ticks(100) - 3);
            Assert.Equal(Ticks(100), h.ElapsedTicks);
            // 阶段切换晚了整整 20 个模拟 tick（相对动作时钟不变）。
            var phaseActive = h.TickIndex;
            h.TickN(Ticks(S) - Ticks(100));
            Assert.Equal(c + Ticks(S) + 20, h.Of<ActionPhaseChangedEvent>().First(p => p.Event.Phase == ActionPhase.Active).Tick);
            Assert.True(phaseActive > c);
        }

        [Fact]
        public void InvulnerabilityAndMotionWindows_FollowMarkers()
        {
            var skill = TlSkill(
                "skill.sample_roll", 50, 150, 100,
                markers: new[] { Marker("invuln_start", 50), Marker("invuln_end", 150), Marker("motion_start", 50), Marker("motion_end", 150) });
            var h = Make(new[] { skill });
            var c = h.CastInTick("skill.sample_roll");
            var start = Ticks(50);
            var end = Ticks(150);
            for (var i = 1; i <= end + 2; i++)
            {
                h.Tick();
                var e = h.ElapsedTicks;
                Assert.Equal(e >= start && e < end, h.Query.IsInvulnerable(Actor));
            }

            Assert.False(h.Query.IsInvulnerable(Actor));
        }

        [Fact]
        public void ActionState_Motion_IsFilledFromDeclaredMotionBlock_WithCalibratedDistanceAndWindowTicks()
        {
            var timeline = new List<(string, JsonValue)>
            {
                ("startup_ms", J.N(50)), ("active_ms", J.N(150)), ("recovery_ms", J.N(100)),
                ("markers", J.A(Marker("motion_start", 50), Marker("motion_end", 150))),
                ("motion", J.O(("driver", J.S("code")), ("kind", J.S("dash")), ("distance", J.N(3)), ("direction", J.S("input_snapshot")))),
            };
            var skill = J.O(
                ("id", J.S("skill.sample_dash")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")), ("range", J.N(0)),
                ("cast_time", J.N(0.3)), ("respects_gcd", J.B(true)), ("target_shape_ref", J.S(Chain.Value)),
                ("effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(7)), ("coefficient", J.N(0))))))),
                ("timeline", J.O(timeline.ToArray())));
            var h = Make(new[] { skill, TlSkill("skill.sample_plain_tl", 100, 100, 100) });

            // 没有 motion 块的时间线动作：Motion 为 null。
            h.CastInTick("skill.sample_plain_tl");
            Assert.Null(h.Query.Current(Actor)!.Value.Motion);
            h.World.Host.CancelAction(Actor, ActionCancelReason.Cleared);

            var direction = new Vec2(0, 2); // 未归一的输入方向：快照落定为单位向量。
            h.World.Host.CastSkillWithContext(Actor, new Id("skill.sample_dash"), Array.Empty<Id>(), new ActionCastContext(direction, 0));
            var m = h.Query.Current(Actor)!.Value.Motion;
            Assert.NotNull(m);

            var start = Ticks(50);
            var end = Ticks(150);
            Assert.Equal(start, m!.Value.StartTick);
            Assert.Equal(end, m.Value.EndTick);
            Assert.Equal(ActionMotionKind.Dash, m.Value.Declaration.Kind);
            Assert.Equal(ActionMotionDirection.InputSnapshot, m.Value.Declaration.Direction);
            Assert.Equal(new Vec2(0, 1), m.Value.Direction);
            Assert.Null(m.Value.TargetId);
            // 距离：身高倍数 × 标定参考身高（框架测试标定参考身高 2.0）。
            Assert.Equal(3 * h.Feel!.Calibration.ReferenceHeight, m.Value.DistanceWorld, 9);
            Assert.NotEqual(3.0, m.Value.DistanceWorld);

            // 窗口随动作时间：逐 tick 比对 IsActiveAt 与 ActionState.ElapsedTicks。
            var activeTicks = 0;
            for (var i = 0; i < end + 2; i++)
            {
                h.Tick();
                var now = h.Query.Current(Actor);
                if (now == null) break;
                Assert.Equal(now.Value.ElapsedTicks >= start && now.Value.ElapsedTicks < end, now.Value.Motion!.Value.IsActiveAt(now.Value.ElapsedTicks));
                if (now.Value.Motion!.Value.IsActiveAt(now.Value.ElapsedTicks)) activeTicks++;
            }

            Assert.Equal(end - start, activeTicks);
        }

        // ------------------------------------------------------------------ 7. 兼容与确定性

        [Fact]
        public void SkillWithoutTimeline_BehavesAsBefore_NoActionEventsNoActionState()
        {
            var plain = J.O(
                ("id", J.S("skill.sample_plain")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")), ("range", J.N(0)),
                ("cast_time", J.N(0.5)), ("respects_gcd", J.B(true)), ("target_shape_ref", J.S(Chain.Value)),
                ("effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(7)), ("coefficient", J.N(0))))))));
            var h = Make(new[] { plain });
            h.Tick(() => Assert.True(h.World.Host.CastSkill(Actor, new Id("skill.sample_plain"), Array.Empty<Id>()).Success));
            Assert.Null(h.Query.Current(Actor));
            Assert.Empty(h.World.Combat.ResolveCalls);
            h.Tick(() => h.World.Host.Update(0.5));
            Assert.Single(h.World.Combat.ResolveCalls);
            Assert.Empty(h.Of<ActionStartedEvent>());
            Assert.Empty(h.Of<ActionFinishedEvent>());
            Assert.Single(h.Of<SkillCastSuccessEvent>());
        }

        [Fact]
        public void ReplayingTheSameScript_ProducesIdenticalEventStream()
        {
            string Run()
            {
                var skills = CancelSkills().Concat(ComboSkills()).ToList();
                var h = Make(skills);
                h.Binding.Map("input.dodge", "skill.sample_dodge");
                h.CastInTick("skill.sample_slash1");
                h.Tick(() => h.Input.Push(Actor, new Id("input.attack"), ActionClass.Attack));
                h.TickN(30);
                h.Tick(() => h.Input.Push(Actor, new Id("input.attack"), ActionClass.Attack));
                h.TickN(60);
                h.CastInTick("skill.sample_slash");
                h.TickN(6);
                h.Tick(() => h.Input.Push(Actor, new Id("input.dodge"), ActionClass.Dodge));
                h.TickN(40);
                return h.Fingerprint();
            }

            var first = Run();
            Assert.Contains("cancelled CancelInto", first);
            Assert.Equal(first, Run());
        }
    }
}
