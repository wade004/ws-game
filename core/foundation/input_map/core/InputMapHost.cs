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

        /// <summary>诊断出口只读暴露（ABI 只新增只读属性，见 architecture/adr/0042-诊断契约统一转发到宿主控制台.md）：
        /// 供 adapters/unity 侧统一诊断转发机制轮询本实例累积的 Warnings/Errors，不改变本类型任何既有公开签名。</summary>
        public IInputMapDiagnostics Diagnostics => _diagnostics;

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

            /// <summary>边沿记录用的"上一次求值时是否激活"（仅 <see cref="InputMapHost.SetEdgeSink"/> 登记后才使用）。</summary>
            public bool EdgeActive;

            /// <summary>玩家设置对轴处理的覆盖（<see cref="InputMapHost.SetAxisProcessing"/>）；null 即用动作定义里的默认值。</summary>
            public AxisProcessing? Override;

            /// <summary>当前生效的自定义响应曲线（<see cref="AxisProcessing.TryGetCustomCurveId"/> 为真时解析出的曲线）。</summary>
            public PiecewiseCurve? Curve;

            /// <summary>平滑状态：上一步输出的幅值与方向（只在有平滑时使用）。</summary>
            public double SmoothedMagnitude;

            public Vec2 LastDirection;

            public AxisProcessing? Effective => Override ?? Definition.AxisProcessing;
        }

        private IInputEdgeSink? _edgeSink;
        private readonly List<KeyValuePair<string, bool>> _edgeScratch = new List<KeyValuePair<string, bool>>();

        /// <summary>
        /// 登记按钮边沿接收端（见 <see cref="IInputMapHost.SetEdgeSink"/>）。判断记录：边沿按"本批次内逐事件之后重算每个按钮动作的
        /// 激活状态"得出，因此一个动作绑定多个按键时遵循与 <see cref="IsActionActive"/> 相同的"或"语义（任一按下 → 激活，最后一个
        /// 抬起 → 非激活），同一批次内"按下→抬起"产生两条边沿（点按不丢）；与既有 <see cref="InputActionTriggeredEvent"/> 的判定互不影响。
        /// </summary>
        public void SetEdgeSink(IInputEdgeSink? sink)
        {
            _edgeSink = sink;
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

                if (def.ControlSpace == InputControlSpace.CameraRelative && _options.CameraOrientation == null)
                {
                    throw new InvalidOperationException(
                        $"动作 \"{name}\" 声明了 {InputControlSpace.CameraRelative} 控制空间，但输入映射宿主没有配相机朝向查询（InputMapOptions.CameraOrientation）；"
                        + "不静默当成偏航 0");
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
                    Curve = ResolveCurve(name, def.AxisProcessing),
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

        /// <summary>已声明动作名的快照（声明顺序）；返回新列表，之后再声明不影响已取得的快照。</summary>
        public IReadOnlyList<string> GetDeclaredActionNames() => _actionOrder.ToArray();

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

            // 手感设计/01 第 1 节：登记了边沿接收端时，批次开始先把"上次激活状态"对齐到当前状态（防重绑定后漂移），
            // 每个事件处理完后重算按钮动作激活状态，状态翻转即记一条边沿，批次末统一按发生顺序交付。
            var edgeSink = _edgeSink;
            if (edgeSink != null)
            {
                _edgeScratch.Clear();
                foreach (var name in _actionOrder)
                {
                    var st = _actions[name];
                    st.EdgeActive = st.CurrentActive;
                }
            }

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

                if (edgeSink != null)
                {
                    foreach (var name in _actionOrder)
                    {
                        var st = _actions[name];
                        if (st.Definition.Kind != ActionKind.Button) continue;
                        var nowActive = EvaluateDigital(st.ParsedBindings);
                        if (nowActive != st.EdgeActive)
                        {
                            st.EdgeActive = nowActive;
                            _edgeScratch.Add(new KeyValuePair<string, bool>(name, nowActive));
                        }
                    }
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
                        var axis1d = EvaluateAxis1D(state.ParsedBindings, input, out var analog1d);
                        state.CachedAxis = new Vec2(axis1d, 0);
                        if (analog1d && state.Effective is AxisProcessing processing1d)
                        {
                            state.CachedAxis = ProcessAxis(state, processing1d, state.CachedAxis);
                        }

                        break;
                    case ActionKind.Axis2D:
                        state.CachedAxis = EvaluateAxis2D(state, input);

                        if (state.Definition.ControlSpace == InputControlSpace.CameraRelative)
                        {
                            state.CachedAxis = RotateByCameraYaw(state.CachedAxis, _options.CameraOrientation!.YawRadians);
                        }

                        break;
                }
            }

            if (edgeSink != null && _edgeScratch.Count > 0)
            {
                // 交付期间接收端可能再调用本类型的只读查询，先复制一份避免遍历中被改动。
                var delivery = _edgeScratch.ToArray();
                _edgeScratch.Clear();
                for (int i = 0; i < delivery.Length; i++)
                {
                    edgeSink.OnButtonEdge(delivery[i].Key, delivery[i].Value);
                }
            }
        }

        /// <summary>
        /// 相机相对：摇杆轴值（x 向右、y 向上）经相机偏航旋转成世界平面方向 <c>x·右 + y·上</c>（右 = (cos yaw, sin yaw)，上 = (−sin yaw, cos yaw)），
        /// 模长不变。偏航恰为 0 时结果与输入逐位相同。
        /// </summary>
        private static Vec2 RotateByCameraYaw(Vec2 axis, double yawRadians)
        {
            var c = Math.Cos(yawRadians);
            var s = Math.Sin(yawRadians);
            return new Vec2(axis.X * c - axis.Y * s, axis.X * s + axis.Y * c);
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

        private double EvaluateAxis1D(List<ParsedBinding> bindings, IInput input, out bool analog)
        {
            analog = false;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Kind == BindingKind.PadAxis)
                {
                    analog = true;
                    return input.GetGamepadAxis(_options.GamepadIndex, bindings[i].Name!);
                }
            }
            return 0;
        }

        /// <summary>
        /// 判断记录（消费方反馈 P2 缺口 4：键盘与手柄摇杆同时绑在一个二维轴动作上，只有第一条轴型绑定生效）：此前这里在遇到第一条
        /// <c>composite2d</c>/<c>pad_stick</c> 绑定时直接返回——<c>["composite2d:key:w|key:s|key:a|key:d", "pad_stick:left"]</c> 这种
        /// "键盘 + 手柄都能移动"的常规写法里，手柄摇杆永远读不到（反过来写则键盘读不到），且没有任何诊断。
        /// 现在按列出顺序逐条求值，<b>第一条求值非零的绑定胜出</b>；全部为零时返回零。摇杆绑定先按本动作的轴处理（死区、响应曲线、平滑）
        /// 求值再判断是否非零，所以摇杆在死区内（处理后为零）不会压住键盘。只有一条轴型绑定时结果与改动前逐位相同。
        /// 同一动作声明两条摇杆绑定时，平滑状态只随"被求值到的那条"推进（排在非零绑定之后的不求值）。
        /// </summary>
        private Vec2 EvaluateAxis2D(ActionState state, IInput input)
        {
            var bindings = state.ParsedBindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                Vec2 value;
                switch (b.Kind)
                {
                    case BindingKind.Composite2D:
                        value = EvaluateComposite2D(b);
                        break;
                    case BindingKind.PadStick:
                        value = EvaluatePadStick(b, input);
                        if (state.Effective is AxisProcessing processing)
                        {
                            value = ProcessAxis(state, processing, value);
                        }

                        break;
                    default:
                        continue;
                }

                if (value.X != 0.0 || value.Y != 0.0)
                {
                    return value;
                }
            }

            return Vec2.Zero;
        }

        // -----------------------------------------------------------------
        // 模拟轴处理：死区、响应曲线、平滑（ADR-0143）
        // -----------------------------------------------------------------

        /// <summary>
        /// 对模拟轴原始值做死区 → 响应曲线 → 平滑（只下降沿）。恒等处理（<see cref="AxisProcessing.IsIdentity"/>）直接返回原始值，保证未声明时逐位等于原始值直通。
        /// 1D 轴（<paramref name="raw"/>.Y 恒为 0）与 2D 轴同一套算法：幅值取向量长度，方向取符号/单位向量。
        /// </summary>
        private Vec2 ProcessAxis(ActionState state, AxisProcessing processing, Vec2 raw)
        {
            if (processing.IsIdentity) return raw;

            var length = raw.Length;
            var direction = length > 0.0 ? raw * (1.0 / length) : Vec2.Zero;

            // 死区 + 重标度。幅值超过 1 的（驱动给出的越界值）夹到 1，保证曲线输入在 0～1。
            double magnitude;
            if (length <= processing.DeadZone)
            {
                magnitude = 0.0;
            }
            else
            {
                var clamped = Math.Min(length, 1.0);
                magnitude = processing.DeadZone > 0.0 ? (clamped - processing.DeadZone) / (1.0 - processing.DeadZone) : clamped;
            }

            // 响应曲线（对 0～1 幅值逐点映射）。
            if (processing.ResponseCurve == AxisProcessing.Expo)
            {
                magnitude *= magnitude;
            }
            else if (state.Curve != null && processing.ResponseCurve != AxisProcessing.Linear)
            {
                magnitude = Math.Min(1.0, Math.Max(0.0, state.Curve.Evaluate(magnitude)));
            }

            // 平滑：只作用于幅值的下降沿；上升与方向变化即时生效。无输入方向时沿用上一个有效方向让幅值回落。
            if (processing.SmoothingMs > 0.0)
            {
                var step = _options.StepSeconds / (processing.SmoothingMs / 1000.0);
                var previous = state.SmoothedMagnitude;
                if (magnitude < previous)
                {
                    magnitude = Math.Max(magnitude, previous - step);
                }

                if (direction.SqrLength > 0.0)
                {
                    state.LastDirection = direction;
                }
                else
                {
                    direction = state.LastDirection;
                }

                state.SmoothedMagnitude = magnitude;
            }

            return magnitude > 0.0 ? direction * magnitude : Vec2.Zero;
        }

        private PiecewiseCurve? ResolveCurve(string actionName, AxisProcessing? processing)
        {
            if (processing == null || !processing.TryGetCustomCurveId(out var curveId)) return null;
            var curve = _options.CurveResolver?.Invoke(curveId);
            if (curve == null)
            {
                throw new InvalidOperationException(
                    $"动作 \"{actionName}\" 的响应曲线 \"custom:{curveId}\" 解析不到（InputMapOptions.CurveResolver 未配置或不认识该曲线 id）；不静默当线性");
            }

            return curve;
        }

        /// <summary>
        /// 玩家设置覆盖某个轴动作的处理参数（死区、响应曲线、平滑）；<paramref name="processing"/> 为 null 清除覆盖、回到动作定义里的默认值。
        /// 只适用于轴动作；自定义曲线解析不到抛 <see cref="InvalidOperationException"/>。覆盖随 <see cref="ExportAxisSettings"/> 导出、
        /// <see cref="ImportAxisSettings"/> 读回（设置文件，不进存档）。平滑状态被重置。
        /// </summary>
        public void SetAxisProcessing(string actionName, AxisProcessing? processing)
        {
            var state = RequireAction(actionName);
            if (state.Definition.Kind == ActionKind.Button)
            {
                throw new InvalidOperationException($"动作 \"{actionName}\" 是 Button 类型，轴处理不适用");
            }

            var curve = ResolveCurve(actionName, processing ?? state.Definition.AxisProcessing);
            state.Override = processing;
            state.Curve = curve;
            state.SmoothedMagnitude = 0.0;
            state.LastDirection = Vec2.Zero;
        }

        /// <summary>某个轴动作当前生效的处理参数（玩家覆盖优先，其次动作定义默认值）；都没有为 null（原始值直通）。</summary>
        public AxisProcessing? GetAxisProcessing(string actionName) => RequireAction(actionName).Effective;

        /// <summary>导出玩家覆盖的轴处理参数（动作名 → 三项设置）；没有任何覆盖时为空对象。写入设置文件的 <c>input_axis_settings</c> 键。</summary>
        public JsonObject ExportAxisSettings()
        {
            var builder = new JsonObjectBuilder();
            foreach (var name in _actionOrder)
            {
                var state = _actions[name];
                if (state.Override != null) builder.Add(name, state.Override.ToJson());
            }

            return builder.Build();
        }

        /// <summary>
        /// 读回玩家覆盖的轴处理参数。同 <see cref="ImportBindings"/> 的"先全量校验、再统一落地"口径：动作不存在、不是轴动作、值格式非法、
        /// 自定义曲线解析不到，任一条失败整批拒绝（抛异常）、现有覆盖保持不变；文件里没有出现的轴动作覆盖被清除。
        /// </summary>
        public void ImportAxisSettings(JsonObject settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var staged = new List<(ActionState State, AxisProcessing Processing, PiecewiseCurve? Curve)>(settings.Count);
            foreach (var entry in settings)
            {
                var state = RequireAction(entry.Key);
                if (state.Definition.Kind == ActionKind.Button)
                {
                    throw new ArgumentException($"动作 \"{entry.Key}\" 是 Button 类型，轴处理不适用", nameof(settings));
                }

                if (!(entry.Value is JsonObject obj))
                {
                    throw new ArgumentException($"动作 \"{entry.Key}\" 的轴处理值必须是对象", nameof(settings));
                }

                var processing = AxisProcessing.FromJson(obj);
                staged.Add((state, processing, ResolveCurve(entry.Key, processing)));
            }

            foreach (var name in _actionOrder)
            {
                var state = _actions[name];
                if (state.Override == null) continue;
                state.Override = null;
                state.Curve = ResolveCurve(name, state.Definition.AxisProcessing);
                state.SmoothedMagnitude = 0.0;
                state.LastDirection = Vec2.Zero;
            }

            foreach (var item in staged)
            {
                item.State.Override = item.Processing;
                item.State.Curve = item.Curve;
                item.State.SmoothedMagnitude = 0.0;
                item.State.LastDirection = Vec2.Zero;
            }
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

        /// <summary>
        /// 判断记录（ADR-0121 第 8 条，D8）：先把整份输入全量解析、校验（动作存在、值是字符串数组、
        /// 每条绑定格式合法），全部通过后才统一落地；任一条非法整批拒绝、原绑定与脏标记保持不变。
        /// 此前逐条边解析边落地，非法项之前的合法项已经写进去，留下“前半应用”的状态。
        /// </summary>
        public void ImportBindings(JsonObject bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));

            var staged = new List<(ActionState State, List<string> Bindings, List<ParsedBinding> Parsed)>(bindings.Count);
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

                var parsed = ParseAll(newBindings); // 格式非法直接抛 ArgumentException（此时还没有任何落地）
                staged.Add((state, newBindings, parsed));
            }

            foreach (var item in staged)
            {
                item.State.CurrentBindings = item.Bindings;
                item.State.ParsedBindings = item.Parsed;
                item.State.IsDirty = !BindingListEquals(item.State.CurrentBindings, item.State.Definition.DefaultBindings);
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
