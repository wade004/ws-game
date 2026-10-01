using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.Foundation.Common.Json;

namespace Lab
{
    /// <summary>
    /// 指纹的键（06 第 3.2 节）：脚本与版本、数据集内容哈希、预设、校准版本、摆姿集、适配层组合、格子、tick 率与帧率上限。
    /// 键里只有数据集哈希不一致时是警告（数据集内容改动本身不是回归，真正的判定看度量差异）；其余键不一致说明拿错了基线，
    /// 作为差异报告。
    /// </summary>
    public sealed class FingerprintKey
    {
        public string Script { get; set; } = string.Empty;

        public int ScriptVersion { get; set; }

        public string Dataset { get; set; } = string.Empty;

        public string Preset { get; set; } = string.Empty;

        public string CalibrationVersion { get; set; } = string.Empty;

        public string PoseSet { get; set; } = string.Empty;

        public string Adapters { get; set; } = string.Empty;

        public string Cell { get; set; } = string.Empty;

        public int TickRate { get; set; }

        public int FrameRateCap { get; set; }

        public JsonObject ToJson() =>
            new JsonObjectBuilder()
                .Add("script", LabJson.Str(Script))
                .Add("scriptVersion", LabJson.Num(ScriptVersion))
                .Add("dataset", LabJson.Str(Dataset))
                .Add("preset", LabJson.Str(Preset))
                .Add("calibrationVersion", LabJson.Str(CalibrationVersion))
                .Add("poseSet", LabJson.Str(PoseSet))
                .Add("adapters", LabJson.Str(Adapters))
                .Add("cell", LabJson.Str(Cell))
                .Add("tickRate", LabJson.Num(TickRate))
                .Add("frameRateCap", LabJson.Num(FrameRateCap))
                .Build();
    }

    /// <summary>一份指纹：键 + 各度量组的度量值。</summary>
    public sealed class Fingerprint
    {
        public const int FormatVersion = 1;

        /// <summary>本内核适配层组合标签（无头宿主 + 桩适配层）。</summary>
        public const string HeadlessAdapters = "headless+stub";

        public JsonObject Key { get; }

        public JsonObject Groups { get; }

        public Fingerprint(JsonObject key, JsonObject groups)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Groups = groups ?? throw new ArgumentNullException(nameof(groups));
        }

        public static Fingerprint Build(LabRecording recording, MetricRegistry registry, string datasetHash)
        {
            var meta = recording.Script.Meta;
            var key = new FingerprintKey
            {
                Script = meta.ScriptId,
                ScriptVersion = meta.ScriptVersion,
                Dataset = datasetHash,
                Preset = string.IsNullOrEmpty(meta.PresetId) ? recording.Cell.DefaultPreset : meta.PresetId,
                CalibrationVersion = meta.CalibrationVersion,
                PoseSet = meta.PoseSet,
                Adapters = HeadlessAdapters,
                Cell = recording.Cell.Cell,
                TickRate = meta.TickRate,
                FrameRateCap = meta.FrameRateCap,
            };
            return new Fingerprint(key.ToJson(), registry.Compute(recording));
        }

        public string ToJson()
        {
            var root = new JsonObjectBuilder()
                .Add("formatVersion", LabJson.Num(FormatVersion))
                .Add("key", Key)
                .Add("groups", Groups)
                .Build();
            return LabJson.Write(root);
        }

        public static Fingerprint Parse(string text, string what = "指纹")
        {
            var root = LabJson.ParseObject(text, what);
            return FromJson(root, what);
        }

        public static Fingerprint FromJson(JsonObject root, string what = "指纹")
        {
            var format = LabJson.RequireInt(root, "formatVersion", what);
            if (format > FormatVersion)
            {
                throw new LabFormatException($"{what} 的 formatVersion={format} 高于本内核支持的 {FormatVersion}");
            }

            return new Fingerprint(LabJson.RequireObject(root, "key", what), LabJson.RequireObject(root, "groups", what));
        }

