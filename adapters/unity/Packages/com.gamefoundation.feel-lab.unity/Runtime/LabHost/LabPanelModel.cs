#nullable enable
// LabPanelModel：实验室面板的模型（手感设计/06 第 4 节"面板"）。纯 C#，不依赖编辑器 API，因此编辑模式测试可以直接验证
// "覆盖与 A/B 的存储逻辑"；编辑器窗口（Editor/LabHost/FeelLabWindow）只是它的一层界面。
//
// 面板能做的事：选脚本与格子并运行；按运行结果列出度量；改手感档案覆盖（本地覆盖存储文件，不改源数据）；
// 在同一脚本上拿两组覆盖（或"无覆盖"基准）各跑一次，对比度量差异。
//
// 判断记录（运行方式由委托给出）：模型不直接依赖引擎舞台——委托 <see cref="PanelRunner"/> 在引擎宿主上跑（<see cref="EngineLabHost"/>），
// 测试可以换成无头宿主，覆盖存储与 A/B 逻辑完全相同。
// 判断记录（覆盖一律经存储文件落盘）：每次修改覆盖组都立即校验并保存；校验不通过不落盘、不改内存里的存储，把逐条问题文本返回给界面，
// 不静默吞掉。覆盖存储文件默认放在工程的 Library 目录下（不进版本库），不会改动任何源数据文件。
using Adapter.Unity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Core.Foundation.Feel;
using Lab;

namespace FeelLab.Unity
{
    /// <summary>一次面板运行的结果。</summary>
    public sealed class PanelRunResult
    {
        public Fingerprint Fingerprint { get; }

        /// <summary>引擎侧记录（无头运行为 null）。</summary>
        public EngineRecording? Engine { get; }

        public IReadOnlyList<ExpectationResult> Expectations { get; }

        public PanelRunResult(Fingerprint fingerprint, EngineRecording? engine, IReadOnlyList<ExpectationResult> expectations)
        {
            Fingerprint = fingerprint;
            Engine = engine;
            Expectations = expectations;
        }
    }

    /// <summary>面板用的运行委托：脚本、格子、可选覆盖组 → 结果。</summary>
    public delegate PanelRunResult PanelRunner(InputScript script, string cell, OverrideSet? overrides);

    /// <summary>度量表的一行。</summary>
    public sealed class MetricRow
    {
        public string Group { get; }

        public string Metric { get; }

        public MetricClass Class { get; }

        public string Value { get; }

        public MetricRow(string group, string metric, MetricClass cls, string value)
        {
            Group = group;
            Metric = metric;
            Class = cls;
            Value = value;
        }

        public string FullName => Group + "." + Metric;
    }

    public sealed class LabPanelModel
    {
        private readonly IReadOnlyList<InputScript> _scripts;
        private readonly Func<InputScript, IReadOnlyList<string>> _cellsOf;
        private readonly PanelRunner _runner;
        private readonly MetricRegistry _registry;
        private readonly FeelFieldSet _fields;

        public LabPanelModel(
            IReadOnlyList<InputScript> scripts, Func<InputScript, IReadOnlyList<string>> cellsOf, PanelRunner runner,
            MetricRegistry registry, string storePath, FeelFieldSet? fields = null)
        {
            _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
            _cellsOf = cellsOf ?? throw new ArgumentNullException(nameof(cellsOf));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _fields = fields ?? FeelFields.Default;
            StorePath = storePath ?? throw new ArgumentNullException(nameof(storePath));
            Store = OverrideStore.Load(StorePath);
        }

        // ───────── 选择 ─────────

        public IReadOnlyList<InputScript> Scripts => _scripts;

        public InputScript? SelectedScript { get; private set; }

        public IReadOnlyList<string> Cells { get; private set; } = Array.Empty<string>();

        public string? SelectedCell { get; private set; }

        /// <summary>选脚本：格子列表随之刷新，缺省选第一个格子。找不到脚本返回 false。</summary>
        public bool SelectScript(string scriptId)
        {
            foreach (var script in _scripts)
            {
                if (string.Equals(script.Meta.ScriptId, scriptId, StringComparison.Ordinal))
                {
                    SelectedScript = script;
                    Cells = _cellsOf(script);
                    SelectedCell = Cells.Count > 0 ? Cells[0] : null;
                    LastRun = null;
                    return true;
                }
            }

            return false;
        }

