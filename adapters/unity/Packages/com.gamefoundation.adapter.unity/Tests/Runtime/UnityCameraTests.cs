#nullable enable
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
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
    }
}
