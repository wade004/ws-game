#nullable enable
using System.Collections;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityUISurfaceTests
    {
        private GameObject _rootGo = null!;
        private UnityUISurface _ui = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("UIRoot");
            _ui = new UnityUISurface(_rootGo.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rootGo);
        }

        [Test]
        public void CreateSurface_CreatesCanvasWithGivenSize()
        {
            var surfaceId = new Id("ui.sample_hud");

            _ui.CreateSurface(surfaceId, 1280, 720);

            var canvasGo = _rootGo.transform.Find("Surface_" + surfaceId.Value);
            Assert.IsNotNull(canvasGo);
            Assert.IsNotNull(canvasGo!.GetComponent<Canvas>());

            var rect = canvasGo.GetComponent<RectTransform>();
            Assert.AreEqual(1280, rect.sizeDelta.x, 0.01f);
            Assert.AreEqual(720, rect.sizeDelta.y, 0.01f);
        }

        [Test]
        public void DrawText_CreatesChildUnderSurface()
        {
            var surfaceId = new Id("ui.sample_hud");
            _ui.CreateSurface(surfaceId, 800, 600);

            _ui.DrawText(surfaceId, "HP: 10", new Vec2(10, -10), new Id("font.sample_default"), 24);

            var canvasGo = _rootGo.transform.Find("Surface_" + surfaceId.Value)!;
            Assert.AreEqual(1, canvasGo.childCount);
        }

        [Test]
        public void DrawText_WithoutCreateSurface_Throws()
        {
            Assert.Throws<System.InvalidOperationException>(() =>
                _ui.DrawText(new Id("ui.sample_missing"), "x", Vec2.Zero, new Id("font.sample_default"), 12));
        }

        [Test]
        public void SetFocus_GetFocusedElement_RoundTrips()
        {
            var elementId = new Id("ui.sample_button");

            Assert.IsNull(_ui.GetFocusedElement());

            _ui.SetFocus(elementId);

            Assert.AreEqual(elementId, _ui.GetFocusedElement());
        }

        [UnityTest]
        public IEnumerator ClearSurface_RemovesAllDrawnText()
        {
            var surfaceId = new Id("ui.sample_hud");
            _ui.CreateSurface(surfaceId, 800, 600);
            _ui.DrawText(surfaceId, "a", Vec2.Zero, new Id("font.sample_default"), 12);
            _ui.DrawText(surfaceId, "b", Vec2.Zero, new Id("font.sample_default"), 12);

            _ui.ClearSurface(surfaceId);
            yield return null; // UnityEngine.Object.Destroy 是延迟到本帧末尾才真正生效的。

            var canvasGo = _rootGo.transform.Find("Surface_" + surfaceId.Value)!;
            Assert.AreEqual(0, canvasGo.childCount);
        }

        [Test]
        public void SetLayout_StoresOpaqueStringUnchanged()
        {
            var surfaceId = new Id("ui.sample_hud");
            _ui.CreateSurface(surfaceId, 800, 600);

            _ui.SetLayout(surfaceId, "{\"anything\":true}");

            Assert.AreEqual("{\"anything\":true}", _ui.GetLayoutForTest(surfaceId));
        }
    }
}
