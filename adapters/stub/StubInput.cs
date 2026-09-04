// StubInput：IInput 的最小可用桩实现——可编程输入，不接触任何真实输入设备。
// 用途：测试驱动 InputMap 等上层逻辑时，用 Press/Release/SetAxis/MoveMouse 等测试专用方法
// （均不属于 IInput 接口）模拟按键、手柄轴、鼠标位置的变化，PollEvents 返回自上次调用以来
// 产生的离散事件。
// 与真实实现的差异：不轮询任何操作系统输入队列；文本输入会话只是把 BeginTextInput 传入的
// placeholder 原样通过 EndTextInput 返回（除非测试用 SetPendingTextInput 覆盖）。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubInput : IInput
    {
        private readonly HashSet<string> _keysDown = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<(int, string), double> _gamepadAxes = new Dictionary<(int, string), double>();
        private readonly List<InputEvent> _pendingEvents = new List<InputEvent>();
        private Vec2 _mousePosition = Vec2.Zero;
        private string _pendingTextInput = string.Empty;

        public IReadOnlyList<InputEvent> PollEvents()
        {
            var events = new List<InputEvent>(_pendingEvents);
            _pendingEvents.Clear();
            return events;
        }

        public bool IsKeyDown(string key) => _keysDown.Contains(key);

        public Vec2 GetMousePosition() => _mousePosition;

        public double GetGamepadAxis(int gamepadIndex, string axis)
        {
            return _gamepadAxes.TryGetValue((gamepadIndex, axis), out var value) ? value : 0.0;
        }

        public void BeginTextInput(string placeholder)
        {
            _pendingTextInput = placeholder;
        }

        public string EndTextInput() => _pendingTextInput;

        /// <summary>测试用：模拟按下一个键，登记 KeyDown 事件并把该键标记为按下。</summary>
        public void Press(string key)
        {
            _keysDown.Add(key);
            _pendingEvents.Add(new InputEvent(InputEventKind.KeyDown, key: key));
        }

        /// <summary>测试用：模拟松开一个键，登记 KeyUp 事件并把该键标记为松开。</summary>
        public void Release(string key)
        {
            _keysDown.Remove(key);
            _pendingEvents.Add(new InputEvent(InputEventKind.KeyUp, key: key));
        }

        /// <summary>测试用：设置某个手柄轴的当前值。</summary>
        public void SetAxis(int gamepadIndex, string axis, double value)
        {
            _gamepadAxes[(gamepadIndex, axis)] = value;
        }

        /// <summary>测试用：设置鼠标位置并登记一次 MouseMoved 事件。</summary>
        public void MoveMouse(Vec2 position)
        {
            _mousePosition = position;
            _pendingEvents.Add(new InputEvent(InputEventKind.MouseMoved, position: position));
        }

        /// <summary>测试用：覆盖下一次 EndTextInput() 将要返回的文本。</summary>
        public void SetPendingTextInput(string text) => _pendingTextInput = text;
    }
}
