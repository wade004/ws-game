using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Xunit;
using static Tests.Foundation.Feel.FeelTestSupport;

namespace Tests.Foundation.Feel
{
    /// <summary>装配规则（没有手感数据不装配；有数据而无标定装配失败）与框架手感数据（预设、原型、武器、运动模式规则）。</summary>
    public class FeelAssemblyAndFrameworkDataTests
    {
        private static readonly FeelAssemblyOptions Options = new FeelAssemblyOptions();

        private const string SecondCalibrationRow = @"{ ""id"": ""feel.calibration.test_b"", ""base_preset"": ""feel.preset.rpg_classic"",
      ""reference_height"": 1.5, ""base_speed"": 6.0, ""animation_fps"": 60.0, ""reference_camera_height"": 12.0,
      ""reference_zoom"": 1.5, ""pixels_per_unit"": 16.0, ""marker_tolerance_ms"": 30.0 }";

        private static string TwoCalibrations() => CalibrationJson.Replace("]\n}", ",\n" + SecondCalibrationRow + "\n]\n}");

        // ------------------------------------------------------------------ 装配规则

        [Fact]
        public void NoFeelData_IsNotAssembled_AndCreatesNothing()
        {
            var (registry, report) = Load(Array.Empty<(string, string)>());
            Assert.Equal(0, report.ErrorCount);

            var result = FeelAssembly.Assemble(registry, Options);

            Assert.False(result.IsAssembled);
            Assert.Null(result.System);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        }

        [Fact]
        public void FeelDataWithoutCalibration_FailsAssemblyWithClearDiagnostic()
        {
            var (registry, _) = Load(FrameworkFeelTables());

            var ex = Assert.Throws<FeelAssemblyException>(() => FeelAssembly.Assemble(registry, Options));

            Assert.Equal(FeelChecks.CalibrationMissing, ex.Code);
            Assert.Contains("feel.calibration", ex.Message);
        }

        [Fact]
        public void MultipleCalibrationRows_RequireAnExplicitId_AndUnknownIdFails()
        {
            var tables = FrameworkFeelTables();
            tables.Add(("feel.calibration", TwoCalibrations()));
            var (registry, report) = Load(tables);
            Assert.Equal(0, report.ErrorCount);

            var ambiguous = Assert.Throws<FeelAssemblyException>(() => FeelAssembly.Assemble(registry, Options));
            Assert.Equal(FeelChecks.CalibrationMissing, ambiguous.Code);

            var unknown = Assert.Throws<FeelAssemblyException>(() =>
                FeelAssembly.Assemble(registry, new FeelAssemblyOptions { CalibrationId = "feel.calibration.nope" }));
            Assert.Equal(FeelChecks.CalibrationMissing, unknown.Code);

            var ok = FeelAssembly.Assemble(registry, new FeelAssemblyOptions { CalibrationId = "feel.calibration.test_b" });
            Assert.True(ok.IsAssembled);
            Assert.Equal("feel.calibration.test_b", ok.System!.Calibration.Id);
            Assert.Equal("feel.preset.rpg_classic", ok.System.Calibration.BasePresetId);
        }

        [Fact]
        public void CalibrationWithUnknownBasePreset_FailsAssembly()
        {
            var profiles = FrameworkProfiles();
            var cal = CalA("feel.preset.does_not_exist");
            var set = new FeelProfileSet(profiles.Fields, profiles.Presets.Concat(profiles.Archetypes).Concat(profiles.Weapons),
                profiles.MotionModeRules, new[] { cal });

            var ex = Assert.Throws<FeelAssemblyException>(() => FeelAssembly.AssembleFrom(set, Options));

            Assert.Equal(FeelChecks.CalibrationMissing, ex.Code);
            Assert.Contains("does_not_exist", ex.Message);
        }

