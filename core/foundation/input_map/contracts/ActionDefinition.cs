using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 一条动作声明：动作 id、类型、默认绑定、重绑分组、说明（见 03_运行时骨架.md 第 7 节、
    /// 第 9 节 <c>InputMapHost.declareActionSet(actionSetId, actions: List&lt;ActionDefinition&gt;)</c>）。
    /// 对应数据表 <c>found.input_action</c> 一行，见 <see cref="InputActionSchema"/>、
    /// <see cref="FromRecord"/>。
    /// </summary>
    public sealed class ActionDefinition
    {
        /// <summary>动作 id，如 <c>input.action.move</c>。</summary>
        public Id ActionId { get; }

        public ActionKind Kind { get; }

        /// <summary>默认绑定字符串列表，语法见 <see cref="BindingParser"/>；同一动作的多条绑定
        /// 之间是"或"关系（任一激活即视为该动作激活，见 <c>IInputMapHost</c> 判断记录）。</summary>
        public IReadOnlyList<string> DefaultBindings { get; }

        /// <summary>重绑分组：同组内的绑定互相独占（见 03 第 7 节"冲突检测"、
        /// <c>IInputMapHost.Rebind</c>）。未声明时默认 <c>"default"</c>。</summary>
        public string RebindGroup { get; }

        public string? Description { get; }

        /// <summary>
        /// 动作类别（手感设计/01 第 2.1 节 <c>class</c>）：取消窗口、优先级、连招都按类别工作。
        /// <para>
        /// 判断记录：设计文档把 <c>class</c> 列为必填，但既有数据（<c>data/_framework</c> 的 14 行、游戏层既有动作集）没有这个字段，
        /// "缺省值让现有输入行为完全不变"要求既有数据零改动合法，因此本字段在 schema 里可选；<b>未声明类别的动作不经输入缓冲</b>
        /// （<c>null</c>，行为与此前逐位一致），声明了非 <see cref="ActionClass.Move"/> 类别的按钮动作才入缓冲。
        /// </para>
        /// </summary>
        public ActionClass? Class { get; }

        /// <summary>动作级缓冲窗口（毫秒）；<c>null</c> 取手感档案输入组 <c>buffer_ms</c>（无手感档案时取 0，即不缓冲）。0 表示不缓冲。</summary>
        public double? BufferMs { get; }

        /// <summary>显式优先级；<c>null</c> 取类别缺省（<see cref="ActionClassDefaults.Priority"/>）。</summary>
        public int? Priority { get; }

        /// <summary>按住阈值（毫秒）：声明后区分点按与按住（蓄力类动作）；<c>null</c> 表示本动作自己不声明（档案缺省按住阈值仍可对 attack/skill 类生效）。</summary>
        public double? HoldThresholdMs { get; }

        /// <summary>同一动作在缓冲未过期时再次按下：刷新（缺省）或忽略。</summary>
        public InputRepeatPolicy RepeatPolicy { get; }

        /// <summary>显式的"接受时朝向对齐"；<c>null</c> 取类别缺省（<see cref="ActionClassDefaults.FaceOnAccept"/>）。</summary>
        public bool? FaceOnAccept { get; }

        /// <summary>宽限窗口适用的条件名（<c>found.grace_condition</c> 的 key，手感设计/01 第 2.4 节）；缺省为空。</summary>
        public IReadOnlyList<Id> GraceConditions { get; }

        /// <summary>缓冲接受后施放的技能所在的技能绑定槽位名（S10 加法字段）；<c>null</c> 表示本动作不映射技能。</summary>
        public string? SkillSlot { get; }

        /// <summary>
        /// 控制空间（<see cref="InputControlSpace"/>）：<c>world</c>（缺省，轴值原样当世界方向）或 <c>camera_relative</c>（轴值按
        /// 当前相机偏航换算成世界方向，第三人称/俯角镜头用）。只对 <see cref="ActionKind.Axis2D"/> 动作有意义；
        /// 声明 <c>camera_relative</c> 的动作要求输入映射宿主配了相机朝向查询（<see cref="InputMapOptions.CameraOrientation"/>）。
        /// 未声明时恒为 <see cref="InputControlSpace.World"/>，轴值与此前逐位一致。
        /// </summary>
        public string ControlSpace { get; }

        /// <summary>
        /// 模拟轴处理默认值（<c>dead_zone</c>/<c>response_curve</c>/<c>smoothing_ms</c>，仅 axis 动作，ADR-0143）；<c>null</c> 表示未声明，
        /// 轴值与此前逐位一致（原始值直通）。玩家设置可经 <see cref="InputMapHost.SetAxisProcessing"/> 覆盖。
        /// </summary>
        public AxisProcessing? AxisProcessing { get; }

        /// <summary>
        /// 按住释放时施放的技能所在的技能绑定槽位名（<c>hold_skill_slot</c>，点按/按住变体，ADR-0143）：缓冲记录以 <see cref="BufferHoldState.HoldReleased"/>
        /// 被接受时走本槽位，点按走 <see cref="SkillSlot"/>；<c>null</c> 表示点按与按住施放同一技能（既有行为）。
        /// 分界只有一个：<see cref="HoldThresholdMs"/>（或档案缺省按住阈值，attack/skill 类）。
        /// </summary>
        public string? HoldSkillSlot { get; }

        /// <summary>
        /// 可变跳高的裁切比例（<c>jump_cut_ratio</c>，仅 <see cref="ActionClass.Jump"/>，ADR-0143）：起跳后松键（或起跳时已松键）若仍在上升，
        /// 上升速度乘以该比例（0～1 开区间）；<c>null</c> 表示不裁切（既有行为）。
        /// </summary>
        public double? JumpCutRatio { get; }

        /// <summary>生效优先级：显式值，否则类别缺省；未声明类别的动作为 0。</summary>
        public int EffectivePriority => Priority ?? (Class.HasValue ? ActionClassDefaults.Priority(Class.Value) : 0);

        /// <summary>生效的接受时朝向对齐：显式值，否则类别缺省；未声明类别的动作为 false。</summary>
        public bool EffectiveFaceOnAccept => FaceOnAccept ?? (Class.HasValue && ActionClassDefaults.FaceOnAccept(Class.Value));

        /// <summary>该动作是否经输入缓冲：必须是按钮型动作且声明了非 <see cref="ActionClass.Move"/> 的类别。</summary>
        public bool IsBuffered => Kind == ActionKind.Button && Class.HasValue && ActionClassDefaults.IsBuffered(Class.Value);

        public ActionDefinition(
            Id actionId,
            ActionKind kind,
            IReadOnlyList<string> defaultBindings,
            string rebindGroup = "default",
            string? description = null)
            : this(actionId, kind, defaultBindings, rebindGroup, description,
                actionClass: null, bufferMs: null, priority: null, holdThresholdMs: null,
                repeatPolicy: InputRepeatPolicy.Refresh, faceOnAccept: null, graceConditions: null, skillSlot: null)
        {
        }

        /// <summary>
        /// 十二参数构造（手感落地 S1 的签名）原样保留，转调带 <c>skillSlot</c> 的十三参数重载（纯加法，ABI 只增不减）。
        /// </summary>
        public ActionDefinition(
            Id actionId,
            ActionKind kind,
            IReadOnlyList<string> defaultBindings,
            string rebindGroup,
            string? description,
            ActionClass? actionClass,
            double? bufferMs,
            int? priority,
            double? holdThresholdMs,
            InputRepeatPolicy repeatPolicy,
            bool? faceOnAccept,
            IReadOnlyList<Id>? graceConditions)
            : this(actionId, kind, defaultBindings, rebindGroup, description, actionClass, bufferMs, priority,
                holdThresholdMs, repeatPolicy, faceOnAccept, graceConditions, skillSlot: null)
        {
        }

        /// <summary>
        /// 带手感字段的构造（手感设计/01 第 2.1 节，纯加法重载；上面的五参数构造原样保留并转调本重载，各手感字段取缺省）。
        /// 这里不带默认值，避免与五参数构造的"只传 3～5 个参数"调用点产生重载二义性。
        /// </summary>
        public ActionDefinition(
            Id actionId,
            ActionKind kind,
            IReadOnlyList<string> defaultBindings,
            string rebindGroup,
            string? description,
            ActionClass? actionClass,
            double? bufferMs,
            int? priority,
            double? holdThresholdMs,
            InputRepeatPolicy repeatPolicy,
            bool? faceOnAccept,
            IReadOnlyList<Id>? graceConditions,
            string? skillSlot)
            : this(actionId, kind, defaultBindings, rebindGroup, description, actionClass, bufferMs, priority,
                holdThresholdMs, repeatPolicy, faceOnAccept, graceConditions, skillSlot, controlSpace: null)
        {
        }

        /// <summary>
        /// 带控制空间的构造（纯加法重载，十三参数构造原样保留并转调本重载，<paramref name="controlSpace"/> 取缺省）。
        /// <paramref name="controlSpace"/> 为 <c>null</c> 或空串视为 <see cref="InputControlSpace.World"/>；取值不在
        /// <see cref="InputControlSpace"/> 里、或在非 <see cref="ActionKind.Axis2D"/> 动作上声明 <c>camera_relative</c> 都抛
        /// <see cref="ArgumentException"/>（不静默忽略）。
        /// </summary>
        public ActionDefinition(
            Id actionId,
            ActionKind kind,
            IReadOnlyList<string> defaultBindings,
            string rebindGroup,
            string? description,
            ActionClass? actionClass,
            double? bufferMs,
            int? priority,
            double? holdThresholdMs,
            InputRepeatPolicy repeatPolicy,
            bool? faceOnAccept,
            IReadOnlyList<Id>? graceConditions,
            string? skillSlot,
            string? controlSpace)
            : this(actionId, kind, defaultBindings, rebindGroup, description, actionClass, bufferMs, priority,
                holdThresholdMs, repeatPolicy, faceOnAccept, graceConditions, skillSlot, controlSpace,
                axisProcessing: null, holdSkillSlot: null, jumpCutRatio: null)
        {
        }

        /// <summary>
        /// 带轴处理、按住变体槽位、可变跳高的构造（ADR-0143，纯加法重载；十四参数构造原样保留并转调本重载，三项取缺省）。
        /// 校验：<paramref name="axisProcessing"/> 只能用于轴动作；<paramref name="holdSkillSlot"/> 只能用于按钮动作且要有按住阈值来源
        /// （动作自己的 <c>hold_threshold_ms</c>，或 attack/skill 类走档案缺省阈值）；<paramref name="jumpCutRatio"/> 只能用于
        /// <see cref="ActionClass.Jump"/> 且在 (0, 1) 内；jump 类不支持 <c>hold_threshold_ms</c>。不静默忽略，违反即抛 <see cref="ArgumentException"/>。
        /// </summary>
        public ActionDefinition(
            Id actionId,
            ActionKind kind,
            IReadOnlyList<string> defaultBindings,
            string rebindGroup,
            string? description,
            ActionClass? actionClass,
            double? bufferMs,
            int? priority,
            double? holdThresholdMs,
            InputRepeatPolicy repeatPolicy,
            bool? faceOnAccept,
            IReadOnlyList<Id>? graceConditions,
            string? skillSlot,
            string? controlSpace,
            AxisProcessing? axisProcessing,
            string? holdSkillSlot,
            double? jumpCutRatio)
        {
            if (defaultBindings == null || defaultBindings.Count == 0)
            {
                throw new ArgumentException($"动作 \"{actionId}\" 的 DefaultBindings 不能为空", nameof(defaultBindings));
            }
            if (string.IsNullOrEmpty(rebindGroup))
            {
                throw new ArgumentException($"动作 \"{actionId}\" 的 RebindGroup 不能为空", nameof(rebindGroup));
            }

            if (bufferMs.HasValue && !(bufferMs.Value >= 0))
            {
                throw new ArgumentException($"动作 \"{actionId}\" 的 BufferMs 必须是非负数", nameof(bufferMs));
            }
            if (holdThresholdMs.HasValue && !(holdThresholdMs.Value > 0))
            {
                throw new ArgumentException($"动作 \"{actionId}\" 的 HoldThresholdMs 必须是正数", nameof(holdThresholdMs));
            }

            ActionId = actionId;
            Kind = kind;
            DefaultBindings = defaultBindings;
            RebindGroup = rebindGroup;
            Description = description;
            Class = actionClass;
            BufferMs = bufferMs;
            Priority = priority;
            HoldThresholdMs = holdThresholdMs;
            RepeatPolicy = repeatPolicy;
            FaceOnAccept = faceOnAccept;
            GraceConditions = graceConditions ?? Array.Empty<Id>();
            SkillSlot = string.IsNullOrEmpty(skillSlot) ? null : skillSlot;

            var space = string.IsNullOrEmpty(controlSpace) ? InputControlSpace.World : controlSpace!;
            if (!InputControlSpace.IsValid(space))
            {
                throw new ArgumentException(
                    $"动作 \"{actionId}\" 的 ControlSpace 取值 \"{space}\" 不合法（{InputControlSpace.World}|{InputControlSpace.CameraRelative}）",
                    nameof(controlSpace));
            }

            if (space == InputControlSpace.CameraRelative && kind != ActionKind.Axis2D)
            {
                throw new ArgumentException(
                    $"动作 \"{actionId}\" 声明了 {InputControlSpace.CameraRelative}，但类型是 {kind}：控制空间只适用于 Axis2D 动作",
                    nameof(controlSpace));
            }

            ControlSpace = space;

            if (axisProcessing != null && kind == ActionKind.Button)
            {
                throw new ArgumentException(
                    $"动作 \"{actionId}\" 声明了轴处理（dead_zone/response_curve/smoothing_ms），但类型是 {kind}：只适用于轴动作", nameof(axisProcessing));
            }

            if (jumpCutRatio.HasValue)
            {
                if (actionClass != ActionClass.Jump)
                {
                    throw new ArgumentException($"动作 \"{actionId}\" 声明了 jump_cut_ratio，但类别不是 jump", nameof(jumpCutRatio));
                }

                if (!(jumpCutRatio.Value > 0.0 && jumpCutRatio.Value < 1.0))
                {
                    throw new ArgumentException($"动作 \"{actionId}\" 的 jump_cut_ratio 必须在 (0, 1) 内", nameof(jumpCutRatio));
                }
            }

            if (actionClass == ActionClass.Jump && holdThresholdMs.HasValue)
            {
                throw new ArgumentException(
                    $"动作 \"{actionId}\" 是 jump 类，不支持 hold_threshold_ms（跳跃不区分点按与按住；可变跳高用 jump_cut_ratio）", nameof(holdThresholdMs));
            }

            var holdSlot = string.IsNullOrEmpty(holdSkillSlot) ? null : holdSkillSlot;
            if (holdSlot != null)
            {
                var hasThreshold = holdThresholdMs.HasValue || actionClass == ActionClass.Attack || actionClass == ActionClass.Skill;
                if (kind != ActionKind.Button || !actionClass.HasValue || actionClass == ActionClass.Move || actionClass == ActionClass.Jump || !hasThreshold)
                {
                    throw new ArgumentException(
                        $"动作 \"{actionId}\" 声明了 hold_skill_slot：只适用于声明了类别的按钮动作，且需要按住阈值来源（hold_threshold_ms，或 attack/skill 类的档案缺省阈值）",
                        nameof(holdSkillSlot));
                }
            }

            AxisProcessing = axisProcessing;
            HoldSkillSlot = holdSlot;
            JumpCutRatio = jumpCutRatio;
        }

        /// <summary>
        /// 返回一份与本动作完全相同、只有 <see cref="ControlSpace"/> 换成 <paramref name="controlSpace"/> 的新定义
        /// （宿主按场景/设置把某个轴动作切成相机相对时用；数据行不必为此改写）。校验同构造函数。
        /// </summary>
        public ActionDefinition WithControlSpace(string controlSpace) =>
            new ActionDefinition(ActionId, Kind, DefaultBindings, RebindGroup, Description, Class, BufferMs, Priority,
                HoldThresholdMs, RepeatPolicy, FaceOnAccept, GraceConditions, SkillSlot, controlSpace, AxisProcessing, HoldSkillSlot, JumpCutRatio);

        /// <summary>
        /// 从 <see cref="DataRecord"/>（<c>found.input_action</c> 表的一行）构造。
        /// <para>
        /// 判断记录：<c>found.input_action</c> 按登记表处理，主键字段名为 <c>key</c>
        /// 而非一般内容表的 <c>id</c>（见本模块 README、<c>data/README.md</c>"记录主键"、
        /// <c>schema/found.input_action.md</c>）——字段里的取值仍是一个合法 <see cref="Id"/>
        /// （如 <c>input.action.move</c>），只是承载它的 JSON 字段名按登记表惯例叫 <c>key</c>，
        /// 与 <c>found.event_catalog</c> 同一惯例。
        /// </para>
        /// </summary>
        public static ActionDefinition FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var actionId = record.GetId("key");
            var kindText = record.GetString("kind");
            var kind = ParseKind(record, kindText);

            var bindingsArray = record.GetArray("default_bindings");
            var bindings = new List<string>(bindingsArray.Count);
            for (int i = 0; i < bindingsArray.Count; i++)
            {
                if (!(bindingsArray[i] is JsonString s))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "default_bindings",
                        $"第 {i} 个元素不是字符串");
                }
                bindings.Add(s.Value);
            }

            var rebindGroup = record.TryGetString("rebind_group", out var rg) ? rg : "default";
            var description = record.TryGetString("description", out var d) ? d : null;

            ActionClass? actionClass = null;
            if (record.TryGetString("class", out var classText))
            {
                actionClass = ParseClass(record, classText);
            }

            double? bufferMs = record.TryGetNumber("buffer_ms", out var bm) ? bm : (double?)null;
            int? priority = record.TryGetInt("priority", out var pr) ? checked((int)pr) : (int?)null;
            double? holdMs = record.TryGetNumber("hold_threshold_ms", out var hm) ? hm : (double?)null;

            var repeat = InputRepeatPolicy.Refresh;
            if (record.TryGetString("repeat_policy", out var repeatText))
            {
                switch (repeatText)
                {
                    case "refresh": repeat = InputRepeatPolicy.Refresh; break;
                    case "ignore": repeat = InputRepeatPolicy.Ignore; break;
                    default:
                        throw new DataFieldException(record.Table.Name, record.Key, "repeat_policy",
                            $"取值 \"{repeatText}\" 不是合法枚举（refresh|ignore）");
                }
            }

            bool? faceOnAccept = record.TryGetBool("face_on_accept", out var fa) ? fa : (bool?)null;
            IReadOnlyList<Id>? graceConditions = record.TryGetIdList("grace_conditions", out var gc) ? gc : null;

            string? skillSlot = record.TryGetString("skill_slot", out var ss) ? ss : null;

            string? controlSpace = null;
            if (record.TryGetString("control_space", out var cs))
            {
                if (!InputControlSpace.IsValid(cs))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "control_space",
                        $"取值 \"{cs}\" 不是合法枚举（{InputControlSpace.World}|{InputControlSpace.CameraRelative}）");
                }

                controlSpace = cs;
            }

            AxisProcessing? axis = null;
            var hasDz = record.TryGetNumber("dead_zone", out var dz);
            var hasCurve = record.TryGetString("response_curve", out var curve);
            var hasSmooth = record.TryGetNumber("smoothing_ms", out var smooth);
            if (hasDz || hasCurve || hasSmooth)
            {
                if (hasCurve && !AxisProcessing.IsValidCurve(curve))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "response_curve",
                        $"取值 \"{curve}\" 不是合法枚举（{AxisProcessing.Linear}|{AxisProcessing.Expo}|{AxisProcessing.CustomPrefix}<curve_id>）");
                }

                if (hasDz && !(dz >= 0.0 && dz < 1.0))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "dead_zone", "必须在 [0, 1) 内");
                }

                if (hasSmooth && !(smooth >= 0.0))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "smoothing_ms", "必须是非负数");
                }

                axis = new AxisProcessing(hasDz ? dz : 0.0, hasCurve ? curve : null, hasSmooth ? smooth : 0.0);
            }

            string? holdSkillSlot = record.TryGetString("hold_skill_slot", out var hs) ? hs : null;
            double? jumpCut = record.TryGetNumber("jump_cut_ratio", out var jc) ? jc : (double?)null;

            return new ActionDefinition(actionId, kind, bindings, rebindGroup, description,
                actionClass, bufferMs, priority, holdMs, repeat, faceOnAccept, graceConditions, skillSlot, controlSpace,
                axis, holdSkillSlot, jumpCut);
        }

        private static ActionClass ParseClass(DataRecord record, string classText)
        {
            switch (classText)
            {
                case "move": return ActionClass.Move;
                case "attack": return ActionClass.Attack;
                case "skill": return ActionClass.Skill;
                case "dodge": return ActionClass.Dodge;
                case "interact": return ActionClass.Interact;
                case "item": return ActionClass.Item;
                case "menu": return ActionClass.Menu;
                case "jump": return ActionClass.Jump;
                default:
                    throw new DataFieldException(record.Table.Name, record.Key, "class",
                        $"取值 \"{classText}\" 不是合法枚举（move|attack|skill|dodge|interact|item|menu|jump）");
            }
        }

        private static ActionKind ParseKind(DataRecord record, string kindText)
        {
            switch (kindText)
            {
                case "button": return ActionKind.Button;
                case "axis1d": return ActionKind.Axis1D;
                case "axis2d": return ActionKind.Axis2D;
                default:
                    throw new DataFieldException(record.Table.Name, record.Key, "kind",
                        $"取值 \"{kindText}\" 不是合法枚举（button|axis1d|axis2d）");
            }
        }
    }
}
