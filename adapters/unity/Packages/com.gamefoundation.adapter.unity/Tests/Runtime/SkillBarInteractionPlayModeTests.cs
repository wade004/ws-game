#nullable enable
// SkillBarInteractionPlayModeTests：技能栏交互控件（ADR-0180）——SkillTooltipView 排版与摆位、SkillBarInteraction 的悬停/点击/射线、UiInputBridge 的虚拟按键与吞鼠标。
// 期望值全部由测试里的输入（内容、轮询次数、格子位置）算出；不依赖任何游戏数据。
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.Ui;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using Presentation.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:ui")]
    public sealed class SkillBarInteractionPlayModeTests : PlayModeTestBase
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _spawned.Clear();
            UiSkin.Reset();
            yield return null;
        }

        private sealed class FakeInput : IInput
        {
            public readonly List<InputEvent> Pending = new List<InputEvent>();
            public readonly HashSet<string> Down = new HashSet<string>();
            public Vec2 Mouse;

            public IReadOnlyList<InputEvent> PollEvents()
            {
                var copy = new List<InputEvent>(Pending);
                Pending.Clear();
                return copy;
            }

            public bool IsKeyDown(string key) => Down.Contains(key);

            public Vec2 GetMousePosition() => Mouse;

            public double GetGamepadAxis(int gamepadIndex, string axis) => 0.0;

            public void BeginTextInput(string placeholder) { }

            public string EndTextInput() => string.Empty;
        }

        private static SkillTooltipContent Content(string name, int rows, string description)
        {
            var list = new List<SkillTooltipRow>();
            for (var i = 0; i < rows; i++)
            {
                list.Add(new SkillTooltipRow("标签" + i, "值" + i));
            }

            return new SkillTooltipContent(
                new Id("skill.test_" + name), name, "类型", false, 1.0, 2.0, 3.0,
                new List<SkillTooltipCost>(), new List<KeyValuePair<string, double>>(), description, list);
        }

        private (UiRoot Root, RectTransform Bar, List<Image> Frames) BuildBar(int slots)
        {
            var root = UiRoot.Create("SkillBarTestRoot");
            _spawned.Add(root.gameObject);
            var bar = UiWidgets.CreateRoot("Bar", root.Content);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(slots * 120f, 100f);
            bar.anchoredPosition = new Vector2(0f, 20f);
            var frames = new List<Image>();
            for (var i = 0; i < slots; i++)
            {
                var go = new GameObject("Slot" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = (RectTransform)go.transform;
                rect.SetParent(bar, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(100f, 100f);
                rect.anchoredPosition = new Vector2(i * 120f, 0f);
                frames.Add(go.GetComponent<Image>());
            }

            return (root, bar, frames);
        }

        [UnityTest]
        public IEnumerator HoverShowsTooltipAboveTheSlot_ExitHides_ClickCallsBackWithTheIndex()
        {
            var (root, bar, frames) = BuildBar(3);
            var clicks = new List<int>();
            var contents = new Dictionary<int, SkillTooltipContent?>
            {
                [0] = Content("alpha", 3, "说明一"),
                [1] = Content("beta", 1, string.Empty),
                [2] = null, // 空格子
            };
            var interaction = SkillBarInteraction.Install(bar, root.Content, i => contents[i], clicks.Add);
            for (var i = 0; i < frames.Count; i++)
            {
                interaction.Register(frames[i], i);
            }

            yield return null;
            Canvas.ForceUpdateCanvases();

            ExecuteEvents.Execute(frames[0].gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            Assert.AreEqual(0, interaction.HoveredSlot);
            var tip = interaction.Tooltip;
            Assert.IsTrue(tip.IsVisible);
            Assert.AreEqual("alpha", tip.NameText);
            Assert.AreEqual("说明一", tip.DescriptionText);
            Assert.AreEqual(3, tip.RowTexts.Count);
            Assert.AreEqual(("标签1", "值1"), tip.RowTexts[1]);

            // 提示框摆在被悬停格的正上方：水平居中于格子，底边在格子上沿之上。
            var slotCorners = new Vector3[4];
            frames[0].rectTransform.GetWorldCorners(slotCorners);
            var tipCorners = new Vector3[4];
            tip.Root.GetWorldCorners(tipCorners);
            Assert.GreaterOrEqual(tipCorners[0].y, slotCorners[1].y - 0.01f, "提示框底边应在格子上沿之上");
            var slotCenterX = (slotCorners[0].x + slotCorners[2].x) * 0.5f;
            var tipCenterX = (tipCorners[0].x + tipCorners[2].x) * 0.5f;
            var parentCorners = new Vector3[4];
            root.Content.GetWorldCorners(parentCorners);
            Assert.IsTrue(tipCorners[0].x >= parentCorners[0].x - 0.01f && tipCorners[2].x <= parentCorners[2].x + 0.01f, "提示框不越出画布");
            if (tipCorners[0].x > parentCorners[0].x + 0.01f && tipCorners[2].x < parentCorners[2].x - 0.01f)
            {
                Assert.AreEqual(slotCenterX, tipCenterX, 0.5f, "没被画布夹住时水平居中于格子");
            }

            // 空格子悬停不显示；换格子内容跟着换；离开隐藏。
            ExecuteEvents.Execute(frames[2].gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            Assert.IsFalse(tip.IsVisible);
            ExecuteEvents.Execute(frames[1].gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            Assert.AreEqual("beta", tip.NameText);
            Assert.AreEqual(1, tip.RowTexts.Count);
            ExecuteEvents.Execute(frames[0].gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
            Assert.IsTrue(tip.IsVisible, "离开的不是当前悬停的格子，提示框不隐藏");
            ExecuteEvents.Execute(frames[1].gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
            Assert.IsFalse(tip.IsVisible);
            Assert.AreEqual(-1, interaction.HoveredSlot);

            // 只有左键点击算数。
            ExecuteEvents.Execute(frames[1].gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, clickCount = 1 }, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.Execute(frames[2].gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Right, clickCount = 1 }, ExecuteEvents.pointerClickHandler);
            CollectionAssert.AreEqual(new[] { 1 }, clicks);
        }

        [UnityTest]
        public IEnumerator Tooltip_NeverReceivesRaycasts_BarSlotsDo_AndPointerOverUiIsTrueOnlyOverTheSlots()
        {
            var (root, bar, frames) = BuildBar(2);
            var interaction = SkillBarInteraction.Install(bar, root.Content, i => Content("x" + i, 2, "说明"), _ => { });
            for (var i = 0; i < frames.Count; i++)
            {
                interaction.Register(frames[i], i);
            }

            yield return null;
            Canvas.ForceUpdateCanvases();
            interaction.Hover(0);

            foreach (var graphic in interaction.Tooltip.Root.GetComponentsInChildren<Graphic>(true))
            {
                Assert.IsFalse(graphic.raycastTarget, graphic.name + " 不应接收射线");
            }

            var corners = new Vector3[4];
            frames[1].rectTransform.GetWorldCorners(corners);
            var overSlot = RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[2]) * 0.5f);
            Assert.IsTrue(UiInputBridge.PointerOverUi(overSlot), "指针在格子上应算在界面上");
            // 提示框所在位置（格子上方）不算界面：射线检测不会命中提示框。
            var tipCorners = new Vector3[4];
            interaction.Tooltip.Root.GetWorldCorners(tipCorners);
            var overTip = RectTransformUtility.WorldToScreenPoint(null, (tipCorners[0] + tipCorners[2]) * 0.5f);
            Assert.IsFalse(UiInputBridge.PointerOverUi(overTip), "提示框不挡鼠标");
            Assert.IsFalse(UiInputBridge.PointerOverUi(new Vector2(2f, Screen.height - 2f)), "空白处不在界面上");
        }

        [Test]
        public void Bridge_PressKey_TapGivesDownThenUpOnNextPoll_HoldGivesUpAfterHoldPolls()
        {
            var inner = new FakeInput();
            var bridge = new UiInputBridge(inner, _ => false);

            bridge.PressKey("j");
            Assert.IsTrue(bridge.HasPendingInjection);
            var first = bridge.PollEvents();
            Assert.AreEqual(1, first.Count);
            Assert.AreEqual(InputEventKind.KeyDown, first[0].Kind);
            Assert.AreEqual("j", first[0].Key);
            Assert.IsTrue(bridge.IsKeyDown("j"));
            var second = bridge.PollEvents();
            Assert.AreEqual(1, second.Count);
            Assert.AreEqual(InputEventKind.KeyUp, second[0].Kind);
            Assert.IsFalse(bridge.IsKeyDown("j"));
            Assert.IsFalse(bridge.HasPendingInjection);
            Assert.AreEqual(0, bridge.PollEvents().Count);

            const int holdPolls = 7;
            bridge.PressKey("k", holdPolls);
            Assert.AreEqual(InputEventKind.KeyDown, bridge.PollEvents()[0].Kind);
            for (var i = 1; i < holdPolls; i++)
            {
                Assert.AreEqual(0, bridge.PollEvents().Count, "按住期间没有事件，第 " + i + " 次轮询");
                Assert.IsTrue(bridge.IsKeyDown("k"));
            }

            var up = bridge.PollEvents();
            Assert.AreEqual(1, up.Count);
            Assert.AreEqual(InputEventKind.KeyUp, up[0].Kind);
        }

        [Test]
        public void Bridge_SameKeyPressedTwice_SecondWaitsForTheFirstToFinish_AndBothEdgesAppear()
        {
            var bridge = new UiInputBridge(new FakeInput(), _ => false);
            bridge.PressKey("j");
            bridge.PressKey("j");

            var kinds = new List<InputEventKind>();
            for (var i = 0; i < 8; i++)
            {
                foreach (var evt in bridge.PollEvents())
                {
                    kinds.Add(evt.Kind);
                }
            }

            CollectionAssert.AreEqual(
                new[] { InputEventKind.KeyDown, InputEventKind.KeyUp, InputEventKind.KeyDown, InputEventKind.KeyUp }, kinds);
        }

        [Test]
        public void Bridge_PassesRealEventsThroughUnchanged_WhenNothingInjectedAndPointerNotOverUi()
        {
            var inner = new FakeInput();
            inner.Pending.Add(new InputEvent(InputEventKind.KeyDown, key: "w"));
            inner.Pending.Add(new InputEvent(InputEventKind.MouseButtonDown, key: "MouseLeft"));
            inner.Pending.Add(new InputEvent(InputEventKind.MouseButtonUp, key: "MouseLeft"));
            inner.Down.Add("w");
            var bridge = new UiInputBridge(inner, _ => false);

            var events = bridge.PollEvents();

            Assert.AreEqual(3, events.Count);
            Assert.AreEqual(InputEventKind.KeyDown, events[0].Kind);
            Assert.AreEqual(InputEventKind.MouseButtonDown, events[1].Kind);
            Assert.AreEqual(InputEventKind.MouseButtonUp, events[2].Kind);
            Assert.IsTrue(bridge.IsKeyDown("w"));
            Assert.AreEqual(0, bridge.SwallowedMouseDowns);
        }

        [Test]
        public void Bridge_MouseDownOverUi_IsSwallowedWithItsPairedUp_ButOtherEventsAndLaterClicksPass()
        {
            var inner = new FakeInput();
            var overUi = true;
            var bridge = new UiInputBridge(inner, _ => overUi);

            inner.Pending.Add(new InputEvent(InputEventKind.MouseButtonDown, key: "MouseLeft"));
            inner.Pending.Add(new InputEvent(InputEventKind.KeyDown, key: "w"));
            var first = bridge.PollEvents();
            Assert.AreEqual(1, first.Count, "鼠标按下被吞，键盘事件照常");
            Assert.AreEqual("w", first[0].Key);
            Assert.AreEqual(1, bridge.SwallowedMouseDowns);

            overUi = false; // 抬起时指针已移出界面：配对的抬起仍要吞掉，否则世界侧收到孤立的抬起
            inner.Pending.Add(new InputEvent(InputEventKind.MouseButtonUp, key: "MouseLeft"));
            Assert.AreEqual(0, bridge.PollEvents().Count);

            inner.Pending.Add(new InputEvent(InputEventKind.MouseButtonDown, key: "MouseLeft"));
            inner.Pending.Add(new InputEvent(InputEventKind.MouseButtonUp, key: "MouseLeft"));
            Assert.AreEqual(2, bridge.PollEvents().Count, "离开界面后的点击正常放行");
        }

        [Test]
        public void Bridge_MouseDownInWorld_ThenReleasedOverUi_StillDeliversTheUp()
        {
            var inner = new FakeInput();
            var overUi = false;
            var bridge = new UiInputBridge(inner, _ => overUi);

            inner.Pending.Add(new InputEvent(InputEventKind.MouseButtonDown, key: "MouseLeft"));
            Assert.AreEqual(1, bridge.PollEvents().Count);
            overUi = true;
            inner.Pending.Add(new InputEvent(InputEventKind.MouseButtonUp, key: "MouseLeft"));
            var up = bridge.PollEvents();
            Assert.AreEqual(1, up.Count, "世界里按下的鼠标，抬起一定要送达");
            Assert.AreEqual(InputEventKind.MouseButtonUp, up[0].Kind);
        }

        [Test]
        public void Bridge_PressBound_UsesTheFirstKeyboardBinding_AndIgnoresPadOnlyActions()
        {
            var bridge = new UiInputBridge(new FakeInput(), _ => false);

            Assert.IsFalse(bridge.PressBound(new[] { "pad:x" }));
            Assert.IsFalse(bridge.HasPendingInjection);
            Assert.IsTrue(bridge.PressBound(new[] { "pad:x", "key:l", "key:leftshift" }));
            var down = bridge.PollEvents();
            Assert.AreEqual(1, down.Count);
            Assert.AreEqual("l", down[0].Key);
        }

        [Test]
        public void Bridge_HoldPollsFor_CoversTheThresholdInFixedSteps()
        {
            const double thresholdMs = 250;
            const double step = 0.02;
            var polls = UiInputBridge.HoldPollsFor(thresholdMs, step);

            // 按下到抬起之间的固定步数乘步长不小于阈值；点按（无阈值）取 1 次。
            Assert.GreaterOrEqual((polls - 1) * step * 1000.0, thresholdMs);
            Assert.AreEqual(1, UiInputBridge.HoldPollsFor(0, step));
            Assert.AreEqual(1, UiInputBridge.HoldPollsFor(thresholdMs, 0));
        }
    }
}
