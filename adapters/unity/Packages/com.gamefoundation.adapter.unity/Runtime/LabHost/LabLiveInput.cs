#nullable enable
// LabLiveInput：人手试玩的输入轮询（ADR-0141）。把真实输入适配器（IInput：键盘按键、左摇杆）与手柄按钮读成"按下/松开/轴值"，
// 换成与脚本同一种 ScriptEvent 交给会话注入——所以人手输入和脚本输入走的是宿主里同一条路径（输入映射 → 输入缓冲 → 技能），
// 录下来的就是可无头重放的脚本。
//
// 判断记录（绑定从哪来）：战斗动作取自数据 found.input_action 里声明了 skill_slot 的按钮动作（动作式实验室数据根是 lab_a_attack/dodge/skill/charge），
// 键盘按键取其 default_bindings 里的 key:<名>；移动动作取 composite2d 的四个键（缺省 w|s|a|d，另加方向键）与左摇杆。
// 数据里这些动作没有手柄绑定（实验室数据根只声明了键盘键），所以手柄按钮来自本类的"手柄叠加表"（按 skill_slot 给 pad:<名>），这是试玩宿主自己的约定，
// 不是框架数据；真实输入适配器 UnityInput 目前也不轮询手柄按钮（只有摇杆轴与连接事件），所以手柄按钮这里直接读 Input System 的 Gamepad.current。
// 轴值：摇杆做半径 0.15 的死区、不重新缩放（输入映射对摇杆值不做归一化，归一化发生在移动处理器——这正是实验室要度量的当前行为）；
// 键盘合成轴的斜向归一成单位向量（与框架 composite2d 的 8 向单位向量一致）。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.InputMap;
using Lab;
using UnityEngine.InputSystem;

namespace Adapter.Unity.LabHost
{
    public sealed class LabLiveInput
    {
        /// <summary>手柄叠加表（按 skill_slot）：实验室数据根的四个战斗动作各给一个手柄按钮。</summary>
        public static readonly IReadOnlyDictionary<string, string> DefaultPadBySlot = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["lab_a.attack"] = "pad:x",
            ["lab_a.dodge"] = "pad:a",
            ["lab_a.skill"] = "pad:y",
            ["lab_a.charge"] = "pad:rb",
        };

        private const double StickDeadzone = 0.15;

        private sealed class ButtonEntry
        {
            public string ActionId = string.Empty;
            public string Slot = string.Empty;
            public List<string> Keys = new List<string>();
            public List<string> Pads = new List<string>();
            public bool Down;
        }

        private readonly IInput _input;
        private readonly string _moveAction;
        private readonly Func<string, bool> _padButton;
        private readonly List<ButtonEntry> _buttons = new List<ButtonEntry>();
        private readonly string[] _moveKeys = { "w", "s", "a", "d" };
        private readonly string[] _moveAltKeys = { "upArrow", "downArrow", "leftArrow", "rightArrow" };
        private Vec2 _lastAxis = Vec2.Zero;

        /// <summary>读手柄按钮的函数（缺省读 Input System 的 <c>Gamepad.current</c>）；测试可以换成假的。</summary>
        public static Func<string, bool> DefaultPadReader => PadButtonDown;