        [Fact]
        public void ProfileCheckErrors_FailAssembly_WithTheCheckerIssuesAttached()
        {
            var profiles = FrameworkProfiles();
            var badWeapon = FeelRow.Weapon("feel.weapon.bad", new[] { Set("accel_ms", 100) }); // 武器层写角色为主字段
            var set = new FeelProfileSet(profiles.Fields, profiles.Presets.Concat(profiles.Archetypes).Concat(profiles.Weapons).Append(badWeapon),
                profiles.MotionModeRules, profiles.Calibrations);

            var ex = Assert.Throws<FeelAssemblyException>(() => FeelAssembly.AssembleFrom(set, Options));

            Assert.Equal("feel_assembly_profile_invalid", ex.Code);
            Assert.Contains(ex.Issues, i => i.Check == FeelChecks.CompositionViolation && i.Field == "accel_ms");
        }

        [Fact]
        public void Assembled_ResolvesFrameworkData_AndDebugOverridesInvalidateAndRestoreExactly()
        {
            var (registry, report) = LoadFrameworkWithCalibration();
            Assert.Equal(0, report.ErrorCount);

            var result = FeelAssembly.Assemble(registry, Options);

            Assert.True(result.IsAssembled);
            var system = result.System!;
            Assert.NotNull(system.DebugOverrides);
            var before = system.Resolver.Resolve(Unit1);
            var beforeStride = before.GetRaw("stride_scale");
            Assert.Equal("feel.calibration.test_a", system.Calibration.Id);
            var versionBefore = before.Version;

            system.DebugOverrides!.SetUnit(Unit1, Set("stride_scale", 2.5));
            var during = system.Resolver.Resolve(Unit1);
            Assert.Equal(2.5, during.GetRaw("stride_scale").AsNumber());
            Assert.True(during.Version > versionBefore);
            // 另一个单位不受影响。
            Assert.Equal(beforeStride, system.Resolver.Resolve(Unit2).GetRaw("stride_scale"));

            system.DebugOverrides.ClearUnit(Unit1);
            var after = system.Resolver.Resolve(Unit1);
            Assert.Equal(beforeStride, after.GetRaw("stride_scale"));
        }

        [Fact]
        public void CallerSuppliedDebugProvider_IsUsed_AndAssemblyCreatesNoOverlay()
        {
            var (registry, _) = LoadFrameworkWithCalibration();
            var own = new FeelDebugOverrides(FeelFields.Default);
            var providers = new FeelProviders { Debug = own };

            var result = FeelAssembly.Assemble(registry, new FeelAssemblyOptions { Providers = providers });

            Assert.True(result.IsAssembled);
            Assert.Null(result.System!.DebugOverrides);
            own.SetGlobal(Set("stride_scale", 3));
            // 自带提供者没有接失效回调，调用方负责失效（解析器缓存此前未解析过则直接读到新值）。
            Assert.Equal(3.0, result.System.Resolver.Resolve(Unit1).GetRaw("stride_scale").AsNumber());
        }

        [Fact]
        public void InvalidStepSeconds_IsRejected()
        {
            var profiles = FrameworkProfiles();
            Assert.Throws<ArgumentException>(() => FeelAssembly.AssembleFrom(profiles, new FeelAssemblyOptions { StepSeconds = 0 }));
            Assert.Throws<ArgumentException>(() => FeelAssembly.AssembleFrom(profiles, new FeelAssemblyOptions { StepSeconds = double.NaN }));
        }

        // ------------------------------------------------------------------ 框架数据

        [Fact]
        public void FrameworkFeelData_LoadsWithZeroErrorsAndZeroWarnings_AndPassesTheChecker()
        {
            var (registry, report) = LoadFrameworkWithCalibration();
            Assert.Equal(0, report.ErrorCount);
            Assert.Equal(0, report.WarningCount);

            var profiles = FeelProfileSet.FromRegistry(registry, FeelFields.Default);
            Assert.Empty(FeelProfileChecker.Check(profiles));
            Assert.Equal(2, profiles.Presets.Count);
            Assert.Equal(3, profiles.Archetypes.Count);
            Assert.Equal(2, profiles.Weapons.Count);
        }

        [Fact]
        public void FrameworkPresets_CoverEveryNonOptionalField()
        {
            var profiles = FrameworkProfiles();
            foreach (var preset in profiles.Presets)
            {
                var values = profiles.EffectivePresetValues(preset.Id, out _);
                for (var i = 0; i < profiles.Fields.Count; i++)
                {
                    if (!profiles.Fields[i].Optional) Assert.False(values[i].IsNone, preset.Id + ":" + profiles.Fields[i].Name);
                }
            }
        }

