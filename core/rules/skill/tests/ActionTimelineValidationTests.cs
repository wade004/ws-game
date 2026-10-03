using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Skill;
using Xunit;
using static Tests.Rules.Skill.TimelineHarness;

namespace Tests.Rules.Skill
{
    /// <summary><c>skill.def.timeline</c> 的加载期校验（<see cref="SkillTimelineRule"/>）与剪辑标记导入/一致性比对。</summary>
    public sealed class ActionTimelineValidationTests
    {
        private static ValidationReport Validate(params JsonObject[] skills)
        {
            var builder = new SkillWorldBuilder().ValidationRule(new SkillTimelineRule());
            foreach (var s in skills) builder.SkillDef(s);
            return builder.Validate();
        }


        [Fact]
        public void ValidTimeline_HasNoTimelineIssues()
        {
            var report = Validate(TlSkill("skill.sample_ok", 200, 100, 300, markers: new[] { Hit(250) },
                cancelWindows: new[] { CancelWindow("dodge", 100, 250) }));
            Assert.DoesNotContain(report.Issues, i => i.Check.StartsWith("timeline_"));
            Assert.False(report.IsBlocking);
        }

        [Fact]
        public void SkillWithoutTimeline_IsIgnoredByTheRule()
        {
            var plain = J.O(
                ("id", J.S("skill.sample_plain")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")), ("range", J.N(0)),
                ("cast_time", J.N(1.5)), ("respects_gcd", J.B(true)), ("target_shape_ref", J.S(Chain.Value)));
            var report = Validate(plain);
            Assert.DoesNotContain(report.Issues, i => i.Check.StartsWith("timeline_"));
        }

        [Fact]
        public void CastTimeMustEqualPhaseSum()
        {
            var report = Validate(TlSkill("skill.sample_bad", 200, 100, 300, castTimeSeconds: 1.0));
            var issue = Assert.Single(report.Issues, i => i.Check == "timeline_cast_time_mismatch");
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void ZeroTotalDuration_IsAnError()
        {
            var report = Validate(TlSkill("skill.sample_zero", 0, 0, 0, castTimeSeconds: 0));
            Assert.Contains(report.Issues, i => i.Check == "timeline_zero_duration" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void MarkerBeyondTotalDuration_IsAnError()
        {
            var report = Validate(TlSkill("skill.sample_bad", 200, 100, 300, markers: new[] { Hit(700) }));
            Assert.Contains(report.Issues, i => i.Check.StartsWith("timeline_marker") && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void UnpairedInvulnerabilityStart_IsFlagged()
        {
            var report = Validate(TlSkill("skill.sample_bad", 200, 100, 300, markers: new[] { Marker("invuln_start", 50) }));
            Assert.Contains(report.Issues, i => i.Check.StartsWith("timeline_invuln"));
        }

        [Fact]
        public void ArmorWindowMarkers_FollowTheSameRulesAsInvulnerability()
        {
            var unpaired = Validate(TlSkill("skill.sample_a1", 200, 100, 300, markers: new[] { Marker("armor_end", 150) }));
            Assert.Contains(unpaired.Issues, i => i.Check == "timeline_armor_unpaired" && i.Severity == ValidationSeverity.Error);

            var unterminated = Validate(TlSkill("skill.sample_a2", 200, 100, 300, markers: new[] { Marker("armor_start", 50) }));
            Assert.Contains(unterminated.Issues, i => i.Check == "timeline_armor_unterminated" && i.Severity == ValidationSeverity.Warning);

            var reversed = Validate(TlSkill("skill.sample_a3", 200, 100, 300, markers: new[] { Marker("armor_start", 150), Marker("armor_end", 50) }));
            Assert.Contains(reversed.Issues, i => i.Check == "timeline_armor_order" && i.Severity == ValidationSeverity.Error);

            var ok = Validate(TlSkill("skill.sample_a4", 200, 100, 300, markers: new[] { Marker("armor_start", 50), Marker("armor_end", 150) }));
            Assert.DoesNotContain(ok.Issues, i => i.Check.StartsWith("timeline_armor") || i.Check == "timeline_marker_unknown");
        }

        [Fact]
        public void GuardWindowMarkers_FollowTheSameRulesAsArmor()
        {
            var unpaired = Validate(TlSkill("skill.sample_g1", 200, 100, 300, markers: new[] { Marker("guard_end", 150) }));
            Assert.Contains(unpaired.Issues, i => i.Check == "timeline_guard_unpaired" && i.Severity == ValidationSeverity.Error);

            var unterminated = Validate(TlSkill("skill.sample_g2", 200, 100, 300, markers: new[] { Marker("guard_start", 50) }));
            Assert.Contains(unterminated.Issues, i => i.Check == "timeline_guard_unterminated" && i.Severity == ValidationSeverity.Warning);

            var reversed = Validate(TlSkill("skill.sample_g3", 200, 100, 300, markers: new[] { Marker("guard_start", 150), Marker("guard_end", 50) }));
            Assert.Contains(reversed.Issues, i => i.Check == "timeline_guard_order" && i.Severity == ValidationSeverity.Error);

            var ok = Validate(TlSkill("skill.sample_g4", 200, 100, 300, markers: new[] { Marker("guard_start", 50), Marker("guard_end", 150) }));
            Assert.DoesNotContain(ok.Issues, i => i.Check.StartsWith("timeline_guard") || i.Check == "timeline_marker_unknown");
        }

        [Fact]
        public void ComboNext_MustExistAndDeclareATimeline()
        {
            var missing = Validate(TlSkill("skill.sample_a", 200, 100, 300, combo: ComboBlock("skill.sample_nowhere", 300, 600)));
            Assert.Contains(missing.Issues, i => i.Check == "timeline_combo_next_missing" && i.Severity == ValidationSeverity.Error);

            var plain = J.O(
                ("id", J.S("skill.sample_plain")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")), ("range", J.N(0)),
                ("cast_time", J.N(0.1)), ("respects_gcd", J.B(true)), ("target_shape_ref", J.S(Chain.Value)));
            var noTimeline = Validate(TlSkill("skill.sample_a", 200, 100, 300, combo: ComboBlock("skill.sample_plain", 300, 600)), plain);
            Assert.Contains(noTimeline.Issues, i => i.Check == "timeline_combo_next_no_timeline");
        }

        [Fact]
        public void CancelWindow_OpenBeyondTotalOrCloseBeforeOpen_IsAnError_CloseBeyondTotalIsClamped()
        {
            var open = Validate(TlSkill("skill.sample_bad", 200, 100, 300, cancelWindows: new[] { CancelWindow("dodge", 900) }));
            Assert.Contains(open.Issues, i => i.Check == "timeline_window_out_of_range" && i.Severity == ValidationSeverity.Error);

            var order = Validate(TlSkill("skill.sample_bad", 200, 100, 300, cancelWindows: new[] { CancelWindow("dodge", 300, 100) }));
            Assert.Contains(order.Issues, i => i.Check == "timeline_window_order" && i.Severity == ValidationSeverity.Error);

            var clamped = Validate(TlSkill("skill.sample_ok", 200, 100, 300, cancelWindows: new[] { CancelWindow("dodge", 300, 900) }));
            Assert.DoesNotContain(clamped.Issues, i => i.Check.StartsWith("timeline_window"));
        }

        [Fact]
        public void EffectsWithoutHitMarker_IsAWarningNotBlocking()
        {
            var report = Validate(TlSkill("skill.sample_nohit", 200, 100, 300));
            var warn = Assert.Single(report.Issues, i => i.Check == "timeline_effects_without_hit");
            Assert.Equal(ValidationSeverity.Warning, warn.Severity);
            Assert.False(report.IsBlocking);
        }

        // ------------------------------------------------------------------ 剪辑标记

        private static ClipMarkerSet Clip(double totalMs = 600) => new ClipMarkerSet(
            "clip.sample_slash", totalMs,
            new[]
            {
                new ClipEvent("active_start", 200 / totalMs), new ClipEvent("hit:0", 220 / totalMs), new ClipEvent("hit:1", 260 / totalMs),
                new ClipEvent("active_end", 300 / totalMs), new ClipEvent("combo_open", 300 / totalMs), new ClipEvent("combo_close", 600 / totalMs),
                new ClipEvent("cancel_open:dodge", 100 / totalMs), new ClipEvent("trail_start", 210 / totalMs),
            });

        [Fact]
        public void ClipImporter_CopiesArmorWindowMarkersLikeInvulnerability()
        {
            var t = TimelineClipImporter.Import(new ClipMarkerSet("clip.sample_armor", 500, new[]
            {
                new ClipEvent("armor_start", 0.2), new ClipEvent("armor_end", 0.6), new ClipEvent("invuln_start", 0.1), new ClipEvent("invuln_end", 0.3),
            }));
            Assert.Equal(new[] { "armor_start", "armor_end", "invuln_start", "invuln_end" }, t.Markers.Select(m => m.Name));
            Assert.Equal(new[] { 100.0, 300.0, 50.0, 150.0 }, t.Markers.Select(m => System.Math.Round(m.AtMs, 6)));
            Assert.DoesNotContain(t.Notes, n => n.Contains("armor"));
        }

        [Fact]
        public void ClipImporter_DerivesPhasesMarkersAndWindows()
        {
            var t = TimelineClipImporter.Import(Clip());
            Assert.Equal(200, t.StartupMs, 6);
            Assert.Equal(100, t.ActiveMs, 6);
            Assert.Equal(300, t.RecoveryMs, 6);
            Assert.Equal(new[] { "0", "1" }, t.Markers.Select(m => m.Args["segment"]));
            Assert.Equal(new[] { 220.0, 260.0 }, t.Markers.Select(m => System.Math.Round(m.AtMs, 6)));
            Assert.All(t.Markers, m => Assert.Equal("hit", m.Name));
            var win = Assert.Single(t.CancelWindows);
            Assert.Equal(100, win.OpenMs, 6);
            Assert.Null(win.CloseMs);
            Assert.Equal(300, t.ComboOpenMs!.Value, 6);
            Assert.Equal(600, t.ComboCloseMs!.Value, 6);
            Assert.Empty(t.Notes); // 表现类事件（trail_*）静默忽略。
        }

        [Fact]
        public void ClipImporter_MissingActiveEvents_NotesAndDefaults()
        {
            var t = TimelineClipImporter.Import(new ClipMarkerSet("c", 500, new[] { new ClipEvent("hit", 0.4) }));
            Assert.Equal(0, t.StartupMs);
            Assert.Equal(500, t.ActiveMs);
            Assert.Equal(0, t.RecoveryMs);
            Assert.Equal(2, t.Notes.Count);
        }

        private static JsonObject SlashWithSource(string source, double startup = 200, double hit1 = 260)
        {
            var timeline = J.O(
                ("source", J.S(source)), ("startup_ms", J.N(startup)), ("active_ms", J.N(100)), ("recovery_ms", J.N(300)),
                ("markers", J.A(Hit(220), Hit(hit1))),
                ("cancel_windows", J.A(CancelWindow("dodge", 100))),
                ("combo", ComboBlock("skill.sample_slash", 300, 600)));
            return J.O(
                ("id", J.S("skill.sample_slash")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")), ("range", J.N(0)),
                ("cast_time", J.N((startup + 400) / 1000.0)), ("respects_gcd", J.B(true)), ("target_shape_ref", J.S(Chain.Value)),
                ("effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(7)), ("coefficient", J.N(0))))))),
                ("timeline", timeline));
        }

        private static ValidationReport Consistency(JsonObject skill, ClipMarkerSet? clip, double toleranceMs = 50)
        {
            var source = new InMemoryClipMarkerSource();
            if (clip != null) source.Add(new Id("skill.sample_slash"), clip);
            return new SkillWorldBuilder().SkillDef(skill).ValidationRule(new TimelineClipConsistencyRule(source, toleranceMs)).Validate();
        }

        [Fact]
        public void ClipSource_ExactCopy_Passes_AndAnyDeviationIsAnError()
        {
            Assert.DoesNotContain(Consistency(SlashWithSource("clip"), Clip()).Issues, i => i.Check.StartsWith("timeline_clip"));

            var drift = Consistency(SlashWithSource("clip", hit1: 280), Clip());
            var issue = Assert.Single(drift.Issues, i => i.Check == "timeline_clip_mismatch");
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
        }

        [Fact]
        public void ClipSource_WithoutClip_IsAnError_ButDataSourceIsSkipped()
        {
            Assert.Contains(Consistency(SlashWithSource("clip"), null).Issues, i => i.Check == "timeline_clip_missing");
            Assert.DoesNotContain(Consistency(SlashWithSource("data"), null).Issues, i => i.Check.StartsWith("timeline_clip"));
        }

        [Fact]
        public void DataSource_DeviationWithinToleranceIsSilent_BeyondIsAWarning()
        {
            // 标定 marker_tolerance_ms = 50：偏差 40 毫秒不报，偏差 80 毫秒给警告（不阻断）。
            var within = Consistency(SlashWithSource("data", hit1: 300), Clip(), toleranceMs: 50);
            Assert.DoesNotContain(within.Issues, i => i.Check.StartsWith("timeline_clip"));

            var beyond = Consistency(SlashWithSource("data", hit1: 340), Clip(), toleranceMs: 50);
            var warn = Assert.Single(beyond.Issues, i => i.Check == "timeline_clip_deviation");
            Assert.Equal(ValidationSeverity.Warning, warn.Severity);
            Assert.False(beyond.IsBlocking, string.Join("; ", beyond.Issues.Select(i => i.ToString())));
        }
    }
}
