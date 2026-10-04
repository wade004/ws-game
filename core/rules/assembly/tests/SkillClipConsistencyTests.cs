// M5-S4（ADR-0147，手感设计/01 第 3.2 节）：时间线与剪辑标记一致性的生产来源与校验。
// 复现：技能经武器表现（普攻/施法覆盖）对应到姿势集剪辑，剪辑事件 x 总时长 = 毫秒，与 skill.def.timeline 比对；
// 不变量：期望毫秒由剪辑的百分比与总时长算出；没有总时长的剪辑取不到标记；施法覆盖优先于普攻。
using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Rules.Assembly;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Assembly
{
    public sealed class SkillClipConsistencyTests
    {
        private const double TotalMs = 800;
        private const double ActiveStartPct = 0.25;
        private const double HitPct = 0.3125;
        private const double ActiveEndPct = 0.5;

        private static IDataRegistryView Build(bool withDuration, bool overrideBasic = true, string skillTimeline = "")
        {
            var duration = withDuration ? "\"duration_ms\": 800," : string.Empty;
            var source = new InMemoryDataSource()
                .Add("display.anim_set", @"{ ""table"": ""display.anim_set"", ""schema_version"": 1, ""rows"": [
                    { ""id"": ""display.anim_set.t1"", ""clips"": {
                        ""attack"": { ""resource_ref"": ""anim.t_slash"", " + duration + @" ""events"": [
                            { ""name"": ""active_start"", ""time_pct"": 0.25 }, { ""name"": ""hit"", ""time_pct"": 0.3125 }, { ""name"": ""active_end"", ""time_pct"": 0.5 },
                            { ""name"": ""footstep"", ""time_pct"": 0.1 } ] },
                        ""cast"": { ""resource_ref"": ""anim.t_cast"", " + duration + @" ""events"": [
                            { ""name"": ""active_start"", ""time_pct"": 0.5 }, { ""name"": ""release"", ""time_pct"": 0.625 }, { ""name"": ""active_end"", ""time_pct"": 0.75 } ] } } } ] }")
                .Add("display.weapon_style", @"{ ""table"": ""display.weapon_style"", ""schema_version"": 1, ""rows"": [
                    { ""id"": ""display.weapon_style.t_sword"", ""auto_attack_anim"": ""anim.t_slash"",
                      ""cast_anim_override"": { ""skill.t_spell"": ""anim.t_cast""" + (overrideBasic ? @", ""skill.t_basic"": ""anim.t_cast""" : string.Empty) + @" } } ] }")
                .Add("display.map", @"{ ""table"": ""display.map"", ""schema_version"": 1, ""rows"": [
                    { ""id"": ""display.map.t_sword"", ""weapon_style_ref"": ""display.weapon_style.t_sword"" } ] }")
                .Add("feel.weapon", @"{ ""table"": ""feel.weapon"", ""schema_version"": 1, ""rows"": [
                    { ""id"": ""feel.weapon.t_sword"", ""auto_attack_timeline_ref"": ""skill.t_basic"" } ] }")
                .Add("item.template", @"{ ""table"": ""item.template"", ""schema_version"": 1, ""rows"": [
                    { ""id"": ""item.t_sword"", ""feel_weapon_ref"": ""feel.weapon.t_sword"", ""display_ref"": ""display.map.t_sword"" } ] }");
            if (skillTimeline.Length > 0)
            {
                source.Add("skill.def", skillTimeline);
            }

            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.LoadAll();
            return registry;
        }

        [Fact]
        public void Source_MapsSkillsThroughWeaponStyles_AndConvertsEventPercentagesWithTheClipDuration()
        {
            var source = new DisplayClipMarkerSource(Build(withDuration: true));

            // 施法覆盖：skill.t_spell -> anim.t_cast；普攻：skill.t_basic -> anim.t_slash，但同时被施法覆盖指到 anim.t_cast，覆盖优先。
            Assert.True(source.TryGetClip(new Id("skill.t_spell"), out var spell));
            Assert.Equal(TotalMs, spell.TotalMs);
            Assert.True(source.TryGetClip(new Id("skill.t_basic"), out var basic));
            Assert.Equal("cast", basic.ClipName);

            var import = TimelineClipImporter.Import(spell);
            Assert.Equal(0.5 * TotalMs, import.StartupMs, 6);                 // active_start x 总时长
            Assert.Equal((0.75 - 0.5) * TotalMs, import.ActiveMs, 6);
            Assert.Equal(TotalMs - 0.75 * TotalMs, import.RecoveryMs, 6);
            Assert.Contains(import.Markers, m => m.Name == "release" && Math.Abs(m.AtMs - 0.625 * TotalMs) < 1e-6);
        }

        [Fact]
        public void Source_ClipsWithoutDuration_AreNotAvailable()
        {
            var source = new DisplayClipMarkerSource(Build(withDuration: false));
            Assert.False(source.TryGetClip(new Id("skill.t_spell"), out _));
        }

        [Fact]
        public void Source_AutoAttackSkillWithoutOverride_UsesTheWeaponStylesAutoAttackClip()
        {
            // 普攻技能经 item.feel_weapon_ref -> feel.weapon.auto_attack_timeline_ref 与 item.display_ref -> display.map.weapon_style_ref ->
            // display.weapon_style.auto_attack_anim 取到 anim.t_slash（施法覆盖没有指到它时）。
            var source = new DisplayClipMarkerSource(Build(withDuration: true, overrideBasic: false));
            Assert.True(source.TryGetClip(new Id("skill.t_basic"), out var basic));
            Assert.Equal("attack", basic.ClipName);
            var import = TimelineClipImporter.Import(basic);
            Assert.Equal(ActiveStartPct * TotalMs, import.StartupMs, 6);
            Assert.Equal((ActiveEndPct - ActiveStartPct) * TotalMs, import.ActiveMs, 6);
            Assert.Contains(import.Markers, m => m.Name == "hit" && Math.Abs(m.AtMs - HitPct * TotalMs) < 1e-6);
        }

        [Fact]
        public void Tolerance_FallsBackToFifty_WhenNoCalibrationRowExists()
        {
            Assert.Equal(SkillClipConsistencyRule.FallbackToleranceMs, SkillClipConsistencyRule.ResolveToleranceMs(Build(withDuration: true)));
        }

        private static string SkillRow(string source, double releaseAtMs) => @"{ ""table"": ""skill.def"", ""schema_version"": 1, ""rows"": [
            { ""id"": ""skill.t_spell"", ""timeline"": { ""source"": """ + source + @""", ""startup_ms"": 400, ""active_ms"": 200, ""recovery_ms"": 200,
              ""markers"": [ { ""name"": ""release"", ""at_ms"": " + releaseAtMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + @" } ] } } ] }";

        private static ValidationReport Validate(string skillRow, bool withDuration = true)
        {
            var view = Build(withDuration, overrideBasic: true, skillTimeline: skillRow);
            var registry = (IDataRegistry)view;
            registry.RegisterValidationRule(new SkillClipConsistencyRule());
            return registry.Validate();
        }

        [Fact]
        public void Rule_ClipSource_ExactCopyPasses_AndDriftIsAnError_AndMissingDurationIsAnError()
        {
            // 剪辑 cast：800 毫秒，release 在 62.5%（500 毫秒），分相 400/200/200（active_start 50%、active_end 75%）。
            Assert.DoesNotContain(Validate(SkillRow("clip", 0.625 * TotalMs)).Issues, i => i.Check.StartsWith("timeline_clip"));

            var drift = Validate(SkillRow("clip", 0.625 * TotalMs + 80));
            Assert.Contains(drift.Issues, i => i.Check == "timeline_clip_mismatch" && i.Severity == ValidationSeverity.Error);

            // 剪辑没有 duration_ms：取不到标记，source: clip 是错误（权威来源缺失），source: data 静默跳过。
            var noDuration = Validate(SkillRow("clip", 0.625 * TotalMs), withDuration: false);
            Assert.Contains(noDuration.Issues, i => i.Check == "timeline_clip_missing" && i.Severity == ValidationSeverity.Error);
            Assert.DoesNotContain(Validate(SkillRow("data", 0.625 * TotalMs), withDuration: false).Issues, i => i.Check.StartsWith("timeline_clip"));
        }

        [Fact]
        public void Rule_DataSource_DeviationBeyondToleranceIsAWarning()
        {
            var report = Validate(SkillRow("data", 0.625 * TotalMs + SkillClipConsistencyRule.FallbackToleranceMs + 30));
            Assert.Contains(report.Issues, i => i.Check == "timeline_clip_deviation" && i.Severity == ValidationSeverity.Warning);
        }
    }
}
