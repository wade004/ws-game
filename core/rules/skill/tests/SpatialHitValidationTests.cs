using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Skill;
using Xunit;
using static Tests.Rules.Skill.SpatialRig;
using static Tests.Rules.Skill.TimelineHarness;

namespace Tests.Rules.Skill
{
    /// <summary>时间线空间命中相关字段的加载期校验（<see cref="SkillTimelineRule"/> 与 <c>SkillSchemas</c> 的 timeline 字段）。</summary>
    public sealed class SpatialHitValidationTests
    {
        private const string ShapedChain = "target.chain.sample";
        private const string ShapelessChain = "target.chain.noshape";

        private static JsonObject ChainRow(string id, bool withShape) =>
            withShape
                ? J.O(("id", J.S(id)), ("source", J.S("all_in_shape")),
                    ("shape", J.O(("kind", J.S("cone")), ("radius", J.N(2)), ("angle", J.N(1.5)))))
                : J.O(("id", J.S(id)), ("source", J.S("current_target")));

        private static SkillWorldBuilder Builder() =>
            new SkillWorldBuilder().ValidationRule(new SkillTimelineRule())
                .TargetChain(ChainRow(ShapedChain, true)).TargetChain(ChainRow(ShapelessChain, false));

        private static ValidationReport Validate(params JsonObject[] skills)
        {
            var builder = Builder();
            foreach (var s in skills) builder.SkillDef(s);
            return builder.Validate();
        }

        private static readonly JsonValue[] NoMarkers = Array.Empty<JsonValue>();

        [Fact]
        public void Continuous_WithAShapedChain_IsValid_AndNoLongerWarnsAsAPlaceholder()
        {
            var report = Validate(SpSkill("skill.sample_spin", 0, 200, 100, NoMarkers, hitPolicy: "continuous"));
            Assert.DoesNotContain(report.Issues, i => i.Check.StartsWith("timeline_"));
            Assert.False(report.IsBlocking);
        }

