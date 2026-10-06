#nullable enable
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:engine_adapter")]
    public sealed class UnityCameraTests : PlayModeTestBase
    {
        private GameObject _cameraGo = null!;
        private UnityCamera _camera = null!;

        [SetUp]
        public void SetUp()
        {
            _cameraGo = new GameObject("TestCamera");
            _cameraGo.AddComponent<Camera>();
            _camera = new UnityCamera(_cameraGo.GetComponent<Camera>());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_cameraGo);
        }

        [Test]
        public void Configure_ThenSetZoom_ClampsToConfiguredRange()
        {
            _camera.Configure(45, 0, new ZoomRange(2, 8));

            _camera.SetZoom(100);
            Assert.AreEqual(8.0, _camera.CurrentZoom, 0.001);

            _camera.SetZoom(-5);
            Assert.AreEqual(2.0, _camera.CurrentZoom, 0.001);
        }

        [Test]
        public void WorldToScreen_ScreenToWorld_RoundTripsApproximately()
        {
            _camera.Configure(45, 0, new ZoomRange(1, 10));
            _camera.SetZoom(5);

            var worldPoint = new Vec2(1.0, 2.0);
            var screen = _camera.WorldToScreen(worldPoint, height: 0);
            var back = _camera.ScreenToWorld(screen);

            Assert.IsNotNull(back);
            Assert.AreEqual(worldPoint.X, back!.Value.X, 0.01);
            Assert.AreEqual(worldPoint.Y, back.Value.Y, 0.01);
        }

        [Test]
        public void Follow_ThenTick_MovesCameraTowardTarget()
        {
            _cameraGo.transform.position = Vector3.zero;
            _camera.Follow(new Vec2(10, 10), smoothing: 0);

            _camera.Tick(0.1);

            Assert.AreEqual(10f, _cameraGo.transform.position.x, 0.01f);
            Assert.AreEqual(10f, _cameraGo.transform.position.y, 0.01f);
        }

        [Test]
        public void Shake_AppliesTemporaryOffset_ThenSettles()
        {
            _cameraGo.transform.position = new Vector3(0, 0, -10);

            _camera.Shake(1.0, 0.05, frequency: 20.0);
            _camera.Tick(0.02);
            _camera.Tick(0.02);
            _camera.Tick(0.02); // 超过 duration，抖动应结束

            Assert.AreEqual(0f, _cameraGo.transform.position.x, 0.001f);
            Assert.AreEqual(0f, _cameraGo.transform.position.y, 0.001f);
        }

        [Test]
        public void Shake_DuringActiveWindow_AppliesNonZeroOffset()
        {
            // ADR-0016 决策 4：frequency 参数驱动 Perlin 噪声采样（见 UnityCamera.Shake 判断记录），
            // 本用例只断言"震屏期间确有偏移产生"，不断言具体数值（噪声轨迹不追求可预测）。
            _cameraGo.transform.position = new Vector3(0, 0, -10);

            _camera.Shake(5.0, 1.0, frequency: 15.0);
            _camera.Tick(0.1);

            var offsetX = _cameraGo.transform.position.x;
            var offsetY = _cameraGo.transform.position.y;
            Assert.IsTrue(offsetX != 0f || offsetY != 0f, "震屏期间相机位置应偏离基准位置");
        }
        // ------------------------------------------------------------------
        // 手感落地 M2-A：ICameraImpulse 引擎实现（手感设计/07 第 5/6 节）
        // ------------------------------------------------------------------

        [Test]
        public void Impulse_Directional_PeakOffsetIsMagnitudeTimesScreenHeight_ThenDecaysToZero()
        {
            // 期望位移由规则算出：幅度（画面高度比例）× 画面可视高度（2 × 正交半高 = 2 × 缩放）。
            _camera.Configure(45, 0, new ZoomRange(1, 10));
            _camera.SetZoom(4);
            _cameraGo.transform.position = new Vector3(0, 0, -10);
            const double magnitude = 0.02;
            const double decayMs = 200;
            var screenHeight = 2.0 * _camera.CurrentZoom;

            Assert.IsTrue(_camera.SupportsCameraImpulse);
            _camera.Impulse(new Vec2(1, 0), magnitude, decayMs);
            Assert.AreEqual(1, _camera.ImpulseCount);
            Assert.AreEqual(magnitude, _camera.LastImpulse!.Value.Magnitude, 1e-12);
            Assert.AreEqual(decayMs, _camera.LastImpulse!.Value.DecayMs, 1e-12);

            // 刚触发的第一帧（推进极小）：位移 ≈ 峰值，沿给定方向（+x）。
            _camera.Tick(0.0001);
            var peak = magnitude * screenHeight;
            Assert.AreEqual(peak, _cameraGo.transform.position.x, peak * 0.01);
            Assert.AreEqual(0f, _cameraGo.transform.position.y, 1e-6f);

            // 线性衰减：推进到衰减时长一半，位移约为峰值一半（不变量：位移随时间单调不增）。
            _camera.Tick(decayMs / 1000.0 / 2.0);
            var half = _cameraGo.transform.position.x;
            Assert.AreEqual(peak / 2.0, half, peak * 0.02);
            Assert.Less(half, peak);

            // 超过衰减时长后回零，镜头回到基准位置。
            _camera.Tick(decayMs / 1000.0);
            Assert.AreEqual(0f, _cameraGo.transform.position.x, 1e-6f);
            Assert.AreEqual(0f, _cameraGo.transform.position.y, 1e-6f);
        }

        [Test]
        public void Impulse_ZeroDirection_UsesIsotropicOffsetWithinPeak_AndInvalidArgumentsAreIgnored()
        {
            _camera.Configure(45, 0, new ZoomRange(1, 10));
            _camera.SetZoom(5);
            _cameraGo.transform.position = new Vector3(0, 0, -10);

            // 非正幅度/衰减：忽略，不计数，不产生位移。
            _camera.Impulse(new Vec2(1, 0), 0.0, 100);
            _camera.Impulse(new Vec2(1, 0), -0.5, 100);
            _camera.Impulse(new Vec2(1, 0), 0.01, 0);
            Assert.AreEqual(0, _camera.ImpulseCount);
            _camera.Tick(0.01);
            Assert.AreEqual(0f, _cameraGo.transform.position.x, 1e-6f);

            // 零向量 = 无方向：位移幅度不超过峰值（各分量都在 [-peak, peak] 内）。
            const double magnitude = 0.03;
            _camera.Impulse(Vec2.Zero, magnitude, 300);
            var peak = (float)(magnitude * 2.0 * _camera.CurrentZoom);
            _camera.Tick(0.02);
            var p = _cameraGo.transform.position;
            Assert.LessOrEqual(Mathf.Abs(p.x), peak * 1.0001f);
            Assert.LessOrEqual(Mathf.Abs(p.y), peak * 1.0001f);
        }

        [Test]
        public void Impulse_DoesNotDisturbFollowBase_AfterItEnds()
        {
            // 冲击叠加在跟随基准之上，结束后只剩跟随位置（不污染基准）。
            _camera.Configure(45, 0, new ZoomRange(1, 10));
            _camera.SetZoom(5);
            _camera.Follow(new Vec2(3, 4), smoothing: 0);
            _camera.Tick(0.01);
            _camera.Impulse(new Vec2(0, 1), 0.05, 100);
            _camera.Tick(0.02);
            Assert.Greater(_cameraGo.transform.position.y, 4f, "冲击沿 +y 把镜头推离跟随点");

            _camera.Tick(0.5);
            Assert.AreEqual(3f, _cameraGo.transform.position.x, 0.01f);
            Assert.AreEqual(4f, _cameraGo.transform.position.y, 0.01f);
        }

        // ───────── 偏航朝向、俯仰与透视（M4-W4） ─────────

        [Test]
        public void Orientation_Default_IsBitIdenticalToTheOrthographicTopDownCamera()
        {
            // 不变量：不声明偏航/俯仰/透视时，Configure 记下的偏航与俯仰不作用到相机（与引入这些可选能力之前逐位一致），朝向查询如实报 0。
            _camera.Configure(55, 30, new ZoomRange(1, 10));
            _camera.SetZoom(5);
            _camera.Tick(0.016);
            Assert.IsTrue(_cameraGo.GetComponent<Camera>().orthographic);
            Assert.AreEqual(Quaternion.identity, _cameraGo.transform.rotation);
            Assert.AreEqual(new Vector3(0f, 0f, -10f), _cameraGo.transform.position);
            Assert.AreEqual(0.0, _camera.YawRadians, 0.0, "没打开偏航开关，相机物理上没有转：朝向查询报 0");
            Assert.AreEqual(0.0, _camera.EffectivePitchDegrees, 0.0);
            Assert.AreEqual(5f, _camera.VisibleHalfHeight, 1e-6f);
        }

        [Test]
        public void Orientation_YawRadiansFollowsTheRealCameraAxes_WhenYawRotationIsOn()
        {
            // 复现：打开偏航后，ICameraOrientation.YawRadians 等于配置偏航，且相机的真实右轴/上轴在世界平面上就是 (cos, sin)/(-sin, cos)。
            _camera.Configure(0, 37, new ZoomRange(1, 10));
            _camera.ApplyYawRotation = true;
            var yaw = 37.0 * System.Math.PI / 180.0;
            Assert.AreEqual(yaw, _camera.YawRadians, 1e-9);
            var tr = _cameraGo.transform;
            Assert.AreEqual(System.Math.Cos(yaw), tr.right.x, 1e-5);
            Assert.AreEqual(System.Math.Sin(yaw), tr.right.y, 1e-5);
            Assert.AreEqual(-System.Math.Sin(yaw), tr.up.x, 1e-5);
            Assert.AreEqual(System.Math.Cos(yaw), tr.up.y, 1e-5);
            _camera.ApplyYawRotation = false;
            Assert.AreEqual(0.0, _camera.YawRadians, 0.0);
            Assert.AreEqual(Quaternion.identity, _cameraGo.transform.rotation, "关闭偏航开关恢复恒等朝向");
        }

        [Test]
        public void Pitch_SquashesTheGroundScreenUpDirectionByCosinePitch_AndKeepsTheRightAxis()
        {
            // 复现：俯仰 p 下，地面上沿"屏幕上"方向相隔 d 的两点，在正交投影的屏幕上相距 d·cos(p)·像素密度；沿右方向仍是 d·像素密度（规则算期望）。
            foreach (var pitch in new[] { 0.0, 30.0, 60.0, 80.0 })
            {
                _camera.Configure(pitch, 0, new ZoomRange(1, 10));
                _camera.SetZoom(5);
                _camera.ApplyPitch = true;
                Assert.AreEqual(pitch, _camera.EffectivePitchDegrees, 1e-9);
                var origin = _camera.WorldToScreen(new Vec2(0, 0), 0);
                var up = _camera.WorldToScreen(new Vec2(0, 1), 0);
                var right = _camera.WorldToScreen(new Vec2(1, 0), 0);
                var unitRight = right.X - origin.X;
                Assert.Greater(unitRight, 0.0, $"pitch={pitch}：前置条件——相机有像素尺寸");
                Assert.AreEqual(unitRight * System.Math.Cos(pitch * System.Math.PI / 180.0), up.Y - origin.Y, 1e-3 * unitRight, $"pitch={pitch}：屏幕上方向被压扁 cos(俯仰) 倍");
                Assert.AreEqual(0.0, up.X - origin.X, 1e-3 * unitRight, $"pitch={pitch}：竖直方向不产生横向屏幕位移");
                Assert.AreEqual(0.0, right.Y - origin.Y, 1e-3 * unitRight, $"pitch={pitch}：右方向不产生纵向屏幕位移");
                Assert.AreEqual(0.0, _camera.YawRadians, 0.0, $"pitch={pitch}：俯仰不改变偏航朝向");
            }
        }

        [Test]
        public void Pitch_ClampsToTheLegalRange_AndOffMeansUpright()
        {
            _camera.Configure(120, 0, new ZoomRange(1, 10));
            _camera.ApplyPitch = true;
            Assert.AreEqual(89.0, _camera.EffectivePitchDegrees, 1e-9, "俯仰上限 89（避免视线与地面平行）");
            _camera.Configure(-20, 0, new ZoomRange(1, 10));
            Assert.AreEqual(0.0, _camera.EffectivePitchDegrees, 1e-9, "负俯仰夹到 0（正俯视）");
            _camera.ApplyPitch = false;
            Assert.AreEqual(Quaternion.identity, _cameraGo.transform.rotation, "关闭俯仰开关恢复恒等朝向");
            Assert.AreEqual(new Vector3(0f, 0f, -10f), _cameraGo.transform.position, "关闭俯仰开关恢复基准位置");
        }

        [Test]
        public void Perspective_ZoomIsTheGroundHalfHeightAtTheFocus_AndScreenToWorldRoundTripsWithPitchAndYaw()
        {
            // 复现：透视下缩放仍是"焦点处地面可视半高"——焦点上方 zoom 处的地面点恰好在屏幕上沿；俯仰+偏航+透视下屏幕/世界往返一致。
            var unityCamera = _cameraGo.GetComponent<Camera>();
            _camera.Configure(0, 0, new ZoomRange(1, 10));
            _camera.SetZoom(4);
            _camera.Perspective = true;
            Assert.IsFalse(unityCamera.orthographic);
            Assert.AreEqual(4f, _camera.VisibleHalfHeight, 1e-6f);
            var origin = _camera.WorldToScreen(new Vec2(0, 0), 0);
            var top = _camera.WorldToScreen(new Vec2(0, 4), 0);
            Assert.AreEqual(unityCamera.pixelHeight / 2.0, top.Y - origin.Y, 0.5, "焦点上方 zoom 处的地面点在屏幕上沿（透视距离 = zoom / tan(视场角/2)）");

            _camera.Configure(50, 25, new ZoomRange(1, 10));
            _camera.ApplyPitch = true;
            _camera.ApplyYawRotation = true;
            foreach (var point in new[] { new Vec2(0, 0), new Vec2(1.5, -0.7), new Vec2(-2, 1) })
            {
                var back = _camera.ScreenToWorld(_camera.WorldToScreen(point, 0));
                Assert.IsNotNull(back, point.ToString());
                Assert.AreEqual(point.X, back!.Value.X, 0.02);
                Assert.AreEqual(point.Y, back.Value.Y, 0.02);
            }

            _camera.Perspective = false;
            Assert.IsTrue(unityCamera.orthographic, "关闭透视回到正交");
        }

        [Test]
        public void Perspective_ImpulsePeakFollowsTheGroundHalfHeight_NotTheProjectionHeight()
        {
            // 不变量：镜头冲击幅度按"画面高度比例"计，高度取 VisibleHalfHeight——正交与透视同一语义（峰值 = 幅度 × 2 × 半高）。
            _camera.Configure(40, 0, new ZoomRange(1, 10));
            _camera.SetZoom(6);
            _camera.ApplyPitch = true;
            _camera.Perspective = true;
            _camera.Impulse(new Vec2(1, 0), 0.05, 100);
            _camera.Tick(0.0);
            Assert.AreEqual(0.05 * 2.0 * 6.0, _camera.CurrentImpulseOffset.magnitude, 1e-4);
            _camera.Tick(1.0);
            Assert.AreEqual(0.0, _camera.CurrentImpulseOffset.magnitude, 1e-6);
        }

        // ───────── 自由镜头：俯仰范围声明、越过水平、焦点高度与地面避让（ADR-0161） ─────────

        private const double Deg = System.Math.PI / 180.0;

        /// <summary>规则算出的期望视线（世界 Z 向下为"地面在前方"）：Rz(偏航) * Rx(-俯仰) 作用在 +Z 上 = (-sin偏航·sin俯仰, cos偏航·sin俯仰, cos俯仰)。</summary>
        private static Vector3 ExpectedForward(double yawDegrees, double pitchDegrees)
        {
            var y = yawDegrees * Deg;
            var p = pitchDegrees * Deg;
            return new Vector3(
                (float)(-System.Math.Sin(y) * System.Math.Sin(p)), (float)(System.Math.Cos(y) * System.Math.Sin(p)), (float)System.Math.Cos(p));
        }

        private void Pose(double yaw, double pitch, bool perspective = true, double zoom = 6.0)
        {
            _camera.Configure(pitch, yaw, new ZoomRange(1, 20));
            _camera.SetZoom(zoom);
            _camera.ApplyYawRotation = true;
            _camera.ApplyPitch = true;
            _camera.Perspective = perspective;
        }

        [Test]
        public void FreeCamera_Defaults_PitchRangeIsZeroTo89_AndPoseMatchesTheClosedForm()
        {
            // 回归：不声明任何自由镜头能力时，范围仍是 [0, 89]，姿态与位置等于闭式解（相机 = 焦点 − 视线 × 距离），焦点在地面、地面避让关。
            Assert.AreEqual(0.0, _camera.PitchMinDegrees, 0.0);
            Assert.AreEqual(89.0, _camera.PitchMaxDegrees, 0.0);
            Assert.AreEqual(0.0, _camera.FocusHeight, 0.0);
            Assert.IsFalse(_camera.GroundAvoidance);
            foreach (var yaw in new[] { 0.0, 37.0, 200.0 })
            {
                foreach (var pitch in new[] { 0.0, 30.0, 60.0, 89.0, 120.0 })
                {
                    Pose(yaw, pitch, perspective: false);
                    var effective = System.Math.Min(pitch, 89.0);
                    Assert.AreEqual(effective, _camera.EffectivePitchDegrees, 1e-9, $"yaw={yaw} pitch={pitch}：缺省范围夹在 [0, 89]");
                    var forward = ExpectedForward(yaw, effective);
                    var tr = _cameraGo.transform;
                    Assert.AreEqual(forward.x, tr.forward.x, 1e-5f);
                    Assert.AreEqual(forward.y, tr.forward.y, 1e-5f);
                    Assert.AreEqual(forward.z, tr.forward.z, 1e-5f);
                    var expectedPosition = -forward * 10f;
                    Assert.AreEqual(expectedPosition.x, tr.position.x, 1e-4f);
                    Assert.AreEqual(expectedPosition.y, tr.position.y, 1e-4f);
                    Assert.AreEqual(expectedPosition.z, tr.position.z, 1e-4f);
                    Assert.IsFalse(_camera.GroundAvoidanceEngaged);
                    Assert.Greater(_camera.CameraHeightAboveGround, 0.0, $"yaw={yaw} pitch={pitch}：缺省范围内相机恒在地面之上");
                }
            }
        }

        [Test]
        public void DeclarePitchRange_WidensTheRangePastHorizontal_AndTheCameraLooksAtTheSky()
        {
            _camera.DeclarePitchRange(5.0, 150.0);
            Assert.AreEqual(5.0, _camera.PitchMinDegrees, 0.0);
            Assert.AreEqual(150.0, _camera.PitchMaxDegrees, 0.0);
            var unityCamera = _cameraGo.GetComponent<Camera>();
            var center = new Vec2(unityCamera.pixelWidth / 2.0, unityCamera.pixelHeight / 2.0);

            Pose(25, 170);
            Assert.AreEqual(150.0, _camera.EffectivePitchDegrees, 1e-9, "声明的上限 150 生效（越过 90 度水平）");
            Pose(25, 2);
            Assert.AreEqual(5.0, _camera.EffectivePitchDegrees, 1e-9, "声明的下限生效");
            Pose(25, 100);
            Assert.AreEqual(100.0, _camera.EffectivePitchDegrees, 1e-9, "范围内的值原样生效");
            Assert.Less(_cameraGo.transform.forward.z, 0f, "俯仰大于 90：视线朝向世界 -Z（'向上'）= 抬头看天");
            _camera.FocusHeight = 1.6;
            _camera.GroundAvoidance = true;
            Assert.Greater(_camera.CameraHeightAboveGround, 0.0, "前置条件：相机在地面之上");
            Assert.IsNull(_camera.ScreenToWorld(center), "视线指向天空：屏幕中心的射线与地面不相交，如实返回 null");
            _camera.GroundAvoidance = false;
            Assert.Less(_camera.CameraHeightAboveGround, 0.0, "前置条件：没开避让时拉远的仰视相机在地面以下");
            Assert.IsNull(_camera.ScreenToWorld(center), "地面以下的相机只会从背面穿过地面：不返回地面点");
            _camera.FocusHeight = 0.0;

            Pose(25, 60);
            Assert.Greater(_cameraGo.transform.forward.z, 0f);
            Assert.IsNotNull(_camera.ScreenToWorld(center), "视线向下时屏幕中心落在地面上");

            _camera.ResetPitchRange();
            Assert.AreEqual(89.0, _camera.PitchMaxDegrees, 0.0);
            Pose(25, 100);
            Assert.AreEqual(89.0, _camera.EffectivePitchDegrees, 1e-9, "恢复缺省范围后再次夹回 89");
        }

        [Test]
        public void DeclarePitchRange_InvalidRange_IsADeclarationError_AndLeavesTheRangeUntouched()
        {
            _camera.DeclarePitchRange(10.0, 120.0);
            Assert.Throws<System.ArgumentException>(() => _camera.DeclarePitchRange(double.NaN, 100.0), "NaN");
            Assert.Throws<System.ArgumentException>(() => _camera.DeclarePitchRange(0.0, double.PositiveInfinity), "无穷");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _camera.DeclarePitchRange(-1.0, 100.0), "下限为负");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _camera.DeclarePitchRange(0.0, UnityCamera.AbsoluteMaxPitchDegrees + 0.5), "上限超过绝对天花板（留出垂直向上的余量）");
            Assert.Throws<System.ArgumentException>(() => _camera.DeclarePitchRange(100.0, 50.0), "下限大于上限");
            Assert.AreEqual(10.0, _camera.PitchMinDegrees, 0.0, "非法声明不得改动已声明的范围");
            Assert.AreEqual(120.0, _camera.PitchMaxDegrees, 0.0);
            _camera.DeclarePitchRange(0.0, UnityCamera.AbsoluteMaxPitchDegrees);
            _camera.DeclarePitchRange(45.0, 45.0);
            Assert.AreEqual(45.0, _camera.PitchMinDegrees, 0.0, "两端相等 = 锁定俯仰，合法");
        }

        [Test]
        public void Pitch_SweepOverTheWholeDeclaredRange_NeverFlips_AndKeepsTheRightAxisAndHorizontalViewDirection()
        {
            // 不变量：俯仰从 0 扫到 179 度、偏航任意：相机姿态连续（相邻 0.5 度之间转过的角度就是 0.5 度，没有两极翻转）、永远不倒挂（上轴的世界向上分量 = sin俯仰 ≥ 0）、
            // 右轴恒在世界平面上且等于 (cos偏航, sin偏航)、视线的水平分量恒为 sin俯仰 · (-sin偏航, cos偏航)。
            _camera.DeclarePitchRange(0.0, UnityCamera.AbsoluteMaxPitchDegrees);
            foreach (var yaw in new[] { 0.0, 45.0, 120.0, 240.0, -90.0 })
            {
                Quaternion? previous = null;
                for (var pitch = 0.0; pitch <= 179.0; pitch += 0.5)
                {
                    Pose(yaw, pitch);
                    var tr = _cameraGo.transform;
                    var tag = $"yaw={yaw} pitch={pitch}";
                    if (previous.HasValue)
                    {
                        Assert.AreEqual(0.5f, Quaternion.Angle(previous.Value, tr.rotation), 0.02f, tag + "：姿态连续，没有翻转");
                    }

                    previous = tr.rotation;
                    var yawRad = yaw * Deg;
                    var pitchRad = pitch * Deg;
                    Assert.AreEqual(System.Math.Sin(pitchRad), -tr.up.z, 1e-4, tag + "：上轴的世界向上（-Z）分量 = sin(俯仰) >= 0，相机从不倒挂");
                    Assert.AreEqual(System.Math.Cos(yawRad), tr.right.x, 1e-4, tag);
                    Assert.AreEqual(System.Math.Sin(yawRad), tr.right.y, 1e-4, tag);
                    Assert.AreEqual(0.0, tr.right.z, 1e-4, tag + "：右轴恒在世界平面上");
                    Assert.AreEqual(-System.Math.Sin(yawRad) * System.Math.Sin(pitchRad), tr.forward.x, 1e-4, tag);
                    Assert.AreEqual(System.Math.Cos(yawRad) * System.Math.Sin(pitchRad), tr.forward.y, 1e-4, tag);
                    Assert.IsFalse(float.IsNaN(tr.position.x + tr.position.y + tr.position.z), tag);
                }
            }
        }

        [Test]
        public void CameraRelative_DependsOnYawOnly_AtEveryPitchIncludingPastHorizontal()
        {
            // ADR-0135 决策 5 在越过水平后仍成立：输入映射只读偏航，摇杆 (x, y) → 世界方向 x·右 + y·上轴(偏航)，其中右 = (cos, sin)、前 = (-sin, cos)；
            // 真实相机的右轴恒等于它，视线的水平方向恒等于"前"（sin俯仰 > 0）——俯仰任意取值，换算都不需要俯仰。
            // 变的只有"屏幕上轴"的世界平面投影：它含 cos(俯仰) 因子，越过水平后翻到背面（与"前"反向），所以稳定语义是"摇杆向上 = 朝相机水平视线方向走"。
            _camera.DeclarePitchRange(0.0, UnityCamera.AbsoluteMaxPitchDegrees);
            foreach (var yaw in new[] { 0.0, 33.0, 90.0, 180.0, -120.0, 305.0 })
            {
                foreach (var pitch in new[] { 10.0, 60.0, 89.0, 90.0, 91.0, 120.0, 150.0, 175.0 })
                {
                    Pose(yaw, pitch);
                    var tag = $"yaw={yaw} pitch={pitch}";
                    Assert.AreEqual(yaw * Deg, _camera.YawRadians, 1e-12, tag + "：朝向查询只报偏航，不随俯仰变");
                    var c = System.Math.Cos(_camera.YawRadians);
                    var s = System.Math.Sin(_camera.YawRadians);
                    // 摇杆 (0, 1)（向上）与 (1, 0)（向右）按偏航换算（与输入映射的公式同形，期望由规则算出）。
                    var worldForward = new Vector2((float)(-s), (float)c);
                    var worldRight = new Vector2((float)c, (float)s);
                    var tr = _cameraGo.transform;
                    var viewHorizontal = new Vector2(tr.forward.x, tr.forward.y).normalized;
                    Assert.AreEqual(worldForward.x, viewHorizontal.x, 1e-4f, tag + "：摇杆向上 = 相机水平视线方向");
                    Assert.AreEqual(worldForward.y, viewHorizontal.y, 1e-4f, tag);
                    Assert.AreEqual(worldRight.x, tr.right.x, 1e-4f, tag + "：摇杆向右 = 相机右轴");
                    Assert.AreEqual(worldRight.y, tr.right.y, 1e-4f, tag);
                    var upProjection = new Vector2(tr.up.x, tr.up.y);
                    var along = Vector2.Dot(upProjection, worldForward);
                    Assert.AreEqual(System.Math.Cos(pitch * Deg), along, 1e-4, tag + "：屏幕上轴在世界平面上的投影沿 前 的分量 = cos(俯仰)，越过 90 度后翻成负数");
                }
            }
        }

        [Test]
        public void FocusHeight_RaisesTheOrbitPivot_ZeroKeepsTheGroundFocus_AndInvalidValuesAreRejected()
        {
            Pose(0, 60, perspective: false);
            var atGround = _cameraGo.transform.position;
            _camera.FocusHeight = 1.6;
            var raised = _cameraGo.transform.position;
            Assert.AreEqual(atGround.x, raised.x, 1e-5f);
            Assert.AreEqual(atGround.y, raised.y, 1e-5f);
            Assert.AreEqual(-1.6f, raised.z - atGround.z, 1e-5f, "焦点抬高 1.6：相机整体沿世界 -Z（向上）平移 1.6，姿态与距离不变");
            _camera.FocusHeight = 0.0;
            Assert.AreEqual(atGround, _cameraGo.transform.position, "回到 0 逐位还原");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _camera.FocusHeight = -0.1);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _camera.FocusHeight = double.NaN);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _camera.GroundAvoidanceMargin = -1.0);
        }

        [Test]
        public void GroundAvoidance_Off_TheLookUpCameraSinksBelowTheGround_On_ItPullsInAlongTheViewRayAboveTheMargin()
        {
            _camera.DeclarePitchRange(5.0, 150.0);
            _camera.FocusHeight = 1.6;
            Pose(70, 120);
            var nominal = 6.0 / System.Math.Tan(40.0 * Deg / 2.0);
            Assert.AreEqual(nominal, _camera.CurrentDistance, 1e-3, "透视名义距离 = 缩放 / tan(视场角/2)");
            Assert.Less(_camera.CameraHeightAboveGround, 0.0, "关着避让：仰视 30 度、拉远后相机落到地面以下（复现缺陷场景）");
            Assert.IsFalse(_camera.GroundAvoidanceEngaged);

            _camera.GroundAvoidanceMargin = 0.4;
            _camera.GroundAvoidance = true;
            Assert.IsTrue(_camera.GroundAvoidanceEngaged);
            Assert.AreEqual(0.4, _camera.CameraHeightAboveGround, 2e-3, "打开避让：相机恰好停在离地余量处");
            Assert.Less(_camera.CurrentDistance, nominal, "沿视线向焦点靠近");
            var tr = _cameraGo.transform;
            var focus = tr.position + tr.forward * (float)_camera.CurrentDistance;
            Assert.AreEqual(0f, focus.x, 1e-3f, "仍然盯着焦点（相机在原视线上移动，没有改变朝向）");
            Assert.AreEqual(0f, focus.y, 1e-3f);
            Assert.AreEqual(-1.6f, focus.z, 1e-3f);

            Pose(70, 60);
            Assert.IsFalse(_camera.GroundAvoidanceEngaged, "俯视时相机本来就在地面之上，避让不介入");
            Assert.AreEqual(nominal, _camera.CurrentDistance, 1e-3);

            _camera.GroundAvoidance = false;
            Pose(70, 120);
            Assert.Less(_camera.CameraHeightAboveGround, 0.0, "再关掉：回到穿地的原行为");
        }

        [Test]
        public void GroundAvoidance_On_KeepsTheCameraAboveTheMarginAtEveryYawAndPitchOfTheRange()
        {
            _camera.DeclarePitchRange(0.0, 170.0);
            _camera.FocusHeight = 1.6;
            _camera.GroundAvoidanceMargin = 0.3;
            _camera.GroundAvoidance = true;
            var nominal = 14.0 / System.Math.Tan(40.0 * Deg / 2.0);
            foreach (var yaw in new[] { 0.0, 90.0, 213.0 })
            {
                var lastDistance = double.MaxValue;
                for (var pitch = 0.0; pitch <= 170.0; pitch += 5.0)
                {
                    Pose(yaw, pitch, zoom: 14.0);
                    var tag = $"yaw={yaw} pitch={pitch}";
                    Assert.GreaterOrEqual(_camera.CameraHeightAboveGround, 0.3 - 2e-3, tag + "：相机始终高于地面余量");
                    Assert.Greater(_camera.CurrentDistance, 0.01 - 1e-9, tag + "：不会压到焦点上");
                    Assert.LessOrEqual(_camera.CurrentDistance, nominal + 1e-6, tag);
                    Assert.LessOrEqual(_camera.CurrentDistance, lastDistance + 1e-6, tag + "：越往上仰，被拉近得越多（单调不增）");
                    lastDistance = _camera.CurrentDistance;
                }
            }
        }
    }
}
