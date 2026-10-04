using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common.Json;

namespace Lab
{
    /// <summary>
    /// 输入噪声的一次记录：模型标签与脚本里每条事件被平移的 tick 数（按事件下标）。回放同一份记录得到逐位相同的带噪脚本——
    /// "可注入、可录放"的输入抖动（06 第 4 节第 2 点）。
    /// </summary>
    public sealed class InputNoiseRecord
    {
        public const int FormatVersion = 1;

        public string ModelId { get; }

        /// <summary>每条事件的 tick 偏移（含系统延迟）；与脚本事件一一对应。不被平移的事件（宿主事件）记 0。</summary>
        public IReadOnlyList<int> Offsets { get; }

        public InputNoiseRecord(string modelId, IReadOnlyList<int> offsets)
        {
            ModelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
            Offsets = offsets ?? throw new ArgumentNullException(nameof(offsets));
        }

        public string ToJson()
        {
            var items = new List<JsonValue>();
            foreach (var offset in Offsets)
            {
                items.Add(LabJson.Num(offset));
            }

            return LabJson.Write(new JsonObjectBuilder()
                .Add("formatVersion", LabJson.Num(FormatVersion))
                .Add("model", LabJson.Str(ModelId))
                .Add("offsets", new JsonArray(items))
                .Build());
        }

        public static InputNoiseRecord Parse(string text)
        {
            var root = LabJson.ParseObject(text, "输入噪声记录");
            var format = LabJson.RequireInt(root, "formatVersion", "输入噪声记录");
            if (format > FormatVersion)
            {
                throw new LabFormatException($"输入噪声记录的 formatVersion={format} 高于本内核支持的 {FormatVersion}");
            }

            var offsets = new List<int>();
            foreach (var item in LabJson.RequireArray(root, "offsets", "输入噪声记录"))
            {
                if (!(item is JsonNumber number))
                {
                    throw new LabFormatException("输入噪声记录的 offsets 里有非数字元素");
                }

                offsets.Add((int)number.Value);
            }

            return new InputNoiseRecord(LabJson.RequireString(root, "model", "输入噪声记录"), offsets);
        }
    }

    /// <summary>
    /// 输入噪声模型：对脚本里的设备输入事件（按下/抬起/摇杆轴）加系统延迟与每条事件的随机抖动（tick 为单位），
    /// 用来在没有真机的情况下注入"真实输入设备的抖动与延迟"。
    /// <para>
    /// 判断记录（合成噪声，不是真机测量）：模型只能注入人为设定的抖动分布，不能替代真机实测；真机的延迟与抖动分布仍未测量
    /// （见 <c>lab/README.md</c> 判断记录 54）。随机源是内核自带的确定性 xorshift（不用 <see cref="Random"/>，保证跨运行时与跨平台
    /// 同种子同结果），所以一份种子就是一份可复现的"录音"；<see cref="Apply"/> 同时给出 <see cref="InputNoiseRecord"/>，<see cref="Replay"/> 用它还原。
    /// </para>
    /// <para>
    /// 判断记录（只平移设备输入）：<c>press</c>/<c>release</c>/<c>axis</c> 是设备事件，会被平移；<c>cast</c>（靶子出手）、<c>equip</c>/<c>unequip</c>
    /// 是宿主事件，不平移。同一动作上的事件保持先后顺序（平移后的 tick 不小于同一动作上一条事件的平移后 tick），不会把"按下-抬起"颠倒。
    /// 平移后 tick 下限为 0。
    /// </para>
    /// </summary>
    public sealed class InputNoiseModel
    {
        public static InputNoiseModel None { get; } = new InputNoiseModel(0UL, 0, 0);

        public ulong Seed { get; }

        /// <summary>系统延迟（tick，≥ 0）：所有设备事件统一后移。</summary>
        public int LatencyTicks { get; }

        /// <summary>抖动幅度（tick，≥ 0）：每条设备事件再加一个 [−J, +J] 的均匀整数偏移。</summary>
        public int JitterTicks { get; }

        public bool IsNone => LatencyTicks == 0 && JitterTicks == 0;

        /// <summary>模型标签（人读，写进引擎记录与记录文件）。</summary>
        public string Id => IsNone
            ? "none"
            : "noise(seed=" + Seed.ToString(CultureInfo.InvariantCulture) + ",latency=" + LatencyTicks.ToString(CultureInfo.InvariantCulture)
              + ",jitter=" + JitterTicks.ToString(CultureInfo.InvariantCulture) + ")";