        public LabLiveInput(
            IInput input, IEnumerable<ActionDefinition> actions, string moveAction, Func<string, bool>? padButton = null,
            IReadOnlyDictionary<string, string>? padBySlot = null)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _moveAction = moveAction;
            _padButton = padButton ?? PadButtonDown;
            padBySlot ??= DefaultPadBySlot;
            foreach (var action in actions)
            {
                if (string.Equals(action.ActionId.Value, moveAction, StringComparison.Ordinal))
                {
                    TryReadMoveKeys(action);
                    continue;
                }

                if (action.Kind != ActionKind.Button || string.IsNullOrEmpty(action.SkillSlot))
                {
                    continue;
                }

                var entry = new ButtonEntry { ActionId = action.ActionId.Value, Slot = action.SkillSlot! };
                foreach (var binding in action.DefaultBindings)
                {
                    if (binding.StartsWith("key:", StringComparison.Ordinal))
                    {
                        entry.Keys.Add(KeyName(binding.Substring(4)));
                    }
                    else if (binding.StartsWith("pad:", StringComparison.Ordinal))
                    {
                        entry.Pads.Add(binding);
                    }
                }

                if (padBySlot.TryGetValue(entry.Slot, out var pad) && !entry.Pads.Contains(pad))
                {
                    entry.Pads.Add(pad);
                }

                _buttons.Add(entry);
            }
        }

        /// <summary>被轮询的战斗动作 id（按数据顺序）。</summary>
        public IReadOnlyList<string> ActionIds
        {
            get
            {
                var ids = new List<string>();
                foreach (var b in _buttons)
                {
                    ids.Add(b.ActionId);
                }

                return ids;
            }
        }

        /// <summary>该动作当前绑定的人读文本（键盘键与手柄按钮）。</summary>
        public string Describe(string actionId)
        {
            foreach (var b in _buttons)
            {
                if (string.Equals(b.ActionId, actionId, StringComparison.Ordinal))
                {
                    var parts = new List<string>();
                    foreach (var k in b.Keys) parts.Add(k.ToUpperInvariant());
                    foreach (var p in b.Pads) parts.Add(p);
                    return string.Join(" / ", parts);
                }
            }

            return string.Empty;
        }

        public string MoveAction => _moveAction;

        /// <summary>轮询一次：按键状态变化产出 Press/Release，移动轴变化产出 Axis；每个事件交给 <paramref name="emit"/>（调用方负责注入会话）。</summary>
        public void Poll(Action<ScriptEvent> emit)
        {
            foreach (var b in _buttons)
            {
                var down = false;
                foreach (var key in b.Keys)
                {
                    if (_input.IsKeyDown(key))
                    {
                        down = true;
                        break;
                    }
                }

                if (!down)
                {
                    foreach (var pad in b.Pads)
                    {
                        if (_padButton(pad))
                        {
                            down = true;
                            break;
                        }
                    }
                }

                if (down != b.Down)
                {
                    b.Down = down;
                    emit(new ScriptEvent(0, b.ActionId, down ? ScriptEventKind.Press : ScriptEventKind.Release));
                }
            }

            var axis = ReadMoveAxis();
            if (Math.Abs(axis.X - _lastAxis.X) > 1e-4 || Math.Abs(axis.Y - _lastAxis.Y) > 1e-4)
            {
                _lastAxis = axis;
                emit(new ScriptEvent(0, _moveAction, ScriptEventKind.Axis, axis));
            }
        }

        private Vec2 ReadMoveAxis()
        {
            var sx = _input.GetGamepadAxis(0, "LeftStickX");
            var sy = _input.GetGamepadAxis(0, "LeftStickY");
            var mag = Math.Sqrt(sx * sx + sy * sy);
            if (mag > StickDeadzone)
            {
                return new Vec2(sx, sy);
            }

            double Pressed(int i) => _input.IsKeyDown(_moveKeys[i]) || _input.IsKeyDown(_moveAltKeys[i]) ? 1.0 : 0.0;
            var x = Pressed(3) - Pressed(2);
            var y = Pressed(0) - Pressed(1);
            if (x != 0.0 && y != 0.0)
            {
                x *= Math.Sqrt(0.5);
                y *= Math.Sqrt(0.5);
            }

            return new Vec2(x, y);
        }

        private void TryReadMoveKeys(ActionDefinition move)
        {
            foreach (var binding in move.DefaultBindings)
            {
                const string prefix = "composite2d:";
                if (!binding.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = binding.Substring(prefix.Length).Split('|');
                if (parts.Length != 4)
                {
                    continue;
                }

                for (var i = 0; i < 4; i++)
                {
                    _moveKeys[i] = parts[i].StartsWith("key:", StringComparison.Ordinal) ? KeyName(parts[i].Substring(4)) : _moveKeys[i];
                }
            }
        }

        /// <summary>数据里的键名（<c>key:j</c> 的 <c>j</c>）→ Input System 按键枚举名（<see cref="UnityInput.IsKeyDown"/> 按枚举名解析）：数字键要写成 DigitN。</summary>
        internal static string KeyName(string dataName)
        {
            if (dataName.Length == 1 && char.IsDigit(dataName[0]))
            {
                return "Digit" + dataName;
            }

            return dataName;
        }

        private static bool PadButtonDown(string binding)
        {
            var pad = Gamepad.current;
            if (pad == null || !binding.StartsWith("pad:", StringComparison.Ordinal))
            {
                return false;
            }

            switch (binding.Substring(4))
            {
                case "a": return pad.buttonSouth.isPressed;
                case "b": return pad.buttonEast.isPressed;
                case "x": return pad.buttonWest.isPressed;
                case "y": return pad.buttonNorth.isPressed;
                case "lb": return pad.leftShoulder.isPressed;
                case "rb": return pad.rightShoulder.isPressed;
                case "lt": return pad.leftTrigger.isPressed;
                case "rt": return pad.rightTrigger.isPressed;
                case "l3": return pad.leftStickButton.isPressed;
                case "r3": return pad.rightStickButton.isPressed;
                case "start": return pad.startButton.isPressed;
                case "select": return pad.selectButton.isPressed;
                default: return false;
            }
        }
    }
}