        [Fact]
        public void Continuous_OnAChainWithoutShape_IsAnError()
        {
            var report = Validate(SpSkill("skill.sample_spin", 0, 200, 100, NoMarkers, hitPolicy: "continuous", chain: ShapelessChain));
            Assert.Contains(report.Issues, i => i.Check == "timeline_continuous_without_shape" && i.Severity == ValidationSeverity.Error);
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void Continuous_WithHitModeInstant_IsAConflictError()
        {
            var report = Validate(SpSkill("skill.sample_spin", 0, 200, 100, NoMarkers, hitPolicy: "continuous", hitMode: "instant"));
            Assert.Contains(report.Issues, i => i.Check == "timeline_continuous_instant_conflict" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void HitModeSpatial_OnAChainWithoutShape_IsAnError_ButInstantOrAutoAreFine()
        {
            var spatial = Validate(SpSkill("skill.sample_a", 50, 200, 100, new[] { HitAt(100) }, hitMode: "spatial", chain: ShapelessChain));
            Assert.Contains(spatial.Issues, i => i.Check == "timeline_spatial_without_shape" && i.Severity == ValidationSeverity.Error);

            var instant = Validate(SpSkill("skill.sample_b", 50, 200, 100, new[] { HitAt(100) }, hitMode: "instant", chain: ShapelessChain));
            var auto = Validate(SpSkill("skill.sample_c", 50, 200, 100, new[] { HitAt(100) }, chain: ShapelessChain));
            Assert.DoesNotContain(instant.Issues, i => i.Check.StartsWith("timeline_"));
            Assert.DoesNotContain(auto.Issues, i => i.Check.StartsWith("timeline_"));
        }

        [Fact]
        public void HitModeValue_IsAnEnum_UnknownValueIsRejectedBySchema()
        {
            var report = Validate(SpSkill("skill.sample_bad", 50, 200, 100, new[] { HitAt(100) }, hitMode: "sweep"));
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void CostAtFirstHit_WithoutHitMarker_IsAnErrorForMarkerPolicy_ButAllowedForContinuous()
        {
            var marker = Validate(SpSkill("skill.sample_a", 50, 200, 100, NoMarkers, costAt: "first_hit", cost: 5));
            Assert.Contains(marker.Issues, i => i.Check == "timeline_cost_first_hit_without_hit");

            var continuous = Validate(SpSkill("skill.sample_b", 0, 200, 100, NoMarkers, hitPolicy: "continuous", costAt: "first_hit", cost: 5));
            Assert.DoesNotContain(continuous.Issues, i => i.Check == "timeline_cost_first_hit_without_hit");
        }

        [Fact]
        public void TargetAssist_ChainRefMustExist_AndCloseDistanceNeedsMotion()
        {
            JsonObject Assist(string chain, string mode = "face_only") =>
                J.O(("chain_ref", J.S(chain)), ("max_distance", J.N(5)), ("max_angle_deg", J.N(90)), ("mode", J.S(mode)));

            var missing = Validate(SpSkill("skill.sample_a", 50, 200, 100, new[] { HitAt(100) }, targetAssist: Assist("target.chain.nowhere")));
            Assert.Contains(missing.Issues, i => i.Check == "timeline_target_assist_chain_missing" && i.Severity == ValidationSeverity.Error);

            var noMotion = Validate(SpSkill("skill.sample_b", 50, 200, 100, new[] { HitAt(100) }, targetAssist: Assist(ShapedChain, "close_distance")));
            Assert.Contains(noMotion.Issues, i => i.Check == "timeline_target_assist_close_distance_without_motion" && i.Severity == ValidationSeverity.Warning);

            var ok = Validate(SpSkill("skill.sample_c", 50, 200, 100, new[] { HitAt(100) }, targetAssist: Assist(ShapedChain)));
            Assert.DoesNotContain(ok.Issues, i => i.Check.StartsWith("timeline_target_assist"));
            Assert.False(ok.IsBlocking);
        }

        [Fact]
        public void TargetAssist_RequiresDistanceAndAngleFields()
        {
            var incomplete = J.O(("chain_ref", J.S(ShapedChain)));
            var report = Validate(SpSkill("skill.sample_a", 50, 200, 100, new[] { HitAt(100) }, targetAssist: incomplete));
            Assert.True(report.IsBlocking);
        }

        private static JsonObject TimelineBlock(JsonObject skill)
        {
            Assert.True(skill.TryGetValue("timeline", out var block));
            return (JsonObject)block;
        }

        [Fact]
        public void TimelineDef_ParsesTheNewFields_WithDefaultsWhenAbsent()
        {
            var plain = SpSkill("skill.sample_plain", 50, 200, 100, new[] { HitAt(100) });
            var full = SpSkill(
                "skill.sample_full", 0, 200, 100, NoMarkers, hitPolicy: "continuous", rehitMs: 80, sampleStepMs: 4, hitMode: "spatial",
                targetAssist: J.O(("chain_ref", J.S(ShapedChain)), ("max_distance", J.N(6)), ("max_angle_deg", J.N(45)), ("mode", J.S("close_distance"))));
            var a = TimelineDef.Parse(TimelineBlock(plain));
            Assert.Equal(TimelineHitMode.Auto, a.HitMode);
            Assert.Equal(0.0, a.SampleStepMs);
            Assert.Equal(0.0, a.RehitIntervalMs);
            Assert.Null(a.TargetAssist);

            var b = TimelineDef.Parse(TimelineBlock(full));
            Assert.Equal(TimelineHitMode.Spatial, b.HitMode);
            Assert.Equal(TimelineHitPolicy.Continuous, b.HitPolicy);
            Assert.Equal(4.0, b.SampleStepMs);
            Assert.Equal(80.0, b.RehitIntervalMs);
            Assert.Equal(new Id(ShapedChain), b.TargetAssist!.ChainRef);
            Assert.Equal(6.0, b.TargetAssist.MaxDistance);
            Assert.Equal(45.0, b.TargetAssist.MaxAngleDeg);
            Assert.Equal(TimelineAssistMode.CloseDistance, b.TargetAssist.Mode);
        }
    }
}