        public InputNoiseModel(ulong seed, int latencyTicks, int jitterTicks)
        {
            if (latencyTicks < 0) throw new ArgumentOutOfRangeException(nameof(latencyTicks), "系统延迟不能为负");
            if (jitterTicks < 0) throw new ArgumentOutOfRangeException(nameof(jitterTicks), "抖动幅度不能为负");
            Seed = seed;
            LatencyTicks = latencyTicks;
            JitterTicks = jitterTicks;
        }

        /// <summary>对脚本施加噪声：返回带噪脚本（元数据与期望清单原样）并通过 <paramref name="record"/> 给出记录。</summary>
        public InputScript Apply(InputScript script, out InputNoiseRecord record)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            var rng = new Xorshift(Seed);
            var offsets = new List<int>(script.Events.Count);
            var lastShifted = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in script.Events)
            {
                if (!IsDeviceEvent(e))
                {
                    offsets.Add(0);
                    continue;
                }

                var jitter = JitterTicks == 0 ? 0 : (int)(rng.Next() % (ulong)(2 * JitterTicks + 1)) - JitterTicks;
                var target = Math.Max(0, e.Tick + LatencyTicks + jitter);
                if (lastShifted.TryGetValue(e.Action, out var last) && target < last)
                {
                    target = last;
                }

                lastShifted[e.Action] = target;
                offsets.Add(target - e.Tick);
            }

            record = new InputNoiseRecord(Id, offsets);
            return Shift(script, offsets);
        }

        /// <summary>按记录还原带噪脚本（与 <see cref="Apply"/> 当时产出的逐事件相同）。</summary>
        public static InputScript Replay(InputScript script, InputNoiseRecord record)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.Offsets.Count != script.Events.Count)
            {
                throw new LabFormatException(
                    $"输入噪声记录有 {record.Offsets.Count} 条偏移，脚本 {script.Meta.ScriptId} 有 {script.Events.Count} 条事件，无法回放");
            }

            var offsets = new List<int>(record.Offsets);
            for (var i = 0; i < offsets.Count; i++)
            {
                if (!IsDeviceEvent(script.Events[i]) && offsets[i] != 0)
                {
                    throw new LabFormatException($"输入噪声记录第 {i} 条偏移作用在宿主事件上（只有设备输入事件可以被平移）");
                }
            }

            return Shift(script, offsets);
        }

        private static bool IsDeviceEvent(ScriptEvent e) =>
            e.Kind == ScriptEventKind.Press || e.Kind == ScriptEventKind.Release || e.Kind == ScriptEventKind.Axis;

        private static InputScript Shift(InputScript script, IReadOnlyList<int> offsets)
        {
            var shifted = new List<KeyValuePair<int, ScriptEvent>>(script.Events.Count);
            for (var i = 0; i < script.Events.Count; i++)
            {
                var e = script.Events[i];
                var tick = Math.Max(0, e.Tick + offsets[i]);
                shifted.Add(new KeyValuePair<int, ScriptEvent>(
                    i, tick == e.Tick ? e : new ScriptEvent(tick, e.Action, e.Kind, e.Value, e.RealTimestamp, e.Actor, e.Text)));
            }

            // 稳定排序：同 tick 保持原先后（先按 tick、再按原下标）。
            shifted.Sort((a, b) =>
            {
                var c = a.Value.Tick.CompareTo(b.Value.Tick);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            var events = new List<ScriptEvent>(shifted.Count);
            foreach (var pair in shifted)
            {
                events.Add(pair.Value);
            }

            return new InputScript(script.Meta, events, script.Expectations);
        }

        /// <summary>xorshift64*：确定性、跨平台，足够做测试噪声。种子 0 映射到一个固定非零常数。</summary>
        private sealed class Xorshift
        {
            private ulong _state;

            public Xorshift(ulong seed)
            {
                _state = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;
            }

            public ulong Next()
            {
                var x = _state;
                x ^= x >> 12;
                x ^= x << 25;
                x ^= x >> 27;
                _state = x;
                return x * 0x2545F4914F6CDD1DUL;
            }
        }
    }
}