        [Fact]
        public void RpgClassicPreset_JudgingFieldsEqualLegacyBehaviourByRule()
        {
            var profiles = FrameworkProfiles();
            var cal = CalA("feel.preset.rpg_classic");
            var r = new FeelResolver(profiles, cal, 1.0 / 60.0).Resolve(Unit1);

            // 既有行为：无缓冲、瞬时达速与停止、瞬时转向、无顿帧/硬直/击退/倒地、无取消与连招窗口、不滑墙、反应不封顶以外全无。
            foreach (var def in profiles.Fields.Fields.Where(f => f.Half == FeelHalf.Judging && f.Unit == FeelUnit.Milliseconds && !f.Optional))
            {
                Assert.Equal(0.0, r.GetRaw(def.Name).AsNumber());
                Assert.Equal(0, r.Judging.GetTicks(def.Name));
            }
            Assert.Equal(0.0, r.GetRaw("turn_rate_deg_s").AsNumber());
            Assert.Equal(0.0, r.GetRaw("knockback_distance").AsNumber());
            Assert.Equal(0.0, r.GetRaw("stagger_power").AsNumber());
            Assert.Equal(0.0, r.GetRaw("cancel_window_scale").AsNumber());
            Assert.Equal(0.0, r.GetRaw("combo_window_scale").AsNumber());
            Assert.Equal(0.0, r.GetRaw("dead_zone").AsNumber());
            Assert.False(r.GetRaw("wall_slide").AsBool());
            Assert.False(r.GetRaw("arrival_decel").AsBool());
            Assert.False(r.GetRaw("apply_to_path_following").AsBool());
            Assert.Equal("none", r.GetRaw("reaction_cap").AsText());
            Assert.Equal(1.0, r.GetRaw("walk_speed_ratio").AsNumber());
            Assert.Equal(1.0, r.GetRaw("kill_hitstop_scale").AsNumber());
        }

        [Fact]
        public void ArpgResponsivePreset_HasTheDocumentedStartingPointNumbers()
        {
            var profiles = FrameworkProfiles();
            var r = new FeelResolver(profiles, CalA(), 1.0 / 60.0).Resolve(Unit1);
            Assert.Equal(120.0, r.GetRaw("buffer_ms").AsNumber());
            Assert.Equal(2.0, r.GetRaw("buffer_slots").AsNumber());
            Assert.Equal(100.0, r.GetRaw("grace_ms").AsNumber());
            Assert.Equal(0.15, r.GetRaw("dead_zone").AsNumber());
            Assert.Equal(720.0, r.GetRaw("turn_rate_deg_s").AsNumber());
            Assert.Equal(0.5, r.GetRaw("walk_speed_ratio").AsNumber());
            Assert.Equal(7, r.Judging.GetTicks("buffer_ms")); // 120 ms / 16.667 ms = 7.2 → 7
        }

        [Fact]
        public void WeaponStartingPoints_MatchDesignDocNumbers_AndAreMarkedExperimental()
        {
            var profiles = FrameworkProfiles();
            Func<string, FeelResolver> make = weapon => new FeelResolver(profiles, CalA(), 1.0 / 60.0,
                new FakeState { Main = weapon }.AsProviders());

            var sword = make("feel.weapon.sword_1h").Resolve(Unit1);
            Assert.Equal(35.0, sword.GetRaw("attacker_hitstop_ms").AsNumber());
            Assert.Equal(45.0, sword.GetRaw("target_hitstop_ms").AsNumber());
            Assert.Equal(0.15, sword.GetRaw("knockback_distance").AsNumber());
            Assert.Equal(1.0, sword.GetRaw("stagger_power").AsNumber());
            Assert.Equal(1.5, sword.GetRaw("kill_hitstop_scale").AsNumber());
            Assert.Equal("light", sword.GetRaw("impact_class").AsText());

            var great = make("feel.weapon.greatsword").Resolve(Unit1);
            Assert.Equal(65.0, great.GetRaw("attacker_hitstop_ms").AsNumber());
            Assert.Equal(75.0, great.GetRaw("target_hitstop_ms").AsNumber());
            Assert.Equal(0.3, great.GetRaw("knockback_distance").AsNumber());
            Assert.Equal(3.0, great.GetRaw("stagger_power").AsNumber());
            Assert.Equal(2.0, great.GetRaw("kill_hitstop_scale").AsNumber());
            Assert.Equal("heavy", great.GetRaw("impact_class").AsText());

            var weaponText = FrameworkFeelTables().Single(t => t.Table == "feel.weapon").Json;
            Assert.Equal(2, CountOccurrences(weaponText, "\"maturity\": \"experimental\""));
            foreach (var needle in new[]
            {
                "\"startup_ms\": 110", "\"active_ms\": 80", "\"recovery_ms\": 190", "\"dodge_cancel_open_progress\": 0.65", "\"inflicted_hit_stun_ms\": 150",
                "\"startup_ms\": 170", "\"active_ms\": 100", "\"recovery_ms\": 290", "\"dodge_cancel_open_progress\": 0.75", "\"inflicted_hit_stun_ms\": 230",
            })
            {
                Assert.Contains(needle, weaponText);
            }
        }

