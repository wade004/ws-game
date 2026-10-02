using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Feel;

namespace Lab
{
    /// <summary>一条覆盖写入：字段、操作、值，可选作用单位（出场标签；空表示全局）。</summary>
    public sealed class OverrideWrite
    {
        public string Field { get; }

        public FeelOp Op { get; }

        public FeelValue Value { get; }

        /// <summary>作用单位的出场标签（玩家 <c>player</c>，靶子取靶子集条目名）；空表示全局覆盖。</summary>
        public string Unit { get; }

        public OverrideWrite(string field, FeelOp op, FeelValue value, string unit = "")
        {
            if (string.IsNullOrEmpty(field)) throw new ArgumentException("字段名不能为空", nameof(field));
            Field = field;
            Op = op;
            Value = value;
            Unit = unit ?? string.Empty;
        }

        public FeelWrite ToFeelWrite() => new FeelWrite(Field, Op, Value);

        public override string ToString() =>
            (Unit.Length == 0 ? "全局 " : Unit + " ") + Field + " " + FeelProvenanceOps.FromOp(Op) + " " + Value;
    }

    /// <summary>一组命名的覆盖（面板里的"A 组/B 组"）：有序写入清单。同一作用域内同字段后写覆盖先写，与第 8 层调试覆盖的语义一致。</summary>
    public sealed class OverrideSet
    {
        public string Name { get; }

        public List<OverrideWrite> Writes { get; } = new List<OverrideWrite>();

        public OverrideSet(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("覆盖组名不能为空", nameof(name));
            Name = name;
        }

        public OverrideSet Add(OverrideWrite write)
        {
            Writes.Add(write ?? throw new ArgumentNullException(nameof(write)));
            return this;
        }

        public OverrideSet Clone(string? newName = null)
        {
            var copy = new OverrideSet(newName ?? Name);
            copy.Writes.AddRange(Writes);
            return copy;
        }
    }

    /// <summary>
    /// 覆盖存储（06 第 4 节"覆盖存储"）：一个本地 JSON 文件，里面是若干命名覆盖组。<b>覆盖不改动任何源数据</b>——它只活在这个文件里，
    /// 运行时经第 8 层调试覆盖（<see cref="FeelDebugOverrides"/>）叠加到手感解析上；清掉文件或不应用，数据与基线都不变。
    /// <para>
    /// 判断记录（校验与失败方式）：保存前与应用前都按手感字段登记表（<see cref="FeelFieldSet"/>）校验——字段必须已登记、操作必须是该字段类型
    /// 允许的、值种类必须与字段类型一致；有问题给出逐条人读文本，不静默吞掉（未登记字段让调试覆盖层抛异常，这里提前拦下并说清原因）。
    /// 读文件时格式不对抛 <see cref="LabFormatException"/>，文件不存在视为空存储。写文件用"写临时文件再替换"，不留下半截文件。
    /// </para>
    /// </summary>
    public sealed class OverrideStore
    {
        public const int FormatVersion = 1;

        private readonly List<OverrideSet> _sets = new List<OverrideSet>();

        public IReadOnlyList<OverrideSet> Sets => _sets;

        public OverrideSet? Find(string name)
        {
            foreach (var set in _sets)
            {
                if (string.Equals(set.Name, name, StringComparison.Ordinal))
                {
                    return set;
                }
            }

            return null;
        }

        /// <summary>新增或整体替换同名覆盖组（保持原位置；新组追加在末尾）。</summary>
        public void Upsert(OverrideSet set)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            for (var i = 0; i < _sets.Count; i++)
            {
                if (string.Equals(_sets[i].Name, set.Name, StringComparison.Ordinal))
                {
                    _sets[i] = set;
                    return;
                }
            }

            _sets.Add(set);
        }

