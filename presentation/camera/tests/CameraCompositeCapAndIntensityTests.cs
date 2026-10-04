using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Feel;
using Presentation.Camera;
using Presentation.VfxSfx.Contracts;
using Tests.Presentation.FeedbackBinder;
using Xunit;
using static Tests.Presentation.FeedbackBinder.ImpactTestKit;

namespace Tests.PresentationCamera
{
    /// <summary>支持镜头冲击与缩放脉冲两个可选能力的假相机（缩放脉冲能力可关）。</summary>
    internal sealed class FullCapabilityCamera : ICamera, ICameraImpulse, ICameraZoomPunch
    {
        public readonly List<(Vec2 Direction, double Magnitude, double DecayMs)> Impulses = new List<(Vec2, double, double)>();
        public readonly List<(double Magnitude, double DecayMs)> Punches = new List<(double, double)>();
        public readonly List<(double Intensity, double Seconds, double Frequency)> Shakes = new List<(double, double, double)>();

        public bool SupportsCameraImpulse { get; set; } = true;

        public bool SupportsCameraZoomPunch { get; set; } = true;

        public void Configure(double pitchDegrees, double yawDegrees, ZoomRange zoomRange) { }

        public void Follow(Vec2 planePos, double smoothing) { }

        public void SetZoom(double zoom) { }

        public Vec2 WorldToScreen(Vec2 planePos, double height) => planePos;

        public Vec2? ScreenToWorld(Vec2 screen) => screen;

        public void Shake(double intensity, double durationSeconds, double frequency) => Shakes.Add((intensity, durationSeconds, frequency));

        public void Impulse(Vec2 direction, double magnitude, double decayMs) => Impulses.Add((direction, magnitude, decayMs));

        public void ZoomPunch(double magnitude, double decayMs) => Punches.Add((magnitude, decayMs));
    }

    internal sealed class FixedIntensity : IFeelIntensity
    {
        public readonly Dictionary<FeelIntensityKind, double> Values = new Dictionary<FeelIntensityKind, double>();

        public double Get(FeelIntensityKind kind) => Values.TryGetValue(kind, out var v) ? v : 1.0;
    }

    /// <summary>
    /// 镜头相机侧合成上限与玩家强度（ADR-0148，手感设计/07 第 2 节）：<c>shake_cap</c> 约束的是仍在衰减的全部冲击与震屏的合成幅度
    /// （方向冲击按向量合成，反向的互相抵消），而不是流水线批内的单个最大值；玩家强度四个系数在出口生效。期望值由规则算出。
    /// </summary>
    public sealed class CameraCompositeCapAndIntensityTests
    {
        private const double Cap = 0.03;
        private const double Eps = 1e-9;
        private static readonly Id Entity = Player;

        private static (CameraHost Host, FullCapabilityCamera Camera) MakeHost(double shakeCap, IFeelIntensity? intensity = null)
        {
            var camera = new FullCapabilityCamera();
            var host = new CameraHost(camera, new ScriptedTarget());
            host.Configure(new CameraProfile(new Id("camera_profile.kit"), 45, 0, 5, 20, 10, 0.2));
            host.Follow(Entity);
            var feel = new ImpactTestKit()
                .WithCharacter(Player, "feel.character.kit_cap", Set(FeelFieldNames.CameraShakeCap, shakeCap))
                .Build();
            host.EnableFeel(id => CameraFeelProfile.FromPresenting(feel.ResolvePresenting(id)), feel.Calibration.ReferenceCameraHeight);
            host.Intensity = intensity;
            return (host, camera);
        }

        private static double SentMagnitudeAlong(FullCapabilityCamera camera, Vec2 axis)
        {
            double sum = 0;
            foreach (var i in camera.Impulses) sum += i.Direction.X * axis.X * i.Magnitude + i.Direction.Y * axis.Y * i.Magnitude;
            return sum;
        }

        // ------------------------------------------------------------------ shake_cap：相机侧合成上限

        [Fact]
        public void ShakeCap_SameDirectionImpulsesStacking_AreClampedToTheCompositeCap()
        {
            var (host, camera) = MakeHost(Cap);
            for (var n = 0; n < 6; n++) host.Impulse(new Vec2(1, 0), 0.02, 120);

            // 复现：旧实现把冲击原样交给相机，6 次同向冲击合成 0.12，是上限的 4 倍。
            Assert.True(host.CompositeMagnitude <= Cap + Eps, "合成幅度 " + host.CompositeMagnitude + " 超过上限 " + Cap);
            Assert.True(SentMagnitudeAlong(camera, new Vec2(1, 0)) <= Cap + Eps);
            Assert.True(host.CapClampedCount > 0);
            // 上限是被用满而不是被丢光：合成幅度等于上限。
            Assert.Equal(Cap, host.CompositeMagnitude, 9);
        }

        [Fact]
        public void ShakeCap_Zero_MeansNoCameraSideLimit_AndPassesImpulsesThroughUnchanged()
        {
            var (host, camera) = MakeHost(0.0);
            for (var n = 0; n < 6; n++) host.Impulse(new Vec2(1, 0), 0.02, 120);

            Assert.Equal(6, camera.Impulses.Count);
            Assert.All(camera.Impulses, i => Assert.Equal(0.02, i.Magnitude));
            Assert.Equal(0, host.CapClampedCount);
        }