        [Fact]
        public void SwordAsOffhand_StacksOnlyStackablePresentingFields()
        {
            var profiles = FrameworkProfiles();
            var solo = new FeelResolver(profiles, CalA(), 1.0 / 60.0, new FakeState { Main = "feel.weapon.greatsword" }.AsProviders()).Resolve(Unit1);
            var dual = new FeelResolver(profiles, CalA(), 1.0 / 60.0,
                new FakeState { Main = "feel.weapon.greatsword", Off = "feel.weapon.sword_1h" }.AsProviders()).Resolve(Unit1);

            Assert.Equal(solo.GetRaw("sfx_sweetener_tier").AsNumber() + 1, dual.GetRaw("sfx_sweetener_tier").AsNumber());
            Assert.Equal(solo.GetRaw("impact_vfx_scale").AsNumber() * 1.1, dual.GetRaw("impact_vfx_scale").AsNumber(), 12);
            Assert.Equal(solo.GetRaw("attacker_hitstop_ms"), dual.GetRaw("attacker_hitstop_ms"));
            Assert.Equal(solo.GetRaw("stagger_power"), dual.GetRaw("stagger_power"));
        }

        [Fact]
        public void ArchetypesLightMediumHeavy_ScaleBodyFieldsInTheDocumentedDirections()
        {
            var profiles = FrameworkProfiles();
            Func<string?, FeelResolvedSnapshot> at = a =>
            {
                var r = new FeelResolver(profiles, CalA(), 1.0 / 60.0, new FakeState { Archetype = a }.AsProviders()).Resolve(Unit1);
                return new FeelResolvedSnapshot(r.GetRaw("accel_ms").AsNumber(), r.GetRaw("turn_rate_deg_s").AsNumber(), r.GetRaw("hit_stun_ms").AsNumber(), r.GetRaw("knockback_distance").AsNumber());
            };
            var none = at(null);
            var light = at("feel.archetype.light");
            var medium = at("feel.archetype.medium");
            var heavy = at("feel.archetype.heavy");

            Assert.Equal(none, medium);
            Assert.True(light.Accel < none.Accel && none.Accel < heavy.Accel);
            Assert.True(light.TurnRate > none.TurnRate && none.TurnRate > heavy.TurnRate);
            Assert.True(light.HitStun > none.HitStun && none.HitStun > heavy.HitStun);
            Assert.True(light.Knockback > none.Knockback && none.Knockback > heavy.Knockback);
        }

        private readonly struct FeelResolvedSnapshot : IEquatable<FeelResolvedSnapshot>
        {
            public readonly double Accel;
            public readonly double TurnRate;
            public readonly double HitStun;
            public readonly double Knockback;

            public FeelResolvedSnapshot(double accel, double turnRate, double hitStun, double knockback)
            {
                Accel = accel; TurnRate = turnRate; HitStun = hitStun; Knockback = knockback;
            }

            public bool Equals(FeelResolvedSnapshot other) =>
                Accel == other.Accel && TurnRate == other.TurnRate && HitStun == other.HitStun && Knockback == other.Knockback;

            public override bool Equals(object? obj) => obj is FeelResolvedSnapshot o && Equals(o);

            public override int GetHashCode() => Accel.GetHashCode() ^ TurnRate.GetHashCode();
        }

