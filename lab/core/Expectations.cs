using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.Foundation.Common.Json;

namespace Lab
{
    /// <summary>期望的比较运算（脚本 <c>expectations[].op</c>）。</summary>
    public enum ExpectOp
    {
        /// <summary>相等：数值差不超过 1e-9（度量值本身已规整到 9 位小数），字符串与数组按规范文本逐字相等。</summary>
        Eq,

        Ne,

        Lt,

        Le,

        Gt,

        Ge,

        /// <summary>闭区间 <c>[min, max]</c>，两端可只给一端（上界或下界）。</summary>
        Between,

        /// <summary>近似相等：<c>|实际 - 期望| ≤ tolerance</c>（缺省 1e-6）。</summary>
        Approx,

        /// <summary>实际值（规范文本）等于 <c>value</c> 数组里的某一项。</summary>
        In,

        /// <summary>字符串度量包含给定子串；数组度量包含给定元素。</summary>
        Contains,
    }

    /// <summary>
    /// 度量选择器：<c>组.度量</c>，可选再取数组的某个元素（<see cref="At"/>，负数从末尾数）或做聚合（<see cref="Agg"/>：
    /// <c>len</c>、<c>sum</c>、<c>min</c>、<c>max</c>、<c>first</c>、<c>last</c>）。
    /// </summary>
    public sealed class MetricSelector
    {
        public static readonly string[] Aggregates = { "len", "sum", "min", "max", "first", "last" };

        public string Metric { get; }

        public int? At { get; }

        public string Agg { get; }

        public MetricSelector(string metric, int? at = null, string agg = "")
        {
            Metric = metric ?? throw new ArgumentNullException(nameof(metric));
            At = at;
            Agg = agg ?? string.Empty;
        }

        public string Group
        {
            get
            {
                var dot = Metric.IndexOf('.');
                return dot < 0 ? Metric : Metric.Substring(0, dot);
            }
        }

        public string Name
        {
            get
            {
                var dot = Metric.IndexOf('.');
                return dot < 0 ? string.Empty : Metric.Substring(dot + 1);
            }
        }

        public override string ToString() =>
            Metric + (At.HasValue ? "[" + At.Value.ToString(CultureInfo.InvariantCulture) + "]" : string.Empty)
            + (Agg.Length > 0 ? "." + Agg + "()" : string.Empty);
    }

    /// <summary>相对关系的另一端：另一个度量（可来自另一个格子），按 <c>other × scale + offset</c> 参与比较。</summary>
    public sealed class ExpectVersus
    {
        /// <summary>另一端所在格子；空表示与被检格子相同。</summary>
        public string Cell { get; }

        public MetricSelector Selector { get; }

        public double Scale { get; }

        public double Offset { get; }

        public ExpectVersus(string cell, MetricSelector selector, double scale = 1.0, double offset = 0.0)
        {
            Cell = cell ?? string.Empty;
            Selector = selector ?? throw new ArgumentNullException(nameof(selector));
            Scale = scale;
            Offset = offset;
        }
    }

    /// <summary>
    /// 脚本里的一条期望（手感设计 06 第 3.1 节 <c>expectations</c>）：对一个度量在若干格子上的断言。
    /// 绝对形态：<c>{metric, op, value | min/max | tolerance}</c>；相对形态：<c>{metric, op, versus: {cell?, metric, scale?, offset?}}</c>。
    /// <see cref="Id"/> 缺省时按序号自动取 <c>#序号</c>；<see cref="Cells"/> 为空表示适用脚本的全部格子。
    /// </summary>
    public sealed class Expectation
    {
        /// <summary>导出为测试自动生成的期望的 id 前缀；再次导出时只重生成这一类，手写的保留。</summary>
        public const string AutoPrefix = "auto.";

        public string Id { get; }

        public IReadOnlyList<string> Cells { get; }

        public MetricSelector Subject { get; }

        public ExpectOp Op { get; }

        /// <summary>绝对形态的期望值（<see cref="ExpectOp.In"/> 为候选数组）；用到 <see cref="Versus"/> 或区间时为空。</summary>
        public JsonValue? Value { get; }

        public double? Min { get; }

        public double? Max { get; }

        public double? Tolerance { get; }

        public ExpectVersus? Versus { get; }

        public string Note { get; }

        public Expectation(
            string id, IReadOnlyList<string>? cells, MetricSelector subject, ExpectOp op, JsonValue? value, double? min, double? max,
            double? tolerance, ExpectVersus? versus, string note)
        {
            Id = id ?? string.Empty;
            Cells = cells ?? Array.Empty<string>();
            Subject = subject ?? throw new ArgumentNullException(nameof(subject));
            Op = op;
            Value = value;
            Min = min;
            Max = max;
            Tolerance = tolerance;
            Versus = versus;
            Note = note ?? string.Empty;
            ValidateShape();
        }

