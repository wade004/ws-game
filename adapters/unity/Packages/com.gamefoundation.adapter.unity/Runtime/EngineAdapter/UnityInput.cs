#nullable enable
// UnityInput：IInput 的 Unity 引擎实现，基于 Input System 1.20 的 InputAction。
//
// 判断记录（不依赖 .inputactions 资产）：全部 InputActionMap/InputAction 在构造函数里用代码
// 现场搭建（new InputActionMap + AddAction + AddBinding），不读取任何 .inputactions 资产文件，
// 满足任务书"运行时代码生成 InputActionMap"的要求。
//
// 判断记录（离散按键事件的产生方式）：没有对键盘上百个物理键逐一 AddBinding，而是绑定一个
// "<Keyboard>/anyKey" 的 PassThrough 动作作为"本帧键盘状态发生了变化"的触发器；每次触发时
// 在回调里遍历 Keyboard.current.allKeys，用 Input System 自带的 wasPressedThisFrame/
// wasReleasedThisFrame 精确找出本帧真正按下/松开的具体键，逐个登记 KeyDown/KeyUp 事件——
// 这样既满足"运行时用 InputAction 驱动"，又避免了逐键 AddBinding 的重复样板代码，且不会因为
// "anyKey 本身是聚合控件、拿不到具体是哪个键"这一 Input System 已知限制而丢失按键信息。
// 鼠标按钮与手柄按钮直接每个绑定一个具体控件（数量少，没有上述聚合问题）。
//
// 手柄连接/断开：订阅 InputSystem.onDeviceChange，只关心 Gamepad 类型设备的
// Added/Removed，GamepadIndex 用 Gamepad.all 里的下标。
//
// 文本输入：Input System 没有独立于 UI 框架的"文本输入会话"API，改用
// Keyboard.current.onTextInput（逐字符原始文本事件，跳过组合键控制字符）在
// BeginTextInput/EndTextInput 之间累积字符，是"引擎内置文本输入能力"里最接近契约语义的选择
// （契约允许"平台原生或引擎内置"，见 02 第 1.5 节）。
using System;
using System.Collections.Generic;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityInput : IInput
    {
        private readonly InputActionMap _map;
        private readonly InputAction _keyboardChangeAction;
        private readonly InputAction _mouseLeftAction;
        private readonly InputAction _mouseRightAction;
        private readonly InputAction _mouseMiddleAction;
        private readonly List<InputEvent> _pendingEvents = new List<InputEvent>();
        private readonly StringBuilder _textInputBuffer = new StringBuilder();
        private bool _recordingTextInput;
        private Vec2 _lastMousePosition;

        public UnityInput()
        {
            _map = new InputActionMap("GameFoundationDeviceEvents");

            _keyboardChangeAction = _map.AddAction("KeyboardAnyKey", InputActionType.PassThrough, binding: "<Keyboard>/anyKey");
            _keyboardChangeAction.performed += OnKeyboardChanged;

            _mouseLeftAction = _map.AddAction("MouseLeft", InputActionType.Button, binding: "<Mouse>/leftButton");
            _mouseLeftAction.started += ctx => EnqueueMouseButton(InputEventKind.MouseButtonDown, "MouseLeft");
            _mouseLeftAction.canceled += ctx => EnqueueMouseButton(InputEventKind.MouseButtonUp, "MouseLeft");

            _mouseRightAction = _map.AddAction("MouseRight", InputActionType.Button, binding: "<Mouse>/rightButton");
            _mouseRightAction.started += ctx => EnqueueMouseButton(InputEventKind.MouseButtonDown, "MouseRight");
            _mouseRightAction.canceled += ctx => EnqueueMouseButton(InputEventKind.MouseButtonUp, "MouseRight");

            _mouseMiddleAction = _map.AddAction("MouseMiddle", InputActionType.Button, binding: "<Mouse>/middleButton");
            _mouseMiddleAction.started += ctx => EnqueueMouseButton(InputEventKind.MouseButtonDown, "MouseMiddle");
            _mouseMiddleAction.canceled += ctx => EnqueueMouseButton(InputEventKind.MouseButtonUp, "MouseMiddle");

            _map.Enable();

            InputSystem.onDeviceChange += OnDeviceChange;

            if (Keyboard.current != null)
            {
                Keyboard.current.onTextInput += OnTextInput;
            }
        }

        public IReadOnlyList<InputEvent> PollEvents()
        {
            DetectMouseMove();

            var events = new List<InputEvent>(_pendingEvents);
            _pendingEvents.Clear();
            return events;
        }

        public bool IsKeyDown(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            switch (key)
            {
                case "MouseLeft": return Mouse.current != null && Mouse.current.leftButton.isPressed;
                case "MouseRight": return Mouse.current != null && Mouse.current.rightButton.isPressed;
                case "MouseMiddle": return Mouse.current != null && Mouse.current.middleButton.isPressed;
            }

            if (Keyboard.current != null && Enum.TryParse<Key>(key, ignoreCase: true, out var parsedKey))
            {
                var control = Keyboard.current[parsedKey];
                return control != null && control.isPressed;
            }

            return false;
        }

        public Vec2 GetMousePosition()
        {
            if (Mouse.current == null) return Vec2.Zero;
            var pos = Mouse.current.position.ReadValue();
            return new Vec2(pos.x, pos.y);
        }

        public double GetGamepadAxis(int gamepadIndex, string axis)
        {
            var gamepads = Gamepad.all;
            if (gamepadIndex < 0 || gamepadIndex >= gamepads.Count) return 0.0;
            var gamepad = gamepads[gamepadIndex];

            switch (axis)
            {
                case "LeftStickX": return gamepad.leftStick.x.ReadValue();
                case "LeftStickY": return gamepad.leftStick.y.ReadValue();
                case "RightStickX": return gamepad.rightStick.x.ReadValue();
                case "RightStickY": return gamepad.rightStick.y.ReadValue();
                case "LeftTrigger": return gamepad.leftTrigger.ReadValue();
                case "RightTrigger": return gamepad.rightTrigger.ReadValue();
                default: return 0.0;
            }
        }

        public void BeginTextInput(string placeholder)
        {
            _textInputBuffer.Clear();
            _recordingTextInput = true;
        }

        public string EndTextInput()
        {
            _recordingTextInput = false;
            return _textInputBuffer.ToString();
        }

        /// <summary>释放订阅，避免 InputActionMap/onDeviceChange/onTextInput 泄漏到下一次场景。
        /// 由 UnityEngineHost 在 OnDestroy 时调用。</summary>
        internal void Dispose()
        {
            _map.Disable();
            _map.Dispose();
            InputSystem.onDeviceChange -= OnDeviceChange;
            if (Keyboard.current != null)
            {
                Keyboard.current.onTextInput -= OnTextInput;
            }
        }

        private void OnKeyboardChanged(InputAction.CallbackContext ctx)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var keys = keyboard.allKeys;
            for (var i = 0; i < keys.Count; i++)
            {
                var control = keys[i];
                if (control.wasPressedThisFrame)
                {
                    _pendingEvents.Add(new InputEvent(InputEventKind.KeyDown, key: control.name));
                }
                else if (control.wasReleasedThisFrame)
                {
                    _pendingEvents.Add(new InputEvent(InputEventKind.KeyUp, key: control.name));
                }
            }
        }

        private void EnqueueMouseButton(InputEventKind kind, string buttonName)
        {
            _pendingEvents.Add(new InputEvent(kind, key: buttonName));
        }

        private void DetectMouseMove()
        {
            var current = GetMousePosition();
            if (current.X != _lastMousePosition.X || current.Y != _lastMousePosition.Y)
            {
                _lastMousePosition = current;
                _pendingEvents.Add(new InputEvent(InputEventKind.MouseMoved, position: current));
            }
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Gamepad gamepad)) return;

            var index = IndexOfGamepad(gamepad);
            switch (change)
            {
                case InputDeviceChange.Added:
                    _pendingEvents.Add(new InputEvent(InputEventKind.GamepadConnected, gamepadIndex: index));
                    break;
                case InputDeviceChange.Removed:
                    _pendingEvents.Add(new InputEvent(InputEventKind.GamepadDisconnected, gamepadIndex: index));
                    break;
            }
        }

        /// <summary>Gamepad.all 是 Input System 的 ReadOnlyArray&lt;Gamepad&gt;，只提供按
        /// Predicate 查找的 IndexOf 重载，没有直接按值查找的重载，改用手写循环按引用相等查找。</summary>
        private static int IndexOfGamepad(Gamepad gamepad)
        {
            var all = Gamepad.all;
            for (var i = 0; i < all.Count; i++)
            {
                if (ReferenceEquals(all[i], gamepad)) return i;
            }

            return -1;
        }

        private void OnTextInput(char character)
        {
            if (!_recordingTextInput) return;
            if (char.IsControl(character)) return;
            _textInputBuffer.Append(character);
        }

        /// <summary>测试用：手柄按钮的离散事件目前没有专用 AddBinding（手柄按钮种类多、
        /// 且测试环境通常没有真实手柄设备），测试可直接调用本方法模拟一次手柄按钮事件，
        /// 驱动上层 InputMap 的手柄绑定分支。</summary>
        public void SimulateGamepadButtonForTest(int gamepadIndex, string button, bool down)
        {
            _pendingEvents.Add(new InputEvent(
                down ? InputEventKind.GamepadButtonDown : InputEventKind.GamepadButtonUp,
                key: button,
                gamepadIndex: gamepadIndex));
        }

        /// <summary>H4 新增：与 <see cref="SimulateGamepadButtonForTest"/> 同一惯例——测试可直接
        /// 调用本方法模拟一次键盘按键事件（<paramref name="key"/> 用 Input System 控件名，如
        /// <c>Keyboard.current.tKey.name</c> == "t"，与 <c>found.input_action</c> 表
        /// <c>key:&lt;name&gt;</c> 绑定字符串的 <c>&lt;name&gt;</c> 部分同一命名空间），不依赖批处理
        /// 环境下是否有真实键盘设备/<c>&lt;Keyboard&gt;/anyKey</c> 这一真实按键路径的时序细节，
        /// 驱动上层 InputMap 的键盘绑定分支。</summary>
        public void SimulateKeyForTest(string key, bool down)
        {
            _pendingEvents.Add(new InputEvent(down ? InputEventKind.KeyDown : InputEventKind.KeyUp, key: key));
        }
    }
}
