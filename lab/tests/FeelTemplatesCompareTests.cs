using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.Common.Json;
using Core.Rules.Combat;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 默认手感模板（ADR-0142）的实验室对照验收：五个模板脚本 <c>feel_tpl_*</c> 共用同一份输入，预设钉死为各自的 <c>feel.preset.tpl_*</c>；
    /// 实测值等于由"模板预设字段 × 毫秒换算 × 时间线与受击裁决规则"算出的期望（不写裸数），并核对模板之间由数据决定的次序。
    /// 记号：动作式格子 <c>2d_action</c>；挥击技能 <c>skill.lab_a_slash</c>（前摇 150 / 判定 100 / 后摇 250 毫秒，命中标记在前摇末，
    /// 闪避取消窗口 250..500 毫秒）。输入：t=5 攻击；t=8 动作进行中再按攻击（缓冲探针）；t=100 攻击；t=116 闪避（取消探针）。
    /// </summary>
    public sealed class FeelTemplatesCompareTests
    {
        private static readonly string[] Styles = { "classic", "agile", "heavy", "horde", "precise" };
        private const string Cell = "2d_action";
        private const string Slash = "skill.lab_a_slash";
        private const string Dodge = "skill.lab_a_dodge";
        private const string AttackAction = "input.action.lab_a_attack";
        private const string DodgeAction = "input.action.lab_a_dodge";
        private const string PlaceholderVfx = "vfx.placeholder_hit_spark";

        private static readonly string FeelDir = Path.Combine("data", "_feel_templates", "feel");

        private static string ScriptId(string style) => "feel_tpl_" + style;

        private static string PresetId(string style) => "feel.preset.tpl_" + style;

        private static FeelRules Preset(string style) => FeelRules.Preset(PresetId(style));

        private static FeelFp Fp(string style) => FeelFp.Of(ScriptId(style), Cell);

        private static int T(double ms) => FeelRules.T(ms);

        private static int Press(string style, string action, int nth)
        {
            var presses = LabTestSupport.Script(ScriptId(style)).Events
                .Where(e => e.Action == action && e.Kind == ScriptEventKind.Press).ToList();
            return presses[nth].Tick;
        }

        /// <summary>预设"冲击等级 → 受击反应"的生产缺省映射。</summary>
        private static string ReactionFor(string impactClass) => new HitFeelOptions().ImpactReactions[impactClass].ToString();

        private static void AssertAscending(IEnumerable<string> order, Func<string, double> value, string what)
        {
            var list = order.ToList();
            for (var i = 1; i < list.Count; i++)
            {
                Assert.True(value(list[i - 1]) < value(list[i]), $"{what}：{list[i - 1]}({value(list[i - 1])}) 应小于 {list[i]}({value(list[i])})");
            }
        }

        // ---------- 输入与基线 ----------

        [Fact]
        public void TemplateScripts_ShareTheSameInput_AndPinTheirOwnTemplatePreset()
        {
            var reference = LabTestSupport.Script(ScriptId(Styles[0]));
            var referenceEvents = reference.Events.Select(e => e.Tick + ":" + e.Action + ":" + e.Kind).ToList();
            foreach (var style in Styles)
            {
                var script = LabTestSupport.Script(ScriptId(style));
                Assert.Equal(PresetId(style), script.Meta.PresetId);
                Assert.Equal(referenceEvents, script.Events.Select(e => e.Tick + ":" + e.Action + ":" + e.Kind).ToList());
                Assert.Equal(reference.Meta.DurationTicks, script.Meta.DurationTicks);
                Assert.Contains("data/_feel_templates", script.Meta.ExtraDataRoots);
            }
        }

        // ---------- 输入缓冲：缓冲探针的过期 tick = 缓冲宽度 ----------

        [Theory]
        [InlineData("classic")]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void BufferProbe_ExpiresAtThePressPlusTheTemplateBufferWidth(string style)
        {
            var fp = Fp(style);
            var p = Preset(style);
            var first = Press(style, AttackAction, 0);
            var probe = Press(style, AttackAction, 1);
            var bufferTicks = p.Ticks("buffer_ms");
            var attacker = p.Ticks("attacker_hitstop_ms");

            // 命中标记落在前摇末；探针等待期间若发生命中，动作时钟被攻击方顿帧暂停，缓冲计时同样顺延（同 DodgeCancel 用例的规则）。
            var hit = first + T(FeelRules.TimelineMs(Slash, "startup_ms") * p.N("phase_scale.startup"));
            var expire = probe + bufferTicks + 1 + (hit > probe && hit <= probe + bufferTicks + 1 ? attacker : 0);

            var expires = fp.Items("inputbuf.expire_ticks");
            Assert.Equal($"{expire}:lab_a_attack", expires[0]);
            Assert.Contains($"{expire}:lab_a_attack:Expired", fp.Items("inputbuf.drop_events"));
        }

        [Fact]
        public void BufferWidths_OrderByTemplateCharacter_PreciseNarrowestAgileWidest()
        {
            AssertAscending(new[] { "precise", "heavy", "classic", "horde", "agile" }, s => Preset(s).N("buffer_ms"), "缓冲时长");
            AssertAscending(new[] { "precise", "heavy", "classic", "horde", "agile" }, s => Preset(s).Ticks("buffer_ms"), "缓冲 tick");
            // 探针从按下到过期的实测间隔随缓冲宽度同向（精准 < 厚重 < 经典 < 割草；敏捷最宽，另含顿帧顺延，不与割草直接比）。
            Func<string, double> waited = s => FeelFp.TickOf(Fp(s).Items("inputbuf.expire_ticks")[0]) - Press(s, AttackAction, 1);
            Assert.True(waited("precise") < waited("heavy"));
            Assert.True(waited("heavy") < waited("classic"));
            Assert.True(waited("classic") < waited("horde"));
            Assert.True(waited("classic") < waited("agile"));
        }

        // ---------- 相位与取消窗口 ----------

        [Theory]
        [InlineData("classic")]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void Phases_EqualTheAuthoredTimelineScaledByTheTemplate_AndTheActivePhaseCarriesTheAttackerHitstop(string style)
        {
            var fp = Fp(style);
            var p = Preset(style);
            var startup = T(FeelRules.TimelineMs(Slash, "startup_ms") * p.N("phase_scale.startup"));
            var active = T(FeelRules.TimelineMs(Slash, "active_ms") * p.N("phase_scale.active"));
            var recovery = T(FeelRules.TimelineMs(Slash, "recovery_ms") * p.N("phase_scale.recovery"));
            var attacker = p.Ticks("attacker_hitstop_ms");

            Assert.Equal($"player:{Slash}:startup={startup}/active={active + attacker}/recovery={recovery}", fp.Items("actiontl.phase_ticks")[0]);
            var first = Press(style, AttackAction, 0);
            Assert.Equal(first + startup, FeelFp.FirstTickWhere(fp.Items("actiontl.markers"), i => i.EndsWith(":hit", StringComparison.Ordinal)));
        }

        [Fact]
        public void PhaseTotals_OrderFromAgileToHeavy()
        {
            Func<string, double> total = s =>
            {
                var p = Preset(s);
                return T(150 * p.N("phase_scale.startup")) + T(100 * p.N("phase_scale.active")) + T(250 * p.N("phase_scale.recovery"));
            };
            AssertAscending(new[] { "agile", "horde", "classic", "precise", "heavy" }, total, "挥击总 tick");
        }

        /// <summary>取消窗口（规则同 01 第 3 节与时间线调度）：打开 = 判定相末映射后夹在判定相内，长度 = 作者窗口长度 × 取消窗口倍率，封顶到动作总长。</summary>
        private static (bool Exists, int Open, int Close) CancelWindowTicks(FeelRules p)
        {
            var scale = p.N("cancel_window_scale");
            var sMs = FeelRules.TimelineMs(Slash, "startup_ms") * p.N("phase_scale.startup");
            var aMs = FeelRules.TimelineMs(Slash, "active_ms") * p.N("phase_scale.active");
            var rMs = FeelRules.TimelineMs(Slash, "recovery_ms") * p.N("phase_scale.recovery");
            var startup = T(sMs);
            var active = T(aMs);
            var total = startup + active + T(rMs);
            var (openMs, closeMs) = FeelRules.CancelWindowMs(Slash, "dodge");
            var origS = FeelRules.TimelineMs(Slash, "startup_ms");
            var origA = FeelRules.TimelineMs(Slash, "active_ms");

            // 作者毫秒 -> 重映射毫秒：本技能的取消窗口打开点落在判定相末（openMs == origS + origA），映射为 sMs + aMs。
            Assert.Equal(origS + origA, openMs);
            var open = Math.Min(Math.Max(T(sMs + aMs), startup), startup + active);
            if (scale <= 0)
            {
                return (false, open, open);
            }

            var close = Math.Min(total, open + T((closeMs - openMs) * scale));
            return (close > open, open, close);
        }

        [Theory]
        [InlineData("classic")]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void CancelWindow_OpensAndClosesAtTheTemplateScaledTicks_ClassicNeverOpens(string style)
        {
            var fp = Fp(style);
            var p = Preset(style);
            var first = Press(style, AttackAction, 0);
            var attacker = p.Ticks("attacker_hitstop_ms");
            var (exists, open, close) = CancelWindowTicks(p);
            var markers = fp.Items("actiontl.markers");

            if (!exists)
            {
                Assert.Equal("classic", style);
                Assert.DoesNotContain(markers, m => m.Contains(":cancel_open:", StringComparison.Ordinal));
                return;
            }

            Assert.Contains($"player@{first + open + attacker}:cancel_open:dodge", markers);
            Assert.Contains($"player@{first + close + attacker}:cancel_close:dodge", markers);
        }

        [Fact]
        public void CancelWindowLengths_OrderByTemplateCharacter()
        {
            Func<string, double> length = s =>
            {
                var (_, open, close) = CancelWindowTicks(Preset(s));
                return close - open;
            };
            AssertAscending(new[] { "classic", "precise", "horde", "heavy" }, length, "取消窗口长度");
            Assert.Equal(0.0, length("classic"));
        }

        [Theory]
        [InlineData("classic")]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void CancelProbe_SucceedsOnlyWhenTheBufferedDodgeMeetsTheOpenWindow(string style)
        {
            var fp = Fp(style);
            var p = Preset(style);
            var second = Press(style, AttackAction, 2);
            var dodge = Press(style, DodgeAction, 0);
            var bufferTicks = p.Ticks("buffer_ms");
            var attacker = p.Ticks("attacker_hitstop_ms");
            var (exists, open, close) = CancelWindowTicks(p);
            var openAbs = second + open + attacker;
            var closeAbs = second + close + attacker;
            var cancelTick = Math.Max(dodge, openAbs);
            // 缓冲里等窗口：等待时长不超过缓冲宽度（命中顿帧期间缓冲计时同样暂停，宽度顺延攻击方顿帧）。
            var success = exists && cancelTick < closeAbs && cancelTick - dodge <= bufferTicks + attacker;

            if (success)
            {
                Assert.Contains($"player@{cancelTick}:CancelInto>{Dodge}", fp.Items("actiontl.cancels"));
                Assert.Contains($"player@{cancelTick}:{Dodge}#0", fp.Items("actiontl.starts"));
            }
            else
            {
                Assert.Empty(fp.Items("actiontl.cancels"));
                Assert.Contains(fp.Items("inputbuf.expire_ticks"), i => i.EndsWith(":lab_a_dodge", StringComparison.Ordinal));
            }
        }

        // ---------- 命中顿帧、受击反应、镜头冲击 ----------

        [Theory]
        [InlineData("classic")]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void Hitstop_AndReaction_EqualTheTemplatePresetByRule(string style)
        {
            var fp = Fp(style);
            var p = Preset(style);
            var first = Press(style, AttackAction, 0);
            var hit = first + T(FeelRules.TimelineMs(Slash, "startup_ms") * p.N("phase_scale.startup"));
            var attacker = p.Ticks("attacker_hitstop_ms");
            var target = p.Ticks("target_hitstop_ms");
            var started = fp.Items("hitstop.started");

            if (attacker == 0 && target == 0)
            {
                Assert.DoesNotContain(started, s => FeelFp.TickOf(s) == hit);
                Assert.Equal(0.0, fp.Num("hitstop.frozen_ticks_player"));
            }
            else if (attacker == target)
            {
                Assert.Contains($"{hit}:player+stake:{attacker}", started);
            }
            else
            {
                Assert.Contains($"{hit}:player:{attacker}", started);
                Assert.Contains($"{hit}:stake:{target}", started);
            }

            var cap = p.S("reaction_cap");
            var stun = p.Ticks("hit_stun_ms");
            if (cap == "none" || stun == 0)
            {
                Assert.Empty(fp.Items("reaction.reactions"));
            }
            else
            {
                Assert.Contains($"{hit}:stake:{ReactionFor(p.S("impact_class"))}:{stun}", fp.Items("reaction.reactions"));
            }
        }

        [Fact]
        public void HitstopFrozenTicks_OrderFromClassicToHeavy()
        {
            AssertAscending(new[] { "classic", "horde", "agile", "precise", "heavy" }, s => Preset(s).N("target_hitstop_ms"), "受击方顿帧毫秒");
            // 实测受击方冻结 tick 数随之同向（相邻档位 tick 取整后允许相等，但首尾必须严格拉开）。
            Func<string, double> measured = s =>
            {
                var text = Fp(s).Text("hitstop.frozen_ticks_targets");
                return text.Length == 0 ? 0 : double.Parse(text.Substring(text.IndexOf('=') + 1).Split(',')[0], System.Globalization.CultureInfo.InvariantCulture);
            };
            var order = new[] { "classic", "horde", "agile", "precise", "heavy" };
            for (var i = 1; i < order.Length; i++)
            {
                Assert.True(measured(order[i - 1]) <= measured(order[i]), $"{order[i - 1]} 冻结 tick 应不大于 {order[i]}");
            }

            Assert.True(measured("classic") < measured("agile"));
            Assert.True(measured("agile") < measured("heavy"));
            // 脚本里两次挥击都命中：累计冻结 tick = 每次受击方顿帧 tick × 命中确认次数。
            foreach (var s in order)
            {
                Assert.Equal(Preset(s).Ticks("target_hitstop_ms") * Fp(s).Items("spatialhit.confirm_ticks").Count, measured(s));
            }
        }

        [Theory]
        [InlineData("classic")]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void CameraImpulse_IsThePresetGainTimesThePackVariantGain_CappedByTheShakeCap(string style)
        {
            var fp = Fp(style);
            var p = Preset(style);
            var gain = p.N("camera_impulse_gain");
            if (gain <= 0)
            {
                Assert.Equal(string.Empty, fp.Text("presentation.camera_cues"));
                Assert.Empty(fp.Nums("presentation.camera_magnitudes"));
                return;
            }

            var variant = PackVariant(style, p.S("impact_class"), "hit");
            var camera = (JsonObject)variant["camera"];
            var packGain = ((JsonNumber)camera["impulse_gain"]).Value;
            var decay = ((JsonNumber)camera["decay_ms"]).Value;
            var expected = Math.Min(gain * packGain, p.N("camera_shake_cap"));

            var magnitudes = fp.Nums("presentation.camera_magnitudes");
            Assert.Equal(2, magnitudes.Count);
            Assert.All(magnitudes, m => Assert.Equal(expected, m, 6));
            Assert.All(fp.Nums("presentation.camera_decay_ms"), d => Assert.Equal(decay, d, 6));
        }

        [Fact]
        public void CameraImpulseAmplitudes_OrderFromAgileToHeavy_ClassicHasNone()
        {
            Func<string, double> magnitude = s =>
            {
                var m = Fp(s).Nums("presentation.camera_magnitudes");
                return m.Count == 0 ? 0 : m[0];
            };
            AssertAscending(new[] { "classic", "agile", "horde", "precise", "heavy" }, magnitude, "镜头冲击幅度");
            Assert.Equal(0.0, magnitude("classic"));
        }

        // ---------- 反馈包、镜头档位、武器外观档（数据覆盖） ----------

        private static JsonObject PackRow(string style) =>
            FeelRules.Row(Path.Combine("data", "_feel_templates", "feedback", "feedback.impact_profile.json"), "feedback.impact_profile.tpl_" + style);

        private static JsonObject PackVariant(string style, string impactClass, string outcome)
        {
            foreach (var v in (JsonArray)PackRow(style)["variants"])
            {
                var o = (JsonObject)v;
                if (((JsonString)o["class"]).Value == impactClass && ((JsonString)o["outcome"]).Value == outcome)
                {
                    return o;
                }
            }

            throw new InvalidOperationException($"{style} 反馈包没有变体 {impactClass}/{outcome}");
        }

        [Theory]
        [InlineData("classic")]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void ImpactPack_CoversEveryImpactTier_WithPlaceholderAssetsOnly(string style)
        {
            var p = Preset(style);
            Assert.Equal("feedback.impact_profile.tpl_" + style, p.S("impact_profile_ref"));

            var classes = new[] { "light", "medium", "heavy", "massive" };
            var hasCamera = p.N("camera_impulse_gain") > 0;
            var previousGain = 0.0;
            foreach (var cls in classes)
            {
                var hit = PackVariant(style, cls, "hit");
                var kill = PackVariant(style, cls, "kill");
                foreach (var variant in new[] { hit, kill })
                {
                    // 资源只经占位引用：特效 id 是框架占位特效，音效只引用手感音效层（层 -> 行由游戏数据根映射）。
                    Assert.Equal(PlaceholderVfx, ((JsonString)((JsonObject)variant["vfx"])["vfx_id"]).Value);
                    Assert.Contains(((JsonArray)variant["sfx"]).Cast<JsonObject>(), s => ((JsonString)s["layer"]).Value == "impact");
                    Assert.Equal(hasCamera, variant.ContainsKey("camera"));
                }

                Assert.True(((JsonNumber)((JsonObject)kill["intensity"])["kill_multiplier"]).Value >= 1.0);
                Assert.Contains(((JsonArray)kill["sfx"]).Cast<JsonObject>(), s => ((JsonString)s["layer"]).Value == "sweetener");
                if (hasCamera)
                {
                    var gain = ((JsonNumber)((JsonObject)hit["camera"])["impulse_gain"]).Value;
                    Assert.True(gain > previousGain, $"{style} 冲击等级 {cls} 的镜头增益应大于上一档");
                    previousGain = gain;
                    var hitDecay = ((JsonNumber)((JsonObject)hit["camera"])["decay_ms"]).Value;
                    var killDecay = ((JsonNumber)((JsonObject)kill["camera"])["decay_ms"]).Value;
                    Assert.True(hitDecay > 0 && killDecay >= hitDecay);
                }
            }

            PackVariant(style, "medium", "avoided");
            var whiff = PackVariant(style, "medium", "whiff");
            Assert.Contains(((JsonArray)whiff["sfx"]).Cast<JsonObject>(), s => ((JsonString)s["layer"]).Value == "whiff");
        }

        [Theory]
        [InlineData("classic")]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void CameraProfile_FollowsTheTemplatePreset_AndCombatZoomStaysInsideTheRange(string style)
        {
            var p = Preset(style);
            var row = FeelRules.Row(Path.Combine("data", "_feel_templates", "camera", "camera_profile.json"), "camera_profile.tpl_" + style);
            var min = ((JsonNumber)row["zoom_min"]).Value;
            var max = ((JsonNumber)row["zoom_max"]).Value;
            var def = ((JsonNumber)row["zoom_default"]).Value;
            Assert.True(min < def && def < max);

            // 战斗缩放系数无论大于还是小于 1，缩放后都在档位的缩放范围内。
            var delta = p.N("camera_combat_zoom_delta");
            Assert.InRange(def * delta, min, max);
            Assert.InRange(def / delta, min, max);

            // 跟随系数 = 一个 1/60 秒步长上的一阶滞后系数（同 07 第 2 节的 a = 1 - exp(-dt/tau)）。
            var lag = p.N("camera_follow_lag_ms");
            Assert.Equal(1 - Math.Exp(-(1000.0 / 60.0) / lag), ((JsonNumber)row["follow_lerp"]).Value, 4);

            // 震屏档位：有镜头冲击的模板才有；幅度 = 冲击基准 × 参考镜头高度（标定 10），时长取反馈包命中变体的衰减。
            var gain = p.N("camera_impulse_gain");
            if (gain <= 0)
            {
                Assert.False(row.ContainsKey("shake_presets"));
                return;
            }

            var shakes = ((JsonArray)row["shake_presets"]).Cast<JsonObject>().ToList();
            var hit = shakes.Single(s => ((JsonString)s["id"]).Value == $"feedback.shake.tpl_{style}_hit");
            var calibration = FeelRules.Row(
                Path.Combine("lab", "fixtures", "data", "feel_templates", "feel", "feel.calibration.json"), "feel.calibration.lab_tpl_" + style);
            var referenceHeight = ((JsonNumber)calibration["reference_camera_height"]).Value;
            Assert.Equal(gain * referenceHeight, ((JsonNumber)hit["amplitude"]).Value, 6);
            var decay = ((JsonNumber)((JsonObject)PackVariant(style, p.S("impact_class"), "hit")["camera"])["decay_ms"]).Value;
            Assert.Equal(decay / 1000.0, ((JsonNumber)hit["duration"]).Value, 6);
        }

        [Fact]
        public void EveryTemplateWeaponRow_HasADisplayStyleOfTheSameId_ResolvingToAPlaceholderPoseFamily()
        {
            var weaponRows = LoadRows(Path.Combine(FeelDir, "feel.weapon.json"));
            Assert.Equal(3 + Styles.Length * 6, weaponRows.Count);
            var styleRows = LoadRows(Path.Combine("data", "_feel_templates", "display", "display.weapon_style.json"));
            var familyAnim = new Dictionary<string, string>
            {
                ["1h"] = "sprite_anim.std_dummy_attack_1h", ["2h"] = "sprite_anim.std_dummy_attack_2h",
                ["polearm"] = "sprite_anim.std_dummy_attack_polearm", ["bow"] = "sprite_anim.std_dummy_attack_bow",
                ["staff"] = "sprite_anim.std_dummy_attack_staff",
            };
            foreach (var weapon in weaponRows)
            {
                var id = ((JsonString)weapon["id"]).Value;
                var style = styleRows.Single(r => ((JsonString)r["id"]).Value == "display.weapon_style." + id.Substring("feel.weapon.".Length));
                Assert.Equal(familyAnim[((JsonString)weapon["family"]).Value], ((JsonString)style["auto_attack_anim"]).Value);
            }
        }

        private static List<JsonObject> LoadRows(string relativePath)
        {
            var root = LabJson.ParseObject(
                File.ReadAllText(Path.Combine(LabTestSupport.RepoRoot(), relativePath), System.Text.Encoding.UTF8), relativePath);
            return ((JsonArray)root["rows"]).Cast<JsonObject>().ToList();
        }
    }
}
