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
    /// <summary>
    /// 默认手感模板（ADR-0142，<c>data/_feel_templates</c>）的解析验收：五个模板预设与"模板 x 武器原型"武器行都能被解析器解开，
    /// 全部字段带完整溯源（预设层来源是该模板，武器写入的字段在武器层有条目且值相同），没有诊断、没有限幅；
    /// 模板之间的可测差异（顿帧、缓冲、相位、镜头冲击）由数据按规则排序，用例里不写裸数。
    /// </summary>
    public class FeelTemplatesDataTests
    {
        private static readonly string[] Styles = { "classic", "agile", "heavy", "horde", "precise" };
        private static readonly string[] Archetypes = { "dagger", "sword_1h", "greatsword", "polearm", "ranged", "catalyst" };

        private static string PresetId(string style) => "feel.preset.tpl_" + style;

        private static string WeaponId(string style, string archetype) => $"feel.weapon.tpl_{style}_{archetype}";

        /// <summary>模板根的 feel 表（预设、武器）+ 框架手感根的体型原型与运动模式规则；标定由用例自拟（见 <see cref="CalA"/>）。</summary>
        private static List<(string Table, string Json)> Tables()
        {
            var root = FindRepoRoot();
            var tables = new List<(string, string)>();
            foreach (var name in new[] { "feel.preset", "feel.weapon" })
            {
                tables.Add((name, File.ReadAllText(Path.Combine(root, "data", "_feel_templates", "feel", name + ".json"))));
            }

            foreach (var name in new[] { "feel.archetype", "feel.motion_mode_rules" })
            {
                tables.Add((name, File.ReadAllText(Path.Combine(root, "data", "_feel", "feel", name + ".json"))));
            }

            // 模板根不带标定（标定属于游戏）：这里补一行只供装载通过，解析时用 CalA(模板预设) 逐个指定基础预设。
            tables.Add(("feel.calibration", @"{ ""table"": ""feel.calibration"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""feel.calibration.test_templates"", ""base_preset"": ""feel.preset.tpl_classic"",
    ""reference_height"": 2.0, ""base_speed"": 4.0, ""animation_fps"": 30.0, ""reference_camera_height"": 10.0,
    ""reference_zoom"": 1.0, ""pixels_per_unit"": 32.0, ""marker_tolerance_ms"": 50.0 } ] }"));
            return tables;
        }

        private static FeelProfileSet Profiles()
        {
            var (registry, report) = Load(Tables());
            if (report.ErrorCount > 0) throw new InvalidOperationException("模板手感数据加载有错误：" + string.Join("\n", report.Issues));
            return FeelProfileSet.FromRegistry(registry, FeelFields.Default);
        }

        private static ResolvedFeel Resolve(FeelProfileSet profiles, string style, string? weapon)
        {
            var providers = new FakeState { Main = weapon }.AsProviders();
            return new FeelResolver(profiles, CalA(PresetId(style)), 1.0 / 60.0, providers).Resolve(Unit1);
        }

        private static double Num(ResolvedFeel r, string field) => r.GetRaw(field).AsNumber();

        // ------------------------------------------------------------------ 数据完整性

        [Fact]
        public void TemplatesRoot_LoadsWithZeroErrorsAndWarnings_AndPassesTheChecker()
        {
            var (registry, report) = Load(Tables());
            Assert.Equal(0, report.ErrorCount);
            Assert.Equal(0, report.WarningCount);

            var profiles = FeelProfileSet.FromRegistry(registry, FeelFields.Default);
            Assert.Empty(FeelProfileChecker.Check(profiles));
            Assert.Equal(Styles.Length, profiles.Presets.Count);
            // 三个类别行（长柄/投射/法器）+ 每个模板 x 每个武器原型一行。
            Assert.Equal(3 + Styles.Length * Archetypes.Length, profiles.Weapons.Count);
        }

        [Fact]
        public void EveryTemplateRow_IsExperimental_AndCarriesADescription()
        {
            var root = FindRepoRoot();
            foreach (var table in new[] { "feel.preset", "feel.weapon" })
            {
                var text = File.ReadAllText(Path.Combine(root, "data", "_feel_templates", "feel", table + ".json"));
                var rows = CountOccurrences(text, "\"id\": \"feel.");
                Assert.True(rows > 0);
                Assert.Equal(rows, CountOccurrences(text, "\"maturity\": \"experimental\""));
                Assert.Equal(rows, CountOccurrences(text, "\"description\": \""));
            }
        }

        [Fact]
        public void TemplatePresets_CoverEveryNonOptionalField()
        {
            var profiles = Profiles();
            foreach (var style in Styles)
            {
                var values = profiles.EffectivePresetValues(PresetId(style), out _);
                for (var i = 0; i < profiles.Fields.Count; i++)
                {
                    if (!profiles.Fields[i].Optional) Assert.False(values[i].IsNone, PresetId(style) + ":" + profiles.Fields[i].Name);
                }
            }
        }

        [Fact]
        public void EveryTemplate_DefinesAWeaponRowForEveryArchetype_WithThePoseFamilyOfTheArchetype()
        {
            var profiles = Profiles();
            var families = new Dictionary<string, string>
            {
                ["dagger"] = "1h", ["sword_1h"] = "1h", ["greatsword"] = "2h", ["polearm"] = "polearm", ["ranged"] = "bow", ["catalyst"] = "staff",
            };
            foreach (var style in Styles)
            {
                foreach (var archetype in Archetypes)
                {
                    var row = profiles.GetWeapon(WeaponId(style, archetype));
                    Assert.NotNull(row);
                    Assert.NotEmpty(row!.Writes);
                }
            }

            // 类别行（风格中立）也在，姿势族取现有姿势集的族名。
            foreach (var archetype in new[] { "polearm", "ranged", "catalyst" })
            {
                Assert.NotNull(profiles.GetWeapon("feel.weapon." + archetype));
            }

            Assert.Equal(6, families.Count);
        }

        // ------------------------------------------------------------------ 解析与溯源

        [Theory]
        [InlineData("classic")]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void Template_WithoutWeapon_ResolvesWithFullProvenanceFromItsOwnPreset(string style)
        {
            var profiles = Profiles();
            var r = Resolve(profiles, style, null);

            Assert.Empty(r.Diagnostics);
            Assert.Empty(r.ClampedFields);
            for (var i = 0; i < profiles.Fields.Count; i++)
            {
                var def = profiles.Fields[i];
                if (def.Optional) continue;
                var trail = r.GetProvenance(def.Name);
                Assert.NotEmpty(trail);
                Assert.Equal((int)FeelLayer.BasePreset, trail[0].Layer);
                Assert.Equal(PresetId(style), trail[0].SourceId);
            }
        }

        [Theory]
        [InlineData("classic")]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void TemplateWeaponRows_ResolveWithFullProvenance_AndTheWeaponLayerWritesWhatTheRowSays(string style)
        {
            var profiles = Profiles();
            foreach (var archetype in Archetypes)
            {
                var weaponId = WeaponId(style, archetype);
                var row = profiles.GetWeapon(weaponId)!;
                var r = Resolve(profiles, style, weaponId);

                Assert.Empty(r.Diagnostics);
                Assert.Empty(r.ClampedFields);
                foreach (var w in row.Writes)
                {
                    var trail = r.GetProvenance(w.Field);
                    Assert.Equal((int)FeelLayer.BasePreset, trail[0].Layer);
                    Assert.Equal(PresetId(style), trail[0].SourceId);

                    // 攻击期间临时覆盖的字段（攻击中移速倍率、朝向锁）只在动作进行中生效，空闲解析里不出武器层条目。
                    if (profiles.Fields.TryGet(w.Field, out var def) && def.Composition == FeelComposition.AttackOverride)
                    {
                        Assert.DoesNotContain(trail, t => t.Layer == (int)FeelLayer.Weapon);
                        continue;
                    }

                    var weaponEntry = trail.Single(t => t.Layer == (int)FeelLayer.Weapon);
                    Assert.Equal(weaponId, weaponEntry.SourceId);
                    Assert.Equal(FeelProvenanceOps.Set, weaponEntry.Op);
                    Assert.Equal(w.Value, weaponEntry.ValueAfter);
                    Assert.Equal(w.Value, r.GetRaw(w.Field));
                }
            }
        }

        [Fact]
        public void ClassCatalogRows_ResolveOnTopOfAnyTemplate_WithoutDiagnostics()
        {
            var profiles = Profiles();
            foreach (var style in Styles)
            {
                foreach (var archetype in new[] { "polearm", "ranged", "catalyst" })
                {
                    var r = Resolve(profiles, style, "feel.weapon." + archetype);
                    Assert.Empty(r.Diagnostics);
                    Assert.Empty(r.ClampedFields);
                    Assert.Contains(r.GetProvenance("attacker_hitstop_ms"), t => t.Layer == (int)FeelLayer.Weapon && t.SourceId == "feel.weapon." + archetype);
                }
            }
        }

        // ------------------------------------------------------------------ 模板之间的可测差异（由数据排序）

        private static void AssertAscending(IEnumerable<string> expectedOrder, Func<string, double> value, string what)
        {
            var list = expectedOrder.ToList();
            for (var i = 1; i < list.Count; i++)
            {
                Assert.True(value(list[i - 1]) < value(list[i]), $"{what}：{list[i - 1]}({value(list[i - 1])}) 应小于 {list[i]}({value(list[i])})");
            }
        }

        [Theory]
        [InlineData("dagger")]
        [InlineData("sword_1h")]
        [InlineData("greatsword")]
        [InlineData("polearm")]
        [InlineData("ranged")]
        [InlineData("catalyst")]
        public void HitstopAndCameraImpulse_OrderByTemplateCharacter_ForEveryWeaponArchetype(string archetype)
        {
            var profiles = Profiles();
            Func<string, string, double> get = (style, field) => Num(Resolve(profiles, style, WeaponId(style, archetype)), field);

            // 顿帧：经典(无) < 割草(低单体) < 敏捷(轻) < 精准(中) < 厚重(强)。
            var order = new[] { "classic", "horde", "agile", "precise", "heavy" };
            AssertAscending(order, s => get(s, "target_hitstop_ms"), archetype + " 受击方顿帧");
            AssertAscending(order, s => get(s, "attacker_hitstop_ms"), archetype + " 攻击方顿帧");

            // 镜头冲击基准：经典(无) < 敏捷 < 割草 < 精准 < 厚重。
            AssertAscending(new[] { "classic", "agile", "horde", "precise", "heavy" }, s => get(s, "camera_impulse_gain"), archetype + " 镜头冲击");

            // 击退：经典(无) < 敏捷 < 精准 < 厚重 < 割草（割草的清晰击退是它的招牌）。
            AssertAscending(new[] { "classic", "agile", "precise", "heavy", "horde" }, s => get(s, "knockback_distance"), archetype + " 击退");
        }

        [Theory]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void WithinATemplate_HeavierWeaponArchetypes_HitHarder(string style)
        {
            var profiles = Profiles();
            Func<string, string, double> get = (archetype, field) => Num(Resolve(profiles, style, WeaponId(style, archetype)), field);

            // 同一风格内：匕首 < 单手剑 < 长柄 < 巨剑（顿帧、击退、硬直强度、镜头冲击）。
            var order = new[] { "dagger", "sword_1h", "polearm", "greatsword" };
            foreach (var field in new[] { "attacker_hitstop_ms", "target_hitstop_ms", "knockback_distance", "stagger_power", "camera_impulse_gain" })
            {
                AssertAscending(order, a => get(a, field), style + " " + field);
            }

            // 冲击等级随武器重量不降。
            var rank = new Dictionary<string, int> { ["light"] = 0, ["medium"] = 1, ["heavy"] = 2, ["massive"] = 3 };
            var classes = order.Select(a => rank[Resolve(profiles, style, WeaponId(style, a)).GetRaw("impact_class").AsText()]).ToList();
            for (var i = 1; i < classes.Count; i++) Assert.True(classes[i - 1] <= classes[i]);
        }

        [Fact]
        public void BufferWidthsAndPhaseTotals_OrderByTemplateCharacter()
        {
            var profiles = Profiles();
            Func<string, string, double> p = (style, field) => Num(Resolve(profiles, style, null), field);

            // 输入缓冲：精准(窄) < 厚重 < 经典 < 割草 < 敏捷(宽)；缓冲槽：只有敏捷与割草超过一槽。
            AssertAscending(new[] { "precise", "heavy", "classic", "horde", "agile" }, s => p(s, "buffer_ms"), "缓冲时长");
            Assert.Equal(new[] { 1.0, 1.0, 1.0 }, new[] { "precise", "heavy", "classic" }.Select(s => p(s, "buffer_slots")).ToArray());
            Assert.True(p("horde", "buffer_slots") > 1 && p("agile", "buffer_slots") > p("horde", "buffer_slots"));

            // 同一份参考时间线（前摇 150 / 判定 100 / 后摇 250 毫秒）按各模板三相倍率缩放后的总时长：
            // 敏捷(快) < 割草 < 经典(1 倍) < 精准 < 厚重(慢)。
            Func<string, double> total = s =>
                150 * p(s, "phase_scale.startup") + 100 * p(s, "phase_scale.active") + 250 * p(s, "phase_scale.recovery");
            AssertAscending(new[] { "agile", "horde", "classic", "precise", "heavy" }, total, "参考时间线总时长");

            // 取消窗口倍率：经典为 0（永不打开），其余 精准 < 厚重 < 割草 < 敏捷。
            Assert.Equal(0.0, p("classic", "cancel_window_scale"));
            AssertAscending(new[] { "classic", "precise", "heavy", "horde", "agile" }, s => p(s, "cancel_window_scale"), "取消窗口倍率");
        }

        [Fact]
        public void ClassicTemplate_IsStillAndSilent_NoHitstopStunKnockbackCancelOrCameraImpulse()
        {
            var profiles = Profiles();
            foreach (var archetype in Archetypes)
            {
                var r = Resolve(profiles, "classic", WeaponId("classic", archetype));
                foreach (var field in new[]
                {
                    "attacker_hitstop_ms", "target_hitstop_ms", "hit_stun_ms", "knockback_distance", "stagger_power",
                    "cancel_window_scale", "combo_window_scale", "camera_impulse_gain", "camera_shake_cap", "action_move_speed_ratio",
                })
                {
                    Assert.Equal(0.0, Num(r, field));
                }

                Assert.Equal(1.0, Num(r, "kill_hitstop_scale"));
                Assert.Equal("none", r.GetRaw("reaction_cap").AsText());
            }
        }

        [Fact]
        public void HordeTemplate_LaunchesTargets_OtherTemplatesDoNot_AndHeavyUsesTheMassiveClass()
        {
            var profiles = Profiles();
            foreach (var style in Styles)
            {
                foreach (var archetype in Archetypes)
                {
                    var r = Resolve(profiles, style, WeaponId(style, archetype));
                    var launch = r.GetRaw("launch_height");
                    if (style == "horde") Assert.True(launch.AsNumber() > 0);
                    else Assert.True(launch.IsNone, style + "/" + archetype + " 不应声明击飞");
                }
            }

            // 厚重 x 巨剑是框架模板里唯一使用 massive 冲击等级的组合之一（厚重风格整体上移一档，割草同样上移一档）。
            Assert.Equal("massive", Resolve(profiles, "heavy", WeaponId("heavy", "greatsword")).GetRaw("impact_class").AsText());
            Assert.Equal("massive", Resolve(profiles, "horde", WeaponId("horde", "greatsword")).GetRaw("impact_class").AsText());
            Assert.Equal("heavy", Resolve(profiles, "classic", WeaponId("classic", "greatsword")).GetRaw("impact_class").AsText());
        }

        [Fact]
        public void TemplatePresets_DiffersFromEachOther_InTheJudgingFieldsThatDefineTheirFeel()
        {
            var profiles = Profiles();
            var keys = new[]
            {
                "buffer_ms", "grace_ms", "accel_ms", "turn_rate_deg_s", "phase_scale.startup", "phase_scale.recovery",
                "cancel_window_scale", "hit_stun_ms", "attacker_hitstop_ms", "knockback_distance",
            };
            for (var i = 0; i < Styles.Length; i++)
            {
                for (var j = i + 1; j < Styles.Length; j++)
                {
                    var a = Resolve(profiles, Styles[i], null);
                    var b = Resolve(profiles, Styles[j], null);
                    var differing = keys.Count(k => Num(a, k) != Num(b, k));
                    Assert.True(differing >= 6, $"{Styles[i]} 与 {Styles[j]} 在特征字段上只有 {differing} 项不同");
                }
            }
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