        public bool Remove(string name)
        {
            for (var i = 0; i < _sets.Count; i++)
            {
                if (string.Equals(_sets[i].Name, name, StringComparison.Ordinal))
                {
                    _sets.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>按字段登记表校验一组覆盖；返回问题清单（空表示可以应用）。</summary>
        public static List<string> Validate(OverrideSet set, FeelFieldSet fields)
        {
            var problems = new List<string>();
            foreach (var write in set.Writes)
            {
                if (!fields.TryGet(write.Field, out var def))
                {
                    problems.Add($"{set.Name}：手感字段 {write.Field} 未登记");
                    continue;
                }

                if (!def.Allows(write.Op))
                {
                    problems.Add($"{set.Name}：字段 {write.Field} 不允许操作 {FeelProvenanceOps.FromOp(write.Op)}");
                    continue;
                }

                var ok = true;
                switch (def.Kind)
                {
                    case FeelFieldKind.Number:
                    case FeelFieldKind.Int:
                        ok = write.Value.Kind == FeelValueKind.Number;
                        break;
                    case FeelFieldKind.Bool:
                        ok = write.Value.Kind == FeelValueKind.Bool;
                        break;
                    case FeelFieldKind.List:
                        ok = write.Value.Kind == FeelValueKind.List || (write.Op != FeelOp.Set && write.Value.Kind == FeelValueKind.Text);
                        break;
                    default:
                        ok = write.Value.Kind == FeelValueKind.Text;
                        break;
                }

                if (!ok)
                {
                    problems.Add($"{set.Name}：字段 {write.Field}（{def.Kind}）的值种类不对：{write.Value.Kind}");
                }
            }

            return problems;
        }

        public string ToJson()
        {
            var sets = new List<JsonValue>();
            foreach (var set in _sets)
            {
                var writes = new List<JsonValue>();
                foreach (var write in set.Writes)
                {
                    var builder = new JsonObjectBuilder()
                        .Add("field", LabJson.Str(write.Field))
                        .Add("op", LabJson.Str(FeelProvenanceOps.FromOp(write.Op)))
                        .Add("value", ValueToJson(write.Value));
                    if (write.Unit.Length > 0)
                    {
                        builder.Add("unit", LabJson.Str(write.Unit));
                    }

                    writes.Add(builder.Build());
                }

                sets.Add(new JsonObjectBuilder().Add("name", LabJson.Str(set.Name)).Add("writes", new JsonArray(writes)).Build());
            }

            return LabJson.Write(new JsonObjectBuilder()
                .Add("formatVersion", LabJson.Num(FormatVersion))
                .Add("sets", new JsonArray(sets))
                .Build());
        }

        public static OverrideStore Parse(string text, string what = "覆盖存储")
        {
            var root = LabJson.ParseObject(text, what);
            var format = LabJson.RequireInt(root, "formatVersion", what);
            if (format > FormatVersion)
            {
                throw new LabFormatException($"{what} 的 formatVersion={format} 高于本内核支持的 {FormatVersion}");
            }

            var store = new OverrideStore();
            foreach (var setValue in LabJson.RequireArray(root, "sets", what))
            {
                if (!(setValue is JsonObject setObj))
                {
                    throw new LabFormatException($"{what} 的 sets 里有非对象元素");
                }

                var set = new OverrideSet(LabJson.RequireString(setObj, "name", what));
                foreach (var writeValue in LabJson.RequireArray(setObj, "writes", what + "." + set.Name))
                {
                    if (!(writeValue is JsonObject writeObj))
                    {
                        throw new LabFormatException($"{what}.{set.Name} 的 writes 里有非对象元素");
                    }

                    var where = what + "." + set.Name;
                    var field = LabJson.RequireString(writeObj, "field", where);
                    var op = ParseOp(LabJson.RequireString(writeObj, "op", where), where);
                    if (!writeObj.TryGetValue("value", out var raw))
                    {
                        throw new LabFormatException($"{where} 的写入 {field} 缺少 value");
                    }

                    set.Add(new OverrideWrite(field, op, ValueFromJson(raw, where + "." + field), LabJson.OptionalString(writeObj, "unit", where) ?? string.Empty));
                }

                if (store.Find(set.Name) != null)
                {
                    throw new LabFormatException($"{what} 里覆盖组 {set.Name} 重复");
                }

                store._sets.Add(set);
            }

            return store;
        }

        /// <summary>读文件；不存在返回空存储。</summary>
        public static OverrideStore Load(string path)
        {
            if (!File.Exists(path))
            {
                return new OverrideStore();
            }

            return Parse(File.ReadAllText(path, Encoding.UTF8), path);
        }

        /// <summary>写文件（先写同目录临时文件再替换）；目录不存在时创建。</summary>
        public void Save(string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = path + ".tmp";
            File.WriteAllText(temp, ToJson(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }

        private static FeelOp ParseOp(string text, string where)
        {
            switch (text)
            {
                case "set": return FeelOp.Set;
                case "multiply": return FeelOp.Multiply;
                case "add": return FeelOp.Add;
                case "remove": return FeelOp.Remove;
                default: throw new LabFormatException($"{where} 的操作 {text} 未知（set/multiply/add/remove）");
            }
        }

        private static JsonValue ValueToJson(FeelValue value)
        {
            switch (value.Kind)
            {
                case FeelValueKind.Number: return LabJson.Num(value.AsNumber());
                case FeelValueKind.Bool: return LabJson.Bool(value.AsBool());
                case FeelValueKind.Text: return LabJson.Str(value.AsText());
                case FeelValueKind.List:
                    var items = new List<JsonValue>();
                    foreach (var item in value.AsList())
                    {
                        items.Add(LabJson.Str(item));
                    }

                    return new JsonArray(items);
                default:
                    throw new LabFormatException("覆盖写入的值不能是 none");
            }
        }

        private static FeelValue ValueFromJson(JsonValue raw, string where)
        {
            switch (raw)
            {
                case JsonNumber n: return FeelValue.Of(n.Value);
                case JsonBool b: return FeelValue.Of(b.Value);
                case JsonString s: return FeelValue.Of(s.Value);
                case JsonArray a:
                    var list = new List<string>();
                    foreach (var item in a)
                    {
                        if (!(item is JsonString text))
                        {
                            throw new LabFormatException($"{where} 的列表值里有非字符串元素");
                        }

                        list.Add(text.Value);
                    }

                    return FeelValue.OfList(list);
                default:
                    throw new LabFormatException($"{where} 的值类型不支持（数字/布尔/字符串/字符串数组）");
            }
        }
    }

    /// <summary>
    /// 把一组覆盖经第 8 层调试覆盖叠加到本次运行（<see cref="LabHostExtension.OnReady"/>，第一个固定步之前）。
    /// <para>
    /// 判断记录（覆盖需要手感装配）：调试覆盖层属于手感系统；非手感场景脚本（没有手感装配）带覆盖运行，抛 <see cref="LabFormatException"/>
    /// 并说明，不静默忽略——否则 A/B 对比的两边其实一样，面板会给出"没有差异"的假结论。作用单位用出场标签解析，找不到同样抛异常。
    /// </para>
    /// </summary>
    public sealed class OverrideExtension : LabHostExtension
    {
        private readonly OverrideSet _set;

        public OverrideExtension(OverrideSet set)
        {
            _set = set ?? throw new ArgumentNullException(nameof(set));
        }

        public override void OnReady(LabHostContext context)
        {
            if (_set.Writes.Count == 0)
            {
                return;
            }

            var feel = context.World.Gameplay.Feel;
            if (feel == null || feel.Feel.DebugOverrides == null)
            {
                throw new LabFormatException(
                    $"覆盖组 {_set.Name} 有 {_set.Writes.Count} 条写入，但本次运行没有手感装配（脚本不是手感场景，或变体关了手感），无法叠加调试覆盖");
            }

            var problems = OverrideStore.Validate(_set, Core.Foundation.Feel.FeelFields.Default);
            if (problems.Count > 0)
            {
                throw new LabFormatException("覆盖组校验失败：" + string.Join("；", problems));
            }

            var overrides = feel.Feel.DebugOverrides;
            foreach (var write in _set.Writes)
            {
                if (write.Unit.Length == 0)
                {
                    overrides.SetGlobal(write.ToFeelWrite());
                    continue;
                }

                var unit = context.FindByLabel(write.Unit);
                if (!unit.HasValue)
                {
                    throw new LabFormatException($"覆盖组 {_set.Name} 的写入 {write.Field} 作用单位 {write.Unit} 不在本次出场的单位里");
                }

                overrides.SetUnit(unit.Value, write.ToFeelWrite());
            }
        }
    }

    /// <summary>一条度量差异（A/B 对比）。</summary>
    public sealed class MetricDifference
    {
        public string Group { get; }

        public string Metric { get; }

        public MetricClass Class { get; }

        /// <summary>A 侧值的紧凑文本（数字、数组、字符串）；该侧没有这个度量时为 <c>&lt;缺失&gt;</c>。</summary>
        public string A { get; }

        public string B { get; }

        /// <summary>两侧都是数字时的 B − A；否则为 null。</summary>
        public double? Delta { get; }

        public MetricDifference(string group, string metric, MetricClass cls, string a, string b, double? delta)
        {
            Group = group;
            Metric = metric;
            Class = cls;
            A = a;
            B = b;
            Delta = delta;
        }

        public string FullName => Group + "." + Metric;

        public override string ToString() =>
            FullName + "：" + A + " -> " + B + (Delta.HasValue ? " (差 " + (Delta.Value >= 0 ? "+" : string.Empty) + LabJson.Fmt(MetricSink.Round(Delta.Value)) + ")" : string.Empty);
    }

    /// <summary>A/B 对比结果：同一脚本、同一格子上两组覆盖（或"无覆盖"）的度量差异。</summary>
    public sealed class AbReport
    {
        public string Script { get; }

        public string Cell { get; }

        public string NameA { get; }

        public string NameB { get; }

        public IReadOnlyList<MetricDifference> Differences { get; }

        public bool Identical => Differences.Count == 0;

        public AbReport(string script, string cell, string nameA, string nameB, IReadOnlyList<MetricDifference> differences)
        {
            Script = script;
            Cell = cell;
            NameA = nameA;
            NameB = nameB;
            Differences = differences;
        }

        public string Format()
        {
            var sb = new StringBuilder();
            sb.Append("A/B ").Append(Script).Append(" @ ").Append(Cell).Append("：").Append(NameA).Append(" vs ").Append(NameB).Append('\n');
            if (Identical)
            {
                sb.Append("  度量完全一致\n");
            }

            foreach (var d in Differences)
            {
                sb.Append("  ").Append(d).Append('\n');
            }

            return sb.ToString();
        }
    }

    /// <summary>A/B 对比：逐度量比较两份指纹（按注册表声明顺序，两侧值文本不同即一条差异）。</summary>
    public static class AbComparison
    {
        public static AbReport Compare(
            string script, string cell, string nameA, Fingerprint a, string nameB, Fingerprint b, MetricRegistry registry)
        {
            var diffs = new List<MetricDifference>();
            foreach (var group in registry.Groups)
            {
                var ga = a.Groups.TryGetValue(group.Name, out var av) ? av as JsonObject : null;
                var gb = b.Groups.TryGetValue(group.Name, out var bv) ? bv as JsonObject : null;
                if (ga == null && gb == null)
                {
                    continue;
                }

                foreach (var spec in group.Specs)
                {
                    JsonValue? va = null;
                    JsonValue? vb = null;
                    var hasA = ga != null && ga.TryGetValue(spec.Name, out va);
                    var hasB = gb != null && gb.TryGetValue(spec.Name, out vb);
                    var ta = hasA ? FingerprintComparer.Render(va!) : "<缺失>";
                    var tb = hasB ? FingerprintComparer.Render(vb!) : "<缺失>";
                    if (string.Equals(ta, tb, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    double? delta = hasA && hasB && va is JsonNumber na && vb is JsonNumber nb ? nb.Value - na.Value : (double?)null;
                    diffs.Add(new MetricDifference(group.Name, spec.Name, spec.Class, ta, tb, delta));
                }
            }

            return new AbReport(script, cell, nameA, nameB, diffs);
        }
    }
}
