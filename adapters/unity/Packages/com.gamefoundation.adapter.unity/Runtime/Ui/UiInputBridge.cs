#nullable enable
// UiInputBridge：界面与输入流之间的桥（ADR-0180）。包装游戏当前的 IInput，做两件事：
//   1. 界面发起的按键（点技能栏格子 = 按对应热键）以虚拟按键事件的形式注入输入流：PressKey(key, holdPolls) 在下一次 PollEvents 给出 KeyDown，
//      holdPolls 次 PollEvents 之后给出 KeyUp，IsKeyDown 在这期间为真。注入的事件与真实设备事件走同一条路径（输入映射 → 输入缓冲 → 技能绑定），
//      所以冷却、资源、读条中等限制与提示和按键一模一样，不另开施法路径。
//   2. 鼠标按下落在界面上（指针在接收射线的控件上，如技能栏）时，吞掉这次按下和与它配对的抬起，世界侧的“鼠标左键 = 攻击/点选”因此不会同时触发。
//
// 判断记录（用轮询次数而不是秒数计时）：游戏每个固定步读一次输入（InputMap.Update），所以“轮询次数 = 固定步数”，按住时长以步数计可以逐步复现，
// 不依赖真实时间和帧率；点按取 holdPolls = 1（下一次轮询按下、再下一次抬起），按住变体取 阈值秒数 ÷ 固定步长 向上取整 + 1。
// 判断记录（配对吞抬起）：只吞按下会让后面到来的抬起变成孤立的“抬起”，只吞配对的抬起，世界里先按下、后移到界面上抬起的鼠标操作不受影响。
// 判断记录（缺省行为不变）：不包装的游戏没有任何变化；包装后没有注入、指针不在界面上时，输出与被包装的输入逐事件一致。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Adapter.Unity.Ui
{
    public sealed class UiInputBridge : IInput
    {
        private sealed class Injection
        {
            public string Key = string.Empty;
            public int PollsUntilDown;
            public int PollsUntilUp;
            public bool Down;
        }

        private readonly IInput _inner;
        private readonly Func<Vec2, bool> _pointerOverUi;
        private readonly List<Injection> _injections = new List<Injection>();
        private readonly HashSet<string> _swallowedMouse = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _injectedDown = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>被吞掉的鼠标按下次数（测试与诊断用）。</summary>
        public int SwallowedMouseDowns { get; private set; }

        /// <param name="inner">被包装的输入。</param>
        /// <param name="pointerOverUi">判断屏幕点（输入的鼠标坐标，左下角为原点的像素）是否在接收射线的界面上；缺省用事件系统的射线检测（<see cref="PointerOverUi"/>）。</param>
        public UiInputBridge(IInput inner, Func<Vec2, bool>? pointerOverUi = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _pointerOverUi = pointerOverUi ?? (p => PointerOverUi(new Vector2((float)p.X, (float)p.Y)));
        }

        public IInput Inner => _inner;

        /// <summary>有注入的按键尚未走完（按下未发出、或按下后还没抬起）。</summary>
        public bool HasPendingInjection => _injections.Count > 0;

        /// <summary>
        /// 注入一次虚拟按键：下一次 <see cref="PollEvents"/> 给出 KeyDown，之后第 <paramref name="holdPolls"/> 次 <see cref="PollEvents"/> 给出 KeyUp
        /// （<paramref name="holdPolls"/> ≥ 1；1 = 点按）。同一个键已有注入在进行中时，排在它走完之后。
        /// </summary>
        public void PressKey(string key, int holdPolls = 1)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("key 不能为空", nameof(key));
            }

            var polls = Math.Max(1, holdPolls);
            var delay = 1;
            foreach (var other in _injections)
            {
                if (other.Key == key)
                {
                    // 排在它抬起之后的下一次轮询：已按下的还剩 PollsUntilUp 次轮询抬起，未按下的还要先等 PollsUntilDown 次。
                    delay = Math.Max(delay, (other.Down ? 0 : other.PollsUntilDown) + other.PollsUntilUp + 1);
                }
            }

            _injections.Add(new Injection { Key = key, PollsUntilDown = delay, PollsUntilUp = polls });
        }

        /// <summary>
        /// 按“动作的绑定列表”（输入映射的 <c>GetBindings</c>，形如 <c>key:j</c>、<c>pad:x</c>）里的第一个键盘键注入一次虚拟按键。
        /// 没有键盘绑定（只绑了手柄）返回 false，什么也不注入。
        /// </summary>
        public bool PressBound(IEnumerable<string> bindings, int holdPolls = 1)
        {
            foreach (var binding in bindings)
            {
                if (binding.StartsWith("key:", StringComparison.Ordinal) && binding.Length > 4)
                {
                    PressKey(binding.Substring(4), holdPolls);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 按住变体（<c>hold_threshold_ms</c>）一次点击需要按住的轮询次数：阈值折算成固定步数向上取整，再加 2 次余量（按下沿与抬起沿各占一次轮询），
        /// 保证抬起时已满阈值。点按用 1。
        /// </summary>
        public static int HoldPollsFor(double holdThresholdMs, double stepSeconds)
        {
            if (!(stepSeconds > 0.0) || !(holdThresholdMs > 0.0))
            {
                return 1;
            }

            return (int)Math.Ceiling(holdThresholdMs / 1000.0 / stepSeconds) + 2;
        }

        public IReadOnlyList<InputEvent> PollEvents()
        {
            var real = _inner.PollEvents();
            var events = new List<InputEvent>(real.Count + _injections.Count);
            var mouse = _inner.GetMousePosition();
            for (var i = 0; i < real.Count; i++)
            {
                var evt = real[i];
                if (evt.Kind == InputEventKind.MouseButtonDown && _pointerOverUi(mouse))
                {
                    _swallowedMouse.Add(evt.Key);
                    SwallowedMouseDowns++;
                    continue;
                }

                if (evt.Kind == InputEventKind.MouseButtonUp && _swallowedMouse.Remove(evt.Key))
                {
                    continue;
                }

                events.Add(evt);
            }

            for (var i = 0; i < _injections.Count; i++)
            {
                var injection = _injections[i];
                if (!injection.Down)
                {
                    if (--injection.PollsUntilDown > 0)
                    {
                        continue;
                    }

                    injection.Down = true;
                    _injectedDown.Add(injection.Key);
                    events.Add(new InputEvent(InputEventKind.KeyDown, key: injection.Key));
                    continue;
                }

                if (--injection.PollsUntilUp > 0)
                {
                    continue;
                }

                events.Add(new InputEvent(InputEventKind.KeyUp, key: injection.Key));
                _injections.RemoveAt(i--);
                var stillDown = false;
                foreach (var other in _injections)
                {
                    stillDown |= other.Down && other.Key == injection.Key;
                }

                if (!stillDown)
                {
                    _injectedDown.Remove(injection.Key);
                }
            }

            return events;
        }

        public bool IsKeyDown(string key) => _injectedDown.Contains(key) || _inner.IsKeyDown(key);

        public Vec2 GetMousePosition() => _inner.GetMousePosition();

        public double GetGamepadAxis(int gamepadIndex, string axis) => _inner.GetGamepadAxis(gamepadIndex, axis);

        public double ConsumeScrollNotches() => _inner.ConsumeScrollNotches();

        public void BeginTextInput(string placeholder) => _inner.BeginTextInput(placeholder);

        public string EndTextInput() => _inner.EndTextInput();

        private static readonly List<RaycastResult> RaycastBuffer = new List<RaycastResult>();

        /// <summary>
        /// 屏幕点是否落在接收射线的界面上。直接用事件系统对该点做射线检测，不依赖输入模块上一帧的指针状态，结果只取决于界面本身，可确定地测试。
        /// 没有事件系统时为否。
        /// </summary>
        public static bool PointerOverUi(Vector2 screenPx)
        {
            var system = EventSystem.current;
            if (system == null)
            {
                return false;
            }

            RaycastBuffer.Clear();
            system.RaycastAll(new PointerEventData(system) { position = screenPx }, RaycastBuffer);
            return RaycastBuffer.Count > 0;
        }
    }
}