        public bool SelectCell(string cell)
        {
            foreach (var candidate in Cells)
            {
                if (string.Equals(candidate, cell, StringComparison.Ordinal))
                {
                    SelectedCell = cell;
                    LastRun = null;
                    return true;
                }
            }

            return false;
        }

        // ───────── 覆盖存储 ─────────

        public string StorePath { get; }

        public OverrideStore Store { get; private set; }

        /// <summary>当前选中应用的覆盖组名；<c>null</c> 表示"无覆盖"（基准）。</summary>
        public string? ActiveOverride { get; private set; }

        public IReadOnlyList<string> OverrideNames
        {
            get
            {
                var names = new List<string>();
                foreach (var set in Store.Sets)
                {
                    names.Add(set.Name);
                }

                return names;
            }
        }

        /// <summary>手感字段登记表里全部字段名（界面下拉用）。</summary>
        public IReadOnlyList<string> FieldNames
        {
            get
            {
                var names = new List<string>();
                foreach (var def in _fields.Fields)
                {
                    names.Add(def.Name);
                }

                return names;
            }
        }

        public bool SelectOverride(string? name)
        {
            if (name != null && Store.Find(name) == null)
            {
                return false;
            }

            ActiveOverride = name;
            return true;
        }

        /// <summary>
        /// 新增/修改一条覆盖写入（<paramref name="opText"/>：set/multiply/add/remove；<paramref name="valueText"/> 按字段类型解析）：
        /// 校验通过才并入覆盖组并立即落盘；返回问题清单（空表示成功）。覆盖组不存在时新建；同组同字段同单位的旧写入被替换。
        /// </summary>
        public List<string> SetOverride(string setName, string field, string opText, string valueText, string unit = "")
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(setName))
            {
                problems.Add("覆盖组名不能为空");
                return problems;
            }

            if (!TryParseWrite(field, opText, valueText, unit, out var write, out var problem))
            {
                problems.Add(problem);
                return problems;
            }

            var existing = Store.Find(setName);
            var candidate = existing == null ? new OverrideSet(setName) : existing.Clone();
            candidate.Writes.RemoveAll(w =>
                string.Equals(w.Field, write!.Field, StringComparison.Ordinal) && string.Equals(w.Unit, write.Unit, StringComparison.Ordinal));
            candidate.Add(write!);
            problems.AddRange(OverrideStore.Validate(candidate, _fields));
            if (problems.Count > 0)
            {
                return problems;
            }

