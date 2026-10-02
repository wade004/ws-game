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
    }
}
