using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Core.Foundation.Common.Json;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 镜头与音画反馈脚本 <c>feel_av_feedback</c>（ADR-0148，手感设计/07）：预设钉死为 <c>feel.preset.av_feedback</c>（镜头距离衰减 <c>linear:6</c>），
    /// 反馈包换成带缩放脉冲与手柄震动的实验室变体行 <c>lab_av</c>。期望值全部由预设字段、标定与档案行按规则算出，不写死裸数；
    /// 对照变体只换预设（<c>arpg_responsive</c> 的旧写法 <c>linear</c> = 不衰减），证明衰减来自取值本身。
    /// </summary>
    public sealed class FeelAvFeedbackTests
    {
        private const string Script = "feel_av_feedback";
        private const string Cell = "2d_action";
        private static readonly string DataDir = Path.Combine("lab", "fixtures", "data", "av_feedback");

        private static JsonObject Variant(string impactClass, string outcome)
        {
            var row = FeelRules.Row(Path.Combine(DataDir, "feedback", "feedback.impact_profile.json"), "feedback.impact_profile.lab_av");
            foreach (var v in (JsonArray)row["variants"])
            {
                var o = (JsonObject)v;
                if (((JsonString)o["class"]).Value == impactClass && ((JsonString)o["outcome"]).Value == outcome)
                {
                    return o;
                }
            }

            throw new InvalidOperationException($"{impactClass}/{outcome}");
        }

        private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

        private static (double Magnitude, double Zoom, string Rumble) Observe(LabRunVariant? variant = null)
        {
            var fp = FeelFp.Of(Script, Cell, variant);
            var ops = fp.Items("presentation.other_ops");
            var zoom = ops.Single(o => o.Contains(":zoom_punch:")).Split(':')[2];
            var rumble = ops.Single(o => o.Contains(":rumble:")).Split(':')[2];
            return (fp.Nums("presentation.camera_magnitudes").Single(), Number(zoom), rumble);
        }

        [Fact]
        public void DistanceAttenuation_LinearSpan_ScalesImpulseAndZoomPunchByTheSameFactor_LegacyLinearDoesNot()
        {
            var preset = FeelRules.Preset("feel.preset.arpg_responsive"); // av_feedback 继承它，只覆盖距离衰减
            var overrides = (JsonObject)FeelRules.Row(Path.Combine(DataDir, "feel", "feel.preset.json"), "feel.preset.av_feedback")["values"];
            var span = Number(((JsonString)overrides["camera_distance_attenuation"]).Value.Substring("linear:".Length));
            var impactClass = preset.S("impact_class");
            var variant = Variant(impactClass, "hit");
            var camera = (JsonObject)variant["camera"];
            var calibration = FeelRules.Row(Path.Combine(DataDir, "feel", "feel.calibration.json"), "feel.calibration.lab_av_feedback");
            var referenceHeight = ((JsonNumber)calibration["reference_height"]).Value;
            var record = LabTestSupport.Runner.Record(LabTestSupport.Script(Script), Cell);
            var distance = (record.Dummies.Single().Value - LabTestSupport.Script(Script).Meta.PlayerStart).Length;

            var factor = Math.Max(0.0, 1.0 - distance / referenceHeight / span);
            var baseGain = preset.N("camera_impulse_gain");
            var variantGain = ((JsonNumber)camera["impulse_gain"]).Value;
            var zoomDeclared = ((JsonNumber)camera["zoom_punch"]).Value;

            var attenuated = Observe();
            Assert.Equal(baseGain * variantGain * factor, attenuated.Magnitude, 6);
            Assert.Equal(zoomDeclared * factor, attenuated.Zoom, 6);
            Assert.True(factor > 0 && factor < 1, "场景前提：命中点落在衰减跨度之内");

            // 对照：同一脚本只换预设为旧写法 linear —— 不衰减，幅度回到增益 × 变体增益、缩放脉冲回到声明值。
            var legacy = Observe(new LabRunVariant { PresetId = "feel.preset.arpg_responsive" });
            Assert.Equal(baseGain * variantGain, legacy.Magnitude, 6);
            Assert.Equal(zoomDeclared, legacy.Zoom, 6);
            Assert.Equal(legacy.Magnitude * factor, attenuated.Magnitude, 6);
        }

        [Fact]
        public void Rumble_IsEmittedOncePerHitBatch_WithTheDeclaredStrengthAndDuration()
        {
            var preset = FeelRules.Preset("feel.preset.arpg_responsive");
            var rumble = (JsonObject)Variant(preset.S("impact_class"), "hit")["rumble"];
            var strength = ((JsonNumber)rumble["strength"]).Value;
            var duration = ((JsonNumber)rumble["duration_ms"]).Value;

            var fp = FeelFp.Of(Script, Cell);
            var entries = fp.Items("presentation.other_ops").Where(o => o.Contains(":rumble:")).ToList();
            Assert.Single(entries);
            // 命中批出的同一 tick（与镜头冲击同 tick）。
            var cueTick = fp.Items("presentation.camera_cues").Single().Split(':')[0];
            Assert.Equal(cueTick + ":rumble:" + strength.ToString("R", CultureInfo.InvariantCulture) + "@" + duration.ToString("R", CultureInfo.InvariantCulture), entries[0]);
        }

        [Fact]
        public void ExistingScripts_EmitNoZoomPunchOrRumble_BecauseTheirProfileDeclaresNone()
        {
            // 不变量（缺省逐位不变）：既有脚本用 lab_default 档案行，没有 zoom_punch / rumble 声明，表现时间线不出现这两类条目。
            var fp = FeelFp.Of("feel_melee", Cell);
            Assert.DoesNotContain(fp.Items("presentation.other_ops"), o => o.Contains(":zoom_punch:") || o.Contains(":rumble:"));
        }
    }
}
