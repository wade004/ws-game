using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Lab
{
    /// <summary>帧数据时间轴上色条的种类（手感设计 06 第 4 节"帧数据时间轴"）。</summary>
    public enum SegmentKind
    {
        Charge,
        Startup,
        Active,
        Recovery,
        /// <summary>取消窗口（<see cref="TimelineSegment.Label"/> 是取消类别）。</summary>
        CancelWindow,
        ComboWindow,
        Invuln,
        Armor,
        Guard,
        Hitstop,
        /// <summary>受击反应（硬直/击退等）。</summary>
        Reaction,
        Downed,
        Getup,
    }

    /// <summary>标记点的种类。</summary>
    public enum MarkKind
    {
        /// <summary>玩家输入（按下/抬起/跳跃/点击移动）。</summary>
        Input,
        ActionStarted,
        ActionFinished,
        ActionCancelled,
        /// <summary>时间线标记（hit/release/cost 等；窗口开闭由色条表示，不重复成点）。</summary>
        Marker,
        /// <summary>命中点（攻击方轨）。</summary>
        Hit,
        /// <summary>裁决结果（受击方轨）。</summary>
        Result,
        /// <summary>目标辅助转向。</summary>
        Assist,
        /// <summary>缓冲丢弃。</summary>
        BufferDropped,
    }

    /// <summary>一条色条：[<see cref="Start"/>, <see cref="End"/>) 的 tick 区间；<see cref="End"/> 小于 0 表示尚未结束（读 <see cref="EndOr"/>）。</summary>
    public sealed class TimelineSegment
    {
        public SegmentKind Kind { get; }

        public int Start { get; }

        public int End { get; internal set; }

        public string Label { get; }

        public bool IsOpen => End < 0;

        public int EndOr(int tick) => End < 0 ? tick : End;

        public bool Covers(int tick) => tick >= Start && (End < 0 || tick < End);

        internal TimelineSegment(SegmentKind kind, int start, int end, string label)
        {
            Kind = kind;
            Start = start;
            End = end;
            Label = label;
        }

        public override string ToString() => Kind + (Label.Length > 0 ? "(" + Label + ")" : string.Empty) + "[" + Start + "," + (End < 0 ? "…" : End.ToString()) + ")";
    }

    public sealed class TimelineMark
    {
        public int Tick { get; }

        public MarkKind Kind { get; }

        public string Text { get; }

        internal TimelineMark(int tick, MarkKind kind, string text)
        {
            Tick = tick;
            Kind = kind;
            Text = text;
        }

        public override string ToString() => Kind + "@" + Tick + " " + Text;
    }

    /// <summary>一个实体一条轨：色条与标记点。</summary>
    public sealed class TimelineTrack
    {
        public string Entity { get; }

        public List<TimelineSegment> Segments { get; } = new List<TimelineSegment>();

        public List<TimelineMark> Marks { get; } = new List<TimelineMark>();

        internal TimelineTrack(string entity)
        {
            Entity = entity;
        }

        /// <summary>某 tick 上覆盖它的全部色条。</summary>
        public List<TimelineSegment> SegmentsAt(int tick)
        {
            var list = new List<TimelineSegment>();
            foreach (var s in Segments)
            {
                if (s.Covers(tick))
                {
                    list.Add(s);
                }
            }

            return list;
        }

        public List<TimelineMark> MarksAt(int tick)
        {
            var list = new List<TimelineMark>();
            foreach (var m in Marks)
            {
                if (m.Tick == tick)
                {
                    list.Add(m);
                }
            }

            return list;
        }
    }

    /// <summary>
    /// 帧数据时间轴的视图模型（ADR-0150，手感设计 06 第 4 节）：纯 C#，只读录制（逻辑事件、每 tick 缓冲槽快照、会话脚本里的输入事件），
    /// 不改任何逻辑——"暂停后逐 tick 拖动"只是移动 <see cref="Cursor"/> 去读已有的记录。每个实体一条轨：前摇/判定/后摇色条、输入标记、
    /// 缓冲槽状态、取消与连招窗口开闭、无敌/霸体/格挡窗口、顿帧、命中点、裁决结果。
    /// <para>
    /// 判断记录（增量同步）：录制随会话增长，<see cref="Sync"/> 只处理上次之后新增的事件与样本，所以每帧调用的代价与新增量成正比，不随一局长度线性增长。
    /// 色条的结束 tick 是不含的（动作相位在 <c>T</c> 切换，则旧相位覆盖到 <c>T-1</c>）；窗口随动作结束/取消一并收尾。
    /// 全部内容派生自既有记录事件，没有新增任何记录通道（命中点之类的几何另见 <see cref="TrajectoryModel"/>）。
    /// </para>
    /// </summary>
    public sealed class TimelineModel
    {
        private readonly Dictionary<string, TimelineTrack> _tracks = new Dictionary<string, TimelineTrack>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, Dictionary<SegmentKind, TimelineSegment>> _open = new Dictionary<string, Dictionary<SegmentKind, TimelineSegment>>(StringComparer.Ordinal);
        private int _eventIndex;
        private int _scriptIndex;
        private int _tickHorizon;

        public const string PlayerLabel = "player";

        /// <summary>轨道（玩家在前，其余按出现顺序）。</summary>
        public IReadOnlyList<TimelineTrack> Tracks
        {
            get
            {
                var list = new List<TimelineTrack>();
                foreach (var name in _order)
                {
                    list.Add(_tracks[name]);
                }

                return list;
            }
        }

        public TimelineTrack? Track(string entity) => _tracks.TryGetValue(entity, out var t) ? t : null;

        /// <summary>已同步到的 tick（含）；没有同步过为 -1。</summary>
        public int LastTick => _tickHorizon - 1;

        /// <summary>
        /// 逐 tick 拖动用的游标：null 表示跟随最新 tick（实时），非 null 时是被钉住的 tick（暂停后拖动）。
        /// 只读记录，不影响逻辑。
        /// </summary>
        public int? Pinned { get; private set; }

        /// <summary>当前读数的 tick：钉住时取钉住值（夹在已记录范围内），否则取最新。</summary>
        public int Cursor => Pinned.HasValue ? Math.Max(0, Math.Min(Pinned.Value, Math.Max(0, LastTick))) : Math.Max(0, LastTick);

        public void Pin(int tick) => Pinned = Math.Max(0, tick);

        public void StepCursor(int delta) => Pin(Cursor + delta);

        public void Follow() => Pinned = null;

        /// <summary>
        /// 把录制里新增的内容并入模型。<paramref name="script"/> 是会话脚本（输入事件的来源）；<paramref name="currentTick"/> 是下一个将要执行的固定步序号。
        /// 手感装配关着（<see cref="LabRecording.Feel"/> 为空）时只同步输入标记。
        /// </summary>
        public void Sync(LabRecording recording, InputScript script, int currentTick)
        {
            Track(PlayerLabel, create: true);
            var feel = recording.Feel;
            if (feel != null)
            {
                for (; _eventIndex < feel.Events.Count; _eventIndex++)
                {
                    Apply(feel.Events[_eventIndex]);
                }

            }

            var events = script.Events;
            for (; _scriptIndex < events.Count; _scriptIndex++)
            {
                ApplyInput(events[_scriptIndex]);
            }

            _tickHorizon = Math.Max(_tickHorizon, currentTick);
        }

        /// <summary>某 tick 的输入缓冲槽（动作短名按槽序以 <c>|</c> 连接，空串为槽空）；该 tick 没有样本返回 null。</summary>
        public static string? BufferAt(LabRecording recording, int tick)
        {
            var feel = recording.Feel;
            if (feel == null)
            {
                return null;
            }

            var sample = SampleAt(feel, tick);
            return sample?.BufferSlots;
        }

        /// <summary>某 tick 的玩家运动模式与来源；没有样本返回 null。</summary>
        public static FeelTickSample? SampleAt(FeelRecording feel, int tick)
        {
            var ticks = feel.Ticks;
            if (tick >= 0 && tick < ticks.Count && ticks[tick].Tick == tick)
            {
                return ticks[tick];
            }

            var lo = 0;
            var hi = ticks.Count - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (ticks[mid].Tick == tick)
                {
                    return ticks[mid];
                }

                if (ticks[mid].Tick < tick)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return null;
        }

        /// <summary>游标处的人读读数（每轨一行：覆盖该 tick 的色条与该 tick 的标记，再加玩家的缓冲槽）。</summary>
        public List<string> Describe(LabRecording recording)
        {
            var tick = Cursor;
            var lines = new List<string>();
            foreach (var track in Tracks)
            {
                var parts = new List<string>();
                foreach (var s in track.SegmentsAt(tick))
                {
                    parts.Add(s.Kind + (s.Label.Length > 0 ? ":" + s.Label : string.Empty));
                }

                foreach (var m in track.MarksAt(tick))
                {
                    parts.Add("◆" + m.Kind + (m.Text.Length > 0 ? " " + m.Text : string.Empty));
                }

                var line = track.Entity + "：" + (parts.Count == 0 ? "—" : string.Join("，", parts));
                if (track.Entity == PlayerLabel)
                {
                    var buffer = BufferAt(recording, tick);
                    line += "　缓冲槽[" + (string.IsNullOrEmpty(buffer) ? "空" : buffer) + "]";
                }

                lines.Add(line);
            }

            return lines;
        }

        // ---------------------------------------------------------------- 内部

        private TimelineTrack Track(string entity, bool create)
        {
            if (!_tracks.TryGetValue(entity, out var track))
            {
                if (!create)
                {
                    throw new KeyNotFoundException(entity);
                }

                track = new TimelineTrack(entity);
                _tracks[entity] = track;
                _open[entity] = new Dictionary<SegmentKind, TimelineSegment>();
                if (string.Equals(entity, PlayerLabel, StringComparison.Ordinal))
                {
                    _order.Insert(0, entity);
                }
                else
                {
                    _order.Add(entity);
                }
            }

            return track;
        }

        private static string Short(string id)
        {
            var i = id.LastIndexOf('.');
            return i >= 0 ? id.Substring(i + 1) : id;
        }

        private void Open(string entity, SegmentKind kind, int tick, string label = "")
        {
            var track = Track(entity, true);
            var open = _open[entity];
            Close(entity, kind, tick);
            var seg = new TimelineSegment(kind, tick, -1, label);
            track.Segments.Add(seg);
            open[kind] = seg;
        }

        private void Close(string entity, SegmentKind kind, int tick)
        {
            if (_open.TryGetValue(entity, out var open) && open.TryGetValue(kind, out var seg))
            {
                open.Remove(kind);
                if (tick <= seg.Start)
                {
                    // 零长度的相位/窗口（如后摇一进入就被缓冲输入取消）画不出来，不进轨道。
                    _tracks[entity].Segments.Remove(seg);
                    return;
                }

                seg.End = tick;
            }
        }

        private static readonly SegmentKind[] PhaseKinds = { SegmentKind.Charge, SegmentKind.Startup, SegmentKind.Active, SegmentKind.Recovery };

        private static readonly SegmentKind[] WindowKinds =
        {
            SegmentKind.CancelWindow, SegmentKind.ComboWindow, SegmentKind.Invuln, SegmentKind.Armor, SegmentKind.Guard,
        };

        private void ClosePhases(string entity, int tick)
        {
            foreach (var k in PhaseKinds)
            {
                Close(entity, k, tick);
            }
        }

        private void CloseWindows(string entity, int tick)
        {
            foreach (var k in WindowKinds)
            {
                Close(entity, k, tick);
            }
        }

        private void Fixed(string entity, SegmentKind kind, int start, int length, string label = "")
        {
            if (length <= 0)
            {
                return;
            }

            Track(entity, true).Segments.Add(new TimelineSegment(kind, start, start + length, label));
        }

        private void Mark(string entity, int tick, MarkKind kind, string text) =>
            Track(entity, true).Marks.Add(new TimelineMark(tick, kind, text));

        private void Apply(FeelEventRecord e)
        {
            switch (e.Kind)
            {
                case "action_started":
                    ClosePhases(e.Actor, e.Tick);
                    Mark(e.Actor, e.Tick, MarkKind.ActionStarted, Short(e.SkillId) + (e.A > 0 ? " 连击" + e.A : string.Empty));
                    break;
                case "action_phase":
                    ClosePhases(e.Actor, e.Tick);
                    if (Enum.TryParse<SegmentKind>(e.Detail, out var phase) && Array.IndexOf(PhaseKinds, phase) >= 0)
                    {
                        Open(e.Actor, phase, e.Tick);
                    }

                    break;
                case "action_marker":
                    ApplyMarker(e);
                    break;
                case "action_cancelled":
                    ClosePhases(e.Actor, e.Tick);
                    CloseWindows(e.Actor, e.Tick);
                    Mark(e.Actor, e.Tick, MarkKind.ActionCancelled, e.Detail + (e.SkillId.Length > 0 ? "→" + Short(e.SkillId) : string.Empty));
                    break;
                case "action_finished":
                    ClosePhases(e.Actor, e.Tick);
                    CloseWindows(e.Actor, e.Tick);
                    Mark(e.Actor, e.Tick, MarkKind.ActionFinished, string.Empty);
                    break;
                case "target_assisted":
                    Mark(e.Actor, e.Tick, MarkKind.Assist, "转向 " + e.D.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "° → " + e.Target);
                    break;
                case "hit_confirmed":
                {
                    var text = e.Detail + " " + e.Detail2.Replace(";", " ");
                    Mark(e.Actor, e.Tick, MarkKind.Hit, "→" + e.Target + " " + text);
                    Mark(e.Target, e.Tick, MarkKind.Result, text);
                    break;
                }

                case "reaction":
                    Fixed(e.Target, SegmentKind.Reaction, e.Tick, e.A, e.Detail);
                    break;
                case "knocked_down":
                    Fixed(e.Target, SegmentKind.Downed, e.Tick, e.A);
                    break;
                case "getup_started":
                    Fixed(e.Target, SegmentKind.Getup, e.Tick, e.A);
                    Fixed(e.Target, SegmentKind.Invuln, e.Tick, e.B, "getup");
                    break;
                case "hitstop_started":
                    foreach (var label in e.Detail.Split('+'))
                    {
                        if (label.Length > 0)
                        {
                            Fixed(label, SegmentKind.Hitstop, e.Tick, e.A);
                        }
                    }

                    break;
                case "buffer_dropped":
                    Mark(e.Actor, e.Tick, MarkKind.BufferDropped, Short(e.Detail) + " " + e.Detail2);
                    break;
            }
        }

        private void ApplyMarker(FeelEventRecord e)
        {
            var name = e.Detail;
            var colon = name.IndexOf(':');
            var head = colon >= 0 ? name.Substring(0, colon) : name;
            var tail = colon >= 0 ? name.Substring(colon + 1) : string.Empty;
            switch (head)
            {
                case "cancel_open": Open(e.Actor, SegmentKind.CancelWindow, e.Tick, tail); break;
                case "cancel_close": Close(e.Actor, SegmentKind.CancelWindow, e.Tick); break;
                case "combo_open": Open(e.Actor, SegmentKind.ComboWindow, e.Tick); break;
                case "combo_close": Close(e.Actor, SegmentKind.ComboWindow, e.Tick); break;
                case "invuln_start": Open(e.Actor, SegmentKind.Invuln, e.Tick); break;
                case "invuln_end": Close(e.Actor, SegmentKind.Invuln, e.Tick); break;
                case "armor_start": Open(e.Actor, SegmentKind.Armor, e.Tick); break;
                case "armor_end": Close(e.Actor, SegmentKind.Armor, e.Tick); break;
                case "guard_start": Open(e.Actor, SegmentKind.Guard, e.Tick); break;
                case "guard_end": Close(e.Actor, SegmentKind.Guard, e.Tick); break;
                default:
                    Mark(e.Actor, e.Tick, MarkKind.Marker, name);
                    break;
            }
        }

        private void ApplyInput(ScriptEvent e)
        {
            string text;
            switch (e.Kind)
            {
                case ScriptEventKind.Press: text = "↓" + Short(e.Action); break;
                case ScriptEventKind.Release: text = "↑" + Short(e.Action); break;
                case ScriptEventKind.Jump: text = "跳 " + e.Actor; break;
                case ScriptEventKind.MoveTo: text = "点击移动"; break;
                case ScriptEventKind.Cast: text = "施放 " + Short(e.Action); break;
                default: return;
            }

            Mark(e.Kind == ScriptEventKind.Jump && e.Actor.Length > 0 ? e.Actor : PlayerLabel, e.Tick, MarkKind.Input, text);
        }
    }
}
