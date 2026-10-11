#nullable enable
using Adapter.Unity.Ui;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    /// <summary>ADR-0173：引导箭头的摆放数学与控件显隐。期望值由容器矩形与留白算出，不写死裸坐标。</summary>
    [Category("module:ui")]
    public sealed class GuideArrowTests
    {
        private static readonly Rect Screen = new Rect(-960f, -540f, 1920f, 1080f);

        [Test]
        public void TargetInside_ArrowSitsAboveTargetPointingDown()
        {
            var p = GuideArrowMath.Place(new Vector2(100f, -50f), Screen, 56f, 64f);
            Assert.IsTrue(p.OnScreen);
            Assert.AreEqual(100f, p.Position.x, 1e-3f);
            Assert.AreEqual(-50f + 64f, p.Position.y, 1e-3f);
            Assert.AreEqual(-90f, p.AngleDegrees, 1e-3f);
        }

        [Test]
        public void TargetFarRight_ArrowClampsToRightEdgePointingRight()
        {
            var p = GuideArrowMath.Place(new Vector2(5000f, 0f), Screen, 56f, 64f);
            Assert.IsFalse(p.OnScreen);
            Assert.AreEqual(Screen.xMax - 56f, p.Position.x, 1e-3f);
            Assert.AreEqual(0f, p.Position.y, 1e-3f);
            Assert.AreEqual(0f, p.AngleDegrees, 1e-3f);
        }

        [Test]
        public void TargetFarUp_ArrowClampsToTopEdgePointingUp()
        {
            var p = GuideArrowMath.Place(new Vector2(0f, 9000f), Screen, 56f, 64f);
            Assert.IsFalse(p.OnScreen);
            Assert.AreEqual(Screen.yMax - 56f, p.Position.y, 1e-3f);
            Assert.AreEqual(90f, p.AngleDegrees, 1e-3f);
        }

        [Test]
        public void TargetDiagonalOffscreen_ArrowStaysOnRectBoundaryAlongTheLineToTarget()
        {
            var target = new Vector2(-3000f, -1000f);
            var p = GuideArrowMath.Place(target, Screen, 56f, 64f);
            Assert.IsFalse(p.OnScreen);
            var half = new Vector2(Screen.width * 0.5f - 56f, Screen.height * 0.5f - 56f);
            Assert.IsTrue(Mathf.Approximately(Mathf.Abs(p.Position.x), half.x) || Mathf.Approximately(Mathf.Abs(p.Position.y), half.y));
            var cross = p.Position.x * target.y - p.Position.y * target.x;
            Assert.AreEqual(0f, cross, 1f);
            Assert.AreEqual(Mathf.Atan2(target.y, target.x) * Mathf.Rad2Deg, p.AngleDegrees, 1e-3f);
        }

        [Test]
        public void Widget_IsHiddenUntilPresentedAndHidesAgain()
        {
            var canvasGo = new GameObject("c", typeof(RectTransform));
            try
            {
                ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(1920f, 1080f);
                var arrow = GuideArrow.Create(canvasGo.transform);
                Assert.IsFalse(arrow.IsShown);
                arrow.PresentLocal(new Vector2(99999f, 0f));
                Assert.IsTrue(arrow.IsShown);
                Assert.IsFalse(arrow.LastPlacement.OnScreen);
                arrow.Hide();
                Assert.IsFalse(arrow.IsShown);
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }
    }
}
