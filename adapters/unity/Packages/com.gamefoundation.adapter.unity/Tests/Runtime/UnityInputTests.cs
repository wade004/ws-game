#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using InputEvent = Core.Foundation.EngineAdapter.InputEvent;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:engine_adapter")]
    public sealed class UnityInputTests : PlayModeTestBase
    {
        private UnityInput _input = null!;
        private readonly List<InputDevice> _addedDevices = new List<InputDevice>();
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehavior;
#endif
        private InputSettings.BackgroundBehavior _savedBackgroundBehavior;

        [SetUp]
        public void SetUp()
        {
            _input = new UnityInput();
            // 批处理没有窗口焦点：默认设置会在失焦时停用鼠标这类不能后台运行的设备，虚拟设备的事件进不来；用例内忽略焦点。
            _savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            // 编辑器里的鼠标/键盘事件默认只在游戏视图有焦点时才进游戏；同理让虚拟设备的事件无条件送达。
            _savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        }

        [TearDown]
        public void TearDown()
        {
            _input.Dispose();
            foreach (var device in _addedDevices)
            {
                InputSystem.RemoveDevice(device);
            }

            _addedDevices.Clear();
            InputSystem.settings.backgroundBehavior = _savedBackgroundBehavior;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = _savedEditorBehavior;
#endif
        }

        // ---------------------------------------------------------------
        // 鼠标按钮名（ADR-0182）：用例加一个真实的 Mouse 设备（虚拟的，但走 Input System 的真实设备与动作路径），
        // 让适配层自己把按键翻译成事件，不直接塞规范名。
        // ---------------------------------------------------------------

        private Mouse AddVirtualMouse()
        {
            var mouse = InputSystem.AddDevice<Mouse>("UnityInputTestsMouse");
            _addedDevices.Add(mouse);
            mouse.MakeCurrent(); // IsKeyDown 读 Mouse.current
            return mouse;
        }

        private static void SetButton(Mouse mouse, MouseButton button, bool down)
        {
            var state = new MouseState { position = mouse.position.ReadValue() };
            InputSystem.QueueStateEvent(mouse, down ? state.WithButton(button) : state);
        }

        /// <summary>每个物理按钮（含适配层不处理的前进/后退）与它应当对应的规范名；规范名 null = 适配层不发事件。</summary>
        private static readonly (MouseButton Physical, string? Expected)[] PhysicalButtons =
        {
            (MouseButton.Left, MouseButtons.Left),
            (MouseButton.Right, MouseButtons.Right),
            (MouseButton.Middle, MouseButtons.Middle),
            (MouseButton.Forward, null),
            (MouseButton.Back, null),
        };

        private static List<InputEvent> MouseButtonEvents(IReadOnlyList<InputEvent> events)
        {
            var list = new List<InputEvent>();
            foreach (var e in events)
            {
                if (e.Kind == InputEventKind.MouseButtonDown || e.Kind == InputEventKind.MouseButtonUp)
                {
                    list.Add(e);
                }
            }

            return list;
        }

        /// <summary>不变量：真实设备上每个物理按钮，适配层发出的鼠标按钮事件名都在规范名集合里、能被绑定解析器解析；
        /// 且规范名集合里的每个名字都被某个物理按钮发出过（词汇表没有多余的名字）。按下与抬起同名，按住期间 IsKeyDown 同名为真。</summary>
        [UnityTest]
        public IEnumerator RealMouseDevice_EveryEmittedButtonName_IsCanonical_AndParsesAsBinding_AndCoversTheVocabulary()
        {
            var mouse = AddVirtualMouse();
            yield return null;
            _input.PollEvents(); // 清掉加设备引起的事件

            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (physical, expected) in PhysicalButtons)
            {
                SetButton(mouse, physical, true);
                yield return null;
                Assert.IsTrue(expected == null || _input.IsKeyDown(expected), $"{physical} 按住期间 IsKeyDown(\"{expected}\") 应为真");
                var downs = MouseButtonEvents(_input.PollEvents());

                SetButton(mouse, physical, false);
                yield return null;
                Assert.IsTrue(expected == null || !_input.IsKeyDown(expected), $"{physical} 松开后 IsKeyDown 应为假");
                var ups = MouseButtonEvents(_input.PollEvents());

                if (expected == null)
                {
                    Assert.AreEqual(0, downs.Count + ups.Count, $"{physical} 适配层不处理，不应发出鼠标按钮事件");
                    continue;
                }

                Assert.AreEqual(1, downs.Count, $"{physical} 按下应恰好一条事件");
                Assert.AreEqual(InputEventKind.MouseButtonDown, downs[0].Kind);
                Assert.AreEqual(1, ups.Count, $"{physical} 松开应恰好一条事件");
                Assert.AreEqual(InputEventKind.MouseButtonUp, ups[0].Kind);
                Assert.AreEqual(downs[0].Key, ups[0].Key, "按下与抬起必须同名");
                emitted.Add(downs[0].Key);
            }

            foreach (var name in emitted)
            {
                Assert.IsTrue(MouseButtons.IsCanonical(name), $"适配层发出了非规范鼠标按钮名 \"{name}\"");
                var binding = BindingParser.Parse("mouse:" + name);
                Assert.AreEqual(BindingKind.Mouse, binding.Kind);
                Assert.AreEqual(name, binding.Name);
            }

            foreach (var name in MouseButtons.All)
            {
                Assert.IsTrue(emitted.Contains(name), $"规范名 \"{name}\" 没有任何物理按钮会发出它");
            }
        }

        /// <summary>复现：适配层产生的真实鼠标事件经输入映射触发 mouse:left / mouse:right / mouse:middle 绑定的动作
        /// （修复前真机上一个都不触发）。每个物理按钮只触发自己那一条，松开后复位。</summary>
        [UnityTest]
        public IEnumerator RealMouseDevice_ThroughInputMap_TriggersTheMatchingMouseBinding()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(InputMapEventKeys.ActionTriggered, "input", new[] { "actionName" }),
                new EventDefinition(InputMapEventKeys.RebindConflict, "input", new[] { "actionName", "binding" }),
            });
            var host = new InputMapHost(new EventBus(catalog));
            var cases = new[]
            {
                (Physical: MouseButton.Left, Name: MouseButtons.Left),
                (Physical: MouseButton.Right, Name: MouseButtons.Right),
                (Physical: MouseButton.Middle, Name: MouseButtons.Middle),
            };
            var defs = new List<ActionDefinition>();
            foreach (var c in cases)
            {
                defs.Add(new ActionDefinition(new Id("input.action.m_" + c.Name), ActionKind.Button, new[] { "mouse:" + c.Name }));
            }

            host.DeclareActionSet(new Id("input.set.gameplay"), defs.ToArray());
            var mouse = AddVirtualMouse();
            yield return null;
            host.Update(_input);

            foreach (var c in cases)
            {
                SetButton(mouse, c.Physical, true);
                yield return null;
                host.Update(_input);
                foreach (var other in cases)
                {
                    Assert.AreEqual(other.Name == c.Name, host.IsActionActive("input.action.m_" + other.Name),
                        $"按下 {c.Physical} 时 mouse:{other.Name} 动作的激活状态");
                }

                SetButton(mouse, c.Physical, false);
                yield return null;
                host.Update(_input);
                Assert.IsFalse(host.IsActionActive("input.action.m_" + c.Name), $"松开 {c.Physical} 后动作应复位");
            }

            Assert.IsEmpty(((InMemoryInputMapDiagnostics)host.Diagnostics).Warnings, "适配层发出的鼠标按钮名不应触发输入映射的非规范名警告");
        }

        [Test]
        public void IsKeyDown_UnknownOrUnpressedKey_ReturnsFalse()
        {
            Assert.IsFalse(_input.IsKeyDown("A"));
            Assert.IsFalse(_input.IsKeyDown("NotARealKeyName"));
        }

        [Test]
        public void GetMousePosition_ReturnsFiniteVector()
        {
            var pos = _input.GetMousePosition();

            Assert.IsFalse(double.IsNaN(pos.X));
            Assert.IsFalse(double.IsNaN(pos.Y));
        }

        [Test]
        public void GetGamepadAxis_NoGamepad_ReturnsZero()
        {
            Assert.AreEqual(0.0, _input.GetGamepadAxis(0, "LeftStickX"));
        }

        [Test]
        public void SimulateGamepadButtonForTest_EnqueuesEvent_PollEventsReturnsIt()
        {
            _input.SimulateGamepadButtonForTest(0, "South", down: true);

            var events = _input.PollEvents();

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(InputEventKind.GamepadButtonDown, events[0].Kind);
            Assert.AreEqual("South", events[0].Key);
            Assert.AreEqual(0, events[0].GamepadIndex);
        }

        [Test]
        public void ConsumeScrollNotches_ReturnsAccumulatedNotchesOnceThenZero()
        {
            IInput input = _input;
            Assert.AreEqual(0.0, input.ConsumeScrollNotches(), "没滚过：0");
            _input.SimulateScrollForTest(2.0);
            _input.SimulateScrollForTest(-0.5);
            Assert.AreEqual(1.5, input.ConsumeScrollNotches(), 1e-12, "累积求和");
            Assert.AreEqual(0.0, input.ConsumeScrollNotches(), "取走即清零");
        }

        [Test]
        public void SimulateKeyForTest_EnqueuesEvent_PollEventsReturnsIt()
        {
            _input.SimulateKeyForTest("t", down: true);

            var events = _input.PollEvents();

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(InputEventKind.KeyDown, events[0].Kind);
            Assert.AreEqual("t", events[0].Key);
        }

        [Test]
        public void SimulateKeyForTest_KeyUp_EnqueuesKeyUpEvent()
        {
            _input.SimulateKeyForTest("t", down: false);

            var events = _input.PollEvents();

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(InputEventKind.KeyUp, events[0].Kind);
        }

        [Test]
        public void PollEvents_DrainsQueue_SecondCallReturnsEmpty()
        {
            _input.SimulateGamepadButtonForTest(0, "South", down: true);
            _input.PollEvents();

            var second = _input.PollEvents();

            Assert.AreEqual(0, second.Count);
        }

        [Test]
        public void BeginAndEndTextInput_WithoutTyping_ReturnsEmptyString()
        {
            _input.BeginTextInput("placeholder");
            var result = _input.EndTextInput();

            Assert.AreEqual(string.Empty, result);
        }
    }
}