        [Fact]
        public void MotionModeRules_HaveTheSevenModesOfTheDesign()
        {
            var profiles = FrameworkProfiles();
            Assert.Equal(FeelMotionModes.Modes.OrderBy(x => x), profiles.MotionModeRules.Select(m => m.Mode).OrderBy(x => x));
            Assert.Equal(7, profiles.MotionModeRules.Count);
            var frozen = profiles.MotionModeRules.Single(m => m.Mode == "frozen");
            Assert.Equal("restore_previous_mode", frozen.KeepsMomentumOnExit);
            Assert.Equal("by_profile", profiles.MotionModeRules.Single(m => m.Mode == "action").AcceptsInputDisplacement);
        }

        [Fact]
        public void FrameworkFeelData_AssemblesAlone_WithItsOwnDefaultCalibration()
        {
            var (registry, report) = Load(FrameworkFeelTablesWithOwnCalibration());
            Assert.Equal(0, report.ErrorCount);

            var result = FeelAssembly.Assemble(registry, new FeelAssemblyOptions());

            Assert.True(result.IsAssembled);
            var system = result.System!;
            Assert.Equal("feel.calibration.framework_default", system.Calibration.Id);
            Assert.Equal("feel.preset.arpg_responsive", system.Calibration.BasePresetId);
            // 缺省标定的毫秒换算依据是装配缺省步长 1/60 秒：预设缓冲窗口 120 ms 换算出的 tick 数由规则算出，不写死裸数。
            var expectedTicks = FeelCalibration.MillisecondsToTicks(120, 1.0 / 60.0);
            Assert.Equal(expectedTicks, system.Resolver.Resolve(Unit1).Judging.GetTicks("buffer_ms"));
            // 参考身高 1：身高倍数单位的绝对值等于相对值（框架缺省“1 世界单位 = 1 个身高”）。
            Assert.Equal(0.15 * system.Calibration.ReferenceHeight, system.Resolver.Resolve(Unit1).Judging.GetNumber("knockback_distance"), 9);
        }

        [Fact]
        public void FrameworkFeelData_CarriesExactlyOneCalibrationRow_WhichGameRowsMustOverrideByCalibrationId()
        {
            var calibrations = FrameworkFeelTablesWithOwnCalibration().Where(t => t.Table == "feel.calibration").ToList();
            Assert.Single(calibrations);
            Assert.Equal(1, CountOccurrences(calibrations[0].Json, "\"id\": \"feel.calibration."));
        }

        [Fact]
        public void DefaultDataRootsAndTemplate_ContainNoCalibrationRows()
        {
            // S1 起框架手感数据根 data/_feel 自带一行缺省标定（使其可单独校验与装配），不再列入“不含标定行”的清单；
            // 默认数据根与新游戏模板仍然不带——没有手感数据的游戏不会被迫提供标定。
            var root = FindRepoRoot();
            var scanned = 0;
            foreach (var dir in new[] { Path.Combine(root, "data", "_framework"), Path.Combine(root, "games", "_template") })
            {
                Assert.True(Directory.Exists(dir), dir);
                foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                {
                    scanned++;
                    Assert.False(Path.GetFileName(file).StartsWith("feel.calibration", StringComparison.Ordinal), file);
                    Assert.DoesNotContain("\"table\": \"feel.calibration\"", File.ReadAllText(file));
                }
            }
            Assert.True(scanned > 0);
        }

        [Fact]
        public void FrameworkDataRoot_HasNoFeelTables_SoGamesWithoutFeelDataAreNotAssembled()
        {
            var dir = Path.Combine(FindRepoRoot(), "data", "_framework");
            Assert.False(Directory.Exists(Path.Combine(dir, "feel")));
            Assert.Empty(Directory.GetFiles(dir, "feel.*.json", SearchOption.AllDirectories));
        }

        private static int CountOccurrences(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }
    }
}
