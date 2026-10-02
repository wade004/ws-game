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
    /// <summary>记录每一次调用（含完整比特）的 <see cref="ICamera"/> 假实现，可选声明镜头冲击能力。</summary>
    internal sealed class FeelRecordingCamera : ICamera, ICameraImpulse
    {
        public readonly List<(long X, long Y, long Smoothing)> Follows = new List<(long, long, long)>();
        public readonly List<Vec2> FollowPositions = new List<Vec2>();
        public readonly List<double> FollowSmoothings = new List<double>();
        public readonly List<long> Zooms = new List<long>();
        public readonly List<double> ZoomValues = new List<double>();
        public readonly List<(double Intensity, double Seconds, double Frequency)> Shakes = new List<(double, double, double)>();
        public readonly List<(Vec2 Direction, double Magnitude, double DecayMs)> Impulses = new List<(Vec2, double, double)>();

        public bool SupportsCameraImpulse { get; set; }

        public void Configure(double pitchDegrees, double yawDegrees, ZoomRange zoomRange) { }

        public void Follow(Vec2 planePos, double smoothing)
        {
            Follows.Add((BitConverter.DoubleToInt64Bits(planePos.X), BitConverter.DoubleToInt64Bits(planePos.Y), BitConverter.DoubleToInt64Bits(smoothing)));
            FollowPositions.Add(planePos);
            FollowSmoothings.Add(smoothing);
        }

        public void SetZoom(double zoom)
        {
            Zooms.Add(BitConverter.DoubleToInt64Bits(zoom));
            ZoomValues.Add(zoom);
        }

        public Vec2 WorldToScreen(Vec2 planePos, double height) => planePos;

        public Vec2? ScreenToWorld(Vec2 screen) => screen;

        public void Shake(double intensity, double durationSeconds, double frequency) => Shakes.Add((intensity, durationSeconds, frequency));

        public void Impulse(Vec2 direction, double magnitude, double decayMs) => Impulses.Add((direction, magnitude, decayMs));
    }

    /// <summary>脚本化跟随目标：测试每帧设定位置。</summary>
    internal sealed class ScriptedTarget : ICameraFollowTarget
    {
        public Vec2 Position;

        public Vec2 GetPosition(Id entityId, double alpha) => Position;
    }

    /// <summary>
    /// 镜头手感档案（手感设计/07 第 2 节）运行时冒烟：缺省档案下镜头输出与改动前逐位一致；非缺省档案下
    /// 跟随轨迹等于一阶滞后公式、死区内不动、前瞻随速度方向、战斗缩放线性过渡；冲击不支持时退化为震屏并记诊断。
    /// 期望值由档案字段与标定（参考身高 2、参考镜头高度 10）按规则算出。
    /// </summary>
    public sealed class CameraFeelTests
    {
        private static readonly Id Entity = Player;
        private const double Dt = 1.0 / 60.0;

        private static CameraProfile Profile() =>
            new CameraProfile(new Id("camera_profile.kit"), pitchDegrees: 45, yawDegrees: 0, zoomMin: 5, zoomMax: 20, zoomDefault: 10, followLerp: 0.2);

        private static (CameraHost Host, FeelRecordingCamera Camera, ScriptedTarget Target) MakeHost(FeelResolver? feel)
        {
            var camera = new FeelRecordingCamera();
            var target = new ScriptedTarget();
            var host = new CameraHost(camera, target);
            host.Configure(Profile());
            host.Follow(Entity);
            if (feel != null)
            {
                host.EnableFeel(id => CameraFeelProfile.FromPresenting(feel.ResolvePresenting(id)), feel.Calibration.ReferenceCameraHeight);
            }
            return (host, camera, target);
        }

        private static FeelResolver NonNeutral(params FeelWrite[] characterWrites) =>
            new ImpactTestKit().WithCharacter(Player, "feel.character.kit_cam", characterWrites).Build();

        private static Vec2 Path(int frame) => new Vec2(Math.Sin(frame * 0.07) * 6.0 + frame * 0.05, Math.Cos(frame * 0.05) * 3.0);

        // ------------------------------------------------------------------ 缺省档案：逐位一致

        [Fact]
        public void DefaultProfile_CameraOutputSequence_IsBitIdenticalToWithoutFeel()
        {
            var plain = MakeHost(null);
            var feelDefault = MakeHost(new ImpactTestKit().Build()); // rpg_classic：镜头组全部中性
            for (var frame = 0; frame < 120; frame++)
            {
                plain.Target.Position = Path(frame);
                feelDefault.Target.Position = Path(frame);
                plain.Host.Update(0.5, Dt);
                feelDefault.Host.Update(0.5, Dt);
                if (frame == 40) { plain.Host.SetInCombat(true); feelDefault.Host.SetInCombat(true); }
                if (frame == 80) { plain.Host.SetInCombat(false); feelDefault.Host.SetInCombat(false); }
            }

            Assert.Equal(120, plain.Camera.Follows.Count);
            Assert.Equal(plain.Camera.Follows, feelDefault.Camera.Follows);
            // 缩放调用序列同样逐位一致（战斗缩放倍率为 1 → 不发任何战斗缩放）。
            Assert.Equal(plain.Camera.Zooms, feelDefault.Camera.Zooms);
            // 这条序列不是平凡的：跟随位置确实随路径变化，平滑参数是档案的 follow_lerp。
            Assert.NotEqual(plain.Camera.FollowPositions[0], plain.Camera.FollowPositions[100]);
            Assert.All(plain.Camera.FollowSmoothings, s => Assert.Equal(0.2, s));
        }

        [Fact]
        public void NonDefaultProfile_ChangesTheOutputSequence()
        {
            var plain = MakeHost(null);
            var lagged = MakeHost(NonNeutral(Set(FeelFieldNames.CameraDampingXMs, 200)));
            for (var frame = 0; frame < 30; frame++)
            {
                plain.Target.Position = Path(frame);
                lagged.Target.Position = Path(frame);
                plain.Host.Update(0.5, Dt);
                lagged.Host.Update(0.5, Dt);
            }

            Assert.NotEqual(plain.Camera.Follows, lagged.Camera.Follows);
        }

        // ------------------------------------------------------------------ 阻尼 = 一阶滞后公式

        [Fact]
        public void Damping_TrajectoryEqualsFirstOrderLagFormula_PerAxis()
        {
            const double dampX = 150.0;
            const double dampY = 60.0;
            var (host, camera, target) = MakeHost(NonNeutral(
                Set(FeelFieldNames.CameraDampingXMs, dampX), Set(FeelFieldNames.CameraDampingYMs, dampY)));

            target.Position = new Vec2(0, 0);
            host.Update(0.5, Dt); // 首帧对齐到目标
            Assert.Equal(new Vec2(0, 0), camera.FollowPositions[0]);

            // 目标阶跃到 (10, -4)，之后保持。
            target.Position = new Vec2(10, -4);
            double x = 0, y = 0;
            var ax = 1.0 - Math.Exp(-Dt / (dampX / 1000.0));
            var ay = 1.0 - Math.Exp(-Dt / (dampY / 1000.0));
            for (var n = 1; n <= 20; n++)
            {
                x += (10 - x) * ax;
                y += (-4 - y) * ay;
                host.Update(0.5, Dt);
                Assert.Equal(x, camera.FollowPositions[n].X, 12);
                Assert.Equal(y, camera.FollowPositions[n].Y, 12);
            }

            // 到达前单调逼近、不越过目标。
            Assert.True(camera.FollowPositions[20].X < 10);
            Assert.True(camera.FollowPositions[20].X > camera.FollowPositions[10].X);
        }

        [Fact]
        public void FollowLag_ConvertsToFollowSmoothingTimeConstantInSeconds()
        {
            var (host, camera, target) = MakeHost(NonNeutral(Set(FeelFieldNames.CameraFollowLagMs, 250)));
            target.Position = new Vec2(1, 1);

            host.Update(0.5, Dt);

            Assert.Equal(0.25, camera.FollowSmoothings[0], 12);
        }

        // ------------------------------------------------------------------ 死区

        [Fact]
        public void DeadZone_TargetInsideDoesNotMoveCamera_OutsideShiftsFocusToTheEdge()
        {
            const double widthBodyHeights = 1.0; // 绝对宽 = 1 × 参考身高 2 = 2，半宽 1
            var height = Calibration().ReferenceHeight;
            var halfWidth = widthBodyHeights * height / 2.0;
            var (host, camera, target) = MakeHost(NonNeutral(
                Set(FeelFieldNames.CameraDeadZoneWidth, widthBodyHeights), Set(FeelFieldNames.CameraDeadZoneHeight, widthBodyHeights)));

            target.Position = new Vec2(0, 0);
            host.Update(0.5, Dt);

            target.Position = new Vec2(halfWidth * 0.9, -halfWidth * 0.9); // 死区内
            host.Update(0.5, Dt);
            Assert.Equal(new Vec2(0, 0), camera.FollowPositions[1]);

            target.Position = new Vec2(5, 0); // 死区外：关注点停在"目标 - 半宽"
            host.Update(0.5, Dt);
            Assert.Equal(5 - halfWidth, camera.FollowPositions[2].X, 12);
            Assert.Equal(0.0, camera.FollowPositions[2].Y, 12);
        }

        // ------------------------------------------------------------------ 前瞻

        [Fact]
        public void LookAhead_FollowsVelocityDirection_AndLagSmoothsTheOffset()
        {
            const double lookAheadBodyHeights = 0.5; // 绝对前瞻 = 0.5 × 2 = 1
            var ahead = lookAheadBodyHeights * Calibration().ReferenceHeight;
            var (host, camera, target) = MakeHost(NonNeutral(Set(FeelFieldNames.CameraLookAhead, lookAheadBodyHeights)));

            target.Position = new Vec2(0, 0);
            host.Update(0.5, Dt);
            // 沿 +x 匀速、无前瞻滞后：关注点 = 目标 + 前瞻 × 速度单位方向。
            target.Position = new Vec2(0.1, 0);
            host.Update(0.5, Dt);
            Assert.Equal(0.1 + ahead, camera.FollowPositions[1].X, 12);
            Assert.Equal(0.0, camera.FollowPositions[1].Y, 12);

            // 反向：偏移翻到 -x 方向。
            target.Position = new Vec2(0.0, 0);
            host.Update(0.5, Dt);
            Assert.Equal(0.0 - ahead, camera.FollowPositions[2].X, 12);

            // 静止：偏移回零。
            host.Update(0.5, Dt);
            Assert.Equal(0.0, camera.FollowPositions[3].X, 12);
        }

        [Fact]
        public void LookAheadLag_PassesThroughZeroOnReversal_InsteadOfSnapping()
        {
            const double lag = 250.0;
            var ahead = 0.5 * Calibration().ReferenceHeight;
            var (host, camera, target) = MakeHost(NonNeutral(
                Set(FeelFieldNames.CameraLookAhead, 0.5), Set(FeelFieldNames.CameraLookAheadLagMs, lag)));

            target.Position = new Vec2(0, 0);
            host.Update(0.5, Dt);
            var a = 1.0 - Math.Exp(-Dt / (lag / 1000.0));
            double offset = 0;
            double tx = 0;
            for (var i = 1; i <= 10; i++)
            {
                tx += 0.1;
                target.Position = new Vec2(tx, 0);
                offset += (ahead - offset) * a;
                host.Update(0.5, Dt);
                Assert.Equal(tx + offset, camera.FollowPositions[i].X, 12);
            }

            // 反向走：期望偏移翻号，实际偏移按同一滞后公式从正值穿过零，不是瞬间翻转。
            tx -= 0.1;
            target.Position = new Vec2(tx, 0);
            offset += (-ahead - offset) * a;
            host.Update(0.5, Dt);
            Assert.Equal(tx + offset, camera.FollowPositions[11].X, 12);
            Assert.True(offset > -ahead && offset < ahead);
        }

        // ------------------------------------------------------------------ 战斗缩放

        [Fact]
        public void CombatZoom_BlendsLinearlyToDeltaAndBack_NeutralDeltaNeverTouchesZoom()
        {
            const double delta = 1.5;
            const double blendMs = 300.0;
            var (host, camera, target) = MakeHost(NonNeutral(
                Set(FeelFieldNames.CameraCombatZoomDelta, delta), Set(FeelFieldNames.CameraCombatZoomBlendMs, blendMs)));
            target.Position = new Vec2(0, 0);
            var zoomCallsAfterConfigure = camera.ZoomValues.Count; // Configure 已设 10

            host.Update(0.5, Dt);
            Assert.Equal(zoomCallsAfterConfigure, camera.ZoomValues.Count); // 非战斗：不动

            host.SetInCombat(true);
            host.Update(0.5, Dt);
            var step = Math.Abs(delta - 1.0) * (Dt * 1000.0) / blendMs; // 每帧系数增量
            Assert.Equal(10.0 * (1.0 + step), camera.ZoomValues[^1], 12);

            for (var i = 0; i < 40; i++) host.Update(0.5, Dt);
            Assert.Equal(10.0 * delta, camera.ZoomValues[^1], 12); // 过渡时长之后停在 基准 × 倍率
            Assert.Equal(delta, host.CombatZoomFactor, 12);

            host.SetInCombat(false);
            for (var i = 0; i < 40; i++) host.Update(0.5, Dt);
            Assert.Equal(10.0, camera.ZoomValues[^1], 12);
            Assert.Equal(1.0, host.CombatZoomFactor, 12);
        }

        [Fact]
        public void SetZoom_DuringCombatZoom_KeepsTheCombatFactorOnTopOfTheNewBase()
        {
            var (host, camera, target) = MakeHost(NonNeutral(
                Set(FeelFieldNames.CameraCombatZoomDelta, 1.2), Set(FeelFieldNames.CameraCombatZoomBlendMs, 0)));
            target.Position = new Vec2(0, 0);
            host.SetInCombat(true);
            host.Update(0.5, Dt); // 过渡时长 0：直接到 1.2

            host.SetZoom(8.0);

            Assert.Equal(8.0 * 1.2, camera.ZoomValues[^1], 12);
        }

        // ------------------------------------------------------------------ 镜头冲击

        [Fact]
        public void Impulse_UsesCapableAdapter_ElseFallsBackToShakeWithOneDiagnostic()
        {
            var capable = new FeelRecordingCamera { SupportsCameraImpulse = true };
            var hostA = new CameraHost(capable, new ScriptedTarget());
            hostA.Impulse(new Vec2(1, 0), 0.02, 120);
            Assert.Equal((new Vec2(1, 0), 0.02, 120.0), capable.Impulses[0]);
            Assert.Empty(capable.Shakes);
            Assert.True(hostA.SupportsImpulse);

            var incapable = new FeelRecordingCamera { SupportsCameraImpulse = false };
            var hostB = new CameraHost(incapable, new ScriptedTarget());
            hostB.EnableFeel(null, shakeReferenceHeight: 10.0);
            hostB.Impulse(new Vec2(1, 0), 0.02, 120);
            hostB.Impulse(new Vec2(0, 1), 0.03, 90);

            Assert.Empty(incapable.Impulses);
            Assert.Equal(2, incapable.Shakes.Count);
            Assert.Equal(0.02 * 10.0, incapable.Shakes[0].Intensity, 12);
            Assert.Equal(0.120, incapable.Shakes[0].Seconds, 12);
            var diagnostics = (PresentationDiagnosticsRecorder)hostB.Diagnostics;
            Assert.Single(diagnostics.Warnings, w => w.Contains("ICameraImpulse"));
        }

        /// <summary>
        /// 复现用例（手感落地 M3-B）：震屏退化的参考镜头高度取实时来源——来源值从 10 变为 20 后，同一冲击的震屏强度从 0.02 × 10 变为 0.02 × 20，不必重建镜头宿主；
        /// 来源给出非正数时本次回落到 1；旧重载（固定值）逐位不变。
        /// </summary>
        [Fact]
        public void Impulse_FallbackShake_ReadsTheLiveReferenceHeightSource()
        {
            var camera = new FeelRecordingCamera { SupportsCameraImpulse = false };
            var host = new CameraHost(camera, new ScriptedTarget());
            var reference = 10.0;
            host.EnableFeel(null, () => reference);

            host.Impulse(new Vec2(1, 0), 0.02, 120);
            reference = 20.0;
            host.Impulse(new Vec2(1, 0), 0.02, 120);
            reference = 0.0;
            host.Impulse(new Vec2(1, 0), 0.02, 120);

            Assert.Equal(3, camera.Shakes.Count);
            Assert.Equal(0.02 * 10.0, camera.Shakes[0].Intensity, 12);
            Assert.Equal(0.02 * 20.0, camera.Shakes[1].Intensity, 12);
            Assert.Equal(0.02 * 1.0, camera.Shakes[2].Intensity, 12);

            var fixedCamera = new FeelRecordingCamera { SupportsCameraImpulse = false };
            var fixedHost = new CameraHost(fixedCamera, new ScriptedTarget());
            fixedHost.EnableFeel(null, shakeReferenceHeight: 10.0);
            fixedHost.Impulse(new Vec2(1, 0), 0.02, 120);
            Assert.Equal(camera.Shakes[0].Intensity, fixedCamera.Shakes[0].Intensity);
        }

        [Fact]
        public void Impulse_NonPositiveMagnitude_IsIgnored()
        {
            var camera = new FeelRecordingCamera { SupportsCameraImpulse = true };
            var host = new CameraHost(camera, new ScriptedTarget());

            host.Impulse(new Vec2(1, 0), 0.0, 120);

            Assert.Empty(camera.Impulses);
            Assert.Empty(camera.Shakes);
        }
    }
}