        /// <summary>
        /// 只含指定类别度量的规范文本（组名、度量名、值，键序按注册表声明顺序）。
        /// 逻辑组字节比较、双跑一致、跨格子不变量、三个帧率上限的逻辑组一致，都用它——两份文本相等就是"逐字节一致"。
        /// </summary>
        public string Project(MetricRegistry registry, params MetricClass[] classes)
        {
            var wanted = new HashSet<MetricClass>(classes);
            var builder = new JsonObjectBuilder();
            foreach (var group in registry.Groups)
            {
                if (!Groups.TryGetValue(group.Name, out var gv) || !(gv is JsonObject go))
                {
                    continue;
                }

                var inner = new JsonObjectBuilder();
                foreach (var spec in group.Specs)
                {
                    if (wanted.Contains(spec.Class) && go.TryGetValue(spec.Name, out var v))
                    {
                        inner.Add(spec.Name, v);
                    }
                }

                builder.Add(group.Name, inner.Build());
            }

            return LabJson.Write(builder.Build());
        }
    }

    /// <summary>一条差异。</summary>
    public sealed class DiffEntry
    {
        /// <summary><c>key</c>、<c>metric</c>、<c>missing_metric</c>、<c>unknown_metric</c>、<c>removed_group</c>。</summary>
        public string Kind { get; }

        public string Name { get; }

        public string Rule { get; }

        public string Baseline { get; }

        public string Actual { get; }

        public DiffEntry(string kind, string name, string rule, string baseline, string actual)
        {
            Kind = kind;
            Name = name;
            Rule = rule;
            Baseline = baseline;
            Actual = actual;
        }

        public override string ToString() => $"[{Name}] {Rule}：基线 {Baseline} -> 实际 {Actual}";
    }

    /// <summary>比较结果：差异清单（失败）与警告清单（不影响通过与否）。</summary>
    public sealed class FingerprintDiff
    {
        public List<DiffEntry> Entries { get; } = new List<DiffEntry>();

        public List<string> Warnings { get; } = new List<string>();

        public bool Ok => Entries.Count == 0;

        /// <summary>人读的差异报告：每条一行，先差异后警告。</summary>
        public string Format()
        {
            var sb = new StringBuilder();
            foreach (var e in Entries)
            {
                sb.Append("  差异 ").Append(e).Append('\n');
            }

            foreach (var w in Warnings)
            {
                sb.Append("  警告 ").Append(w).Append('\n');
            }

            return sb.ToString();
        }
    }

    /// <summary>指纹比较器：按注册表里每个度量声明的类别与允差逐项判定。</summary>
    public static class FingerprintComparer
    {
        public static FingerprintDiff Compare(Fingerprint baseline, Fingerprint actual, MetricRegistry registry)
        {
            var diff = new FingerprintDiff();

            foreach (var name in new[] { "script", "scriptVersion", "cell", "tickRate", "frameRateCap", "preset", "calibrationVersion", "poseSet", "adapters" })
            {
                var b = Text(baseline.Key, name);
                var a = Text(actual.Key, name);
                if (!string.Equals(b, a, StringComparison.Ordinal))
                {
                    diff.Entries.Add(new DiffEntry("key", "key." + name, "键必须一致", b, a));
                }
            }

            var bd = Text(baseline.Key, "dataset");
            var ad = Text(actual.Key, "dataset");
            if (!string.Equals(bd, ad, StringComparison.Ordinal))
            {
                diff.Warnings.Add($"key.dataset：数据集内容哈希变化（基线 {bd} -> 实际 {ad}），以度量差异为准");
            }

            foreach (var pair in baseline.Groups)
            {
                if (registry.Find(pair.Key) == null)
                {
                    diff.Entries.Add(new DiffEntry("removed_group", pair.Key, "基线里的度量组已不在注册表中", "存在", "缺失"));
                }
            }

            foreach (var group in registry.Groups)
            {
                if (!baseline.Groups.TryGetValue(group.Name, out var bgv) || !(bgv is JsonObject bg))
                {
                    diff.Warnings.Add($"度量组 {group.Name} 不在基线里（新增组：用 --update-baseline 扩充基线后才参与比较）");
                    continue;
                }

                var ag = actual.Groups.TryGetValue(group.Name, out var agv) && agv is JsonObject ago
                    ? ago
                    : new JsonObjectBuilder().Build();

                foreach (var spec in group.Specs)
                {
                    var full = group.Name + "." + spec.Name;
                    var inBase = bg.TryGetValue(spec.Name, out var bv);
                    var inAct = ag.TryGetValue(spec.Name, out var av);
                    if (!inBase)
                    {
                        diff.Warnings.Add($"{full} 不在基线里（新增度量：用 --update-baseline 扩充基线）");
                        continue;
                    }

                    if (!inAct)
                    {
                        diff.Entries.Add(new DiffEntry("missing_metric", full, Rule(spec), Render(bv), "缺失"));
                        continue;
                    }

                    if (!Within(spec, bv, av))
                    {
                        diff.Entries.Add(new DiffEntry("metric", full, Rule(spec), Render(bv), Render(av) + Delta(bv, av)));
                    }
                }

                foreach (var pair in bg)
                {
                    var known = false;
                    foreach (var spec in group.Specs)
                    {
                        if (string.Equals(spec.Name, pair.Key, StringComparison.Ordinal))
                        {
                            known = true;
                            break;
                        }
                    }

                    if (!known)
                    {
                        diff.Entries.Add(new DiffEntry("unknown_metric", group.Name + "." + pair.Key, "基线里的度量已不在组声明中", Render(pair.Value), "缺失"));
                    }
                }
            }

            return diff;
        }

        private static string Rule(MetricSpec spec) => $"{spec.Class.ToString().ToLowerInvariant()} {spec.DescribeTolerance()}";

        private static string Text(JsonObject obj, string name) => obj.TryGetValue(name, out var v) ? Render(v) : "<缺失>";

        private static bool Within(MetricSpec spec, JsonValue baseline, JsonValue actual)
        {
            if (spec.Tolerance == ToleranceKind.Exact)
            {
                return string.Equals(Render(baseline), Render(actual), StringComparison.Ordinal);
            }

            if (!(baseline is JsonNumber bn) || !(actual is JsonNumber an))
            {
                return string.Equals(Render(baseline), Render(actual), StringComparison.Ordinal);
            }

            if (spec.Tolerance == ToleranceKind.Absolute)
            {
                return Math.Abs(an.Value - bn.Value) <= spec.Amount + 1e-12;
            }

            return an.Value <= Math.Max(bn.Value * spec.Amount, spec.Floor);
        }

        private static string Delta(JsonValue baseline, JsonValue actual)
        {
            if (baseline is JsonNumber bn && actual is JsonNumber an)
            {
                var d = an.Value - bn.Value;
                return " (差 " + (d >= 0 ? "+" : string.Empty) + MetricSink.Round(d).ToString("R", CultureInfo.InvariantCulture) + ")";
            }

            return string.Empty;
        }

        /// <summary>值的紧凑文本（数字不变文化往返格式，数组方括号逗号分隔，字符串带引号）。</summary>
        public static string Render(JsonValue value)
        {
            switch (value)
            {
                case JsonNumber n:
                    return n.TryGetInt64(out var l)
                        ? l.ToString(CultureInfo.InvariantCulture)
                        : n.Value.ToString("R", CultureInfo.InvariantCulture);
                case JsonString s:
                    return "\"" + s.Value + "\"";
                case JsonBool b:
                    return b.Value ? "true" : "false";
                case JsonArray arr:
                    var parts = new List<string>();
                    foreach (var item in arr)
                    {
                        parts.Add(Render(item));
                    }

                    return "[" + string.Join(",", parts) + "]";
                default:
                    return "null";
            }
        }
    }
}
