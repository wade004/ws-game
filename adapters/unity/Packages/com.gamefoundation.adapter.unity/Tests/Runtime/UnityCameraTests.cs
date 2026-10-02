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
    }
}
