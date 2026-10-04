using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;
using static Tests.Presentation.FeedbackBinder.ImpactTestKit;
using FeedbackBinderCore = Presentation.FeedbackBinder.Core.FeedbackBinder;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>
    /// 镜头与音画反馈（ADR-0148，手感设计/07）的反馈包层：距离衰减取值（none / 旧写法 linear / linear:跨度）、缩放脉冲提示、手柄震动提示、
    /// 闪白对齐 impact 标记。期望值由写入的字段值与规则公式算出。
    /// </summary>
    public sealed class ImpactAudioVisualTests
    {
        private const double Gain = 0.02;
        private static readonly Id ProfileId = new Id("feedback.impact_profile.kit_av");
        private static readonly Id Enemy = new Id("unit.kit_enemy_1");

        private sealed class FakeGate : IAnimMarkerGate
        {
            public readonly List<(Id Entity, string Marker, Action Action, double Timeout)> Deferred = new List<(Id, string, Action, double)>();

            public void DeferUntilMarker(Id entityId, string marker, Action action, double timeoutSeconds) =>
                Deferred.Add((entityId, marker, action, timeoutSeconds));
        }

        private static ImpactPipeline MakePipeline(
            ImpactVariant variant, string attenuation = "none", double referenceHeight = 2.0, PresentationDiagnosticsRecorder? diagnostics = null)
        {
            var kit = new ImpactTestKit()
                .WithWeapon(Player, "feel.weapon.kit_av", Set(FeelFieldNames.CameraImpulseGain, Gain), Set(FeelFieldNames.ImpactProfileRef, ProfileId.Value))
                .WithCharacter(Player, "feel.character.kit_av", Set(FeelFieldNames.CameraShakeCap, 0.2), Set(FeelFieldNames.CameraDistanceAttenuation, attenuation));
            var profile = new ImpactProfile(ProfileId, new[] { variant });
            var options = new ImpactOptions
            {
                FeelSource = new PresentingImpactFeelSource(kit.Build()),
                ProfileResolver = id => id.Equals(ProfileId) ? profile : null,
                CameraOwnerResolver = () => Player,
                PositionResolver = id => id.Equals(Player) ? new Vec2(0, 0) : new Vec2(10, 0),
                ReferenceHeight = referenceHeight,
                StepSeconds = StepSeconds,
            };
            return new ImpactPipeline(options, diagnostics);
        }

        private static ImpactVariant CameraVariant(double zoomPunch = 0.0, ImpactRumbleSpec? rumble = null, ImpactFlashSpec? flash = null) =>
            new ImpactVariant(
                "medium", ImpactOutcome.Hit, flash, null, Array.Empty<ImpactSfxSpec>(),
                new ImpactCameraSpec(1.0, null, 120, zoomPunch), null, null, ImpactFreezeLayers.Default, null, rumble);

        private static double CameraMagnitudeAt(ImpactPipeline pipeline, Vec2 contact)
        {
            pipeline.Offer(Hit(Player, Enemy, contact: contact), null);
            return pipeline.Flush()!.Camera!.Magnitude;
        }

        // ------------------------------------------------------------------ 距离衰减取值

        [Fact]
        public void Attenuation_None_NeverAttenuates_AtAnyDistance()
        {
            var pipeline = MakePipeline(CameraVariant(), "none");
            Assert.Equal(Gain, CameraMagnitudeAt(pipeline, new Vec2(1, 0)), 12);
            pipeline.SetTick(100);
            Assert.Equal(Gain, CameraMagnitudeAt(pipeline, new Vec2(500, 0)), 12);
        }

        [Fact]
        public void Attenuation_LegacyBareLinear_KeepsTheOldBehavior_AndWarnsOnceWithAMigrationHint()
        {
            // 复现：旧数据 "linear" 名义上是线性衰减、实际从来不衰减（要查曲线表而没有 CurveResolver）。行为保持，但要有迁移提示。
            var diagnostics = new PresentationDiagnosticsRecorder();
            var pipeline = MakePipeline(CameraVariant(), "linear", diagnostics: diagnostics);

            Assert.Equal(Gain, CameraMagnitudeAt(pipeline, new Vec2(100, 0)), 12);
            pipeline.SetTick(100);
            Assert.Equal(Gain, CameraMagnitudeAt(pipeline, new Vec2(100, 0)), 12);

            var warning = Assert.Single(diagnostics.Warnings, w => w.Contains("none") && w.Contains("linear"));
            Assert.Contains("linear:", warning);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(1.0)]
        [InlineData(2.5)]
        [InlineData(4.0)]
        [InlineData(7.0)]
        public void Attenuation_LinearWithSpan_FallsLinearlyToZeroOverTheSpanInBodyHeights(double bodyHeights)
        {
            const double span = 5.0;
            const double reference = 2.0;
            var pipeline = MakePipeline(CameraVariant(), "linear:" + span, reference);

            var magnitude = pipeline.Offer(Hit(Player, Enemy, contact: new Vec2(bodyHeights * reference, 0)), null)
                ? pipeline.Flush()!.Camera?.Magnitude ?? 0.0
                : 0.0;

            var expected = Gain * Math.Max(0.0, 1.0 - bodyHeights / span);
            Assert.Equal(expected, magnitude, 12);
        }

        [Fact]
        public void Attenuation_DistanceAttenuationHelpers_ParseAndRejectMalformedValues()
        {
            Assert.True(global::Presentation.Camera.DistanceAttenuation.TryParseLinearSpan("linear:5", out var span));
            Assert.Equal(5.0, span);
            Assert.False(global::Presentation.Camera.DistanceAttenuation.TryParseLinearSpan("linear:0", out _));
            Assert.False(global::Presentation.Camera.DistanceAttenuation.TryParseLinearSpan("linear:abc", out _));
            Assert.False(global::Presentation.Camera.DistanceAttenuation.TryParseLinearSpan("linear", out _));
            Assert.False(global::Presentation.Camera.DistanceAttenuation.IsWellFormed("linear:-3"));
            Assert.True(global::Presentation.Camera.DistanceAttenuation.IsWellFormed("curve.kit_falloff"));
        }

        // ------------------------------------------------------------------ 缩放脉冲 / 手柄震动提示

        [Fact]
        public void ZoomPunch_IsCarriedOnTheCue_ScaledLikeTheImpulse_AndZeroByDefault()
        {
            const double zoom = 0.06;
            const double span = 10.0;
            var punch = MakePipeline(CameraVariant(zoomPunch: zoom), "linear:" + span);
            punch.Offer(Hit(Player, Enemy, contact: new Vec2(10, 0)), null); // 距离 10 / 参考身高 2 = 5 个身高
            var cue = punch.Flush()!.Camera!;
            var factor = 1.0 - 5.0 / span;
            Assert.Equal(zoom * factor, cue.ZoomPunch, 12);
            Assert.Equal(Gain * factor, cue.Magnitude, 12);

            var plain = MakePipeline(CameraVariant());
            plain.Offer(Hit(Player, Enemy), null);
            Assert.Equal(0.0, plain.Flush()!.Camera!.ZoomPunch);
        }

        [Fact]
        public void Rumble_CueTakesTheStrongestHit_ScaledByIntensityRules_ClampedToOne()
        {
            var pipeline = MakePipeline(CameraVariant(rumble: new ImpactRumbleSpec(0.5, 90)));
            pipeline.Offer(Hit(Player, Enemy), null);
            var cue = pipeline.Flush()!.Rumble!;
            Assert.Equal(0.5, cue.Strength, 12); // 中性强度规则：因子 1
            Assert.Equal(90.0, cue.DurationMs);

            var none = MakePipeline(CameraVariant());
            none.Offer(Hit(Player, Enemy), null);
            Assert.Null(none.Flush()!.Rumble);
        }

        [Fact]
        public void Rumble_OnlyForHitsInvolvingTheCameraOwner()
        {
            var pipeline = MakePipeline(CameraVariant(rumble: new ImpactRumbleSpec(1.0, 100)));
            // 与镜头持有者（玩家）无关的两个单位之间的命中不震手柄。
            pipeline.Offer(Hit(new Id("unit.other_a"), new Id("unit.other_b")), null);
            var batch = pipeline.Flush();
            Assert.True(batch == null || batch.Rumble == null);
        }

        [Fact]
        public void RumbleAndZoomSpecs_RejectOutOfRangeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ImpactRumbleSpec(1.5, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ImpactRumbleSpec(0.5, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ImpactCameraSpec(1.0, null, 120, -0.1));
        }

        // ------------------------------------------------------------------ 经 FeedbackBinder：震动出口、闪白对齐 impact 标记

        private static (FeedbackBinderCore Binder, IEventBus Bus, ImpactRecordingSink Sink) MakeBinder(ImpactVariant variant, IAnimMarkerGate? gate)
        {
            var kit = new ImpactTestKit()
                .WithWeapon(Player, "feel.weapon.kit_av", Set(FeelFieldNames.CameraImpulseGain, Gain), Set(FeelFieldNames.ImpactProfileRef, ProfileId.Value))
                .WithCharacter(Player, "feel.character.kit_av", Set(FeelFieldNames.CameraShakeCap, 0.2));
            var profile = new ImpactProfile(ProfileId, new[] { variant });
            var options = new ImpactOptions
            {
                FeelSource = new PresentingImpactFeelSource(kit.Build()),
                ProfileResolver = id => id.Equals(ProfileId) ? profile : null,
                CameraOwnerResolver = () => Player,
                StepSeconds = StepSeconds,
            };
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new ImpactRecordingSink();
            var rules = new[]
            {
                new FeedbackRule(new Id("feedback.impact_default"), RulesEventKeys.CombatHitConfirmed, null,
                    new FeedbackAction[] { new PlayImpactAction(null) }),
            };
            var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink,
                null, null, null, null, null, null, null, null, new ImpactPipeline(options));
            binder.MarkerGate = gate;
            return (binder, bus, sink);
        }

        private static CombatHitConfirmedEvent HitEvent() =>
            new CombatHitConfirmedEvent(
                new Id("attack.kit_1"), 0, Player, Enemy, null, HitResult.Hit, 10, 1.0, false, false,
                new Vec2(1, 1), new Vec2(0, 1), new Vec2(1, 0), "medium", 0, 0, HitReaction.Flinch, null);

        [Fact]
        public void Rumble_ReachesTheSinkOnce_PerTickBatch()
        {
            var (binder, bus, sink) = MakeBinder(CameraVariant(rumble: new ImpactRumbleSpec(0.7, 80)), null);
            bus.PublishImmediate(HitEvent());
            bus.PublishImmediate(new SimTickFinishedEvent(1));

            var rumble = Assert.Single(sink.Rumbles);
            Assert.Equal(0.7, rumble.Strength, 12);
            Assert.Equal(80.0, rumble.DurationMs);
            binder.Dispose();
        }

        [Fact]
        public void FlashSync_Immediate_FlashesInTheSameBatch_AsBefore()
        {
            var flash = new ImpactFlashSpec(new Id("camera_profile.flash_hit"), FeedbackAttachTarget.Target);
            var gate = new FakeGate();
            var (binder, bus, sink) = MakeBinder(CameraVariant(flash: flash), gate);
            bus.PublishImmediate(HitEvent());
            bus.PublishImmediate(new SimTickFinishedEvent(1));

            Assert.Single(sink.Flashes);
            Assert.Empty(gate.Deferred);
            binder.Dispose();
        }

        [Fact]
        public void FlashSync_ImpactMarker_DefersTheFlashToTheMarkerGate_ThenFlashesWhenItIsReleased()
        {
            var flash = new ImpactFlashSpec(new Id("camera_profile.flash_hit"), FeedbackAttachTarget.Target, ImpactFlashSync.ImpactMarker);
            var gate = new FakeGate();
            var (binder, bus, sink) = MakeBinder(CameraVariant(flash: flash), gate);
            bus.PublishImmediate(HitEvent());
            bus.PublishImmediate(new SimTickFinishedEvent(1));

            // 复现：旧实现没有同步方式，闪白一律在批出时立即闪。
            Assert.Empty(sink.Flashes);
            var deferred = Assert.Single(gate.Deferred);
            Assert.Equal(Enemy, deferred.Entity);
            Assert.Equal("impact", deferred.Marker);
            Assert.True(deferred.Timeout > 0);

            deferred.Action();
            Assert.Equal((Enemy, new Id("camera_profile.flash_hit")), Assert.Single(sink.Flashes));
            binder.Dispose();
        }

        [Fact]
        public void FlashSync_ImpactMarker_WithoutAGate_DegradesToAnImmediateFlash()
        {
            var flash = new ImpactFlashSpec(new Id("camera_profile.flash_hit"), FeedbackAttachTarget.Target, ImpactFlashSync.ImpactMarker);
            var (binder, bus, sink) = MakeBinder(CameraVariant(flash: flash), null);
            bus.PublishImmediate(HitEvent());
            bus.PublishImmediate(new SimTickFinishedEvent(1));

            Assert.Single(sink.Flashes);
            binder.Dispose();
        }
    }
}
