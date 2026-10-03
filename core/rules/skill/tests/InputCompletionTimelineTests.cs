using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.InputMap;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;
using static Tests.Rules.Skill.SpatialRig;
using static Tests.Rules.Skill.TimelineHarness;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 输入层补全（ADR-0143）在动作时间线上的两个机制：取消/连招窗口的 <c>requires</c>/<c>into</c> 条件，以及按住维持
    /// （<c>timeline.active_until_release</c>）。每个机制一条复现、一条不变量；期望 tick 一律由 <c>Ticks(ms)</c> 与规则字段算出。
    /// </summary>
    public sealed class InputCompletionTimelineTests
    {
        private const double S = 50;
        private const double A = 100;
        private const double R = 400;
        private static readonly Id Slash = new Id("skill.sample_slash");
        private static readonly Id Dodge1 = new Id("skill.sample_dodge");
        private static readonly Id Dodge2 = new Id("skill.sample_dodge2");
        private static readonly Id InputDodge1 = new Id("input.dodge");
        private static readonly Id InputDodge2 = new Id("input.dodge2");
        private static readonly Id InputHold = new Id("input.hold");

        private static JsonObject Sk(
            string id, double startup, double active, double recovery, IEnumerable<JsonValue>? markers = null,
            IEnumerable<JsonValue>? cancelWindows = null, JsonObject? combo = null, double? sustainMaxMs = null)
        {
            var timeline = new List<(string, JsonValue)>
            {
                ("startup_ms", J.N(startup)), ("active_ms", J.N(active)), ("recovery_ms", J.N(recovery)),
                ("markers", new JsonArray(markers ?? Array.Empty<JsonValue>())),
            };
            if (cancelWindows != null) timeline.Add(("cancel_windows", new JsonArray(cancelWindows)));
            if (combo != null) timeline.Add(("combo", combo));
            if (sustainMaxMs.HasValue) timeline.Add(("active_until_release", J.O(("max_ms", J.N(sustainMaxMs.Value)))));
            return J.O(
                ("id", J.S(id)), ("school", J.S("skill.school_sample")), ("kind", J.S("active")), ("range", J.N(0)),
                ("cast_time", J.N((startup + active + recovery) / 1000.0)), ("respects_gcd", J.B(true)),
                ("cooldown_duration", J.N(0)), ("target_shape_ref", J.S("target.chain.sample")),
                ("timeline", J.O(timeline.ToArray())),
                ("effects", new JsonArray(new[] { Damage() })));
        }

        private static JsonValue Window(string requires, string[]? into = null)
        {
            var fields = new List<(string, JsonValue)>
            {
                ("class", J.S("dodge")), ("open_ms", J.N(S)), ("requires", J.S(requires)),
            };
            if (into != null) fields.Add(("into", new JsonArray(into.Select(x => (JsonValue)J.S(x)).ToArray())));
            return J.O(fields.ToArray());
        }

        private static SpatialRig Rig(IEnumerable<JsonObject> skills)
        {
            var rig = Create(skills, Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.H.Binding.Map(InputDodge1.Value, Dodge1.Value).Map(InputDodge2.Value, Dodge2.Value);
            return rig;
        }

        private static JsonObject DodgeSkill(string id) => Sk(id, 50, 50, 100);

        /// <summary>施放斩击，推进到 <paramref name="ticks"/> 个动作 tick 之后（动作时间 0 = 施法所在 tick）。</summary>
        private static int CastAndAdvance(SpatialRig rig, int ticks)
        {
            var c = rig.H.CastInTick(Slash.Value);
            while (rig.H.TickIndex - c < ticks) rig.H.Tick();
            return c;
        }

        private static bool DodgeCancelled(SpatialRig rig) => rig.H.Of<ActionCancelledEvent>().Any(e => e.Event.Reason == ActionCancelReason.CancelInto);

        private static void PushDodge(SpatialRig rig, Id input) =>
            rig.H.Tick(() => rig.H.Input.Push(TimelineHarness.Actor, input, ActionClass.Dodge));

        // ---------------------------------------------------------------- requires

        [Theory]
        [InlineData("hit", true, true)] // 命中后：hit 窗口开，取消发生
        [InlineData("hit", false, false)] // 挥空：hit 窗口不开
        [InlineData("whiff", true, false)] // 命中后：whiff 窗口不开
        [InlineData("whiff", false, true)] // 挥空：whiff 窗口开
        [InlineData("any", true, true)] // 缺省语义：无条件
        [InlineData("any", false, true)]
        public void CancelWindowRequires_OpensOnlyWhenTheHitConditionHolds(string requires, bool foeInRange, bool expectCancel)
        {
            var rig = Rig(new[]
            {
                Sk(Slash.Value, S, A, R, new[] { HitAt(S + 10) }, new[] { Window(requires) }),
                DodgeSkill(Dodge1.Value), DodgeSkill(Dodge2.Value),
            });
            rig.Units.SetPosition(Foe, foeInRange ? new Vec2(1, 0) : new Vec2(10, 0));

            // 推进到判定相之后（命中确认已发生或确定不会发生），再入缓冲。
            CastAndAdvance(rig, Ticks(S) + Ticks(A) + 2);
            Assert.Equal(foeInRange ? 1 : 0, rig.Hits.Count());
            PushDodge(rig, InputDodge1);
            rig.H.TickN(2);

            Assert.Equal(expectCancel, DodgeCancelled(rig));
            Assert.Equal(expectCancel, !rig.H.Input.PendingActions.Contains(InputDodge1));
        }

        [Fact]
        public void CancelWindowRequires_QueryMatchesTheDecision()
        {
            var rig = Rig(new[]
            {
                Sk(Slash.Value, S, A, R, new[] { HitAt(S + 10) }, new[] { Window("hit") }),
                DodgeSkill(Dodge1.Value),
            });
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            CastAndAdvance(rig, Ticks(S)); // 窗口刚开、命中标记（S+10ms）之前：命中条件未满足
            Assert.False(rig.H.Query.IsCancelOpen(TimelineHarness.Actor, ActionClass.Dodge));
            rig.H.TickN(Ticks(A) + 2);
            Assert.True(rig.H.Query.IsCancelOpen(TimelineHarness.Actor, ActionClass.Dodge));
        }

        // ---------------------------------------------------------------- into

        [Fact]
        public void CancelWindowInto_OnlyWhitelistedTargetsMayCancelIn()
        {
            var rig = Rig(new[]
            {
                Sk(Slash.Value, S, A, R, null, new[] { Window("any", new[] { Dodge2.Value }) }),
                DodgeSkill(Dodge1.Value), DodgeSkill(Dodge2.Value),
            });

            // 映射到白名单之外技能的动作：窗口不接。
            CastAndAdvance(rig, Ticks(S) + 2);
            PushDodge(rig, InputDodge1);
            rig.H.TickN(2);
            Assert.False(DodgeCancelled(rig));

            // 同类别、映射到白名单技能的动作：接。
            PushDodge(rig, InputDodge2);
            rig.H.TickN(2);
            var cancelled = Assert.Single(rig.H.Of<ActionCancelledEvent>());
            Assert.Equal(Dodge2, cancelled.Event.NextSkillId);
        }

        [Fact]
        public void CancelWindowInto_Absent_KeepsLegacyBehavior_AnyMappedSkillOfTheClassMayCancelIn()
        {
            var rig = Rig(new[]
            {
                Sk(Slash.Value, S, A, R, null, new[] { Window("any") }),
                DodgeSkill(Dodge1.Value), DodgeSkill(Dodge2.Value),
            });
            CastAndAdvance(rig, Ticks(S) + 2);
            PushDodge(rig, InputDodge1);
            rig.H.TickN(2);
            Assert.Equal(Dodge1, Assert.Single(rig.H.Of<ActionCancelledEvent>()).Event.NextSkillId);
        }

        // ---------------------------------------------------------------- combo requires

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ComboRequiresHit_ChainsOnlyAfterAHit(bool foeInRange)
        {
            var next = Sk("skill.sample_slash2", S, A, R, new[] { HitAt(S + 10) });
            var combo = J.O(("next", J.S("skill.sample_slash2")), ("open_ms", J.N(S + A)), ("close_ms", J.N(S + A + R)), ("requires", J.S("hit")));
            var rig = Rig(new[] { Sk(Slash.Value, S, A, R, new[] { HitAt(S + 10) }, null, combo), next });
            rig.H.Binding.Map("input.attack", Slash.Value);
            rig.Units.SetPosition(Foe, foeInRange ? new Vec2(1, 0) : new Vec2(10, 0));

            CastAndAdvance(rig, Ticks(S) + Ticks(A) + 2);
            rig.H.Tick(() => rig.H.Input.Push(TimelineHarness.Actor, new Id("input.attack"), ActionClass.Attack));
            rig.H.TickN(2);

            var chained = rig.H.Of<ActionStartedEvent>().Any(e => e.Event.SkillId == new Id("skill.sample_slash2"));
            Assert.Equal(foeInRange, chained);
        }

        // ---------------------------------------------------------------- 加载期校验

        private static Core.Foundation.DataRegistry.ValidationReport Validate(params JsonObject[] skills)
        {
            var builder = new SkillWorldBuilder().ValidationRule(new SkillTimelineRule());
            foreach (var skill in skills) builder.SkillDef(skill);
            return builder.Validate();
        }

        [Fact]
        public void Validation_SustainNeedsPositiveMaxAndActivePhase_AndHitRequiresWithoutHitMarkerWarns()
        {
            var badMax = Validate(Sk("skill.sample_a", S, A, R, null, null, null, sustainMaxMs: 0));
            Assert.Contains(badMax.Issues, i => i.Check == "timeline_sustain_max");
            Assert.True(badMax.IsBlocking);

            var noActive = Validate(Sk("skill.sample_b", S, 0, R, null, null, null, sustainMaxMs: 300));
            Assert.Contains(noActive.Issues, i => i.Check == "timeline_sustain_no_active");

            var hitWithoutMarker = Validate(Sk("skill.sample_c", S, A, R, null, new[] { Window("hit") }));
            Assert.Contains(hitWithoutMarker.Issues, i => i.Check == "timeline_window_requires_without_hit" && i.Severity == Core.Foundation.DataRegistry.ValidationSeverity.Warning);

            // 不变量：声明合法的维持与带命中标记的 hit 窗口没有任何新增诊断。
            var ok = Validate(
                Sk("skill.sample_d", S, A, R, new[] { HitAt(S + 10) }, new[] { Window("hit") }, null, sustainMaxMs: 300));
            Assert.DoesNotContain(ok.Issues, i => i.Check == "timeline_sustain_max" || i.Check == "timeline_sustain_no_active"
                || i.Check == "timeline_window_requires_without_hit");
        }

        // ---------------------------------------------------------------- 按住维持

        private static SpatialRig SustainRig(double sustainMaxMs, bool declare = true)
        {
            var rig = Rig(new[] { Sk(Slash.Value, S, A, R, null, null, null, declare ? sustainMaxMs : (double?)null) });
            rig.H.Input.Held.Clear();
            return rig;
        }

        private static int CastHeld(SpatialRig rig, bool trigger = true)
        {
            rig.H.Tick(() =>
            {
                if (trigger) rig.H.Input.Held.Add(InputHold);
                var ctx = new ActionCastContext(null, 0, trigger ? InputHold : (Id?)null);
                var r = rig.H.World.Host.CastSkillWithContext(TimelineHarness.Actor, Slash, Array.Empty<Id>(), ctx);
                Assert.True(r.Success);
            });
            return rig.H.TickIndex;
        }

        [Fact]
        public void Sustain_HoldingPastActive_FreezesAtActiveTail_ThenResumesOnRelease_ShiftingTheEndByExactlyTheSustainedTicks()
        {
            var baseline = SustainRig(0, declare: false);
            var c0 = CastHeld(baseline, trigger: false);
            baseline.RunToEnd();
            var normalTotal = baseline.H.TickIndex - c0;

            var rig = SustainRig(2000);
            var c = CastHeld(rig);
            var holdTick = Ticks(S) + Ticks(A) - 1;
            while (rig.H.TickIndex - c < holdTick + 8) rig.H.Tick();

            // 键仍按着：动作停在判定相最后一个 tick，仍是判定相，维持标记发出一次。
            Assert.True(rig.H.Query.IsSustained(TimelineHarness.Actor));
            Assert.Equal(holdTick, rig.H.ElapsedTicks);
            Assert.Equal(ActionPhase.Active, rig.H.Query.Current(TimelineHarness.Actor)!.Value.Phase);
            Assert.Single(rig.H.Of<ActionMarkerEvent>().Where(m => m.Event.Name == "sustain_start"));

            // 抬起后继续：动作结束时刻 = 无维持时的结束时刻 + 实际维持的 tick 数。
            rig.H.Input.Held.Clear();
            rig.RunToEnd();
            var end = rig.H.Of<ActionFinishedEvent>().Single().Tick;
            Assert.False(rig.H.Query.IsSustained(TimelineHarness.Actor));
            var start = Assert.Single(rig.H.Of<ActionMarkerEvent>().Where(m => m.Event.Name == "sustain_start"));
            var stop = Assert.Single(rig.H.Of<ActionMarkerEvent>().Where(m => m.Event.Name == "sustain_end"));
            Assert.Equal("release", stop.Event.Args["reason"]);
            var sustained = stop.Tick - start.Tick;
            Assert.True(sustained >= 8);
            Assert.Equal(normalTotal + sustained, end - c);
        }

        [Fact]
        public void Sustain_ReachesMaxMs_EndsWithReasonMax_AndNeverLongerThanMax()
        {
            const double maxMs = 300;
            var rig = SustainRig(maxMs);
            var c = CastHeld(rig);
            rig.RunToEnd(); // 键一直按着

            var markers = rig.H.Of<ActionMarkerEvent>().Where(m => m.Event.Name.StartsWith("sustain_")).ToList();
            Assert.Equal(new[] { "sustain_start", "sustain_end" }, markers.Select(m => m.Event.Name).ToArray());
            Assert.Equal("max", markers[1].Event.Args["reason"]);
            // 维持时长不超过 max_ms 换算的 tick 数（含起止 tick 的 ±1 边界）。
            Assert.InRange(markers[1].Tick - markers[0].Tick, Ticks(maxMs) - 1, Ticks(maxMs) + 1);
            Assert.Single(rig.H.Of<ActionFinishedEvent>());
        }

        [Fact]
        public void Sustain_ReleasedBeforeActiveEnds_OrNoTriggerAction_BehavesExactlyLikeAFixedLengthAction()
        {
            var baseline = SustainRig(0, declare: false);
            var c0 = CastHeld(baseline, trigger: false);
            baseline.RunToEnd();
            var normalTotal = baseline.H.Of<ActionFinishedEvent>().Single().Tick - c0;

            // 判定相结束时键已经抬起：不维持，时长与定长动作逐位一致，也没有维持标记。
            var early = SustainRig(2000);
            var c1 = CastHeld(early);
            early.H.Input.Held.Clear();
            early.RunToEnd();
            Assert.Equal(normalTotal, early.H.Of<ActionFinishedEvent>().Single().Tick - c1);
            Assert.DoesNotContain(early.H.Of<ActionMarkerEvent>(), m => m.Event.Name.StartsWith("sustain_"));

            // 不是由输入动作触发（没有 trigger_action，如 AI/脚本直接施法）：不维持。
            var direct = SustainRig(2000);
            var c2 = CastHeld(direct, trigger: false);
            direct.RunToEnd();
            Assert.Equal(normalTotal, direct.H.Of<ActionFinishedEvent>().Single().Tick - c2);
            Assert.DoesNotContain(direct.H.Of<ActionMarkerEvent>(), m => m.Event.Name.StartsWith("sustain_"));
        }

        [Fact]
        public void Sustain_ClockPause_DoesNotAccumulateSustainTicks()
        {
            const double maxMs = 300;
            var rig = SustainRig(maxMs);
            var c = CastHeld(rig);
            var holdTick = Ticks(S) + Ticks(A) - 1;
            while (!rig.H.Query.IsSustained(TimelineHarness.Actor)) rig.H.Tick();
            var startedSustain = rig.H.TickIndex;

            // 顿帧 20 个 tick：动作时钟不走，维持时长不增长。
            for (var i = 0; i < 20; i++) rig.H.Tick(advanceClock: false);
            Assert.True(rig.H.Query.IsSustained(TimelineHarness.Actor));
            Assert.Equal(holdTick, rig.H.ElapsedTicks);

            rig.RunToEnd();
            var markers = rig.H.Of<ActionMarkerEvent>().Where(m => m.Event.Name.StartsWith("sustain_")).ToList();
            // 维持的动作时钟时长仍不超过 max_ms；墙钟比它多出恰好 20 个顿帧 tick。
            Assert.InRange(markers[1].Tick - markers[0].Tick - 20, Ticks(maxMs) - 1, Ticks(maxMs) + 1);
            Assert.Equal(startedSustain, markers[0].Tick);
            _ = c;
        }
    }
}
