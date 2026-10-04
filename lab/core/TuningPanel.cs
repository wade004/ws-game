using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;

namespace Lab
{
    /// <summary>调参面板的一行：一个手感字段在当前作用单位上的取值、来源与覆盖状态（全部是只读视图数据，引擎侧只负责画）。</summary>
    public sealed class TuningRow
    {
        public FeelFieldDef Def { get; }

        public string Name => Def.Name;

        public FeelGroup Group => Def.Group;

        /// <summary>字段尚未生效（登记状态 planned）：面板灰显并注明，改它不会改变运行结果。</summary>
        public bool Planned => Def.Status == FeelFieldStatus.Planned;

        public string StatusNote { get; }

        /// <summary>标定前的取值（相对量字段是倍数）；字段无值（可选字段且没有任何层写入）时为 None。</summary>
        public FeelValue Raw { get; }

        /// <summary>标定后的绝对值（相对量字段才与 <see cref="Raw"/> 不同）。</summary>
        public FeelValue Absolute { get; }

        /// <summary>判定型毫秒字段换算后的 tick 数；其它字段为 null。</summary>
        public int? Ticks { get; }

        public string ValueText { get; }

        /// <summary>绝对值/tick 的补充说明（如 "× 参考基础移速 = 6" 或 "3 tick"）；没有补充时为空。</summary>
        public string AbsoluteText { get; }

        public string UnitText { get; }

        public string RangeText { get; }

        /// <summary>完整来源链（解析器 provenance，按应用顺序）。</summary>
        public IReadOnlyList<FeelProvenanceEntry> Provenance { get; }

        /// <summary>生效来源的层号（最后一个覆盖操作的层；限幅与取整不算来源）；没有来源为 0。</summary>
        public int SourceLayer { get; }

        public string SourceId { get; }

        /// <summary>取值被登记范围限幅过。</summary>
        public bool Clamped { get; }

        /// <summary>当前槽位里有针对该字段的覆盖。</summary>
        public bool Overridden { get; }

        public string OverrideText { get; }

        /// <summary>覆盖事件已注入但还没经过一个固定步（取值要到下一步才反映）。</summary>
        public bool Pending { get; }

        internal TuningRow(
            FeelFieldDef def, string statusNote, FeelValue raw, FeelValue absolute, int? ticks, string valueText, string absoluteText,
            string unitText, string rangeText, IReadOnlyList<FeelProvenanceEntry> provenance, int sourceLayer, string sourceId, bool clamped,
            bool overridden, string overrideText, bool pending)
        {
            Def = def;
            StatusNote = statusNote;
            Raw = raw;
            Absolute = absolute;
            Ticks = ticks;
            ValueText = valueText;
            AbsoluteText = absoluteText;
            UnitText = unitText;
            RangeText = rangeText;
            Provenance = provenance;
            SourceLayer = sourceLayer;
            SourceId = sourceId;
            Clamped = clamped;
            Overridden = overridden;
            OverrideText = overrideText;
            Pending = pending;
        }
    }

    /// <summary>一次写回的一项编辑（对哪个文件的哪一行、改成什么）。</summary>
    public sealed class WriteBackEdit
    {
        public string Field { get; }

        /// <summary>覆盖的作用域（空 = 全局）。</summary>
        public string Scope { get; }

        public string File { get; }

        public string RowId { get; }

        public string OldValueText { get; }

        public string NewValueText { get; }

        public string Note { get; }

        internal WriteBackEdit(string field, string scope, string file, string rowId, string oldValueText, string newValueText, string note)
        {
            Field = field;
            Scope = scope;
            File = file;
            RowId = rowId;
            OldValueText = oldValueText;
            NewValueText = newValueText;
            Note = note;
        }

        public override string ToString() => File + " [" + RowId + "] " + Field + ": " + OldValueText + " -> " + NewValueText;
    }

    /// <summary>
    /// "写回数据表"的计划：每个覆盖定位到它的数据来源行并算出该行应有的取值，补丁文本都是对磁盘上数据表原文的外科式编辑；
    /// 本类型只在内存里持有补丁结果，不动任何文件（写文件由调用方决定，且只写到本地输出目录）。
    /// </summary>
    public sealed class WriteBackPlan
    {
        private readonly Dictionary<string, KeyValuePair<string, string>> _files = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.Ordinal);

        public List<WriteBackEdit> Edits { get; } = new List<WriteBackEdit>();

        /// <summary>无法自动写回、需要人工处理的覆盖（原因写在文本里）。</summary>
        public List<string> Manual { get; } = new List<string>();

        /// <summary>改动的文件（仓库相对路径，若无法相对化则是完整路径）。</summary>
        public IEnumerable<string> Files => _files.Keys;

        internal void Track(string path, string original, string patched) =>
            _files[path] = new KeyValuePair<string, string>(_files.TryGetValue(path, out var prior) ? prior.Key : original, patched);

        internal bool TryGetText(string path, out string patched)
        {
            if (_files.TryGetValue(path, out var pair))
            {
                patched = pair.Value;
                return true;
            }

            patched = string.Empty;
            return false;
        }

        /// <summary>统一差异文本（文件按路径序、可直接 <c>git apply</c>）；没有任何改动返回空串。</summary>
        public string Diff()
        {
            var builder = new StringBuilder();
            var paths = new List<string>(_files.Keys);
            paths.Sort(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                builder.Append(TextPatch.UnifiedDiff(path, _files[path].Key, _files[path].Value));
            }

            return builder.ToString();
        }