        [Fact]
        public void ShakeCap_OppositeDirectionImpulses_CancelInTheComposite_SoBothPassInFull()
        {
            var (host, camera) = MakeHost(Cap);
            host.Impulse(new Vec2(1, 0), 0.02, 120);
            host.Impulse(new Vec2(-1, 0), 0.02, 120);

            // 向量和为 0，合成幅度远低于上限：两次都原样放行。
            Assert.Equal(2, camera.Impulses.Count);
            Assert.Equal(0.02, camera.Impulses[0].Magnitude, 12);
            Assert.Equal(0.02, camera.Impulses[1].Magnitude, 12);
            Assert.True(host.CompositeMagnitude < Eps);
        }

        [Fact]
        public void ShakeCap_CapacityReturnsAsImpulsesDecay()
        {
            var (host, camera) = MakeHost(Cap);
            host.Impulse(new Vec2(1, 0), 0.03, 120); // 用满
            host.Impulse(new Vec2(1, 0), 0.02, 120);
            Assert.Single(camera.Impulses);          // 第二次没有余量

            // 过了衰减时长，合成归零，新冲击全额放行。
            for (var n = 0; n < 10; n++) host.Update(0.5, 0.02);
            Assert.True(host.CompositeMagnitude < Eps);
            host.Impulse(new Vec2(1, 0), 0.02, 120);
            Assert.Equal(2, camera.Impulses.Count);
            Assert.Equal(0.02, camera.Impulses[1].Magnitude, 12);
        }

        [Fact]
        public void ShakeCap_IncapableAdapter_FallbackShakesAreAlsoClamped()
        {
            var camera = new FullCapabilityCamera { SupportsCameraImpulse = false };
            var host = new CameraHost(camera, new ScriptedTarget());
            host.Configure(new CameraProfile(new Id("camera_profile.kit"), 45, 0, 5, 20, 10, 0.2));
            host.Follow(Entity);
            var feel = new ImpactTestKit()
                .WithCharacter(Player, "feel.character.kit_cap", Set(FeelFieldNames.CameraShakeCap, Cap)).Build();
            var reference = feel.Calibration.ReferenceCameraHeight;
            host.EnableFeel(id => CameraFeelProfile.FromPresenting(feel.ResolvePresenting(id)), reference);

            host.Impulse(new Vec2(1, 0), 0.05, 120); // 单次就超过上限

            // 退化通道按"画面高度比例 × 参考镜头高度"换成震屏强度；合成仍受同一个上限约束，单次超限的被缩到上限。
            Assert.True(host.CompositeMagnitude <= Cap + Eps);
            Assert.Single(camera.Shakes);
            Assert.Equal(Cap * reference, camera.Shakes[0].Intensity, 9);
            Assert.True(host.CapClampedCount > 0);
        }

        // ------------------------------------------------------------------ 玩家强度

        [Theory]
        [InlineData(1.0)]
        [InlineData(0.5)]
        [InlineData(0.25)]
        public void UserIntensity_ImpulseCoefficient_ScalesTheMagnitudeSentToTheCamera(double coefficient)
        {
            var intensity = new FixedIntensity();
            intensity.Values[FeelIntensityKind.Impulse] = coefficient;
            var (host, camera) = MakeHost(0.0, intensity);

            host.Impulse(new Vec2(1, 0), 0.02, 120);

            Assert.Equal(0.02 * coefficient, camera.Impulses[0].Magnitude, 12);
        }

        [Fact]
        public void UserIntensity_Zero_TurnsImpulseAndZoomPunchOff()
        {
            var intensity = new FixedIntensity();
            intensity.Values[FeelIntensityKind.Impulse] = 0;
            var (host, camera) = MakeHost(0.0, intensity);

            host.Impulse(new Vec2(1, 0), 0.02, 120);
            host.ZoomPunch(0.05, 120);

            Assert.Empty(camera.Impulses);
            Assert.Empty(camera.Punches);
        }

        [Fact]
        public void UserIntensity_Default_IsOne_SoAnUninjectedHostIsBitIdentical()
        {
            var (hostA, cameraA) = MakeHost(0.0, null);
            var (hostB, cameraB) = MakeHost(0.0, new FixedIntensity());
            hostA.Impulse(new Vec2(0.6, 0.8), 0.02, 120);
            hostB.Impulse(new Vec2(0.6, 0.8), 0.02, 120);
            Assert.Equal(cameraA.Impulses, cameraB.Impulses);
        }

        // ------------------------------------------------------------------ 缩放脉冲

        [Fact]
        public void ZoomPunch_CapableAdapter_ReceivesTheScaledPunch_IncapableIsIgnoredWithOneDiagnostic()
        {
            var intensity = new FixedIntensity();
            intensity.Values[FeelIntensityKind.Impulse] = 0.5;
            var (host, camera) = MakeHost(0.0, intensity);
            host.ZoomPunch(0.04, 100);
            Assert.Equal((0.02, 100.0), camera.Punches[0]);
            Assert.True(host.SupportsZoomPunch);

            var incapable = new FullCapabilityCamera { SupportsCameraZoomPunch = false };
            var hostB = new CameraHost(incapable, new ScriptedTarget());
            hostB.ZoomPunch(0.04, 100);
            hostB.ZoomPunch(0.04, 100);
            Assert.Empty(incapable.Punches);
            Assert.False(hostB.SupportsZoomPunch);
            var diagnostics = (PresentationDiagnosticsRecorder)hostB.Diagnostics;
            Assert.Single(diagnostics.Warnings, w => w.Contains("ICameraZoomPunch"));
        }
    }
}
