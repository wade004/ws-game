using System;
using Core.Foundation.DataRegistry;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.Feel
{
    /// <summary>
    /// 手感解析器（手感设计/05 第 5 节）：八层覆盖、层内 set→multiply→add→remove、末尾取整与限幅、标定换算、
    /// 溯源、按单位缓存与版本号、动作开始快照。
    /// <para>
    /// 判断记录（纯函数与确定性）：解析结果只由 (档案集合, 标定, 步长, 提供者返回的实体只读状态) 决定——
    /// 不读时钟、不读随机、不依赖字典枚举顺序（所有遍历走字段登记顺序，标签映射按 (Priority, Id) 排序，
    /// 临时状态按键的序数序，层内操作按固定操作序）。<b>不线程安全</b>：与模拟步进同线程使用。
    /// </para>
    /// <para>
    /// 判断记录（层内操作序）：同层先 set、再 multiply、再 add、最后 remove（remove 只用于列表字段）。
    /// 同层同字段被同一行写两次是校验错误；不同来源（如主手与副手武器、两个标签映射、两个光环）在同层写同一字段
    /// 是允许的叠加场景，按"操作序 → 来源顺序"确定性应用（后应用者的 set 胜出）。
    /// </para>
    /// <para>
    /// 判断记录（违规写入的处理）：校验器是违规的第一道门；解析器作为第二道，对违规写入（操作不在登记集合内、
    /// 武器层写角色为主字段、体型层对武器为主字段做非 multiply、副手写非叠加字段、类型不符）<b>跳过并记入
    /// <see cref="ResolvedFeel.Diagnostics"/></b>，不抛异常也不静默生效。
    /// </para>
    /// <para>
    /// 判断记录（预设 extends 的溯源）：第 1 层每个字段只记录"最终供值的那一行预设"一条（子覆盖父的中间值不记），
    /// 因为 extends 是预设内部的合并，不是八层覆盖的一层。
    /// </para>
    /// </summary>
    public sealed class FeelResolver : IFeelResolver
    {
        private const string ResolverSource = "resolver";

        private readonly FeelFieldSet _fields;
        private readonly FeelCalibration _calibration;
        private readonly double _stepSeconds;
        private readonly FeelProviders _providers;
        private FeelProfileSet _profiles;

        private readonly Dictionary<Id, ResolvedFeel> _cache = new Dictionary<Id, ResolvedFeel>();
        private readonly Dictionary<Id, int> _versions = new Dictionary<Id, int>();
        private readonly Dictionary<Id, ResolvedFeel> _snapshots = new Dictionary<Id, ResolvedFeel>();

        public FeelFieldSet Fields => _fields;

        public FeelCalibration Calibration => _calibration;

        /// <summary>模拟固定步长（秒）。</summary>
        public double StepSeconds => _stepSeconds;

        /// <summary>当前使用的档案集合（热加载后为新集合）。</summary>
        public FeelProfileSet Profiles => _profiles;

        public FeelResolver(FeelProfileSet profiles, FeelCalibration calibration, double stepSeconds, FeelProviders? providers = null)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
            if (!double.IsFinite(stepSeconds) || stepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stepSeconds));
            _stepSeconds = stepSeconds;
            _fields = profiles.Fields;
            _providers = providers ?? new FeelProviders();
            if (_profiles.GetPreset(calibration.BasePresetId) == null)
            {
                throw new ArgumentException($"标定指定的基础预设 \"{calibration.BasePresetId}\" 不存在", nameof(calibration));
            }
        }

        // ------------------------------------------------------------------ 缓存与版本

        public ResolvedFeel Resolve(Id unitId)
        {
            if (_cache.TryGetValue(unitId, out var cached)) return cached;
            var version = (_versions.TryGetValue(unitId, out var v) ? v : 0) + 1;
            _versions[unitId] = version;
            var action = _providers.Action?.GetActionState(unitId) ?? FeelActionState.Idle;
            var resolved = Compute(unitId, version, action);
            _cache[unitId] = resolved;
            return resolved;
        }

        public JudgingFeelView ResolveJudging(Id unitId) => Resolve(unitId).Judging;

        public PresentingFeelView ResolvePresenting(Id unitId) => Resolve(unitId).Presenting;

        public void Invalidate(Id unitId, string reason)
        {
            if (string.IsNullOrEmpty(reason)) throw new ArgumentException("失效原因不能为空", nameof(reason));
            _cache.Remove(unitId);
        }

        public void InvalidateAll(string reason)
        {
            if (string.IsNullOrEmpty(reason)) throw new ArgumentException("失效原因不能为空", nameof(reason));
            _cache.Clear();
        }

        public int GetVersion(Id unitId) => Resolve(unitId).Version;

        public IReadOnlyList<FeelProvenanceEntry> GetProvenance(Id unitId, string field) =>
            Resolve(unitId).GetProvenance(field);

        /// <summary>
        /// 热加载：换入新的档案集合并使全部缓存失效。已挂在施法实例上的快照不受影响
        /// （手感设计/05 第 8 节"正在进行的动作沿用其开始时的快照"）。新集合必须仍含标定指定的基础预设。
        /// </summary>
        public void Reload(FeelProfileSet profiles)
        {
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            if (!ReferenceEquals(profiles.Fields, _fields)) throw new ArgumentException("热加载不能更换字段登记", nameof(profiles));
            if (profiles.GetPreset(_calibration.BasePresetId) == null)
            {
                throw new ArgumentException($"新档案集合缺少标定指定的基础预设 \"{_calibration.BasePresetId}\"", nameof(profiles));
            }
            _profiles = profiles;
            _cache.Clear();
        }

        // ------------------------------------------------------------------ 动作快照

        public ResolvedFeel BeginAction(Id unitId, Id castInstanceId, string? actionFeelRef)
        {
            var version = Resolve(unitId).Version;
            var snapshot = Compute(unitId, version, new FeelActionState(true, actionFeelRef));
            _snapshots[castInstanceId] = snapshot;
            return snapshot;
        }

        public ResolvedFeel? GetSnapshot(Id castInstanceId) =>
            _snapshots.TryGetValue(castInstanceId, out var s) ? s : null;

        public void EndAction(Id castInstanceId) => _snapshots.Remove(castInstanceId);

        /// <summary>当前挂着的快照数（测试与泄漏检查用）。</summary>
        public int SnapshotCount => _snapshots.Count;

        // ------------------------------------------------------------------ 计算

        private struct SourcedWrite
        {
            public readonly string Source;
            public readonly FeelWrite Write;

            public SourcedWrite(string source, FeelWrite write)
            {
                Source = source;
                Write = write;
            }
        }

        private sealed class Work
        {
            public readonly FeelValue[] Values;
            public readonly List<FeelProvenanceEntry>?[] Provenance;
            public readonly List<string> Diagnostics = new List<string>();

            public Work(int count)
            {
                Values = new FeelValue[count];
                Provenance = new List<FeelProvenanceEntry>?[count];
            }
        }

        private static readonly FeelOp[] OpOrder = { FeelOp.Set, FeelOp.Multiply, FeelOp.Add, FeelOp.Remove };

        private ResolvedFeel Compute(Id unitId, int version, FeelActionState action)
        {
            var n = _fields.Count;
            var work = new Work(n);

            // 第 1 层：基础预设。
            var presetId = _calibration.BasePresetId;
            var presetValues = _profiles.EffectivePresetValues(presetId, out var suppliers);
            for (var i = 0; i < n; i++)
            {
                if (presetValues[i].IsNone) continue;
                work.Values[i] = presetValues[i];
                Record(work, i, (int)FeelLayer.BasePreset, suppliers[i] ?? presetId, FeelProvenanceOps.Set, FeelValue.None, presetValues[i]);
            }

            var layer = new List<SourcedWrite>();

            // 第 2 层：体型原型。
            var archetypeRef = _providers.Body?.GetArchetypeRef(unitId);
            if (archetypeRef != null)
            {
                AddBodyWrites(layer, work, FeelTables.Archetype, archetypeRef);
            }
            Flush(work, FeelLayer.Archetype, layer);

            // 第 3 层：标签映射。
            var tags = _providers.Tags?.GetTags(unitId);
            if (tags != null && tags.Count > 0 && _profiles.TagMaps.Count > 0)
            {
                var matched = new List<FeelRow>();
                for (var i = 0; i < _profiles.TagMaps.Count; i++)
                {
                    var row = _profiles.TagMaps[i];
                    if (row.Tag != null && ContainsTag(tags, row.Tag)) matched.Add(row);
                }
                matched.Sort((a, b) =>
                {
                    var c = a.Priority.CompareTo(b.Priority);
                    return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
                });
                for (var i = 0; i < matched.Count; i++)
                {
                    if (matched[i].ArchetypeRef != null) AddBodyWrites(layer, work, FeelTables.Archetype, matched[i].ArchetypeRef!);
                    AddBodyWrites(layer, work, matched[i]);
                }
            }
            Flush(work, FeelLayer.TagMap, layer);

            // 第 4 层：武器（主手全部；副手只叠加打击层）。
            var mainRef = _providers.Equipment?.GetMainWeaponRef(unitId);
            if (mainRef != null)
            {
                var weapon = _profiles.GetWeapon(mainRef);
                if (weapon == null)
                {
                    work.Diagnostics.Add($"主手武器行 \"{mainRef}\" 不存在");
                }
                else
                {
                    for (var i = 0; i < weapon.Writes.Count; i++)
                    {
                        var w = weapon.Writes[i];
                        if (!_fields.TryGet(w.Field, out var def)) { work.Diagnostics.Add($"{weapon.Id}：字段 \"{w.Field}\" 未登记，已跳过"); continue; }
                        if (def.Composition == FeelComposition.CharacterPrimary)
                        {
                            work.Diagnostics.Add($"{weapon.Id}：字段 \"{w.Field}\" 是角色为主，武器层写入已忽略");
                            continue;
                        }
                        if (def.Composition == FeelComposition.AttackOverride && !action.InAction) continue;
                        layer.Add(new SourcedWrite(weapon.Id, w));
                    }
                }
            }
            var offRef = _providers.Equipment?.GetOffhandWeaponRef(unitId);
            if (offRef != null)
            {
                var weapon = _profiles.GetWeapon(offRef);
                if (weapon == null)
                {
                    work.Diagnostics.Add($"副手武器行 \"{offRef}\" 不存在");
                }
                else
                {
                    for (var i = 0; i < weapon.OffhandWrites.Count; i++)
                    {
                        var w = weapon.OffhandWrites[i];
                        if (!_fields.TryGet(w.Field, out var def)) { work.Diagnostics.Add($"{weapon.Id}：字段 \"{w.Field}\" 未登记，已跳过"); continue; }
                        if (!def.OffhandStackable || (w.Op != FeelOp.Add && w.Op != FeelOp.Multiply))
                        {
                            work.Diagnostics.Add($"{weapon.Id}：副手只允许对 offhand_stackable 字段做 add/multiply，\"{w.Field}\" {FeelProvenanceOps.FromOp(w.Op)} 已忽略");
                            continue;
                        }
                        layer.Add(new SourcedWrite(weapon.Id, w));
                    }
                }
            }
            Flush(work, FeelLayer.Weapon, layer);

            // 第 5 层：角色。
            var characterRef = _providers.Body?.GetCharacterRef(unitId);
            if (characterRef != null)
            {
                var row = _profiles.GetCharacter(characterRef);
                if (row == null) work.Diagnostics.Add($"角色行 \"{characterRef}\" 不存在");
                else AddAll(layer, row);
            }
            Flush(work, FeelLayer.Character, layer);

            // 第 6 层：当前动作。
            if (action.ActionFeelRef != null)
            {
                var row = _profiles.GetAction(action.ActionFeelRef);
                if (row == null) work.Diagnostics.Add($"动作行 \"{action.ActionFeelRef}\" 不存在");
                else AddAll(layer, row);
            }
            Flush(work, FeelLayer.Action, layer);

            // 第 7 层：临时状态（按键的序数序）。
            var entries = _providers.Temporary?.GetEntries(unitId);
            if (entries != null && entries.Count > 0)
            {
                var sorted = new List<FeelTemporaryEntry>(entries);
                sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                for (var i = 0; i < sorted.Count; i++)
                {
                    for (var k = 0; k < sorted[i].Writes.Count; k++)
                    {
                        layer.Add(new SourcedWrite(sorted[i].Key, StackWrite(sorted[i].Writes[k], sorted[i].Stacks)));
                    }
                }
            }
            Flush(work, FeelLayer.Temporary, layer);

            // 第 8 层：调试覆盖（先全局后单位）。
            if (_providers.Debug != null)
            {
                var global = _providers.Debug.GetGlobalOverrides();
                for (var i = 0; i < global.Count; i++) layer.Add(new SourcedWrite("debug:global", global[i]));
                var perUnit = _providers.Debug.GetUnitOverrides(unitId);
                for (var i = 0; i < perUnit.Count; i++) layer.Add(new SourcedWrite("debug:unit", perUnit[i]));
            }
            Flush(work, FeelLayer.Debug, layer);

            // 末尾：取整与限幅，并记录事实。
            var clamped = new List<string>();
            for (var i = 0; i < n; i++)
            {
                var def = _fields[i];
                var value = work.Values[i];
                if (value.Kind != FeelValueKind.Number) continue;
                var x = value.AsNumber();
                if (def.Kind == FeelFieldKind.Int)
                {
                    var r = Math.Round(x, MidpointRounding.AwayFromZero);
                    if (r != x)
                    {
                        var after = FeelValue.Of(r);
                        Record(work, i, 0, ResolverSource, FeelProvenanceOps.Round, value, after);
                        work.Values[i] = value = after;
                        x = r;
                    }
                }
                var min = def.Min!.Value;
                var max = def.Max!.Value;
                if (x < min || x > max)
                {
                    var after = FeelValue.Of(x < min ? min : max);
                    Record(work, i, 0, ResolverSource, FeelProvenanceOps.Clamp, value, after);
                    work.Values[i] = after;
                    clamped.Add(def.Name);
                }
            }

            // 标定。
            var absolute = new FeelValue[n];
            var ticks = new int[n];
            var provenance = new IReadOnlyList<FeelProvenanceEntry>[n];
            for (var i = 0; i < n; i++)
            {
                var def = _fields[i];
                var raw = work.Values[i];
                ticks[i] = -1;
                if (raw.Kind == FeelValueKind.Number)
                {
                    var abs = _calibration.ToAbsolute(def.Unit, raw.AsNumber());
                    absolute[i] = FeelValue.Of(abs);
                    if (def.HasTicks) ticks[i] = FeelCalibration.MillisecondsToTicks(abs, _stepSeconds);
                }
                else
                {
                    absolute[i] = raw;
                }
                provenance[i] = work.Provenance[i] == null ? Array.Empty<FeelProvenanceEntry>() : work.Provenance[i]!.ToArray();
            }

            return new ResolvedFeel(unitId, version, _fields, work.Values, absolute, ticks, provenance, clamped, work.Diagnostics);
        }

        private static bool ContainsTag(IReadOnlyList<string> tags, string tag)
        {
            for (var i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private void AddAll(List<SourcedWrite> layer, FeelRow row)
        {
            for (var i = 0; i < row.Writes.Count; i++) layer.Add(new SourcedWrite(row.Id, row.Writes[i]));
        }

        /// <summary>体型侧（第 2/3 层）：武器为主字段只允许 multiply。</summary>
        private void AddBodyWrites(List<SourcedWrite> layer, Work work, string table, string rowId)
        {
            var row = _profiles.Get(table, rowId);
            if (row == null)
            {
                work.Diagnostics.Add($"{table} 行 \"{rowId}\" 不存在");
                return;
            }
            AddBodyWrites(layer, work, row);
        }

        private void AddBodyWrites(List<SourcedWrite> layer, Work work, FeelRow row)
        {
            for (var i = 0; i < row.Writes.Count; i++)
            {
                var w = row.Writes[i];
                if (_fields.TryGet(w.Field, out var def) && def.Composition == FeelComposition.WeaponPrimary && w.Op != FeelOp.Multiply)
                {
                    work.Diagnostics.Add($"{row.Id}：字段 \"{w.Field}\" 是武器为主，体型层只允许 multiply，{FeelProvenanceOps.FromOp(w.Op)} 已忽略");
                    continue;
                }
                layer.Add(new SourcedWrite(row.Id, w));
            }
        }

        /// <summary>
        /// 条目层数叠加（手感设计/05 第 6 节，M2-B）：数值 <c>multiply</c> 连乘 n 次（逐次相乘，不用 <c>Math.Pow</c>，保证确定性），数值 <c>add</c> 累加 n 次
        /// （同样逐次累加）；<c>set</c> 与列表操作幂等，原样返回。层数为 1 时原样返回（既有行为逐位不变）。
        /// </summary>
        private static FeelWrite StackWrite(FeelWrite write, int stacks)
        {
            if (stacks <= 1 || write.Value.Kind != FeelValueKind.Number) return write;
            if (write.Op != FeelOp.Multiply && write.Op != FeelOp.Add) return write;
            var unit = write.Value.AsNumber();
            var total = write.Op == FeelOp.Multiply ? 1.0 : 0.0;
            for (var i = 0; i < stacks; i++) total = write.Op == FeelOp.Multiply ? total * unit : total + unit;
            return new FeelWrite(write.Field, write.Op, FeelValue.Of(total));
        }

        private void Flush(Work work, FeelLayer layerNumber, List<SourcedWrite> layer)
        {
            if (layer.Count == 0) return;
            for (var o = 0; o < OpOrder.Length; o++)
            {
                for (var i = 0; i < layer.Count; i++)
                {
                    if (layer[i].Write.Op == OpOrder[o]) ApplyOne(work, (int)layerNumber, layer[i].Source, layer[i].Write);
                }
            }
            layer.Clear();
        }

        private void ApplyOne(Work work, int layerNumber, string source, FeelWrite write)
        {
            var idx = _fields.IndexOf(write.Field);
            if (idx < 0)
            {
                work.Diagnostics.Add($"{source}：字段 \"{write.Field}\" 未登记，已跳过");
                return;
            }
            var def = _fields[idx];
            if (!def.Allows(write.Op))
            {
                work.Diagnostics.Add($"{source}：字段 \"{write.Field}\" 不允许 {FeelProvenanceOps.FromOp(write.Op)}，已忽略");
                return;
            }

            var before = work.Values[idx];
            FeelValue after;
            switch (write.Op)
            {
                case FeelOp.Set:
                    if (!TypeMatches(def, write.Value))
                    {
                        work.Diagnostics.Add($"{source}：字段 \"{write.Field}\" 的 set 值类型不符（{write.Value.Kind}），已忽略");
                        return;
                    }
                    if (def.Kind == FeelFieldKind.Enum && !ContainsOrdinal(def.EnumValues!, write.Value.AsText()))
                    {
                        work.Diagnostics.Add($"{source}：字段 \"{write.Field}\" 的枚举值 \"{write.Value}\" 非法，已忽略");
                        return;
                    }
                    after = write.Value;
                    break;

                case FeelOp.Multiply:
                case FeelOp.Add:
                    if (def.IsNumeric)
                    {
                        if (before.Kind != FeelValueKind.Number || write.Value.Kind != FeelValueKind.Number)
                        {
                            work.Diagnostics.Add($"{source}：字段 \"{write.Field}\" 当前无数值或写入值非数值，{FeelProvenanceOps.FromOp(write.Op)} 已忽略");
                            return;
                        }
                        var result = write.Op == FeelOp.Multiply
                            ? before.AsNumber() * write.Value.AsNumber()
                            : before.AsNumber() + write.Value.AsNumber();
                        if (!double.IsFinite(result))
                        {
                            work.Diagnostics.Add($"{source}：字段 \"{write.Field}\" 运算结果非有限数，已忽略");
                            return;
                        }
                        after = FeelValue.Of(result);
                    }
                    else if (def.Kind == FeelFieldKind.List && write.Op == FeelOp.Add)
                    {
                        if (write.Value.Kind != FeelValueKind.List)
                        {
                            work.Diagnostics.Add($"{source}：字段 \"{write.Field}\" 的 add 值不是列表，已忽略");
                            return;
                        }
                        var items = before.Kind == FeelValueKind.List ? new List<string>(before.AsList()) : new List<string>();
                        var add = write.Value.AsList();
                        for (var i = 0; i < add.Count; i++)
                        {
                            if (!ContainsOrdinal(items, add[i])) items.Add(add[i]);
                        }
                        after = FeelValue.OfList(items);
                    }
                    else
                    {
                        work.Diagnostics.Add($"{source}：字段 \"{write.Field}\" 不支持 {FeelProvenanceOps.FromOp(write.Op)}，已忽略");
                        return;
                    }
                    break;

                case FeelOp.Remove:
                    if (def.Kind != FeelFieldKind.List || write.Value.Kind != FeelValueKind.List)
                    {
                        work.Diagnostics.Add($"{source}：字段 \"{write.Field}\" 的 remove 只用于列表字段，已忽略");
                        return;
                    }
                    {
                        var items = before.Kind == FeelValueKind.List ? new List<string>(before.AsList()) : new List<string>();
                        var remove = write.Value.AsList();
                        items.RemoveAll(x => ContainsOrdinal(remove, x));
                        after = FeelValue.OfList(items);
                    }
                    break;

                default:
                    return;
            }

            work.Values[idx] = after;
            Record(work, idx, layerNumber, source, FeelProvenanceOps.FromOp(write.Op), before, after);
        }

        private static bool TypeMatches(FeelFieldDef def, FeelValue value)
        {
            switch (def.Kind)
            {
                case FeelFieldKind.Number:
                case FeelFieldKind.Int: return value.Kind == FeelValueKind.Number;
                case FeelFieldKind.Bool: return value.Kind == FeelValueKind.Bool;
                case FeelFieldKind.Enum:
                case FeelFieldKind.Text:
                case FeelFieldKind.Id: return value.Kind == FeelValueKind.Text;
                case FeelFieldKind.List: return value.Kind == FeelValueKind.List;
                default: return false;
            }
        }

        private static bool ContainsOrdinal(IReadOnlyList<string> list, string item)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], item, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void Record(Work work, int index, int layer, string source, string op, FeelValue before, FeelValue after)
        {
            var list = work.Provenance[index] ??= new List<FeelProvenanceEntry>(4);
            list.Add(new FeelProvenanceEntry(layer, source, op, before, after));
        }
    }
}