            Store.Upsert(candidate);
            Store.Save(StorePath);
            return problems;
        }

        /// <summary>删掉覆盖组中某字段（同单位）的写入；组变空时保留空组。返回是否有改动。</summary>
        public bool RemoveWrite(string setName, string field, string unit = "")
        {
            var set = Store.Find(setName);
            if (set == null)
            {
                return false;
            }

            var copy = set.Clone();
            var removed = copy.Writes.RemoveAll(w =>
                string.Equals(w.Field, field, StringComparison.Ordinal) && string.Equals(w.Unit, unit, StringComparison.Ordinal));
            if (removed == 0)
            {
                return false;
            }

            Store.Upsert(copy);
            Store.Save(StorePath);
            return true;
        }

        public bool RemoveOverrideSet(string setName)
        {
            if (!Store.Remove(setName))
            {
                return false;
            }

            if (string.Equals(ActiveOverride, setName, StringComparison.Ordinal))
            {
                ActiveOverride = null;
            }

            Store.Save(StorePath);
            return true;
        }

        /// <summary>从存储文件重新读入（外部改了文件时用）。</summary>
        public void ReloadStore()
        {
            Store = OverrideStore.Load(StorePath);
            if (ActiveOverride != null && Store.Find(ActiveOverride) == null)
            {
                ActiveOverride = null;
            }
        }

        /// <summary>把文本形式的写入解析成 <see cref="OverrideWrite"/>（按字段类型解析值）；字段未登记、操作未知、值解析失败都给出人读原因。</summary>
        public bool TryParseWrite(string field, string opText, string valueText, string unit, out OverrideWrite? write, out string problem)
        {
            write = null;
            problem = string.Empty;
            if (!_fields.TryGet(field, out var def))
            {
                problem = $"手感字段 {field} 未登记";
                return false;
            }

            FeelOp op;
            switch (opText)
            {
                case "set": op = FeelOp.Set; break;
                case "multiply": op = FeelOp.Multiply; break;
                case "add": op = FeelOp.Add; break;
                case "remove": op = FeelOp.Remove; break;
                default:
                    problem = $"操作 {opText} 未知（set/multiply/add/remove）";
                    return false;
            }

            FeelValue value;
            switch (def.Kind)
            {
                case FeelFieldKind.Number:
                case FeelFieldKind.Int:
                    if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        problem = $"字段 {field} 要数字，得到“{valueText}”";
                        return false;
                    }

                    value = FeelValue.Of(number);
                    break;
                case FeelFieldKind.Bool:
                    if (!bool.TryParse(valueText, out var flag))
                    {
                        problem = $"字段 {field} 要 true/false，得到“{valueText}”";
                        return false;
                    }

                    value = FeelValue.Of(flag);
                    break;
                case FeelFieldKind.List:
                    var items = new List<string>();
                    foreach (var part in valueText.Split(','))
                    {
                        var trimmed = part.Trim();
                        if (trimmed.Length > 0)
                        {
                            items.Add(trimmed);
                        }
                    }

                    value = FeelValue.OfList(items);
                    break;
                default:
                    value = FeelValue.Of(valueText);
                    break;
            }

            write = new OverrideWrite(field, op, value, unit ?? string.Empty);
            return true;
        }

        // ───────── 运行与度量 ─────────

        public PanelRunResult? LastRun { get; private set; }

        private OverrideSet? Resolve(string? name) => name == null ? null : Store.Find(name);

        /// <summary>按当前选择（脚本、格子、选中的覆盖组）运行一次；覆盖组在存储里不存在抛 <see cref="InvalidOperationException"/>。</summary>
        public PanelRunResult Run()
        {
            RequireSelection(out var script, out var cell);
            var set = Resolve(ActiveOverride);
            if (ActiveOverride != null && set == null)
            {
                throw new InvalidOperationException($"覆盖组 {ActiveOverride} 不在存储里");
            }

            LastRun = _runner(script, cell, set);
            return LastRun;
        }

        /// <summary>最近一次运行的度量表（按注册表声明顺序；没有运行过返回空）。</summary>
        public List<MetricRow> MetricRows() => MetricRowsOf(LastRun?.Fingerprint);

        public List<MetricRow> MetricRowsOf(Fingerprint? fingerprint)
        {
            var rows = new List<MetricRow>();
            if (fingerprint == null)
            {
                return rows;
            }

            foreach (var group in _registry.Groups)
            {
                if (!fingerprint.Groups.TryGetValue(group.Name, out var gv) || !(gv is Core.Foundation.Common.Json.JsonObject go))
                {
                    continue;
                }

                foreach (var spec in group.Specs)
                {
                    if (go.TryGetValue(spec.Name, out var v))
                    {
                        rows.Add(new MetricRow(group.Name, spec.Name, spec.Class, FingerprintComparer.Render(v)));
                    }
                }
            }

            return rows;
        }

        /// <summary>
        /// A/B：同一脚本同一格子，A、B 各取一组覆盖（<c>null</c> 表示无覆盖基准）各跑一次，逐度量对比。
        /// 覆盖组名不在存储里抛 <see cref="InvalidOperationException"/>（不静默当成"无覆盖"，否则会得到两边相同的假结论）。
        /// </summary>
        public AbReport RunAb(string? nameA, string? nameB)
        {
            RequireSelection(out var script, out var cell);
            var a = Resolve(nameA);
            var b = Resolve(nameB);
            if (nameA != null && a == null) throw new InvalidOperationException($"覆盖组 {nameA} 不在存储里");
            if (nameB != null && b == null) throw new InvalidOperationException($"覆盖组 {nameB} 不在存储里");
            var runA = _runner(script, cell, a);
            var runB = _runner(script, cell, b);
            return AbComparison.Compare(
                script.Meta.ScriptId, cell, nameA ?? "(无覆盖)", runA.Fingerprint, nameB ?? "(无覆盖)", runB.Fingerprint, _registry);
        }

        private void RequireSelection(out InputScript script, out string cell)
        {
            if (SelectedScript == null || SelectedCell == null)
            {
                throw new InvalidOperationException("还没有选脚本与格子");
            }

            script = SelectedScript;
            cell = SelectedCell;
        }
    }
}
