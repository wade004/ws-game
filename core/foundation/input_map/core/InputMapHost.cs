using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// <see cref="IInputMapHost"/> 的默认实现（见本模块 README）。
    /// <para>
    /// 状态推进模型：<see cref="Update"/> 每次调用先用 <c>input.PollEvents()</c> 增量更新三组
    /// "设备当前按下集合"（键盘键名 / 鼠标按钮名 / 手柄按钮名，均只针对
    /// <see cref="InputMapOptions.GamepadIndex"/> 指定的手柄），再对每个已声明动作按其
    /// <see cref="ActionKind"/> 重算一次状态并缓存，供 <see cref="IsActionActive"/>/
    /// <see cref="GetActionAxis"/> 在两次 <see cref="Update"/> 之间随时查询同一份"本帧状态"
    /// （而不是每次查询都重新计算）。
    /// </para>
    /// </summary>
    public sealed class InputMapHost : IInputMapHost
    {
        private readonly IEventBus _bus;
        private readonly InputMapOptions _options;
        private readonly IInputMapDiagnostics _diagnostics;

        private readonly HashSet<Id> _declaredActionSets = new HashSet<Id>();
        private readonly Dictionary<string, ActionState> _actions = new Dictionary<string, ActionState>(StringComparer.Ordinal);
        private readonly List<string> _actionOrder = new List<string>();

        private readonly HashSet<string> _keysDown = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _mouseDown = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _padDown = new HashSet<string>(StringComparer.Ordinal);

        public InputMapHost(IEventBus bus, InputMapOptions? options = null, IInputMapDiagnostics? diagnostics = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? new InputMapOptions();
            _diagnostics = diagnostics ?? new InMemoryInputMapDiagnostics();
        }

        private sealed class ActionState
        {
            public ActionDefinition Definition = null!;
            public List<string> CurrentBindings = null!;
            public List<ParsedBinding> ParsedBindings = null!;
            public bool CurrentActive;
            public Vec2 CachedAxis;
            public bool IsDirty;
        }

        // -----------------------------------------------------------------
        // 声明
        // -----------------------------------------------------------------

        public void DeclareActionSet(Id actionSetId, IReadOnlyList<ActionDefinition> actions)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            if (!_declaredActionSets.Add(actionSetId))
            {
                throw new InvalidOperationException($"动作集 \"{actionSetId}\" 已声明过，不能重复声明");
            }

            foreach (var def in actions)
            {
                if (def == null) throw new ArgumentException("动作集不能包含 null 元素", nameof(actions));
                var name = def.ActionId.Value;
                if (_actions.ContainsKey(name))
                {
                    throw new InvalidOperationException($"动作 \"{name}\" 已被其它动作集声明过，动作名必须跨动作集唯一");
                }

                var bindings = new List<string>(def.DefaultBindings);
                var state = new ActionState
                {
                    Definition = def,
                    CurrentBindings = bindings,
                    ParsedBindings = ParseAll(bindings),
                    CurrentActive = false,
                    CachedAxis = Vec2.Zero,
                    IsDirty = false,
                };
                _actions.Add(name, state);
                _actionOrder.Add(name);
            }
        }

        private static List<ParsedBinding> ParseAll(IReadOnlyList<string> bindings)
        {
            var result = new List<ParsedBinding>(bindings.Count);
            for (int i = 0; i < bindings.Count; i++)
            {
                result.Add(BindingParser.Parse(bindings[i]));
            }
            return result;
        }

        private ActionState RequireAction(string actionName)
        {
            if (!_actions.TryGetValue(actionName, out var state))
            {
                throw new InvalidOperationException($"未声明的动作：\"{actionName}\"");
            }
            return state;
        }

        // -----------------------------------------------------------------
        // 重绑定 / 冲突检测
        // -----------------------------------------------------------------

        public bool Rebind(string actionName, string newBinding)
        {
            var state = RequireAction(actionName);
            var parsed = BindingParser.Parse(newBinding); // 格式非法直接抛 ArgumentException

            var conflicts = new List<string>();
            foreach (var other in _actionOrder)
            {
                if (string.Equals(other, actionName, StringComparison.Ordinal)) continue;
                var otherState = _actions[other];
                if (!string.Equals(otherState.Definition.RebindGroup, state.Definition.RebindGroup, StringComparison.Ordinal)) continue;
                if (otherState.CurrentBindings.Contains(newBinding))
                {
                    conflicts.Add(other);
                }
            }

            if (conflicts.Count > 0)
            {
                _diagnostics.Warn($"重绑定冲突：动作 \"{actionName}\" 的新绑定 \"{newBinding}\" 与同组（{state.Definition.RebindGroup}）动作 [{string.Join(", ", conflicts)}] 冲突");
                _bus.PublishImmediate(new InputRebindConflictEvent(actionName, newBinding));
                return false;
            }

            state.CurrentBindings = new List<string> { newBinding };
            state.ParsedBindings = new List<ParsedBinding> { parsed };
            state.IsDirty = !BindingListEquals(state.CurrentBindings, state.Definition.DefaultBindings);
            return true;
        }

        public IReadOnlyList<string> GetConflicts(string binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            var result = new List<string>();
            foreach (var name in _actionOrder)
            {
                if (_actions[name].CurrentBindings.Contains(binding))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        // -----------------------------------------------------------------
        // 状态查询
        // -----------------------------------------------------------------

        public bool IsActionActive(string actionName)
        {
            var state = RequireAction(actionName);
            if (state.Definition.Kind != ActionKind.Button)
            {
                throw new InvalidOperationException($"动作 \"{actionName}\" 不是 Button 类型，IsActionActive 不适用，请用 GetActionAxis");
            }
            return state.CurrentActive;
        }

        public Vec2 GetActionAxis(string actionName)
        {
            var state = RequireAction(actionName);
            if (state.Definition.Kind == ActionKind.Button)
            {
                throw new InvalidOperationException($"动作 \"{actionName}\" 是 Button 类型，GetActionAxis 不适用，请用 IsActionActive");
            }
            return state.CachedAxis;
        }

        public IReadOnlyList<string> GetBindings(string actionName) => RequireAction(actionName).CurrentBindings;

        // -----------------------------------------------------------------
        // 每帧推进
        // -----------------------------------------------------------------

        public void Update(IInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            // FND-08 收口：本批次内逐事件维护的"按下边沿"集合（不是 _keysDown 等持有集合的别名，
            // 只在本次 Update 调用期间存活，方法末尾随局部变量一起自然丢弃——不需要额外显式清空）。
            // HashSet.Add 的返回值天然就是"这次调用是否真的发生了 0→1 转变"：同一批次内先 Up
            // 后 Down（held→释放→本批次内重新按下）会先把 key 从 _keysDown 移除、再 Add 一次
            // 返回 true，正确记一次新的按下边沿；同一批次内重复的 Down 事件（如引擎的按键连发）
            // 第二次 Add 返回 false，不会被重复计入——语义与"逐事件回放真实时间线上的 0→1 转变"
            // 完全一致，比"只看批次首尾两个快照"（此前的实现：批次末对 _keysDown 与
            // CurrentActive 比较）更精确：down 紧接着 up 的按下边沿此前会被批次末的净效果
            // （净值仍是"未按下"）完全抹掉，导致点击类操作在同一批次内完成时触发事件丢失。
            var pressEdgeKeys = new HashSet<string>(StringComparer.Ordinal);
            var pressEdgeMouse = new HashSet<string>(StringComparer.Ordinal);
            var pressEdgePad = new HashSet<string>(StringComparer.Ordinal);

            var events = input.PollEvents();
            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                switch (evt.Kind)
                {
                    case InputEventKind.KeyDown:
                        if (_keysDown.Add(evt.Key)) pressEdgeKeys.Add(evt.Key);
                        break;
                    case InputEventKind.KeyUp: _keysDown.Remove(evt.Key); break;
                    case InputEventKind.MouseButtonDown:
                        if (_mouseDown.Add(evt.Key)) pressEdgeMouse.Add(evt.Key);
                        break;
                    case InputEventKind.MouseButtonUp: _mouseDown.Remove(evt.Key); break;
                    case InputEventKind.GamepadButtonDown:
                        if (evt.GamepadIndex == _options.GamepadIndex && _padDown.Add(evt.Key)) pressEdgePad.Add(evt.Key);
                        break;
                    case InputEventKind.GamepadButtonUp:
                        if (evt.GamepadIndex == _options.GamepadIndex) _padDown.Remove(evt.Key);
                        break;
                    default:
                        // MouseMoved / GamepadConnected / GamepadDisconnected：本模块的绑定语法
                        // 不消费这些事件种类，忽略。
                        break;
                }
            }

            foreach (var name in _actionOrder)
            {
                var state = _actions[name];
                switch (state.Definition.Kind)
                {
                    case ActionKind.Button:
                        // active：批次结束时的最终持有状态（同帧 down+up 净效果是"未持有"，
                        // CurrentActive/IsActionActive 如实反映——这是正确的"当前是否按住"语义，
                        // 与下面的"是否应该触发一次 InputActionTriggeredEvent"是两个独立的问题）。
                        var active = EvaluateDigital(state.ParsedBindings);
                        // rising：本批次内是否真的发生过至少一次按下边沿（见上方三个 pressEdge*
                        // 集合的构造过程），不再依赖"批次末状态相对上一帧状态是否变化"——后者在
                        // 同帧 down+up 时必然算出"没有变化"，从而永久丢失这次按下。
                        var rising = EvaluatePressEdge(state.ParsedBindings, pressEdgeKeys, pressEdgeMouse, pressEdgePad);
                        state.CurrentActive = active;
                        state.CachedAxis = Vec2.Zero;
                        if (rising)
                        {
                            _bus.Enqueue(new InputActionTriggeredEvent(name));
                        }
                        break;
                    case ActionKind.Axis1D:
                        state.CachedAxis = new Vec2(EvaluateAxis1D(state.ParsedBindings, input), 0);
                        break;
                    case ActionKind.Axis2D:
                        state.CachedAxis = EvaluateAxis2D(state.ParsedBindings, input);
                        break;
                }
            }
        }

        private bool EvaluateDigital(List<ParsedBinding> bindings)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (IsDigitalActive(bindings[i])) return true;
            }
            return false;
        }

        private bool IsDigitalActive(ParsedBinding b)
        {
            switch (b.Kind)
            {
                case BindingKind.Key: return _keysDown.Contains(b.Name!);
                case BindingKind.Mouse: return _mouseDown.Contains(b.Name!);
                case BindingKind.PadButton: return _padDown.Contains(b.Name!);
                default: return false;
            }
        }

        /// <summary>FND-08 收口新增：与 <see cref="EvaluateDigital"/>/<see cref="IsDigitalActive"/>
        /// 同构，只是查的是本批次的按下边沿集合而不是持有集合——任一绑定本批次内发生过按下边沿即
        /// 返回 true（OR 语义，与 <see cref="EvaluateDigital"/> 一致：一个动作可能绑定多个按键，
        /// 本批次内任意一个真正按下都应该触发一次，不要求"恰好一个"）。</summary>
        private static bool EvaluatePressEdge(
            List<ParsedBinding> bindings, HashSet<string> pressEdgeKeys, HashSet<string> pressEdgeMouse, HashSet<string> pressEdgePad)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                switch (b.Kind)
                {
                    case BindingKind.Key: if (pressEdgeKeys.Contains(b.Name!)) return true; break;
                    case BindingKind.Mouse: if (pressEdgeMouse.Contains(b.Name!)) return true; break;
                    case BindingKind.PadButton: if (pressEdgePad.Contains(b.Name!)) return true; break;
                }
            }
            return false;
        }

        private double EvaluateAxis1D(List<ParsedBinding> bindings, IInput input)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Kind == BindingKind.PadAxis)
                {
                    return input.GetGamepadAxis(_options.GamepadIndex, bindings[i].Name!);
                }
            }
            return 0;
        }

        private Vec2 EvaluateAxis2D(List<ParsedBinding> bindings, IInput input)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                switch (b.Kind)
                {
                    case BindingKind.Composite2D:
                        return EvaluateComposite2D(b);
                    case BindingKind.PadStick:
                        return EvaluatePadStick(b, input);
                }
            }
            return Vec2.Zero;
        }

        private Vec2 EvaluateComposite2D(ParsedBinding b)
        {
            double x = (IsDigitalActive(b.Right!) ? 1 : 0) - (IsDigitalActive(b.Left!) ? 1 : 0);
            double y = (IsDigitalActive(b.Up!) ? 1 : 0) - (IsDigitalActive(b.Down!) ? 1 : 0);
            var raw = new Vec2(x, y);
            var len = raw.Length;
            // 拍板：对角向量归一化到长度 1（单方向本身长度已是 1，同一归一化处理不影响其值）。
            return len > 0 ? raw * (1.0 / len) : Vec2.Zero;
        }

        /// <summary>
        /// 判断记录：<c>pad_stick:&lt;left|right&gt;</c> 的 x/y 分量轴名约定为
        /// <c>"{left|right}x"</c>/<c>"{left|right}y"</c>（如 <c>"leftx"</c>/<c>"lefty"</c>），
        /// 经 <c>IInput.GetGamepadAxis(gamepadIndex, axis)</c> 取值——02_引擎适配层.md 第 1.5 节
        /// 与本仓库 <c>IInput.cs</c> 均未规定手柄轴名字取值集合（"具体轴名由各引擎适配层
        /// 实现约定"），本模块选定这一具体约定并在此记录，供设计层复核。
        /// </summary>
        private Vec2 EvaluatePadStick(ParsedBinding b, IInput input)
        {
            var x = input.GetGamepadAxis(_options.GamepadIndex, b.Name! + "x");
            var y = input.GetGamepadAxis(_options.GamepadIndex, b.Name! + "y");
            return new Vec2(x, y);
        }

        // -----------------------------------------------------------------
        // 重置 / 导出 / 导入
        // -----------------------------------------------------------------

        public void ResetBindings(string actionName)
        {
            var state = RequireAction(actionName);
            state.CurrentBindings = new List<string>(state.Definition.DefaultBindings);
            state.ParsedBindings = ParseAll(state.CurrentBindings);
            state.IsDirty = false;
        }

        public JsonObject ExportBindings()
        {
            var builder = new JsonObjectBuilder();
            foreach (var name in _actionOrder)
            {
                var state = _actions[name];
                if (!state.IsDirty) continue;

                var arr = new List<JsonValue>(state.CurrentBindings.Count);
                foreach (var binding in state.CurrentBindings)
                {
                    arr.Add(new JsonString(binding));
                }
                builder.Add(name, new JsonArray(arr));
            }
            return builder.Build();
        }

        public void ImportBindings(JsonObject bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));

            foreach (var entry in bindings)
            {
                var actionName = entry.Key;
                var state = RequireAction(actionName);

                if (!(entry.Value is JsonArray arr))
                {
                    throw new ArgumentException($"动作 \"{actionName}\" 的绑定值必须是数组", nameof(bindings));
                }

                var newBindings = new List<string>(arr.Count);
                foreach (var v in arr)
                {
                    if (!(v is JsonString s))
                    {
                        throw new ArgumentException($"动作 \"{actionName}\" 的绑定数组元素必须是字符串", nameof(bindings));
                    }
                    newBindings.Add(s.Value);
                }

                var parsed = ParseAll(newBindings); // 格式非法直接抛 ArgumentException
                state.CurrentBindings = newBindings;
                state.ParsedBindings = parsed;
                state.IsDirty = !BindingListEquals(state.CurrentBindings, state.Definition.DefaultBindings);
            }
        }

        private static bool BindingListEquals(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }
}