        /// <summary>该期望是否适用于给定格子（<see cref="Cells"/> 为空即全部适用）。</summary>
        public bool AppliesTo(string cell)
        {
            if (Cells.Count == 0)
            {
                return true;
            }

            foreach (var c in Cells)
            {
                if (string.Equals(c, cell, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void ValidateShape()
        {
            var label = Id.Length > 0 ? Id : Subject.Metric;
            if (Subject.Metric.IndexOf('.') <= 0 || Subject.Name.Length == 0)
            {
                throw new LabFormatException($"期望 {label} 的 metric 必须是 组.度量 形式：{Subject.Metric}");
            }

            ValidateSelector(Subject, label);
            switch (Op)
            {
                case ExpectOp.Between:
                    if (Versus != null || Value != null)
                    {
                        throw new LabFormatException($"期望 {label}：between 只用 min/max，不带 value 或 versus");
                    }

                    if (!Min.HasValue && !Max.HasValue)
                    {
                        throw new LabFormatException($"期望 {label}：between 至少要给 min 或 max 之一");
                    }

                    if (Min.HasValue && Max.HasValue && Min.Value > Max.Value)
                    {
                        throw new LabFormatException($"期望 {label}：min {Min.Value} 大于 max {Max.Value}");
                    }

                    break;
                case ExpectOp.In:
                case ExpectOp.Contains:
                    if (Versus != null || Value == null)
                    {
                        throw new LabFormatException($"期望 {label}：{OpText(Op)} 需要 value（不支持 versus）");
                    }

                    if (Op == ExpectOp.In && !(Value is JsonArray))
                    {
                        throw new LabFormatException($"期望 {label}：in 的 value 必须是数组");
                    }

                    break;
                default:
                    if ((Value == null) == (Versus == null))
                    {
                        throw new LabFormatException($"期望 {label}：{OpText(Op)} 必须且只能给 value 或 versus 之一");
                    }

                    if ((Op == ExpectOp.Lt || Op == ExpectOp.Le || Op == ExpectOp.Gt || Op == ExpectOp.Ge || Op == ExpectOp.Approx)
                        && Value != null && !(Value is JsonNumber))
                    {
                        throw new LabFormatException($"期望 {label}：{OpText(Op)} 的 value 必须是数值");
                    }

                    break;
            }

            if (Op != ExpectOp.Approx && Tolerance.HasValue)
            {
                throw new LabFormatException($"期望 {label}：tolerance 只用于 approx");
            }

            if (Tolerance.HasValue && Tolerance.Value < 0)
            {
                throw new LabFormatException($"期望 {label}：tolerance 不能为负");
            }

            if (Versus != null)
            {
                ValidateSelector(Versus.Selector, label);
                if (Versus.Selector.Metric.IndexOf('.') <= 0 || Versus.Selector.Name.Length == 0)
                {
                    throw new LabFormatException($"期望 {label} 的 versus.metric 必须是 组.度量 形式：{Versus.Selector.Metric}");
                }
            }
        }

        private static void ValidateSelector(MetricSelector selector, string label)
        {
            if (selector.At.HasValue && selector.Agg.Length > 0)
            {
                throw new LabFormatException($"期望 {label}：at 与 agg 不能同时给（{selector.Metric}）");
            }

            if (selector.Agg.Length > 0 && Array.IndexOf(MetricSelector.Aggregates, selector.Agg) < 0)
            {
                throw new LabFormatException(
                    $"期望 {label}：agg 未知：{selector.Agg}（{string.Join("|", MetricSelector.Aggregates)}）");
            }
        }

        public static string OpText(ExpectOp op)
        {
            switch (op)
            {
                case ExpectOp.Eq: return "eq";
                case ExpectOp.Ne: return "ne";
                case ExpectOp.Lt: return "lt";
                case ExpectOp.Le: return "le";
                case ExpectOp.Gt: return "gt";
                case ExpectOp.Ge: return "ge";
                case ExpectOp.Between: return "between";
                case ExpectOp.Approx: return "approx";
                case ExpectOp.In: return "in";
                default: return "contains";
            }
        }

        public static bool TryParseOp(string text, out ExpectOp op)
        {
            switch (text)
            {
                case "eq": op = ExpectOp.Eq; return true;
                case "ne": op = ExpectOp.Ne; return true;
                case "lt": op = ExpectOp.Lt; return true;
                case "le": op = ExpectOp.Le; return true;
                case "gt": op = ExpectOp.Gt; return true;
                case "ge": op = ExpectOp.Ge; return true;
                case "between": op = ExpectOp.Between; return true;
                case "approx": op = ExpectOp.Approx; return true;
                case "in": op = ExpectOp.In; return true;
                case "contains": op = ExpectOp.Contains; return true;
                default: op = ExpectOp.Eq; return false;
            }
        }

        /// <summary>
        /// 解析脚本里的 <c>expectations</c> 数组；缺省 id 补成 <c>#序号</c>（从 1 起），id 重复、字段非法一律抛带路径的
        /// <see cref="LabFormatException"/>（不静默忽略未知字段，防止拼错的键让期望悄悄失效）。
        /// </summary>
        public static List<Expectation> ParseList(JsonArray array, string what)
        {
            var result = new List<Expectation>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < array.Count; i++)
            {
                var path = $"{what}[{i}]";
                if (!(array[i] is JsonObject o))
                {
                    throw new LabFormatException($"{path} 必须是对象");
                }

                var e = Parse(o, path, i + 1);
                if (!seen.Add(e.Id))
                {
                    throw new LabFormatException($"{path} 的 id 重复：{e.Id}");
                }

                result.Add(e);
            }

            return result;
        }

        private static readonly string[] KnownKeys =
            { "id", "cell", "cells", "metric", "at", "agg", "op", "value", "min", "max", "tolerance", "versus", "note" };

        private static readonly string[] KnownVersusKeys = { "cell", "metric", "at", "agg", "scale", "offset" };

        private static void RejectUnknownKeys(JsonObject o, string[] known, string path)
        {
            for (var i = 0; i < o.Count; i++)
            {
                if (Array.IndexOf(known, o[i].Key) < 0)
                {
                    throw new LabFormatException($"{path} 含未知字段 \"{o[i].Key}\"（可用：{string.Join("、", known)}）");
                }
            }
        }

        private static Expectation Parse(JsonObject o, string path, int ordinal)
        {
            RejectUnknownKeys(o, KnownKeys, path);
            var id = LabJson.OptionalString(o, "id", path) ?? ("#" + ordinal.ToString(CultureInfo.InvariantCulture));
            var cells = new List<string>();
            var single = LabJson.OptionalString(o, "cell", path);
            if (!string.IsNullOrEmpty(single) && !string.Equals(single, "*", StringComparison.Ordinal))
            {
                cells.Add(single!);
            }

            if (o.TryGetValue("cells", out var cv) && !(cv is JsonNull))
            {
                if (!(cv is JsonArray ca))
                {
                    throw new LabFormatException($"{path}.cells 必须是数组");
                }

                foreach (var item in ca)
                {
                    cells.Add(item is JsonString cs ? cs.Value : throw new LabFormatException($"{path}.cells 的元素必须是字符串"));
                }
            }

            var metric = LabJson.RequireString(o, "metric", path);
            var opText = LabJson.RequireString(o, "op", path);
            if (!TryParseOp(opText, out var op))
            {
                throw new LabFormatException($"{path} 的 op 未知：{opText}（eq|ne|lt|le|gt|ge|between|approx|in|contains）");
            }

            var subject = new MetricSelector(metric, OptionalInt(o, "at", path), LabJson.OptionalString(o, "agg", path) ?? string.Empty);
            JsonValue? value = o.TryGetValue("value", out var vv) && !(vv is JsonNull) ? vv : null;
            ExpectVersus? versus = null;
            if (o.TryGetValue("versus", out var vsv) && !(vsv is JsonNull))
            {
                if (!(vsv is JsonObject vo))
                {
                    throw new LabFormatException($"{path}.versus 必须是对象");
                }

                RejectUnknownKeys(vo, KnownVersusKeys, path + ".versus");
                versus = new ExpectVersus(
                    LabJson.OptionalString(vo, "cell", path + ".versus") ?? string.Empty,
                    new MetricSelector(
                        LabJson.RequireString(vo, "metric", path + ".versus"),
                        OptionalInt(vo, "at", path + ".versus"),
                        LabJson.OptionalString(vo, "agg", path + ".versus") ?? string.Empty),
                    OptionalNumber(vo, "scale", path + ".versus") ?? 1.0,
                    OptionalNumber(vo, "offset", path + ".versus") ?? 0.0);
            }

            try
            {
                return new Expectation(
                    id, cells, subject, op, value, OptionalNumber(o, "min", path), OptionalNumber(o, "max", path),
                    OptionalNumber(o, "tolerance", path), versus, LabJson.OptionalString(o, "note", path) ?? string.Empty);
            }
            catch (LabFormatException ex)
            {
                throw new LabFormatException($"{path}：{ex.Message}");
            }
        }

        private static int? OptionalInt(JsonObject o, string key, string path)
        {
            if (!o.TryGetValue(key, out var v) || v is JsonNull)
            {
                return null;
            }

            if (v is JsonNumber n && n.TryGetInt64(out var l) && l >= int.MinValue && l <= int.MaxValue)
            {
                return (int)l;
            }

            throw new LabFormatException($"{path} 的字段 \"{key}\" 必须是整数");
        }

        private static double? OptionalNumber(JsonObject o, string key, string path)
        {
            if (!o.TryGetValue(key, out var v) || v is JsonNull)
            {
                return null;
            }

            return v is JsonNumber n ? n.Value : throw new LabFormatException($"{path} 的字段 \"{key}\" 必须是数值");
        }

        /// <summary>规范写出（键序固定：id、cells、metric、at、agg、op、value、min、max、tolerance、versus、note）。</summary>
        public JsonObject ToJson()
        {
            var b = new JsonObjectBuilder().Add("id", LabJson.Str(Id));
            if (Cells.Count > 0)
            {
                var items = new List<JsonValue>();
                foreach (var c in Cells)
                {
                    items.Add(LabJson.Str(c));
                }

                b.Add("cells", new JsonArray(items));
            }

            AddSelector(b, Subject);
            b.Add("op", LabJson.Str(OpText(Op)));
            if (Value != null)
            {
                b.Add("value", Value);
            }

            if (Min.HasValue)
            {
                b.Add("min", LabJson.Num(Min.Value));
            }

            if (Max.HasValue)
            {
                b.Add("max", LabJson.Num(Max.Value));
            }

            if (Tolerance.HasValue)
            {
                b.Add("tolerance", LabJson.Num(Tolerance.Value));
            }

            if (Versus != null)
            {
                var v = new JsonObjectBuilder();
                if (Versus.Cell.Length > 0)
                {
                    v.Add("cell", LabJson.Str(Versus.Cell));
                }

                AddSelector(v, Versus.Selector);
                if (Versus.Scale != 1.0)
                {
                    v.Add("scale", LabJson.Num(Versus.Scale));
                }

                if (Versus.Offset != 0.0)
                {
                    v.Add("offset", LabJson.Num(Versus.Offset));
                }

                b.Add("versus", v.Build());
            }

            if (Note.Length > 0)
            {
                b.Add("note", LabJson.Str(Note));
            }

            return b.Build();
        }

        private static void AddSelector(JsonObjectBuilder b, MetricSelector s)
        {
            b.Add("metric", LabJson.Str(s.Metric));
            if (s.At.HasValue)
            {
                b.Add("at", LabJson.Num(s.At.Value));
            }

            if (s.Agg.Length > 0)
            {
                b.Add("agg", LabJson.Str(s.Agg));
            }
        }
    }

    /// <summary>一条期望在一个格子上的判定状态。</summary>
    public enum ExpectStatus
    {
        Pass,

        /// <summary>度量值不满足期望。</summary>
        Fail,

        /// <summary>期望本身无法判定：度量不存在、类型不符、引用的格子不存在或不可运行等（同样算不通过，并给出原因）。</summary>
        Error,
    }

    /// <summary>一条期望在一个格子上的结果与诊断。</summary>
    public sealed class ExpectationResult
    {
        public Expectation Expectation { get; }

        public string Cell { get; }

        public ExpectStatus Status { get; }

        /// <summary>被检度量的实际值（规范文本；取不到时为空）。</summary>
        public string Actual { get; }

        /// <summary>人读诊断：要求什么、实际是什么；通过时为空。</summary>
        public string Message { get; }

        public bool Ok => Status == ExpectStatus.Pass;

        public ExpectationResult(Expectation expectation, string cell, ExpectStatus status, string actual, string message)
        {
            Expectation = expectation;
            Cell = cell;
            Status = status;
            Actual = actual;
            Message = message;
        }

        public override string ToString() =>
            (Ok ? "通过 " : Status == ExpectStatus.Fail ? "期望失败 " : "期望无法判定 ") + "[" + Expectation.Id + "] " + Cell + " "
            + Expectation.Subject + (Message.Length > 0 ? "：" + Message : string.Empty);
    }

    /// <summary>期望求值器：把一份指纹按脚本里的期望逐条判定（只读，不改任何文件）。</summary>
    public static class ExpectationEvaluator
    {
        private const double EqEpsilon = 1e-9;

        /// <summary>
        /// 判定适用于 <paramref name="cell"/> 的全部期望。<paramref name="fingerprintOf"/> 给出任意格子的指纹
        /// （相对关系引用另一个格子时用；调用方负责缓存，格子不可运行或不存在时可抛 <see cref="LabFormatException"/>，
        /// 这条期望记为 <see cref="ExpectStatus.Error"/>）。
        /// </summary>
        public static List<ExpectationResult> Evaluate(
            IReadOnlyList<Expectation> expectations, string cell, Fingerprint actual, MetricRegistry registry,
            Func<string, Fingerprint> fingerprintOf)
        {
            var results = new List<ExpectationResult>();
            foreach (var e in expectations)
            {
                if (e.AppliesTo(cell))
                {
                    results.Add(EvaluateOne(e, cell, actual, registry, fingerprintOf));
                }
            }

            return results;
        }

        /// <summary>静态检查：期望引用的组、度量、格子是否都在注册表与格子清单里（不跑模拟）；返回问题清单，空表示无问题。</summary>
        public static List<string> Validate(
            IReadOnlyList<Expectation> expectations, MetricRegistry registry, IReadOnlyCollection<string> knownCells)
        {
            var problems = new List<string>();
            foreach (var e in expectations)
            {
                CheckSelector(e, e.Subject, registry, problems);
                foreach (var c in e.Cells)
                {
                    if (!ContainsCell(knownCells, c))
                    {
                        problems.Add($"[{e.Id}] 引用了不存在的格子 {c}");
                    }
                }

                if (e.Versus != null)
                {
                    CheckSelector(e, e.Versus.Selector, registry, problems);
                    if (e.Versus.Cell.Length > 0 && !ContainsCell(knownCells, e.Versus.Cell))
                    {
                        problems.Add($"[{e.Id}] versus 引用了不存在的格子 {e.Versus.Cell}");
                    }
                }
            }

            return problems;
        }

        private static bool ContainsCell(IReadOnlyCollection<string> items, string value)
        {
            foreach (var i in items)
            {
                if (string.Equals(i, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CheckSelector(Expectation e, MetricSelector s, MetricRegistry registry, List<string> problems)
        {
            var error = FindSpec(registry, s, out _);
            if (error != null)
            {
                problems.Add($"[{e.Id}] {error}");
            }
        }

        private static string? FindSpec(MetricRegistry registry, MetricSelector s, out MetricSpec? spec)
        {
            spec = null;
            var group = registry.Find(s.Group);
            if (group == null)
            {
                var names = new List<string>();
                foreach (var g in registry.Groups)
                {
                    names.Add(g.Name);
                }

                return $"度量组 {s.Group} 不存在（已注册：{string.Join("、", names)}）";
            }

            foreach (var candidate in group.Specs)
            {
                if (string.Equals(candidate.Name, s.Name, StringComparison.Ordinal))
                {
                    spec = candidate;
                    return null;
                }
            }

            var metrics = new List<string>();
            foreach (var candidate in group.Specs)
            {
                metrics.Add(candidate.Name);
            }

            return $"度量组 {group.Name} 没有度量 {s.Name}（有：{string.Join("、", metrics)}）";
        }

        private static ExpectationResult EvaluateOne(
            Expectation e, string cell, Fingerprint actual, MetricRegistry registry, Func<string, Fingerprint> fingerprintOf)
        {
            if (!TryResolve(e.Subject, actual, registry, out var subject, out var error))
            {
                return new ExpectationResult(e, cell, ExpectStatus.Error, string.Empty, error);
            }

            var actualText = FingerprintComparer.Render(subject!);
            if (e.Op == ExpectOp.Between)
            {
                if (!(subject is JsonNumber bn))
                {
                    return new ExpectationResult(e, cell, ExpectStatus.Error, actualText, $"between 需要数值度量，实际是 {actualText}");
                }

                var okLow = !e.Min.HasValue || bn.Value >= e.Min.Value - EqEpsilon;
                var okHigh = !e.Max.HasValue || bn.Value <= e.Max.Value + EqEpsilon;
                if (okLow && okHigh)
                {
                    return new ExpectationResult(e, cell, ExpectStatus.Pass, actualText, string.Empty);
                }

                var range = (e.Min.HasValue ? "[" + LabJson.Fmt(e.Min.Value) : "(-∞") + ", " + (e.Max.HasValue ? LabJson.Fmt(e.Max.Value) + "]" : "+∞)");
                return new ExpectationResult(
                    e, cell, ExpectStatus.Fail, actualText, $"要求落在 {range}，实际 {actualText}（{(okLow ? "超出上界" : "低于下界")}）");
            }

            if (e.Op == ExpectOp.In)
            {
                var candidates = new List<string>();
                foreach (var item in (JsonArray)e.Value!)
                {
                    candidates.Add(FingerprintComparer.Render(item));
                }

                return candidates.Contains(actualText)
                    ? new ExpectationResult(e, cell, ExpectStatus.Pass, actualText, string.Empty)
                    : new ExpectationResult(e, cell, ExpectStatus.Fail, actualText, $"要求取值之一 [{string.Join(", ", candidates)}]，实际 {actualText}");
            }

            if (e.Op == ExpectOp.Contains)
            {
                return EvaluateContains(e, cell, subject!, actualText);
            }

            // 比较的另一端：期望值，或另一个度量（可跨格子）按 scale/offset 换算。
            JsonValue other;
            string otherText;
            if (e.Versus != null)
            {
                var otherCell = e.Versus.Cell.Length > 0 ? e.Versus.Cell : cell;
                Fingerprint otherFingerprint;
                try
                {
                    otherFingerprint = string.Equals(otherCell, cell, StringComparison.Ordinal) ? actual : fingerprintOf(otherCell);
                }
                catch (Exception ex) when (ex is LabFormatException || ex is LabCellNotRunnableException)
                {
                    return new ExpectationResult(e, cell, ExpectStatus.Error, actualText, $"取不到格子 {otherCell} 的指纹：{ex.Message}");
                }

                if (!TryResolve(e.Versus.Selector, otherFingerprint, registry, out var resolved, out error))
                {
                    return new ExpectationResult(e, cell, ExpectStatus.Error, actualText, $"对照端：{error}");
                }

                var scaled = resolved!;
                var origin = FingerprintComparer.Render(resolved!);
                if (e.Versus.Scale != 1.0 || e.Versus.Offset != 0.0)
                {
                    if (!(resolved is JsonNumber rn))
                    {
                        return new ExpectationResult(
                            e, cell, ExpectStatus.Error, actualText, $"对照端 {e.Versus.Selector} 不是数值（{origin}），不能乘 scale 或加 offset");
                    }

                    scaled = LabJson.Num(MetricSink.Round(rn.Value * e.Versus.Scale + e.Versus.Offset));
                }

                other = scaled;
                otherText = $"{otherCell}:{e.Versus.Selector}"
                    + (e.Versus.Scale != 1.0 || e.Versus.Offset != 0.0
                        ? $"×{LabJson.Fmt(e.Versus.Scale)}+{LabJson.Fmt(e.Versus.Offset)}"
                        : string.Empty)
                    + $"（={FingerprintComparer.Render(scaled)}）";
            }
            else
            {
                other = e.Value!;
                otherText = FingerprintComparer.Render(other);
            }

            var op = Expectation.OpText(e.Op);
            if (e.Op == ExpectOp.Eq || e.Op == ExpectOp.Ne)
            {
                var equal = Equal(subject!, other);
                var wantEqual = e.Op == ExpectOp.Eq;
                return equal == wantEqual
                    ? new ExpectationResult(e, cell, ExpectStatus.Pass, actualText, string.Empty)
                    : new ExpectationResult(
                        e, cell, ExpectStatus.Fail, actualText, $"要求 {(wantEqual ? "==" : "!=")} {otherText}，实际 {actualText}{Delta(subject!, other)}");
            }

            if (!(subject is JsonNumber a) || !(other is JsonNumber b))
            {
                return new ExpectationResult(
                    e, cell, ExpectStatus.Error, actualText, $"{op} 需要两端都是数值：实际 {actualText}，对照 {otherText}");
            }

            bool pass;
            string symbol;
            switch (e.Op)
            {
                case ExpectOp.Lt: pass = a.Value < b.Value - EqEpsilon; symbol = "<"; break;
                case ExpectOp.Le: pass = a.Value <= b.Value + EqEpsilon; symbol = "<="; break;
                case ExpectOp.Gt: pass = a.Value > b.Value + EqEpsilon; symbol = ">"; break;
                case ExpectOp.Ge: pass = a.Value >= b.Value - EqEpsilon; symbol = ">="; break;
                default:
                    var tolerance = e.Tolerance ?? 1e-6;
                    pass = Math.Abs(a.Value - b.Value) <= tolerance + 1e-12;
                    symbol = "≈(±" + LabJson.Fmt(tolerance) + ")";
                    break;
            }

            return pass
                ? new ExpectationResult(e, cell, ExpectStatus.Pass, actualText, string.Empty)
                : new ExpectationResult(e, cell, ExpectStatus.Fail, actualText, $"要求 {symbol} {otherText}，实际 {actualText}{Delta(a, b)}");
        }

        private static ExpectationResult EvaluateContains(Expectation e, string cell, JsonValue subject, string actualText)
        {
            if (subject is JsonString s && e.Value is JsonString needle)
            {
                return s.Value.Contains(needle.Value)
                    ? new ExpectationResult(e, cell, ExpectStatus.Pass, actualText, string.Empty)
                    : new ExpectationResult(e, cell, ExpectStatus.Fail, actualText, $"要求包含 \"{needle.Value}\"，实际 {Shorten(actualText)}");
            }

            if (subject is JsonArray arr)
            {
                var want = FingerprintComparer.Render(e.Value!);
                foreach (var item in arr)
                {
                    if (string.Equals(FingerprintComparer.Render(item), want, StringComparison.Ordinal))
                    {
                        return new ExpectationResult(e, cell, ExpectStatus.Pass, actualText, string.Empty);
                    }
                }

                return new ExpectationResult(e, cell, ExpectStatus.Fail, actualText, $"要求数组包含 {want}，实际 {Shorten(actualText)}");
            }

            return new ExpectationResult(
                e, cell, ExpectStatus.Error, actualText, $"contains 需要字符串（配字符串 value）或数组度量，实际 {Shorten(actualText)}");
        }

        private static string Shorten(string text) => text.Length <= 160 ? text : text.Substring(0, 157) + "...";

        private static bool Equal(JsonValue a, JsonValue b)
        {
            if (a is JsonNumber an && b is JsonNumber bn)
            {
                return Math.Abs(an.Value - bn.Value) <= EqEpsilon;
            }

            return string.Equals(FingerprintComparer.Render(a), FingerprintComparer.Render(b), StringComparison.Ordinal);
        }

        private static string Delta(JsonValue actual, JsonValue expected)
        {
            if (actual is JsonNumber an && expected is JsonNumber bn)
            {
                var d = MetricSink.Round(an.Value - bn.Value);
                return " (差 " + (d >= 0 ? "+" : string.Empty) + LabJson.Fmt(d) + ")";
            }

            return string.Empty;
        }

        /// <summary>按选择器取出度量值；组/度量不存在、该格子指纹里没有这个条件组、数组下标越界、聚合对象不是数值数组都给出诊断。</summary>
        private static bool TryResolve(
            MetricSelector selector, Fingerprint fingerprint, MetricRegistry registry, out JsonValue? value, out string error)
        {
            value = null;
            error = FindSpec(registry, selector, out _) ?? string.Empty;
            if (error.Length > 0)
            {
                return false;
            }

            if (!fingerprint.Groups.TryGetValue(selector.Group, out var gv) || !(gv is JsonObject group))
            {
                error = $"该格子的指纹里没有度量组 {selector.Group}（条件度量组只在适用的运行里出现，例如手感组只在 feel 脚本里出现）";
                return false;
            }

            if (!group.TryGetValue(selector.Name, out var raw))
            {
                error = $"指纹里没有度量 {selector.Metric}";
                return false;
            }

            if (selector.At.HasValue)
            {
                if (!(raw is JsonArray arr))
                {
                    error = $"度量 {selector.Metric} 不是数组，不能取下标 {selector.At.Value}（实际 {Shorten(FingerprintComparer.Render(raw))}）";
                    return false;
                }

                var index = selector.At.Value < 0 ? arr.Count + selector.At.Value : selector.At.Value;
                if (index < 0 || index >= arr.Count)
                {
                    error = $"度量 {selector.Metric} 只有 {arr.Count} 项，下标 {selector.At.Value} 越界";
                    return false;
                }

                value = arr[index];
                return true;
            }

            if (selector.Agg.Length > 0)
            {
                return TryAggregate(selector, raw, out value, out error);
            }

            value = raw;
            return true;
        }

        private static bool TryAggregate(MetricSelector selector, JsonValue raw, out JsonValue? value, out string error)
        {
            value = null;
            error = string.Empty;
            if (!(raw is JsonArray arr))
            {
                error = $"度量 {selector.Metric} 不是数组，不能做 {selector.Agg}（实际 {Shorten(FingerprintComparer.Render(raw))}）";
                return false;
            }

            if (selector.Agg == "len")
            {
                value = LabJson.Num(arr.Count);
                return true;
            }

            if (selector.Agg == "first" || selector.Agg == "last")
            {
                if (arr.Count == 0)
                {
                    error = $"度量 {selector.Metric} 是空数组，没有 {selector.Agg}";
                    return false;
                }

                value = selector.Agg == "first" ? arr[0] : arr[arr.Count - 1];
                return true;
            }

            var numbers = new List<double>();
            foreach (var item in arr)
            {
                if (!(item is JsonNumber n))
                {
                    error = $"度量 {selector.Metric} 含非数值元素，不能做 {selector.Agg}";
                    return false;
                }

                numbers.Add(n.Value);
            }

            if (numbers.Count == 0)
            {
                error = $"度量 {selector.Metric} 是空数组，没有 {selector.Agg}";
                return false;
            }

            var result = numbers[0];
            var sum = 0.0;
            foreach (var n in numbers)
            {
                sum += n;
                result = selector.Agg == "min" ? Math.Min(result, n) : selector.Agg == "max" ? Math.Max(result, n) : result;
            }

            value = LabJson.Num(MetricSink.Round(selector.Agg == "sum" ? sum : result));
            return true;
        }
    }

    /// <summary>"导出为测试"生成期望时的取舍。</summary>
    public sealed class ExpectationExportOptions
    {
        /// <summary>只导出这些度量组（空表示全部）。</summary>
        public ISet<string> Groups { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>是否把真实时间类度量（帧耗时、分配）也导出为上限期望；缺省不导出（它们随机器抖动，基线已按量级分桶守着）。</summary>
        public bool IncludeRealTime { get; set; }

        /// <summary>文本型度量（序列、事件串）超过这个长度就不导出为期望（完整序列由基线逐字守着，期望只放人能编辑的断言）。</summary>
        public int MaxTextLength { get; set; } = 160;

        /// <summary>表现类数值的绝对容差下限（度量声明的允差更大时用声明值）。</summary>
        public double MinPresentationTolerance { get; set; } = 1e-6;
    }

    /// <summary>
    /// 从一次运行的指纹生成期望清单（手感设计 06 第 3.5 节"导出为测试"）：逻辑类度量精确（数值 <c>eq</c>、文本/数组逐字 <c>eq</c>），
    /// 表现类数值给 <c>approx</c> 带度量声明的允差，真实时间类（需显式开启）给 <c>le</c> 上限（同比较器的倍率上限规则）。
    /// 空值（空字符串、空数组、<c>-1</c> 哨兵）不导出——"没发生"不是值得钉死的断言。
    /// 多个格子取值相同的度量合并成一条不带 <c>cells</c> 的期望（适用全部导出格子）；取值不同的按取值分组写 <c>cells</c>。
    /// 生成的期望 id 以 <see cref="Expectation.AutoPrefix"/> 开头，再次导出只重生成这一类，手写的保留。
    /// </summary>
    public static class ExpectationExporter
    {
        public static List<Expectation> FromFingerprints(
            IReadOnlyList<KeyValuePair<string, Fingerprint>> cells, MetricRegistry registry, ExpectationExportOptions? options = null)
        {
            options ??= new ExpectationExportOptions();
            var result = new List<Expectation>();
            foreach (var group in registry.Groups)
            {
                if (options.Groups.Count > 0 && !options.Groups.Contains(group.Name))
                {
                    continue;
                }

                foreach (var spec in group.Specs)
                {
                    if (spec.Class == MetricClass.RealTime && !options.IncludeRealTime)
                    {
                        continue;
                    }

                    // 按"期望的规范文本"分桶，保持首次出现的顺序。
                    var order = new List<string>();
                    var buckets = new Dictionary<string, KeyValuePair<Expectation, List<string>>>(StringComparer.Ordinal);
                    var participating = 0;
                    foreach (var cell in cells)
                    {
                        if (!cell.Value.Groups.TryGetValue(group.Name, out var gv) || !(gv is JsonObject go) || !go.TryGetValue(spec.Name, out var v))
                        {
                            continue;
                        }

                        participating++;
                        var template = Template(group.Name, spec, v, options);
                        if (template == null)
                        {
                            continue;
                        }

                        var key = LabJson.Write(template.ToJson());
                        if (!buckets.TryGetValue(key, out var bucket))
                        {
                            bucket = new KeyValuePair<Expectation, List<string>>(template, new List<string>());
                            buckets[key] = bucket;
                            order.Add(key);
                        }

                        bucket.Value.Add(cell.Key);
                    }

                    foreach (var key in order)
                    {
                        var bucket = buckets[key];
                        var all = bucket.Value.Count == participating && participating == cells.Count;
                        var id = Expectation.AutoPrefix + group.Name + "." + spec.Name
                            + (all ? string.Empty : "@" + string.Join("+", bucket.Value));
                        var t = bucket.Key;
                        result.Add(new Expectation(
                            id, all ? Array.Empty<string>() : bucket.Value, t.Subject, t.Op, t.Value, t.Min, t.Max, t.Tolerance, null, string.Empty));
                    }
                }
            }

            return result;
        }

        private static Expectation? Template(string group, MetricSpec spec, JsonValue value, ExpectationExportOptions options)
        {
            var subject = new MetricSelector(group + "." + spec.Name);
            switch (value)
            {
                case JsonNumber n:
                    if (n.Value == -1)
                    {
                        return null;
                    }

                    if (spec.Class == MetricClass.RealTime)
                    {
                        var ceiling = Math.Max(n.Value * spec.Amount, spec.Floor);
                        return new Expectation(string.Empty, null, subject, ExpectOp.Le, LabJson.Num(MetricSink.Round(ceiling)), null, null, null, null, string.Empty);
                    }

                    if (spec.Class == MetricClass.Presentation)
                    {
                        var tolerance = Math.Max(spec.Tolerance == ToleranceKind.Absolute ? spec.Amount : 0, options.MinPresentationTolerance);
                        return new Expectation(string.Empty, null, subject, ExpectOp.Approx, n, null, null, tolerance, null, string.Empty);
                    }

                    return new Expectation(string.Empty, null, subject, ExpectOp.Eq, n, null, null, null, null, string.Empty);
                case JsonString s:
                    return s.Value.Length == 0 || s.Value.Length > options.MaxTextLength || spec.Class == MetricClass.RealTime
                        ? null
                        : new Expectation(string.Empty, null, subject, ExpectOp.Eq, s, null, null, null, null, string.Empty);
                case JsonArray a:
                    return a.Count == 0 || FingerprintComparer.Render(a).Length > options.MaxTextLength || spec.Class == MetricClass.RealTime
                        ? null
                        : new Expectation(string.Empty, null, subject, ExpectOp.Eq, a, null, null, null, null, string.Empty);
                default:
                    return null;
            }
        }
    }
}
