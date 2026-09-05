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

        /// <summary>缺口 1 验收（PlayMode 一条）：fontId 现按包 README"资源 id → 路径规则"解析到
        /// Resources/Fonts/noto_sans_cjk_sc 对应的字体资产（本仓库已提交，见
        /// assets/_placeholder/fonts/noto_sans_cjk_sc.otf 与其同步产物），DrawText 用它绘制文本
        /// 不应报错，生成的 TMP_Text 组件应当拿到一个非空字体（不是"生成失败"的默认占位路径）。</summary>
        [Test]
        public void DrawText_WithResolvedFontId_DrawsTextUsingResourceBasedFont()
        {
            var surfaceId = new Id("ui.sample_hud");
            _ui.CreateSurface(surfaceId, 800, 600);

            Assert.DoesNotThrow(() =>
                _ui.DrawText(surfaceId, "HP: 10", new Vec2(10, -10), new Id("font.noto_sans_cjk_sc"), 24));

            var canvasGo = _rootGo.transform.Find("Surface_" + surfaceId.Value)!;
            Assert.AreEqual(1, canvasGo.childCount);
            var tmp = canvasGo.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>();
            Assert.IsNotNull(tmp);
            Assert.IsNotNull(tmp.font, "按 fontId 解析出的 TMP_FontAsset 不应为空");
        }

        /// <summary>缺口 1 验收（未知 fontId 的回退路径）：找不到对应字体资产（Resources/Fonts/missing
        /// 不存在）时应当回退默认字体并只记一条诊断（Debug.LogWarning），不抛异常、不阻断 DrawText。</summary>
        [Test]
        public void DrawText_WithUnknownFontId_FallsBackToDefaultFont_AndLogsWarningOnce()
        {
            var surfaceId = new Id("ui.sample_hud");
            _ui.CreateSurface(surfaceId, 800, 600);

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex(@"\[UnityUISurface\] fontId ""font\.missing"".*"));

            Assert.DoesNotThrow(() =>
                _ui.DrawText(surfaceId, "unknown font", Vec2.Zero, new Id("font.missing"), 24));

            var canvasGo = _rootGo.transform.Find("Surface_" + surfaceId.Value)!;
            Assert.AreEqual(1, canvasGo.childCount);
            var tmp = canvasGo.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>();
            Assert.IsNotNull(tmp.font, "回退路径也应当解析出一个非空的默认/占位字体");

            // 判断记录：Unity Test Framework 只对未被 Expect 消费的 LogType.Error/Exception 自动
            // 判该用例失败，Warning 不受此限制（同 VerticalSliceTests.cs 判断记录），因此这里不能靠
            // "留一条未消费的 Warning 会让用例失败"来验证 ResolveFontAsset 的
            // _missingFontIdsWarned 去重逻辑（同一 fontId 只记一次诊断）；只做一次冒烟检查：同一个
            // 未知 fontId 重复 DrawText 依然不抛异常、不阻断绘制。
            Assert.DoesNotThrow(() =>
                _ui.DrawText(surfaceId, "unknown font again", Vec2.Zero, new Id("font.missing"), 24));
        }
    }
}
