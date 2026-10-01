using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Lab
{
    /// <summary>脚本事件种类：按下、松开、轴值（轴值每个事件设一次并保持，直到下一个同动作的轴事件）。</summary>
    public enum ScriptEventKind
    {
        Press,
        Release,
        Axis,
    }

    /// <summary>
    /// 输入脚本里的一条事件（06 第 2 节"输入脚本：meta + 按 tick 的输入事件"）。
    /// <para><see cref="Tick"/> 是宿主固定步序号（从 0 开始）：该事件在第 <see cref="Tick"/> 步的"意图采集"之前注入
    /// 桩输入，与 tick 步骤 1（意图采集，03 第 3 节）同源——脚本事件走的是引擎侧宿主引导代码读取输入的同一条路径，
    /// 不绕开输入映射直接塞意图。<see cref="RealTimestamp"/> 只在回放导入（<see cref="ScriptReplayImporter"/>）时携带原始
    /// 真实时间戳（秒），注入时忽略，仅供人读与往返对照。</para>
    /// </summary>
    public sealed class ScriptEvent
    {
        public int Tick { get; }

        public string Action { get; }

        public ScriptEventKind Kind { get; }

        public Vec2 Value { get; }

        public double? RealTimestamp { get; }

        public ScriptEvent(int tick, string action, ScriptEventKind kind, Vec2 value = default, double? realTimestamp = null)
        {
            if (tick < 0)
            {
                throw new LabFormatException($"脚本事件 tick 不能为负：{tick}");
            }

            Tick = tick;
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Kind = kind;
            Value = value;
            RealTimestamp = realTimestamp;
        }
    }

    /// <summary>
    /// 输入脚本的 meta 段（06 第 2 节）：数据集根、预设 id、校准版本、tick 率、帧率上限、脚本版本，加上无头宿主
    /// 跑起来必需的扩展字段（脚本 id、玩家出生点、出场靶子分组、时长）。
    /// </summary>
    public sealed class ScriptMeta
    {
        public string ScriptId { get; set; } = string.Empty;

        /// <summary>脚本格式与内容版本；脚本内容改动时人工递增，基线键里携带它，防止拿旧基线比新脚本。</summary>
        public int ScriptVersion { get; set; } = 1;

        public string Description { get; set; } = string.Empty;

        /// <summary>数据集根（人读，仅作记录；实际数据根由运行入口给定）。</summary>
        public string DatasetRoot { get; set; } = "data/_lab";

        /// <summary>手感预设 id；空表示取格子的缺省预设。手感数据域落地前仅作标签。</summary>
        public string PresetId { get; set; } = string.Empty;

        public string CalibrationVersion { get; set; } = "none";

        /// <summary>摆姿集 id（手感设计 06 第 3.2 节假人姿态集）；本切片尚无摆姿机制，取 "none"。</summary>
        public string PoseSet { get; set; } = "none";

        public int TickRate { get; set; } = 50;

        public int FrameRateCap { get; set; } = 60;

        public int DurationTicks { get; set; } = 100;

        public Vec2 PlayerStart { get; set; } = Vec2.Zero;

        /// <summary>本次出场的靶子分组标签（<c>lab.dummy_set</c> 条目的 <c>group</c>）。</summary>
        public List<string> DummyGroups { get; } = new List<string>();
    }

    /// <summary>
    /// 一份输入脚本：meta + 事件清单。可手写，也可由回放导入（<see cref="ScriptReplayImporter"/>）从带真实时间戳的输入
    /// 记录生成；序列化为规范 JSON（键序固定），因此同一脚本的文本永远相同。
    /// </summary>
    public sealed class InputScript
    {
        /// <summary>文件格式版本；格式不兼容变更时递增，读取方拒绝未知的更高版本。</summary>
        public const int FormatVersion = 1;

        public ScriptMeta Meta { get; }

        public IReadOnlyList<ScriptEvent> Events { get; }

        public InputScript(ScriptMeta meta, IReadOnlyList<ScriptEvent> events)
        {
            Meta = meta ?? throw new ArgumentNullException(nameof(meta));
            Events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public static InputScript Parse(string text, string what = "输入脚本")
        {
            var root = LabJson.ParseObject(text, what);
            var format = root.TryGetValue("formatVersion", out var f) && f is JsonNumber fn ? (int)fn.Value : 1;
            if (format > FormatVersion)
            {
                throw new LabFormatException($"{what} 的 formatVersion={format} 高于本内核支持的 {FormatVersion}");
            }

            var metaObj = LabJson.RequireObject(root, "meta", what);
            var meta = new ScriptMeta
            {
                ScriptId = LabJson.RequireString(metaObj, "scriptId", what + ".meta"),
                ScriptVersion = LabJson.RequireInt(metaObj, "scriptVersion", what + ".meta"),
                Description = LabJson.OptionalString(metaObj, "description", what + ".meta") ?? string.Empty,
                DatasetRoot = LabJson.OptionalString(metaObj, "datasetRoot", what + ".meta") ?? "data/_lab",
                PresetId = LabJson.OptionalString(metaObj, "presetId", what + ".meta") ?? string.Empty,
                CalibrationVersion = LabJson.OptionalString(metaObj, "calibrationVersion", what + ".meta") ?? "none",
                PoseSet = LabJson.OptionalString(metaObj, "poseSet", what + ".meta") ?? "none",
                TickRate = LabJson.RequireInt(metaObj, "tickRate", what + ".meta"),
                FrameRateCap = LabJson.RequireInt(metaObj, "frameRateCap", what + ".meta"),
                DurationTicks = LabJson.RequireInt(metaObj, "durationTicks", what + ".meta"),
            };
            if (metaObj.TryGetValue("playerStart", out var ps) && !(ps is JsonNull))
            {
                meta.PlayerStart = LabJson.ReadVec(ps, what + ".meta.playerStart");
            }

            if (metaObj.TryGetValue("dummyGroups", out var dg) && dg is JsonArray groups)
            {
                foreach (var g in groups)
                {
                    meta.DummyGroups.Add(g is JsonString gs
                        ? gs.Value
                        : throw new LabFormatException($"{what}.meta.dummyGroups 的元素必须是字符串"));
                }
            }

            if (meta.TickRate <= 0 || meta.FrameRateCap <= 0 || meta.DurationTicks <= 0)
            {
                throw new LabFormatException($"{what}.meta 的 tickRate/frameRateCap/durationTicks 必须为正");
            }

            var events = new List<ScriptEvent>();
            foreach (var e in LabJson.RequireArray(root, "events", what))
            {
                if (!(e is JsonObject eo))
                {
                    throw new LabFormatException($"{what}.events 的元素必须是对象");
                }

                var tick = LabJson.RequireInt(eo, "tick", what + ".events[]");
                var action = LabJson.RequireString(eo, "action", what + ".events[]");
                var kindText = LabJson.RequireString(eo, "kind", what + ".events[]");
                ScriptEventKind kind;
                switch (kindText)
                {
                    case "press": kind = ScriptEventKind.Press; break;
                    case "release": kind = ScriptEventKind.Release; break;
                    case "axis": kind = ScriptEventKind.Axis; break;
                    default: throw new LabFormatException($"{what}.events[] 的 kind 未知：{kindText}（press|release|axis）");
                }

                var value = Vec2.Zero;
                if (kind == ScriptEventKind.Axis)
                {
                    value = LabJson.ReadVec(
                        eo.TryGetValue("value", out var v) ? v : JsonNull.Instance, what + ".events[].value");
                }

                double? ts = eo.TryGetValue("realTs", out var t) && t is JsonNumber tn ? tn.Value : (double?)null;
                events.Add(new ScriptEvent(tick, action, kind, value, ts));
            }

            return new InputScript(meta, events);
        }

        public string ToJson()
        {
            var meta = new JsonObjectBuilder()
                .Add("scriptId", LabJson.Str(Meta.ScriptId))
                .Add("scriptVersion", LabJson.Num(Meta.ScriptVersion))
                .Add("description", LabJson.Str(Meta.Description))
                .Add("datasetRoot", LabJson.Str(Meta.DatasetRoot))
                .Add("presetId", LabJson.Str(Meta.PresetId))
                .Add("calibrationVersion", LabJson.Str(Meta.CalibrationVersion))
                .Add("poseSet", LabJson.Str(Meta.PoseSet))
                .Add("tickRate", LabJson.Num(Meta.TickRate))
                .Add("frameRateCap", LabJson.Num(Meta.FrameRateCap))
                .Add("durationTicks", LabJson.Num(Meta.DurationTicks))
                .Add("playerStart", LabJson.Vec(Meta.PlayerStart))
                .Add("dummyGroups", new JsonArray(Meta.DummyGroups.ConvertAll(g => (JsonValue)LabJson.Str(g))))
                .Build();

            var events = new List<JsonValue>();
            foreach (var e in Events)
            {
                var b = new JsonObjectBuilder()
                    .Add("tick", LabJson.Num(e.Tick))
                    .Add("action", LabJson.Str(e.Action))
                    .Add("kind", LabJson.Str(e.Kind == ScriptEventKind.Press ? "press" : e.Kind == ScriptEventKind.Release ? "release" : "axis"));
                if (e.Kind == ScriptEventKind.Axis)
                {
                    b.Add("value", LabJson.Vec(e.Value));
                }

                if (e.RealTimestamp.HasValue)
                {
                    b.Add("realTs", LabJson.Num(e.RealTimestamp.Value));
                }

                events.Add(b.Build());
            }

            var root = new JsonObjectBuilder()
                .Add("formatVersion", LabJson.Num(FormatVersion))
                .Add("meta", meta)
                .Add("events", new JsonArray(events))
                .Build();
            return LabJson.Write(root);
        }
    }

    /// <summary>
    /// 回放导入：把"带真实时间戳的输入记录"换算成按 tick 的输入脚本（06 第 2 节"可手写，也可由回放导入"）。
    /// 换算规则：<c>tick = floor(timestamp * tickRate)</c>，同一 tick 内保持记录顺序；时间戳原值写入
    /// <see cref="ScriptEvent.RealTimestamp"/> 供对照，注入时不使用。
    /// </summary>
    public static class ScriptReplayImporter
    {
        public readonly struct TimedInput
        {
            public double Timestamp { get; }

            public string Action { get; }

            public ScriptEventKind Kind { get; }

            public Vec2 Value { get; }

            public TimedInput(double timestamp, string action, ScriptEventKind kind, Vec2 value = default)
            {
                Timestamp = timestamp;
                Action = action;
                Kind = kind;
                Value = value;
            }
        }

        public static InputScript FromTimedInputs(ScriptMeta meta, IReadOnlyList<TimedInput> inputs)
        {
            if (meta == null) throw new ArgumentNullException(nameof(meta));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            var events = new List<ScriptEvent>(inputs.Count);
            var lastTick = 0;
            foreach (var input in inputs)
            {
                if (input.Timestamp < 0)
                {
                    throw new LabFormatException($"回放输入时间戳不能为负：{input.Timestamp}");
                }

                var tick = (int)Math.Floor(input.Timestamp * meta.TickRate);
                if (tick < lastTick)
                {
                    throw new LabFormatException("回放输入必须按时间戳升序");
                }

                lastTick = tick;
                events.Add(new ScriptEvent(tick, input.Action, input.Kind, input.Value, input.Timestamp));
            }

            return new InputScript(meta, events);
        }
    }
}
