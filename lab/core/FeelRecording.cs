using System;
using System.Collections.Generic;

namespace Lab
{
    /// <summary>
    /// 手感场景逻辑时间线上的一条事件（动作开始/相位/标记/取消/结束、目标辅助、命中确认、受击反应、顿帧起止、缓冲丢弃、阻挡变更）。
    /// 实体一律用出场标签（玩家 <c>player</c>，靶子取靶子集条目名）而不是实体 id，攻击实例用序号，保证跨运行稳定。
    /// 字段含义随 <see cref="Kind"/> 变化，见 <see cref="FeelRig"/> 的记录处。
    /// </summary>
    public sealed class FeelEventRecord
    {
        public int Tick { get; }

        /// <summary>
        /// <c>action_started</c>、<c>action_phase</c>、<c>action_marker</c>、<c>action_cancelled</c>、<c>action_finished</c>、
        /// <c>target_assisted</c>、<c>hit_confirmed</c>、<c>reaction</c>、<c>hitstop_started</c>、<c>hitstop_ended</c>、
        /// <c>buffer_dropped</c>、<c>blocking_changed</c>。
        /// </summary>
        public string Kind { get; }

        public string Actor { get; }

        public string Target { get; }

        public string SkillId { get; }

        public string Detail { get; }

        public string Detail2 { get; }

        public int A { get; }

        public int B { get; }

        public int C { get; }

        public double D { get; }

        public FeelEventRecord(
            int tick, string kind, string actor, string target, string skillId, string detail, string detail2,
            int a = 0, int b = 0, int c = 0, double d = 0)
        {
            Tick = tick;
            Kind = kind;
            Actor = actor;
            Target = target;
            SkillId = skillId;
            Detail = detail;
            Detail2 = detail2;
            A = a;
            B = b;
            C = c;
            D = d;
        }
    }

    /// <summary>手感场景逻辑时间线的一步（该宿主固定步 <c>Advance</c> 返回后）：缓冲槽内容、玩家运动层状态、靶子的非常态运动模式。</summary>
    public sealed class FeelTickSample
    {
        public int Tick { get; }

        /// <summary>输入缓冲槽里的动作 id（短名，按槽序以 <c>|</c> 连接，空表示槽空）。</summary>
        public string BufferSlots { get; }

        /// <summary>玩家运动模式（<c>MotionMode</c> 名）。</summary>
        public string Mode { get; }

        /// <summary>玩家位移来源（<c>MotionSource</c> 名）。</summary>
        public string Source { get; }

        /// <summary>玩家速度模长（世界单位/秒）。</summary>
        public double Speed { get; }

        /// <summary>非 <c>Grounded</c> 的靶子运动模式：<c>标签:模式</c> 列表（按出场顺序）。</summary>
        public IReadOnlyList<KeyValuePair<string, string>> TargetModes { get; }

        /// <summary>全部靶子此刻的位置（按出场顺序；巡逻靶的轨迹度量用）。</summary>
        public IReadOnlyList<KeyValuePair<string, Core.Foundation.Common.Vec2>> TargetPositions { get; }

        public FeelTickSample(
            int tick, string bufferSlots, string mode, string source, double speed,
            IReadOnlyList<KeyValuePair<string, string>> targetModes,
            IReadOnlyList<KeyValuePair<string, Core.Foundation.Common.Vec2>> targetPositions)
        {
            TargetPositions = targetPositions;
            Tick = tick;
            BufferSlots = bufferSlots;
            Mode = mode;
            Source = source;
            Speed = speed;
            TargetModes = targetModes;
        }
    }

    /// <summary>表现时间线上假 sink 收到的一条指令（反馈包流水线的出批结果）。</summary>
    public sealed class FeelPresentationRecord
    {
        public int Tick { get; }

        /// <summary><c>sfx</c>、<c>camera</c>、<c>freeze</c>、<c>release</c>、<c>flash</c>、<c>vfx</c>。</summary>
        public string Kind { get; }

        /// <summary>sfx/特效/闪白的资源 id；camera 的命中数与震屏档；freeze/release 的单位标签集合。</summary>
        public string Text { get; }

        /// <summary>camera 的冲击幅度；freeze 的 tick 数；其余为 0。</summary>
        public double Value { get; }

        /// <summary>camera 的衰减毫秒数；其余为 0。</summary>
        public double Value2 { get; }

        public int Count { get; }

        public FeelPresentationRecord(int tick, string kind, string text, double value = 0, double value2 = 0, int count = 0)
        {
            Tick = tick;
            Kind = kind;
            Text = text;
            Value = value;
            Value2 = value2;
            Count = count;
        }
    }

    /// <summary>手感场景的运行期记录（<see cref="LabRecording.Feel"/>；脚本 meta 的 <c>feel</c> 为真时才有）。</summary>
    public sealed class FeelRecording
    {
        /// <summary>本次运行实际生效的手感预设 id（脚本 meta 或格子缺省，或变体覆盖）。</summary>
        public string Preset { get; set; } = string.Empty;

        /// <summary>本次运行装配用的标定行 id。</summary>
        public string CalibrationId { get; set; } = string.Empty;

        /// <summary>手感系统是否开着（变体 <see cref="LabRunVariant.FeelOff"/> 时为假：脚本在旧路径上跑，只记表现之外的空记录）。</summary>
        public bool Assembled { get; set; }

        public List<FeelEventRecord> Events { get; } = new List<FeelEventRecord>();

        public List<FeelTickSample> Ticks { get; } = new List<FeelTickSample>();

        public List<FeelPresentationRecord> Presentation { get; } = new List<FeelPresentationRecord>();

        /// <summary>运行结束时仍未释放的顿帧冻结数（表现层冻结登记，应恒为 0）。</summary>
        public int FrozenAtEnd { get; set; }
    }

    /// <summary>
    /// 一次运行的变体（跨格子不变量与手感场景用）：默认不改变任何行为。
    /// <list type="bullet">
    /// <item><see cref="StripTimelines"/>：<c>null</c> 按格子结算模式（目标选择式格子剥掉额外根技能的 <c>timeline</c> 块并把 <c>cast_time</c> 置 0，
    /// 动作式格子保留）；<c>true/false</c> 显式指定——"动作式格子移除 timeline 并换预设后等于目标选择式格子"的不变量就靠显式指定。</item>
    /// <item><see cref="PresetId"/>：覆盖预设（同时决定标定行）；<c>null</c> 取脚本 meta 与格子缺省。</item>
    /// <item><see cref="FeelOff"/>：手感场景脚本在旧路径上运行（不开手感装配，按钮边沿直接提交施放意图）。</item>
    /// </list>
    /// </summary>
    public sealed class LabRunVariant
    {
        public static LabRunVariant Default { get; } = new LabRunVariant();

        public bool? StripTimelines { get; set; }

        public string? PresetId { get; set; }

        public bool FeelOff { get; set; }

        /// <summary>数据集缓存键的一部分（只含影响数据集内容的字段）。</summary>
        public string DatasetKey(LabScenario? cell)
        {
            var strip = EffectiveStrip(cell);
            return strip ? "strip" : "keep";
        }

        public bool EffectiveStrip(LabScenario? cell) =>
            StripTimelines ?? (cell != null && string.Equals(cell.Settlement, "targeted", StringComparison.Ordinal));
    }
}
