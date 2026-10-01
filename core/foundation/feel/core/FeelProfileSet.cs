using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Feel
{
    /// <summary><c>feel</c> 数据域的八张表名与相关引用字段名。</summary>
    public static class FeelTables
    {
        public const string Preset = "feel.preset";
        public const string Archetype = "feel.archetype";
        public const string Weapon = "feel.weapon";
        public const string Character = "feel.character";
        public const string Action = "feel.action";
        public const string Calibration = "feel.calibration";
        public const string MotionModeRules = "feel.motion_mode_rules";
        public const string TagMap = "feel.tag_map";

        /// <summary>全部八张表，登记顺序。</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            Preset, Archetype, Weapon, Character, Action, Calibration, MotionModeRules, TagMap,
        };

        /// <summary>光环定义上的手感修饰字段（第 7 层来源）。</summary>
        public const string AuraDefTable = "skill.aura_def";

        public const string AuraFeelModifiersField = "feel_modifiers";
    }

    /// <summary>
    /// 一行手感档案（五张分层表与标签映射表的统一内存形状）。预设的 <see cref="Values"/> 是"全字段取值"（只有 set 语义）；
    /// 其余分层表只有 <see cref="Writes"/>（以及武器的 <see cref="OffhandWrites"/>），每条写入是一个 (字段, 操作, 值)。
    /// 列表保持数据里的原始顺序、<b>不去重</b>——同层重复写入由校验器报告，解析器按确定顺序处理。
    /// </summary>
    public sealed class FeelRow
    {
        public string Table { get; }

        public string Id { get; }

        /// <summary>预设的 <c>extends</c>（父预设行 id），其它表为 null。</summary>
        public string? Extends { get; }

        /// <summary>标签映射行的标签（形如 <c>gender:female</c>），其它表为 null。</summary>
        public string? Tag { get; }

        /// <summary>标签映射行引用的体型原型行 id，可空。</summary>
        public string? ArchetypeRef { get; }

        /// <summary>标签映射行的排序号（同层多个标签按 (Priority, Id) 升序应用，后应用者在 set 冲突时胜出）。</summary>
        public int Priority { get; }

        /// <summary>预设的全字段取值（登记顺序无关，字段名唯一）；非预设为空。</summary>
        public IReadOnlyList<FeelWrite> Values { get; }

        /// <summary>分层覆盖写入（原始顺序、含重复）。</summary>
        public IReadOnlyList<FeelWrite> Writes { get; }

        /// <summary>武器作为副手时生效的写入（只允许 <c>offhand_stackable</c> 字段的 add/multiply）。</summary>
        public IReadOnlyList<FeelWrite> OffhandWrites { get; }

        private FeelRow(
            string table, string id, string? extends, string? tag, string? archetypeRef, int priority,
            IReadOnlyList<FeelWrite> values, IReadOnlyList<FeelWrite> writes, IReadOnlyList<FeelWrite> offhandWrites)
        {
            Table = table;
            Id = id;
            Extends = extends;
            Tag = tag;
            ArchetypeRef = archetypeRef;
            Priority = priority;
            Values = values;
            Writes = writes;
            OffhandWrites = offhandWrites;
        }

        private static readonly IReadOnlyList<FeelWrite> NoWrites = Array.Empty<FeelWrite>();

        /// <summary>构造预设行：<paramref name="values"/> 里每个元素必须是 <see cref="FeelOp.Set"/>。</summary>
        public static FeelRow Preset(string id, string? extends, IEnumerable<FeelWrite> values) =>
            new FeelRow(FeelTables.Preset, Require(id), extends, null, null, 0, ToList(values), NoWrites, NoWrites);

        /// <summary>构造原型/角色/动作行。</summary>
        public static FeelRow Overlay(string table, string id, IEnumerable<FeelWrite> writes) =>
            new FeelRow(RequireOverlayTable(table), Require(id), null, null, null, 0, NoWrites, ToList(writes), NoWrites);

        /// <summary>构造武器行（含副手写入）。</summary>
        public static FeelRow Weapon(string id, IEnumerable<FeelWrite> writes, IEnumerable<FeelWrite>? offhandWrites = null) =>
            new FeelRow(FeelTables.Weapon, Require(id), null, null, null, 0, NoWrites, ToList(writes),
                offhandWrites == null ? NoWrites : ToList(offhandWrites));

        /// <summary>构造标签映射行。</summary>
        public static FeelRow TagMap(string id, string tag, string? archetypeRef, int priority, IEnumerable<FeelWrite>? writes = null) =>
            new FeelRow(FeelTables.TagMap, Require(id), null, tag ?? throw new ArgumentNullException(nameof(tag)),
                archetypeRef, priority, NoWrites, writes == null ? NoWrites : ToList(writes), NoWrites);

        private static string Require(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("行 id 不能为空", nameof(id));
            return id;
        }

        private static string RequireOverlayTable(string table)
        {
            if (table != FeelTables.Archetype && table != FeelTables.Character && table != FeelTables.Action && table != FeelTables.Weapon)
            {
                throw new ArgumentException("覆盖表必须是 archetype/weapon/character/action 之一：" + table, nameof(table));
            }
            return table;
        }

        private static IReadOnlyList<FeelWrite> ToList(IEnumerable<FeelWrite> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            return new List<FeelWrite>(items).ToArray();
        }

        public override string ToString() => Table + "[" + Id + "]";
    }

    /// <summary><c>feel.motion_mode_rules</c> 的一行：一个运动模式的三项缺省规则（手感设计/02 第 3.1 节）。</summary>
    public sealed class FeelMotionModeRule
    {
        public string Mode { get; }

        /// <summary>接受输入位移：<c>yes</c>/<c>no</c>/<c>by_profile</c>（按手感档案字段，如 action 模式按 <c>action_move_speed_ratio</c>）。</summary>
        public string AcceptsInputDisplacement { get; }

        /// <summary>允许转向：<c>yes</c>/<c>no</c>/<c>by_profile</c>（如 action 模式按 <c>action_turn_lock</c>）。</summary>
        public string AllowsTurn { get; }

        /// <summary>退出时保留动量：<c>none</c>（不适用）/<c>yes</c>/<c>no</c>/<c>by_profile</c>/<c>restore_previous_mode</c>（顿帧叠加态恢复原模式）。</summary>
        public string KeepsMomentumOnExit { get; }

        public FeelMotionModeRule(string mode, string acceptsInputDisplacement, string allowsTurn, string keepsMomentumOnExit)
        {
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            AcceptsInputDisplacement = acceptsInputDisplacement ?? throw new ArgumentNullException(nameof(acceptsInputDisplacement));
            AllowsTurn = allowsTurn ?? throw new ArgumentNullException(nameof(allowsTurn));
            KeepsMomentumOnExit = keepsMomentumOnExit ?? throw new ArgumentNullException(nameof(keepsMomentumOnExit));
        }
    }

    /// <summary>运动模式与规则取值的固定词汇（手感设计/02 第 3.1 节）。</summary>
    public static class FeelMotionModes
    {
        public static readonly IReadOnlyList<string> Modes = new[]
        {
            "grounded", "action", "forced", "staggered", "rooted", "frozen", "dead",
        };

        public static readonly IReadOnlyList<string> AcceptsInputValues = new[] { "yes", "no", "by_profile" };

        public static readonly IReadOnlyList<string> AllowsTurnValues = new[] { "yes", "no", "by_profile" };

        public static readonly IReadOnlyList<string> KeepsMomentumValues = new[] { "none", "yes", "no", "by_profile", "restore_previous_mode" };
    }

    /// <summary>
    /// 从数据登记表解析（或程序化构造）出的手感档案集合：解析器与校验器的统一输入。
    /// 解析是<b>宽容</b>的——形状不对的条目被跳过（数据登记表的校验已经报告），不抛异常。
    /// </summary>
    public sealed class FeelProfileSet
    {
        private readonly Dictionary<string, FeelRow> _byTableAndId = new Dictionary<string, FeelRow>(StringComparer.Ordinal);

        public FeelFieldSet Fields { get; }

        public IReadOnlyList<FeelRow> Presets { get; }

        public IReadOnlyList<FeelRow> Archetypes { get; }

        public IReadOnlyList<FeelRow> Weapons { get; }

        public IReadOnlyList<FeelRow> Characters { get; }

        public IReadOnlyList<FeelRow> Actions { get; }

        public IReadOnlyList<FeelRow> TagMaps { get; }

        public IReadOnlyList<FeelMotionModeRule> MotionModeRules { get; }

        /// <summary>解析成功的标定行（行 id 升序）。</summary>
        public IReadOnlyList<FeelCalibration> Calibrations { get; }

        /// <summary>标定表的行数（含形状不对而未能解析的行）。</summary>
        public int CalibrationRowCount { get; }

        /// <summary>任何一张手感表有数据（含标定表与运动模式规则表）。</summary>
        public bool HasAnyData =>
            Presets.Count + Archetypes.Count + Weapons.Count + Characters.Count + Actions.Count + TagMaps.Count
            + MotionModeRules.Count + CalibrationRowCount > 0;

        public FeelProfileSet(
            FeelFieldSet fields,
            IEnumerable<FeelRow> rows,
            IEnumerable<FeelMotionModeRule>? motionModeRules = null,
            IEnumerable<FeelCalibration>? calibrations = null,
            int? calibrationRowCount = null)
        {
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var presets = new List<FeelRow>();
            var archetypes = new List<FeelRow>();
            var weapons = new List<FeelRow>();
            var characters = new List<FeelRow>();
            var actions = new List<FeelRow>();
            var tagMaps = new List<FeelRow>();
            foreach (var row in rows)
            {
                if (row == null) throw new ArgumentException("行不能为 null", nameof(rows));
                var key = row.Table + "\u0001" + row.Id;
                if (_byTableAndId.ContainsKey(key)) throw new ArgumentException($"行重复：{row}", nameof(rows));
                _byTableAndId.Add(key, row);
                switch (row.Table)
                {
                    case FeelTables.Preset: presets.Add(row); break;
                    case FeelTables.Archetype: archetypes.Add(row); break;
                    case FeelTables.Weapon: weapons.Add(row); break;
                    case FeelTables.Character: characters.Add(row); break;
                    case FeelTables.Action: actions.Add(row); break;
                    case FeelTables.TagMap: tagMaps.Add(row); break;
                    default: throw new ArgumentException("未知手感表：" + row.Table, nameof(rows));
                }
            }

            Presets = presets;
            Archetypes = archetypes;
            Weapons = weapons;
            Characters = characters;
            Actions = actions;
            TagMaps = tagMaps;
            MotionModeRules = motionModeRules == null ? Array.Empty<FeelMotionModeRule>() : new List<FeelMotionModeRule>(motionModeRules).ToArray();
            var cals = calibrations == null ? new List<FeelCalibration>() : new List<FeelCalibration>(calibrations);
            cals.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            Calibrations = cals.ToArray();
            CalibrationRowCount = calibrationRowCount ?? cals.Count;
        }

        public FeelRow? Get(string table, string? id) =>
            id != null && _byTableAndId.TryGetValue(table + "\u0001" + id, out var row) ? row : null;

        public FeelRow? GetPreset(string? id) => Get(FeelTables.Preset, id);

        public FeelRow? GetArchetype(string? id) => Get(FeelTables.Archetype, id);

        public FeelRow? GetWeapon(string? id) => Get(FeelTables.Weapon, id);

        public FeelRow? GetCharacter(string? id) => Get(FeelTables.Character, id);

        public FeelRow? GetAction(string? id) => Get(FeelTables.Action, id);

        /// <summary>
        /// 沿 <c>extends</c> 链求预设的有效取值（子覆盖父；链中成环或缺父时在断点处停止——环与缺父由校验器报告）。
        /// 返回按字段登记顺序排列的数组，未设置的字段为 <see cref="FeelValue.None"/>；<paramref name="suppliers"/>
        /// 给出每个字段的最终供值预设行 id。
        /// </summary>
        public FeelValue[] EffectivePresetValues(string presetId, out string?[] suppliers)
        {
            var values = new FeelValue[Fields.Count];
            suppliers = new string?[Fields.Count];
            var chain = new List<FeelRow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cur = GetPreset(presetId);
            while (cur != null && seen.Add(cur.Id))
            {
                chain.Add(cur);
                cur = GetPreset(cur.Extends);
            }
            for (var c = chain.Count - 1; c >= 0; c--)
            {
                var row = chain[c];
                for (var k = 0; k < row.Values.Count; k++)
                {
                    var w = row.Values[k];
                    var idx = Fields.IndexOf(w.Field);
                    if (idx < 0) continue;
                    values[idx] = w.Value;
                    suppliers[idx] = row.Id;
                }
            }
            return values;
        }

        // ---------------------------------------------------------------- 从数据登记表解析

        /// <summary>
        /// 从数据登记表读取全部手感表。表不存在（schema 未注册）视为空表。
        /// </summary>
        public static FeelProfileSet FromRegistry(IDataRegistryView view, FeelFieldSet fields)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (fields == null) throw new ArgumentNullException(nameof(fields));

            var rows = new List<FeelRow>();
            foreach (var record in Records(view, FeelTables.Preset))
            {
                var values = new List<FeelWrite>();
                if (record.Raw.TryGetValue("values", out var vv) && vv is JsonObject obj)
                {
                    for (var i = 0; i < obj.Count; i++)
                    {
                        var key = obj[i].Key;
                        if (fields.TryGet(key, out var def) && FeelWriteParser.TryParseValue(def, obj[i].Value, out var value))
                        {
                            values.Add(new FeelWrite(key, FeelOp.Set, value));
                        }
                    }
                }
                rows.Add(FeelRow.Preset(record.Key, OptionalString(record, "extends"), values));
            }

            foreach (var table in new[] { FeelTables.Archetype, FeelTables.Weapon, FeelTables.Character, FeelTables.Action })
            {
                foreach (var record in Records(view, table))
                {
                    var writes = FeelWriteParser.ParseWrites(record.Raw, "writes", fields);
                    var offhand = table == FeelTables.Weapon ? FeelWriteParser.ParseWrites(record.Raw, "offhand_writes", fields) : null;
                    rows.Add(table == FeelTables.Weapon
                        ? FeelRow.Weapon(record.Key, writes, offhand)
                        : FeelRow.Overlay(table, record.Key, writes));
                }
            }

            foreach (var record in Records(view, FeelTables.TagMap))
            {
                var tag = OptionalString(record, "tag");
                if (tag == null) continue;
                var priority = record.TryGetInt("priority", out var p) ? (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, p)) : 0;
                rows.Add(FeelRow.TagMap(record.Key, tag, OptionalString(record, "archetype_ref"), priority,
                    FeelWriteParser.ParseWrites(record.Raw, "writes", fields)));
            }

            var modes = new List<FeelMotionModeRule>();
            foreach (var record in Records(view, FeelTables.MotionModeRules))
            {
                var mode = OptionalString(record, "mode");
                if (mode == null) continue;
                modes.Add(new FeelMotionModeRule(
                    mode,
                    OptionalString(record, "accepts_input_displacement") ?? "no",
                    OptionalString(record, "allows_turn") ?? "no",
                    OptionalString(record, "keeps_momentum_on_exit") ?? "none"));
            }

            var cals = new List<FeelCalibration>();
            var calCount = 0;
            foreach (var record in Records(view, FeelTables.Calibration))
            {
                calCount++;
                if (TryParseCalibration(record, out var cal)) cals.Add(cal);
            }

            return new FeelProfileSet(fields, rows, modes, cals, calCount);
        }

        private static IReadOnlyList<DataRecord> Records(IDataRegistryView view, string table)
        {
            return view.TryGetAll(table, out var records) ? records : Array.Empty<DataRecord>();
        }

        private static string? OptionalString(DataRecord record, string field) =>
            record.TryGetString(field, out var s) ? s : null;

        private static bool TryParseCalibration(DataRecord record, out FeelCalibration calibration)
        {
            calibration = null!;
            var preset = OptionalString(record, "base_preset");
            if (preset == null) return false;
            if (!record.TryGetNumber("reference_height", out var height)
                || !record.TryGetNumber("base_speed", out var speed)
                || !record.TryGetNumber("animation_fps", out var fps)
                || !record.TryGetNumber("reference_camera_height", out var camHeight)
                || !record.TryGetNumber("reference_zoom", out var zoom)
                || !record.TryGetNumber("pixels_per_unit", out var ppu)
                || !record.TryGetNumber("marker_tolerance_ms", out var tol))
            {
                return false;
            }
            try
            {
                calibration = new FeelCalibration(record.Key, preset, height, speed, fps, camHeight, zoom, ppu, tol);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 把数据里的写入条目（<c>{field, op, value}</c> 列表，用于五张分层表的 <c>writes</c>、武器的
    /// <c>offhand_writes</c> 与光环的 <c>feel_modifiers</c>）解析为 <see cref="FeelWrite"/>。宽容：形状不对的条目被跳过。
    /// 光环侧装配（上层模块）直接复用本类解析 <c>feel_modifiers</c>，不另写一份。
    /// </summary>
    public static class FeelWriteParser
    {
        public static FeelOp? ParseOp(string? text)
        {
            switch (text)
            {
                case "set": return FeelOp.Set;
                case "multiply": return FeelOp.Multiply;
                case "add": return FeelOp.Add;
                case "remove": return FeelOp.Remove;
                default: return null;
            }
        }

        /// <summary>解析 <paramref name="owner"/> 里名为 <paramref name="arrayField"/> 的写入数组；缺省或形状不对返回空。</summary>
        public static IReadOnlyList<FeelWrite> ParseWrites(JsonObject owner, string arrayField, FeelFieldSet fields)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (!owner.TryGetValue(arrayField, out var raw) || !(raw is JsonArray array)) return Array.Empty<FeelWrite>();
            return ParseArray(array, fields);
        }

        /// <summary>解析写入数组本身。</summary>
        public static IReadOnlyList<FeelWrite> ParseArray(JsonArray array, FeelFieldSet fields)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            var result = new List<FeelWrite>(array.Count);
            for (var i = 0; i < array.Count; i++)
            {
                if (TryParseWrite(array[i], fields, out var write)) result.Add(write);
            }
            return result;
        }

        public static bool TryParseWrite(JsonValue item, FeelFieldSet fields, out FeelWrite write)
        {
            write = default;
            if (!(item is JsonObject obj)) return false;
            if (!obj.TryGetValue("field", out var f) || !(f is JsonString fieldName)) return false;
            if (!obj.TryGetValue("op", out var o) || !(o is JsonString opText)) return false;
            var op = ParseOp(opText.Value);
            if (op == null) return false;
            if (!fields.TryGet(fieldName.Value, out var def)) return false;
            if (!obj.TryGetValue("value", out var v)) return false;
            if (!TryParseValue(def, v, out var value, op.Value)) return false;
            write = new FeelWrite(fieldName.Value, op.Value, value);
            return true;
        }

        /// <summary>
        /// 按字段类型把 JSON 值转成 <see cref="FeelValue"/>。<paramref name="op"/> 非空且为 add/remove 的列表字段，
        /// 值是要追加/删除的元素列表（同样是字符串数组）。
        /// </summary>
        public static bool TryParseValue(FeelFieldDef def, JsonValue raw, out FeelValue value, FeelOp? op = null)
        {
            value = default;
            switch (def.Kind)
            {
                case FeelFieldKind.Number:
                case FeelFieldKind.Int:
                    if (raw is JsonNumber n && double.IsFinite(n.Value))
                    {
                        value = FeelValue.Of(n.Value);
                        return true;
                    }
                    return false;
                case FeelFieldKind.Bool:
                    if (raw is JsonBool b)
                    {
                        value = FeelValue.Of(b.Value);
                        return true;
                    }
                    return false;
                case FeelFieldKind.Enum:
                case FeelFieldKind.Text:
                case FeelFieldKind.Id:
                    if (raw is JsonString s)
                    {
                        value = FeelValue.Of(s.Value);
                        return true;
                    }
                    return false;
                case FeelFieldKind.List:
                    if (raw is JsonArray arr)
                    {
                        var items = new List<string>(arr.Count);
                        for (var i = 0; i < arr.Count; i++)
                        {
                            if (!(arr[i] is JsonString es)) return false;
                            items.Add(es.Value);
                        }
                        value = FeelValue.OfList(items);
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }
    }
}