        /// <summary>某文件补丁后的完整文本（测试与"重新解析验证"用）。</summary>
        public string PatchedText(string path) => _files[path].Value;
    }

    /// <summary>
    /// 手感调参面板的视图模型与控制器（手感设计 06 第 3.4 节、第 4 节，ADR-0150）：纯 C#、不含引擎类型，无头可测。
    /// <para>
    /// 职责：① 按字段登记生成七个分组的行（取值、单位、范围、来源层与来源行、落地状态）；② 每次改值落成一条覆盖事件注入实时会话
    /// （会话脚本即日志，整局可被无头宿主逐 tick 重放）；③ 两个 A/B 槽位，各自持有预设与覆盖组，切换本身写进录制；
    /// ④ "恢复基准"；⑤ "保存为预设"（新 <c>feel.preset</c> 行文件）；⑥ "写回数据表"（对来源数据表的差异文件）。
    /// </para>
    /// <para>
    /// 判断记录（覆盖只活在事件里）：面板自己不持有第二份"当前取值"——取值永远从解析器读，覆盖状态永远是"当前槽位的覆盖组"，
    /// 而覆盖组的每一次变化同时是一条会话事件。所以录制与面板状态不会分叉：槽位切换 = 预设事件（若预设不同）+ 清空覆盖事件 + 逐条重注入目标槽位的覆盖。
    /// </para>
    /// <para>
    /// 判断记录（改值在下一个固定步生效）：事件盖"下一个将要执行的固定步"的戳（<see cref="LabSession.Inject"/>），所以改值在下一步起生效，
    /// 行的 <see cref="TuningRow.Pending"/> 标出这段时间；这与"在安全的动作边界生效"（05 第 8 节）一致——进行中的动作沿用开始时的快照。
    /// </para>
    /// </summary>
    public sealed class TuningPanel
    {
        public const string GlobalScope = "";
        public const string PlayerScope = "player";

        private sealed class Slot
        {
            public string Preset = string.Empty;
            public readonly List<OverrideWrite> Writes = new List<OverrideWrite>();
        }

        private readonly LabSession _session;
        private readonly LabHostContext _context;
        private readonly FeelSystemHandle _feel;
        private readonly IReadOnlyList<IDataSource> _sources;
        private readonly string _repoRoot;
        private readonly Slot _slotA = new Slot();
        private readonly Slot _slotB = new Slot();
        private readonly Dictionary<string, int> _pendingStamp = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>手感装配的最小句柄（避免面板到处写长链）。</summary>
        private sealed class FeelSystemHandle
        {
            public readonly FeelResolver Resolver;
            public readonly FeelProfileSet Profiles;

            public FeelSystemHandle(FeelResolver resolver)
            {
                Resolver = resolver;
                Profiles = resolver.Profiles;
            }
        }

        public FeelFieldSet Fields => _feel.Resolver.Fields;

        /// <summary>当前作用单位：空 = 全局（对全部单位生效），<c>player</c>，或在场靶子的出场标签。改值落在这个作用域。</summary>
        public string Scope { get; private set; } = GlobalScope;

        public char ActiveSlot { get; private set; } = 'A';

        /// <summary>A/B 切换累计次数（面板显示用；切换本身也在录制里）。</summary>
        public int SlotSwitches { get; private set; }

        /// <summary>注入的覆盖/清除/预设事件总数（含滑条拖动产生的连续事件）。</summary>
        public int EventsInjected { get; private set; }

        /// <summary>最近一次保存/写回的人读说明（面板底部提示条用）。</summary>
        public string LastNote { get; private set; } = string.Empty;

        public TuningPanel(LabSession session, IReadOnlyList<IDataSource> sources, string? repoRoot = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _context = session.Context ?? throw new ArgumentException("调参面板需要带上下文的实时会话（脚本须是手感场景）", nameof(session));
            var feel = _context.World.Gameplay.Feel ?? throw new ArgumentException("调参面板需要手感装配（脚本须是手感场景）", nameof(session));
            _feel = new FeelSystemHandle(feel.Feel.Resolver);
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
            _repoRoot = NormalizePath(repoRoot ?? Directory.GetCurrentDirectory());
            var preset = _feel.Resolver.Calibration.BasePresetId;
            _slotA.Preset = preset;
            _slotB.Preset = preset;
        }

        // ---------------------------------------------------------------- 作用域

        /// <summary>可选作用域：全局、玩家、在场靶子（按出场顺序）。</summary>
        public IReadOnlyList<string> Scopes()
        {
            var list = new List<string> { GlobalScope, PlayerScope };
            foreach (var pair in _context.Dummies)
            {
                list.Add(pair.Key);
            }

            return list;
        }

        public void SetScope(string scope)
        {
            scope ??= GlobalScope;
            if (!IsLiveScope(scope))
            {
                throw new LabFormatException("作用域不存在：" + scope);
            }

            Scope = scope;
        }

        private bool IsLiveScope(string scope)
        {
            if (scope.Length == 0 || string.Equals(scope, PlayerScope, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var pair in _context.Dummies)
            {
                if (string.Equals(pair.Key, scope, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>靶子被清掉后，丢弃两个槽位里作用于已不在场靶子的覆盖（它们已无处生效）。</summary>
        public void PruneDummyScopes()
        {
            foreach (var slot in new[] { _slotA, _slotB })
            {
                slot.Writes.RemoveAll(w => w.Unit.Length > 0 && !string.Equals(w.Unit, PlayerScope, StringComparison.Ordinal) && !IsLiveScope(w.Unit));
            }

            if (!IsLiveScope(Scope))
            {
                Scope = GlobalScope;
            }
        }

        private Id UnitOf(string scope)
        {
            if (scope.Length == 0 || string.Equals(scope, PlayerScope, StringComparison.Ordinal))
            {
                return _context.PlayerId;
            }

            return _context.FindByLabel(scope) ?? throw new LabFormatException("作用域不存在：" + scope);
        }

        // ---------------------------------------------------------------- 取值（行视图）

        public IReadOnlyList<FeelGroup> Groups { get; } = new[]
        {
            FeelGroup.Input, FeelGroup.Movement, FeelGroup.Action, FeelGroup.Reaction, FeelGroup.Camera, FeelGroup.Effects, FeelGroup.Audio,
        };

        /// <summary>分组的中文名（与 05 第 3.1 节七个分组一致）。</summary>
        public static string GroupLabel(FeelGroup group)
        {
            switch (group)
            {
                case FeelGroup.Input: return "输入";
                case FeelGroup.Movement: return "移动";
                case FeelGroup.Action: return "动作";
                case FeelGroup.Reaction: return "受击";
                case FeelGroup.Camera: return "镜头";
                case FeelGroup.Effects: return "特效";
                case FeelGroup.Audio: return "音频";
                default: return group.ToString();
            }
        }

        /// <summary>某分组的全部字段行（登记顺序）。</summary>
        public IReadOnlyList<TuningRow> Rows(FeelGroup group)
        {
            var rows = new List<TuningRow>();
            for (var i = 0; i < Fields.Count; i++)
            {
                if (Fields[i].Group == group)
                {
                    rows.Add(Row(Fields[i].Name));
                }
            }

            return rows;
        }

        /// <summary>全部字段行（先按分组再按登记顺序）。</summary>
        public IReadOnlyList<TuningRow> AllRows()
        {
            var rows = new List<TuningRow>();
            foreach (var group in Groups)
            {
                rows.AddRange(Rows(group));
            }

            return rows;
        }

        public TuningRow Row(string field)
        {
            var def = Fields.Get(field);
            var resolved = _feel.Resolver.Resolve(UnitOf(Scope));
            var raw = resolved.GetRaw(field);
            var absolute = resolved.GetAbsolute(field);
            int? ticks = def.HasTicks && !raw.IsNone ? resolved.GetTicks(field) : (int?)null;
            var provenance = resolved.GetProvenance(field);
            var layer = 0;
            var sourceId = string.Empty;
            var clamped = false;
            foreach (var entry in provenance)
            {
                if (entry.Layer == 0)
                {
                    clamped = true;
                    continue;
                }

                layer = entry.Layer;
                sourceId = entry.SourceId;
            }

            var writes = new List<string>();
            foreach (var w in ActiveWrites)
            {
                if (string.Equals(w.Field, field, StringComparison.Ordinal))
                {
                    writes.Add(w.ToString());
                }
            }

            var pending = false;
            foreach (var w in ActiveWrites)
            {
                if (string.Equals(w.Field, field, StringComparison.Ordinal)
                    && _pendingStamp.TryGetValue(StampKey(w.Unit, field), out var stamp) && stamp >= _session.Tick)
                {
                    pending = true;
                }
            }

            var absoluteText = string.Empty;
            if (ticks.HasValue)
            {
                absoluteText = ticks.Value.ToString(CultureInfo.InvariantCulture) + " tick";
            }
            else if (def.IsRelativeQuantity && absolute.Kind == FeelValueKind.Number && !raw.IsNone && absolute != raw)
            {
                absoluteText = "= " + FormatValue(absolute) + " " + AbsoluteUnit(def.Unit);
            }

            return new TuningRow(
                def, def.StatusNote ?? string.Empty, raw, absolute, ticks, FormatValue(raw), absoluteText, UnitText(def.Unit), RangeText(def),
                provenance, layer, sourceId, clamped, writes.Count > 0, string.Join("；", writes), pending);
        }

        private static string StampKey(string scope, string field) => scope + "|" + field;

        // ---------------------------------------------------------------- 当前槽位的覆盖组

        private Slot Active => ActiveSlot == 'A' ? _slotA : _slotB;

        /// <summary>当前槽位的覆盖（有序、同作用域同字段至多一条）。</summary>
        public IReadOnlyList<OverrideWrite> ActiveWrites => Active.Writes;

        public IReadOnlyList<OverrideWrite> WritesOf(char slot) => (slot == 'A' ? _slotA : _slotB).Writes;

        /// <summary>槽位当前的基础预设。</summary>
        public string PresetOf(char slot) => (slot == 'A' ? _slotA : _slotB).Preset;

        public string ActivePreset => Active.Preset;

        // ---------------------------------------------------------------- 改值

        /// <summary>
        /// 校验一次覆盖：字段已登记、操作被该字段允许、值种类与字段一致、数值 set 在登记范围内、枚举取值在取值集合内、列表元素非空且不含逗号。
        /// 返回问题说明（空串 = 通过）。
        /// </summary>
        public string Validate(string field, FeelOp op, FeelValue value)
        {
            if (!Fields.TryGet(field, out var def))
            {
                return "手感字段 " + field + " 未登记";
            }

            if (!def.Allows(op))
            {
                return "字段 " + field + " 不允许操作 " + FeelProvenanceOps.FromOp(op);
            }

            switch (def.Kind)
            {
                case FeelFieldKind.Number:
                case FeelFieldKind.Int:
                    if (value.Kind != FeelValueKind.Number)
                    {
                        return "字段 " + field + " 需要数值";
                    }

                    var number = value.AsNumber();
                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        return "字段 " + field + " 的取值必须是有限数";
                    }

                    if (op == FeelOp.Set && ((def.Min.HasValue && number < def.Min.Value) || (def.Max.HasValue && number > def.Max.Value)))
                    {
                        return "字段 " + field + " 的取值 " + FormatValue(value) + " 超出范围 " + RangeText(def);
                    }

                    return string.Empty;
                case FeelFieldKind.Bool:
                    return value.Kind == FeelValueKind.Bool ? string.Empty : "字段 " + field + " 需要布尔值";
                case FeelFieldKind.Enum:
                    if (value.Kind != FeelValueKind.Text)
                    {
                        return "字段 " + field + " 需要枚举文本";
                    }

                    return def.EnumValues != null && def.EnumValues.Contains(value.AsText())
                        ? string.Empty
                        : "字段 " + field + " 的取值 " + value.AsText() + " 不在 [" + string.Join("|", def.EnumValues ?? new string[0]) + "] 内";
                case FeelFieldKind.List:
                    if (value.Kind == FeelValueKind.List)
                    {
                        foreach (var item in value.AsList())
                        {
                            if (item.Length == 0 || item.IndexOf(',') >= 0) return "列表字段 " + field + " 的元素不能为空、不能含逗号";
                        }

                        return string.Empty;
                    }

                    if (value.Kind == FeelValueKind.Text && value.AsText().Length > 0 && value.AsText().IndexOf(',') < 0)
                    {
                        return string.Empty;
                    }

                    return "列表字段 " + field + " 需要列表（或单个非空元素）";
                default:
                    return value.Kind == FeelValueKind.Text && value.AsText().Length > 0 ? string.Empty : "字段 " + field + " 需要非空文本";
            }
        }

        /// <summary>改一个字段（在当前作用域）。校验失败抛 <see cref="LabFormatException"/>（不静默吞掉）。</summary>
        public void SetValue(string field, FeelValue value, FeelOp op = FeelOp.Set) => SetValue(Scope, field, value, op);

        public void SetValue(string scope, string field, FeelValue value, FeelOp op = FeelOp.Set)
        {
            scope ??= GlobalScope;
            var problem = Validate(field, op, value);
            if (problem.Length > 0)
            {
                throw new LabFormatException(problem);
            }

            if (!IsLiveScope(scope))
            {
                throw new LabFormatException("作用域不存在：" + scope);
            }

            var write = new OverrideWrite(field, op, value, scope);
            Inject(LabOverrideCodec.Encode(Fields, write.ToFeelWrite(), scope));
            Active.Writes.RemoveAll(w => Same(w, scope, field));
            Active.Writes.Add(write);
            _pendingStamp[StampKey(scope, field)] = _session.Tick;
        }

        /// <summary>清掉当前作用域里某字段的覆盖（回到来源层的取值）。</summary>
        public void Clear(string field) => Clear(Scope, field);

        public void Clear(string scope, string field)
        {
            scope ??= GlobalScope;
            if (!Fields.Contains(field))
            {
                throw new LabFormatException("手感字段 " + field + " 未登记");
            }

            if (Active.Writes.RemoveAll(w => Same(w, scope, field)) == 0)
            {
                return;
            }

            Inject(LabOverrideCodec.EncodeClear(field, scope));
            _pendingStamp[StampKey(scope, field)] = _session.Tick;
        }

        /// <summary>恢复基准：清空覆盖层（当前槽位的预设不变）。覆盖层为空后解析路径与正式运行完全一致（06 第 3.4 节）。</summary>
        public void RestoreBaseline()
        {
            Active.Writes.Clear();
            Inject(LabOverrideCodec.EncodeClear(string.Empty, string.Empty));
            _pendingStamp.Clear();
        }

        private static bool Same(OverrideWrite w, string scope, string field) =>
            string.Equals(w.Unit, scope, StringComparison.Ordinal) && string.Equals(w.Field, field, StringComparison.Ordinal);

        /// <summary>当前槽位里该字段（全局作用域）是否被覆盖成某值。</summary>
        public bool HasGlobalSet(string field, double number)
        {
            foreach (var w in Active.Writes)
            {
                if (w.Unit.Length == 0 && w.Op == FeelOp.Set && string.Equals(w.Field, field, StringComparison.Ordinal)
                    && w.Value.Kind == FeelValueKind.Number && w.Value.AsNumber() == number)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>顿帧开关（热键 F8）：开 = 两个顿帧时长字段没有被置零的全局覆盖。关 = 把攻击方/受击方顿帧时长置 0（逻辑覆盖，录进脚本，按槽位各自保存）。</summary>
        public bool HitStopOn =>
            !(HasGlobalSet(FeelFieldNames.AttackerHitstopMs, 0.0) && HasGlobalSet(FeelFieldNames.TargetHitstopMs, 0.0));

        public void SetHitStop(bool on)
        {
            if (on == HitStopOn)
            {
                return;
            }

            foreach (var field in new[] { FeelFieldNames.AttackerHitstopMs, FeelFieldNames.TargetHitstopMs })
            {
                if (on)
                {
                    Clear(GlobalScope, field);
                }
                else
                {
                    SetValue(GlobalScope, field, FeelValue.Of(0.0));
                }
            }
        }

        // ---------------------------------------------------------------- 预设与槽位

        /// <summary>切基础预设（写进当前槽位）。</summary>
        public void SetPreset(string presetId)
        {
            if (string.IsNullOrEmpty(presetId))
            {
                throw new ArgumentException("预设 id 不能为空", nameof(presetId));
            }

            if (_feel.Profiles.GetPreset(presetId) == null)
            {
                throw new LabFormatException("预设不存在：" + presetId);
            }

            Inject(new ScriptEvent(0, presetId, ScriptEventKind.Preset));
            Active.Preset = presetId;
        }

        /// <summary>
        /// 给"不在场上"的槽位预置基础预设（不产生事件：槽位的预设只在切换时才经预设事件落进录制）。
        /// 用于开局让 B 槽默认是另一个预设，使 A/B 一开始就有可比较的对象；当前槽位不可预置（要改当前槽位用 <see cref="SetPreset"/>）。
        /// </summary>
        public void PrimeInactiveSlotPreset(string presetId)
        {
            if (_feel.Profiles.GetPreset(presetId) == null)
            {
                throw new LabFormatException("预设不存在：" + presetId);
            }

            (ActiveSlot == 'A' ? _slotB : _slotA).Preset = presetId;
        }

        /// <summary>A/B 瞬间切换：预设不同则切预设，清空覆盖后重注入目标槽位的覆盖；切换本身在会话日志里。</summary>
        public void SwitchSlot()
        {
            var nextSlot = ActiveSlot == 'A' ? 'B' : 'A';
            var target = nextSlot == 'A' ? _slotA : _slotB;
            if (!string.Equals(target.Preset, Active.Preset, StringComparison.Ordinal))
            {
                Inject(new ScriptEvent(0, target.Preset, ScriptEventKind.Preset));
            }

            Inject(LabOverrideCodec.EncodeClear(string.Empty, string.Empty));
            _pendingStamp.Clear();
            target.Writes.RemoveAll(w => w.Unit.Length > 0 && !IsLiveScope(w.Unit));
            foreach (var w in target.Writes)
            {
                Inject(LabOverrideCodec.Encode(Fields, w.ToFeelWrite(), w.Unit));
                _pendingStamp[StampKey(w.Unit, w.Field)] = _session.Tick;
            }

            ActiveSlot = nextSlot;
            SlotSwitches++;
        }

        /// <summary>两个槽位的覆盖组导出成覆盖存储（命名 A/B），可存本地文件（沿用既有覆盖存储格式）。</summary>
        public OverrideStore ExportSlots()
        {
            var store = new OverrideStore();
            foreach (var pair in new[] { new KeyValuePair<string, Slot>("A", _slotA), new KeyValuePair<string, Slot>("B", _slotB) })
            {
                var set = new OverrideSet(pair.Key);
                set.Writes.AddRange(pair.Value.Writes);
                store.Upsert(set);
            }

            return store;
        }

        private void Inject(ScriptEvent e)
        {
            _session.Inject(e);
            EventsInjected++;
        }

        // ---------------------------------------------------------------- 值的计算与格式化

        /// <summary>覆盖操作作用到一个值上（与第 8 层的语义一致：数值先算后按范围限幅、整数先取整）；基值无值时只有 set 有意义。</summary>
        internal static FeelValue Apply(FeelFieldDef def, FeelValue baseValue, FeelWrite write)
        {
            switch (def.Kind)
            {
                case FeelFieldKind.Number:
                case FeelFieldKind.Int:
                {
                    var operand = write.Value.AsNumber();
                    double result;
                    if (write.Op == FeelOp.Set)
                    {
                        result = operand;
                    }
                    else
                    {
                        if (baseValue.Kind != FeelValueKind.Number)
                        {
                            return FeelValue.None;
                        }

                        result = write.Op == FeelOp.Multiply ? baseValue.AsNumber() * operand : baseValue.AsNumber() + operand;
                    }

                    if (def.Kind == FeelFieldKind.Int)
                    {
                        result = Math.Round(result, MidpointRounding.AwayFromZero);
                    }

                    if (def.Min.HasValue && result < def.Min.Value) result = def.Min.Value;
                    if (def.Max.HasValue && result > def.Max.Value) result = def.Max.Value;
                    return FeelValue.Of(result);
                }

                case FeelFieldKind.List:
                {
                    var items = baseValue.Kind == FeelValueKind.List ? new List<string>(baseValue.AsList()) : new List<string>();
                    if (write.Op == FeelOp.Set)
                    {
                        return write.Value.Kind == FeelValueKind.List ? write.Value : FeelValue.OfList(new[] { write.Value.AsText() });
                    }

                    var item = write.Value.Kind == FeelValueKind.List ? string.Join(",", write.Value.AsList()) : write.Value.AsText();
                    if (write.Op == FeelOp.Add)
                    {
                        if (!items.Contains(item)) items.Add(item);
                    }
                    else
                    {
                        items.Remove(item);
                    }

                    return FeelValue.OfList(items);
                }

                default:
                    return write.Value;
            }
        }

        public static string FormatValue(FeelValue value)
        {
            switch (value.Kind)
            {
                case FeelValueKind.None: return "—";
                case FeelValueKind.Number: return Math.Round(value.AsNumber(), 6).ToString("0.######", CultureInfo.InvariantCulture);
                case FeelValueKind.Bool: return value.AsBool() ? "是" : "否";
                case FeelValueKind.Text: return value.AsText();
                case FeelValueKind.List: return string.Join(",", value.AsList());
                default: return value.ToString();
            }
        }

        public static string UnitText(FeelUnit unit)
        {
            switch (unit)
            {
                case FeelUnit.Milliseconds: return "毫秒";
                case FeelUnit.BodyHeights: return "身高倍数";
                case FeelUnit.BaseSpeedSeconds: return "基础移速秒数";
                case FeelUnit.BaseSpeedRatio: return "基础移速倍数";
                case FeelUnit.DegreesPerSecond: return "度/秒";
                case FeelUnit.Degrees: return "度";
                case FeelUnit.Ratio: return "倍率";
                case FeelUnit.ClipRatio: return "剪辑比例";
                case FeelUnit.ScreenHeightRatio: return "画面高度比";
                case FeelUnit.IntensityTier: return "强度档";
                case FeelUnit.Count: return "个";
                default: return string.Empty;
            }
        }

        private static string AbsoluteUnit(FeelUnit unit)
        {
            switch (unit)
            {
                case FeelUnit.BodyHeights:
                case FeelUnit.ScreenHeightRatio: return "世界单位";
                case FeelUnit.BaseSpeedRatio: return "世界单位/秒";
                case FeelUnit.BaseSpeedSeconds: return "世界单位";
                default: return string.Empty;
            }
        }

        public static string RangeText(FeelFieldDef def)
        {
            switch (def.Kind)
            {
                case FeelFieldKind.Number:
                case FeelFieldKind.Int:
                    return "[" + (def.Min.HasValue ? FormatValue(FeelValue.Of(def.Min.Value)) : "-∞") + ", " + (def.Max.HasValue ? FormatValue(FeelValue.Of(def.Max.Value)) : "+∞") + "]";
                case FeelFieldKind.Enum:
                    return string.Join("|", def.EnumValues ?? new string[0]);
                case FeelFieldKind.Bool:
                    return "是|否";
                default:
                    return string.Empty;
            }
        }

        // ---------------------------------------------------------------- 保存为预设

        /// <summary>保存为预设的结果。</summary>
        public sealed class SavedPreset
        {
            public string Path { get; }

            public string RowId { get; }

            /// <summary>新行里写入的字段数（即有效覆盖数）。</summary>
            public int WrittenFields { get; }

            /// <summary>没写进新预设的覆盖及原因（作用于靶子的覆盖、基值缺失的相对运算）。</summary>
            public IReadOnlyList<string> Skipped { get; }

            internal SavedPreset(string path, string rowId, int written, IReadOnlyList<string> skipped)
            {
                Path = path;
                RowId = rowId;
                WrittenFields = written;
                Skipped = skipped;
            }
        }

        /// <summary>
        /// 把当前槽位的覆盖生成一个新预设行并写进 <c>feel.preset</c> 表文件（<paramref name="directory"/>/feel/feel.preset.json，本地输出目录，不进仓库）：
        /// 新行 <c>extends</c> 当前槽位的基础预设，<c>values</c> 只含被覆盖的字段（把覆盖操作作用在基础预设取值上）。
        /// 这个文件本身就是一个合法的数据根，放进脚本的额外数据根即可被预设列表选到。
        /// 局限（写进判断记录）：新值是"覆盖作用在基础预设上"的结果，不计入武器/体型等上层对同一字段的改写；作用于靶子的覆盖不写入。
        /// </summary>
        public SavedPreset SaveAsPreset(string name, string directory)
        {
            if (string.IsNullOrEmpty(name) || !IsSlug(name))
            {
                throw new ArgumentException("预设名只能含小写字母、数字与下划线：" + name, nameof(name));
            }

            var baseId = Active.Preset;
            if (_feel.Profiles.GetPreset(baseId) == null)
            {
                throw new LabFormatException("基础预设不存在：" + baseId);
            }

            var baseValues = _feel.Profiles.EffectivePresetValues(baseId, out _);
            var values = new JsonObjectBuilder();
            var skipped = new List<string>();
            var written = 0;
            var chosen = new Dictionary<string, FeelValue>(StringComparer.Ordinal);
            foreach (var w in Active.Writes)
            {
                if (w.Unit.Length > 0 && !string.Equals(w.Unit, PlayerScope, StringComparison.Ordinal))
                {
                    skipped.Add(w.ToString() + "（作用于靶子，不写入预设）");
                    continue;
                }

                var def = Fields.Get(w.Field);
                var current = chosen.TryGetValue(w.Field, out var prior) ? prior : baseValues[Fields.IndexOf(w.Field)];
                var applied = Apply(def, current, w.ToFeelWrite());
                if (applied.IsNone)
                {
                    skipped.Add(w.ToString() + "（基础预设没有该字段的取值，相对运算无从算起）");
                    continue;
                }

                chosen[w.Field] = applied;
            }

            for (var i = 0; i < Fields.Count; i++)
            {
                if (chosen.TryGetValue(Fields[i].Name, out var v))
                {
                    values.Add(Fields[i].Name, OverrideStore.ValueToJson(v));
                    written++;
                }
            }

            var rowId = "feel.preset." + name;
            var row = new JsonObjectBuilder()
                .Add("id", LabJson.Str(rowId))
                .Add("description", LabJson.Str("调参面板保存的预设：在 " + baseId + " 上叠加 " + written + " 个覆盖（实验室产物，未经验证）。"))
                .Add("extends", LabJson.Str(baseId))
                .Add("maturity", LabJson.Str("experimental"))
                .Add("profile_version", LabJson.Num(1))
                .Add("values", values.Build())
                .Build();

            // 输出目录本身是一个数据根（<directory>/feel/feel.preset.json，文件名必须等于表名）；同目录再存时按 id 替换或追加，不覆盖别的预设。
            var path = System.IO.Path.Combine(directory, "feel", FeelTables.Preset + ".json");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var rows = new List<JsonValue>();
            if (File.Exists(path))
            {
                var existing = LabJson.ParseObject(File.ReadAllText(path, Encoding.UTF8), path);
                foreach (var item in LabJson.RequireArray(existing, "rows", path))
                {
                    if (!(item is JsonObject o && o.TryGetValue("id", out var idv) && idv is JsonString ids && ids.Value == rowId))
                    {
                        rows.Add(item);
                    }
                }
            }

            rows.Add(row);
            var table = new JsonObjectBuilder()
                .Add("table", LabJson.Str(FeelTables.Preset))
                .Add("schema_version", LabJson.Num(1))
                .Add("rows", new JsonArray(rows))
                .Build();
            File.WriteAllText(path, LabJson.Write(table), new UTF8Encoding(false));
            LastNote = "已保存预设 " + rowId + "（" + written + " 个字段）→ " + path;
            return new SavedPreset(path, rowId, written, skipped);
        }

        private static bool IsSlug(string name)
        {
            foreach (var c in name)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        // ---------------------------------------------------------------- 写回数据表

        /// <summary>
        /// 为当前槽位的覆盖生成"写回数据表"的计划：每个覆盖按来源链找到最后写它的数据行，算出该行应有取值并对数据表原文做外科式编辑。
        /// 不改任何文件；调用方把 <see cref="WriteBackPlan.Diff"/> 写到本地输出目录，人工评审后走正常提交。
        /// <para>
        /// 规则：来源是 <c>set</c> → 行里的值改成覆盖后的取值；<c>multiply</c> → 行里的系数 = 目标值 / 该行应用前的值；<c>add</c> → 行里的加数 = 目标值 − 该行应用前的值；
        /// 没有任何数据行写过该字段（可选字段）→ 写进基础预设行的 <c>values</c>。无法自动写回的（光环/临时状态来源、列表的 add/remove、除数为 0、
        /// 同一字段在多个作用域有覆盖、找不到数据文件）列入 <see cref="WriteBackPlan.Manual"/>，不静默丢弃。
        /// 注意：数据行的改动对所有引用该行的单位生效，不只是覆盖当时作用的那个单位。
        /// </para>
        /// </summary>
        public WriteBackPlan BuildWriteBack()
        {
            var plan = new WriteBackPlan();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in Active.Writes)
            {
                var def = Fields.Get(w.Field);
                if (!seen.Add(w.Field))
                {
                    plan.Manual.Add(w.ToString() + "：同一字段在多个作用域有覆盖，需要人工取舍写回哪一个");
                    continue;
                }

                var resolved = _feel.Resolver.Resolve(UnitOf(w.Unit));
                var provenance = resolved.GetProvenance(w.Field);
                FeelProvenanceEntry? writer = null;
                var before = FeelValue.None;
                foreach (var entry in provenance)
                {
                    if (entry.SourceId.StartsWith("debug:", StringComparison.Ordinal) || entry.Layer == 0)
                    {
                        continue;
                    }

                    writer = entry;
                    before = entry.ValueAfter;
                }

                var target = Apply(def, before, w.ToFeelWrite());
                if (target.IsNone)
                {
                    plan.Manual.Add(w.ToString() + "：没有来源取值可供相对运算");
                    continue;
                }

                string table;
                string rowId;
                string newText;
                var viaPreset = false;
                if (writer == null)
                {
                    rowId = Active.Preset;
                    table = FeelTables.Preset;
                    viaPreset = true;
                    newText = JsonText(def, target);
                }
                else
                {
                    if (writer.Layer == (int)FeelLayer.Temporary)
                    {
                        plan.Manual.Add(w.ToString() + "：来源是临时状态（" + writer.SourceId + "），不是数据表行，需要改光环定义");
                        continue;
                    }

                    rowId = writer.SourceId;
                    table = TableOf(rowId);
                    viaPreset = string.Equals(table, FeelTables.Preset, StringComparison.Ordinal);
                    if (!TryOperand(def, writer, target, out var operand, out var why))
                    {
                        plan.Manual.Add(w.ToString() + "：" + why);
                        continue;
                    }

                    newText = JsonText(def, operand);
                }

                if (!TryPatch(plan, w, def, table, rowId, viaPreset, writer?.Op ?? FeelProvenanceOps.Set, newText, writer == null))
                {
                    continue;
                }
            }

            return plan;
        }

        /// <summary>计划写成差异文件（本地输出目录）；有人工项时同目录另写 <c>.manual.txt</c>。返回差异文件路径。</summary>
        public string WriteBack(string path)
        {
            var plan = BuildWriteBack();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
            File.WriteAllText(path, plan.Diff(), new UTF8Encoding(false));
            if (plan.Manual.Count > 0)
            {
                File.WriteAllText(path + ".manual.txt", string.Join("\n", plan.Manual) + "\n", new UTF8Encoding(false));
            }

            LastNote = "写回差异（" + plan.Edits.Count + " 项" + (plan.Manual.Count > 0 ? "，" + plan.Manual.Count + " 项需人工" : string.Empty) + "）→ " + path;
            return path;
        }

        private static string TableOf(string rowId)
        {
            var first = rowId.IndexOf('.');
            var second = first < 0 ? -1 : rowId.IndexOf('.', first + 1);
            return second < 0 ? rowId : rowId.Substring(0, second);
        }

        private static bool TryOperand(FeelFieldDef def, FeelProvenanceEntry writer, FeelValue target, out FeelValue operand, out string why)
        {
            operand = FeelValue.None;
            why = string.Empty;
            switch (writer.Op)
            {
                case FeelProvenanceOps.Set:
                    operand = target;
                    return true;
                case FeelProvenanceOps.Multiply:
                    if (writer.ValueBefore.Kind != FeelValueKind.Number || Math.Abs(writer.ValueBefore.AsNumber()) < 1e-12)
                    {
                        why = "来源行是 multiply 而应用前的值为 0，系数算不出，需要人工";
                        return false;
                    }

                    operand = FeelValue.Of(Tidy(target.AsNumber() / writer.ValueBefore.AsNumber(), writer.ValueBefore.AsNumber(), target.AsNumber(), true));
                    return true;
                case FeelProvenanceOps.Add:
                    if (writer.ValueBefore.Kind != FeelValueKind.Number)
                    {
                        why = "来源行是 add 而应用前没有取值，需要人工";
                        return false;
                    }

                    operand = FeelValue.Of(Tidy(target.AsNumber() - writer.ValueBefore.AsNumber(), writer.ValueBefore.AsNumber(), target.AsNumber(), false));
                    return true;
                default:
                    why = "来源行是列表的 " + writer.Op + " 操作，写回需要人工判断";
                    return false;
            }
        }

        /// <summary>系数/加数取最短仍能还原目标值的写法（避免 0.30000000000000004 这类浮点尾巴进数据表）。</summary>
        private static double Tidy(double operand, double before, double target, bool multiply)
        {
            for (var digits = 1; digits <= 15; digits++)
            {
                var rounded = double.Parse(operand.ToString("G" + digits, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                var back = multiply ? before * rounded : before + rounded;
                if (Math.Abs(back - target) <= 1e-9 * Math.Max(1.0, Math.Abs(target)))
                {
                    return rounded;
                }
            }

            return operand;
        }

        private static string JsonText(FeelFieldDef def, FeelValue value)
        {
            switch (value.Kind)
            {
                case FeelValueKind.Number:
                    var n = value.AsNumber();
                    if (n == Math.Floor(n) && Math.Abs(n) < 1e15)
                    {
                        return ((long)n).ToString(CultureInfo.InvariantCulture);
                    }

                    return n.ToString("R", CultureInfo.InvariantCulture);
                case FeelValueKind.Bool:
                    return value.AsBool() ? "true" : "false";
                case FeelValueKind.List:
                    var items = new List<string>();
                    foreach (var item in value.AsList())
                    {
                        items.Add(JsonWriter.Write(new JsonString(item), JsonWriterOptions.Default));
                    }

                    return "[" + string.Join(", ", items) + "]";
                default:
                    return JsonWriter.Write(new JsonString(value.AsText()), JsonWriterOptions.Default);
            }
        }

        private bool TryPatch(
            WriteBackPlan plan, OverrideWrite w, FeelFieldDef def, string table, string rowId, bool viaPreset, string op, string newValueText, bool insertIntoPreset)
        {
            if (!LocateRow(table, rowId, out var location, out var original))
            {
                plan.Manual.Add(w.ToString() + "：找不到数据行 " + rowId + "（表 " + table + "）的磁盘文件");
                return false;
            }

            var display = RelativePath(location);
            var current = plan.TryGetText(display, out var patchedSoFar) ? patchedSoFar : original;
            var root = TextPatch.Parse(current);
            var rows = root.Get("rows");
            JsonSpan? row = null;
            if (rows != null)
            {
                foreach (var candidate in rows.Items)
                {
                    if (string.Equals(candidate.Get("id")?.StringValue(current), rowId, StringComparison.Ordinal))
                    {
                        row = candidate;
                    }
                }
            }

            if (row == null)
            {
                plan.Manual.Add(w.ToString() + "：文件 " + display + " 里找不到行 " + rowId);
                return false;
            }

            string patched;
            string oldText;
            if (viaPreset)
            {
                var values = row.Get("values");
                if (values == null || values.Kind != 'o')
                {
                    plan.Manual.Add(w.ToString() + "：预设行 " + rowId + " 没有 values 对象");
                    return false;
                }

                var existing = values.Get(def.Name);
                if (existing != null)
                {
                    oldText = existing.Text(current);
                    patched = TextPatch.Replace(current, existing, newValueText);
                }
                else if (insertIntoPreset)
                {
                    oldText = "（无）";
                    patched = TextPatch.InsertMember(current, values, def.Name, newValueText);
                }
                else
                {
                    plan.Manual.Add(w.ToString() + "：预设行 " + rowId + " 的 values 里没有字段 " + def.Name);
                    return false;
                }
            }
            else
            {
                var writes = row.Get("writes");
                JsonSpan? item = null;
                if (writes != null)
                {
                    foreach (var candidate in writes.Items)
                    {
                        if (string.Equals(candidate.Get("field")?.StringValue(current), def.Name, StringComparison.Ordinal)
                            && string.Equals(candidate.Get("op")?.StringValue(current), op, StringComparison.Ordinal))
                        {
                            item = candidate;
                        }
                    }
                }

                var valueSpan = item?.Get("value");
                if (valueSpan == null)
                {
                    plan.Manual.Add(w.ToString() + "：行 " + rowId + " 的 writes 里没有字段 " + def.Name + " 的 " + op + " 写入（可能在副手写入里）");
                    return false;
                }

                oldText = valueSpan.Text(current);
                patched = TextPatch.Replace(current, valueSpan, newValueText);
            }

            if (string.Equals(oldText, newValueText, StringComparison.Ordinal))
            {
                return true;
            }

            plan.Track(display, original, patched);
            plan.Edits.Add(new WriteBackEdit(def.Name, w.Unit, display, rowId, oldText, newValueText, w.ToString()));
            return true;
        }

        /// <summary>在数据来源里找某行所在的磁盘文件（后来源覆盖先来源，取最后一个含该行的）。</summary>
        private bool LocateRow(string table, string rowId, out string location, out string original)
        {
            location = string.Empty;
            original = string.Empty;
            var found = false;
            foreach (var source in _sources)
            {
                foreach (var t in source.ListTables())
                {
                    if (!string.Equals(t.TableName, table, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string text;
                    try
                    {
                        text = File.Exists(t.Location) ? File.ReadAllText(t.Location, Encoding.UTF8) : t.ReadText();
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    JsonSpan root;
                    try
                    {
                        root = TextPatch.Parse(text);
                    }
                    catch (LabFormatException)
                    {
                        continue;
                    }

                    var rows = root.Get("rows");
                    if (rows == null)
                    {
                        continue;
                    }

                    foreach (var row in rows.Items)
                    {
                        if (string.Equals(row.Get("id")?.StringValue(text), rowId, StringComparison.Ordinal))
                        {
                            location = t.Location;
                            original = text;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }

        private string RelativePath(string location)
        {
            var full = NormalizePath(location);
            var root = _repoRoot.EndsWith("/", StringComparison.Ordinal) ? _repoRoot : _repoRoot + "/";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return System.IO.Path.GetFullPath(path).Replace('\\', '/');
            }
            catch (Exception)
            {
                return path.Replace('\\', '/');
            }
        }
    }
}
