#nullable enable
// FloatingTextReceiverPlayModeTests：飘字接收器的三维锚点构造与闪避配色（ADR-0178）。
// 期望值由构造参数算出：飘字生成时落在锚点上、朝向与相机一致，Tick 一秒后沿上浮方向移动「上浮速度 × 1 秒」；
// 闪避样式的颜色引用带 dodge 字样，颜色不能是伤害数字的白色。不依赖场景，对象全部在用例内创建并在结束时销毁。
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using NUnit.Framework;
using Presentation.FeedbackBinder.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:feedback_binder")]
    public sealed class FloatingTextReceiverPlayModeTests : PlayModeTestBase
    {
        private static readonly Id DodgeStyle = new Id("feedback.floating_text_style.t_dodge");
        private static readonly Id NormalStyle = new Id("feedback.floating_text_style.t_normal");

        private static Dictionary<Id, FloatingTextStyleDef> Styles() => new Dictionary<Id, FloatingTextStyleDef>
        {
            [DodgeStyle] = new FloatingTextStyleDef(DodgeStyle, new Id("color.t_dodge"), 1.0, null),
            [NormalStyle] = new FloatingTextStyleDef(NormalStyle, new Id("color.t_white"), 1.0, null),
        };

        [UnityTest]
        public IEnumerator WorldAnchorConstructor_PlacesTextAtTheAnchor_RisesAlongTheGivenDirection_AndFacesTheCamera()
        {
            var root = new GameObject("FloatingTextTestRoot");
            var cameraObject = new GameObject("FloatingTextTestCamera", typeof(Camera));
            cameraObject.transform.rotation = Quaternion.Euler(40f, 25f, 0f);
            try
            {
                var anchor = new Vector3(1f, 2f, -3f);
                var rise = new Vector3(0f, 0f, -1f);
                var receiver = new FloatingTextReceiver(root.transform, _ => anchor, Styles(), () => cameraObject.GetComponent<Camera>(), rise);

                receiver.Show(new Id("unit.a"), DodgeStyle, "t");
                var text = root.GetComponentInChildren<TextMeshPro>();
                Assert.IsNotNull(text);
                Assert.AreEqual(1, receiver.SpawnedCount);
                Assert.Less((text.transform.position - anchor).magnitude, 1e-4f, "飘字生成在实体头顶的世界锚点上");
                Assert.Less(Quaternion.Angle(text.transform.rotation, cameraObject.transform.rotation), 0.01f, "飘字朝向相机");

                receiver.Tick(1f);
                var risen = text.transform.position - anchor;
                Assert.Less((risen - rise * 0.6f).magnitude, 1e-4f, "一秒后沿上浮方向走过上浮速度 × 1 秒");
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(cameraObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator DefaultConstructor_StillPlacesTextAboveTheLogicPositionInThePlane()
        {
            var root = new GameObject("FloatingTextTestRoot2");
            try
            {
                var receiver = new FloatingTextReceiver(root.transform, _ => new Vec2(4.0, 5.0), Styles());
                receiver.Show(new Id("unit.a"), NormalStyle, "t");
                var text = root.GetComponentInChildren<TextMeshPro>();
                Assert.IsNotNull(text);
                Assert.Less((text.transform.position - new Vector3(4f, 6.2f, -0.1f)).magnitude, 1e-4f, "二维默认构造行为不变");
            }
            finally
            {
                Object.Destroy(root);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator NonAsciiText_UsesTheKitCjkFont_WhileDamageNumbersKeepTheDefaultFont()
        {
            var root = new GameObject("FloatingTextTestRoot4");
            try
            {
                var receiver = new FloatingTextReceiver(root.transform, _ => Vec2.Zero, Styles());
                receiver.Show(new Id("unit.a"), NormalStyle, "123");
                receiver.Show(new Id("unit.a"), DodgeStyle, "闪避");
                TextMeshPro? number = null, cjk = null;
                foreach (var t in root.GetComponentsInChildren<TextMeshPro>(true))
                {
                    if (t.text == "123") number = t;
                    if (t.text == "闪避") cjk = t;
                }

                Assert.IsNotNull(number);
                Assert.IsNotNull(cjk);
                Assert.AreSame(TMP_Settings.defaultFontAsset, number!.font, "纯 ASCII 的伤害数字仍用 TMP 默认字体（行为不变）");
                Assert.AreSame(Adapter.Unity.Ui.UiSkin.Font, cjk!.font, "含中文的飘字用套件字体（TMP 默认字体没有 CJK 字形）");
            }
            finally
            {
                Object.Destroy(root);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator DodgeStyle_IsNotTheWhiteOfDamageNumbers()
        {
            var root = new GameObject("FloatingTextTestRoot3");
            try
            {
                var receiver = new FloatingTextReceiver(root.transform, _ => Vec2.Zero, Styles());
                receiver.Show(new Id("unit.a"), DodgeStyle, "dodge");
                receiver.Show(new Id("unit.a"), NormalStyle, "hit");
                var texts = root.GetComponentsInChildren<TextMeshPro>(true);
                Assert.AreEqual(2, texts.Length);
                Color dodge = default, normal = default;
                foreach (var t in texts)
                {
                    if (t.text == "dodge") dodge = t.color;
                    if (t.text == "hit") normal = t.color;
                }

                Assert.AreEqual(Color.white, normal);
                Assert.AreNotEqual(Color.white, dodge);
            }
            finally
            {
                Object.Destroy(root);
            }

            yield return null;
        }
    }
}
