#nullable enable
// UnityCameraGroundFootprintTests：UnityCamera 的 ICameraGroundFootprint 实现（ADR-0181）。期望值由缩放、画面宽高比、偏航算出（正交），
// 俯仰透视姿态下与“画面四角的 ScreenToWorld 交点”独立对照，不调用被测的外接矩形算法。
using System;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:engine_adapter")]
    public sealed class UnityCameraGroundFootprintTests : PlayModeTestBase
    {
        private GameObject _go = null!;
        private Camera _cam = null!;
        private UnityCamera _camera = null!;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("FootprintCamera");
            _cam = _go.AddComponent<Camera>();
            _camera = new UnityCamera(_cam);
            _camera.Configure(45, 0, new ZoomRange(1, 40));
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_go);

        [Test]
        public void Orthographic_FootprintIsZoomByAspect_AndDoesNotDependOnWhereTheCameraIs()
        {
            _cam.aspect = 2.0f;
            foreach (var zoom in new[] { 3.0, 5.0, 12.5 })
            {
                _camera.SetZoom(zoom);
                foreach (var target in new[] { new Vec2(0, 0), new Vec2(40, -17) })
                {
                    _camera.Follow(target, smoothing: 0);
                    _camera.Tick(0.1);

                    Assert.IsTrue(_camera.SupportsCameraGroundFootprint);
                    Assert.IsTrue(_camera.TryGetGroundFootprint(out var min, out var max));
                    Assert.AreEqual(-zoom * _cam.aspect, min.X, 1e-3);
                    Assert.AreEqual(-zoom, min.Y, 1e-3);
                    Assert.AreEqual(zoom * _cam.aspect, max.X, 1e-3);
                    Assert.AreEqual(zoom, max.Y, 1e-3);
                }
            }
        }

        [Test]
        public void Orthographic_WiderScreenSeesMoreHorizontally_SameVertically()
        {
            _camera.SetZoom(6);
            _cam.aspect = 4.0f / 3.0f;
            Assert.IsTrue(_camera.TryGetGroundFootprint(out var narrowMin, out var narrowMax));
            _cam.aspect = 21.0f / 9.0f;
            Assert.IsTrue(_camera.TryGetGroundFootprint(out var wideMin, out var wideMax));

            Assert.Greater(wideMax.X - wideMin.X, narrowMax.X - narrowMin.X);
            Assert.AreEqual(narrowMax.Y - narrowMin.Y, wideMax.Y - wideMin.Y, 1e-3);
        }

        [Test]
        public void Orthographic_YawQuarterTurnSwapsTheExtents()
        {
            _cam.aspect = 2.0f;
            _camera.ApplyYawRotation = true;
            _camera.SetZoom(5);
            _camera.SetView(90, 45);
            _camera.Tick(0.1);

            Assert.IsTrue(_camera.TryGetGroundFootprint(out var min, out var max));
            Assert.AreEqual(5.0, max.X, 1e-3);
            Assert.AreEqual(-5.0, min.X, 1e-3);
            Assert.AreEqual(10.0, max.Y, 1e-3);
            Assert.AreEqual(-10.0, min.Y, 1e-3);
        }

        [Test]
        public void Perspective_FootprintBoundsAllFourScreenCornerGroundHits_AndIsTight()
        {
            _cam.aspect = 16.0f / 9.0f;
            _camera.ApplyPitch = true;
            _camera.Perspective = true;
            _camera.FieldOfViewDegrees = 40;
            _camera.SetZoom(8);
            _camera.SetView(0, 55);
            _camera.Follow(new Vec2(12, -4), smoothing: 0);
            _camera.Tick(0.1);

            Assert.IsTrue(_camera.TryGetGroundFootprint(out var min, out var max));
            var focus = new Vec2(12, -4);
            var w = _cam.pixelWidth;
            var h = _cam.pixelHeight;
            var hits = new[]
            {
                _camera.ScreenToWorld(new Vec2(0, 0)), _camera.ScreenToWorld(new Vec2(w, 0)),
                _camera.ScreenToWorld(new Vec2(0, h)), _camera.ScreenToWorld(new Vec2(w, h)),
            };
            double lo = double.MaxValue, hi = double.MinValue, bottom = double.MaxValue, top = double.MinValue;
            foreach (var hit in hits)
            {
                Assert.IsTrue(hit.HasValue);
                lo = Math.Min(lo, hit!.Value.X);
                hi = Math.Max(hi, hit.Value.X);
                bottom = Math.Min(bottom, hit.Value.Y);
                top = Math.Max(top, hit.Value.Y);
            }

            Assert.AreEqual(lo, focus.X + min.X, 0.05);
            Assert.AreEqual(hi, focus.X + max.X, 0.05);
            Assert.AreEqual(bottom, focus.Y + min.Y, 0.05);
            Assert.AreEqual(top, focus.Y + max.Y, 0.05);
        }

        [Test]
        public void Perspective_LookingTowardTheHorizon_HasNoFootprint()
        {
            _cam.aspect = 16.0f / 9.0f;
            _camera.ApplyPitch = true;
            _camera.Perspective = true;
            _camera.FieldOfViewDegrees = 60;
            _camera.SetZoom(8);
            _camera.SetView(0, 80); // 俯仰角从正下方量起：80 度 + 半视场 30 度 = 110 度，画面上沿射线越过地平线朝向天空
            _camera.Tick(0.1);

            Assert.IsFalse(_camera.TryGetGroundFootprint(out _, out _));
        }
    }
}
