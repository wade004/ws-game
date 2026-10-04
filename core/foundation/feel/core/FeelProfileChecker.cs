using System;
using Core.Foundation.DataRegistry;
using System.Collections.Generic;

namespace Core.Foundation.Feel
{
    /// <summary>手感档案校验的检查名（手感设计/05 第 4 节检查表，一项一个常量）。</summary>
    public static class FeelChecks
    {
        /// <summary>操作不在字段登记的 <c>ops</c> 内。</summary>
        public const string OpNotAllowed = "feel_op_not_allowed";

        /// <summary>违反合成来源（武器层写角色为主字段、体型层对武器为主字段做非 multiply、副手写非叠加字段或非 add/multiply）。</summary>
        public const string CompositionViolation = "feel_composition_violation";

        /// <summary>同一行（同层）对同一字段写了两次。</summary>
        public const string DuplicateWrite = "feel_duplicate_write";

        /// <summary>写入值超出登记范围（标定前的相对值）。</summary>
        public const string ValueOutOfRange = "feel_value_out_of_range";

        /// <summary>预设 <c>extends</c> 成环。</summary>
        public const string ExtendsCycle = "feel_extends_cycle";

        /// <summary>预设（含 extends 链）缺少必填字段。</summary>
        public const string PresetMissingField = "feel_preset_missing_field";

        /// <summary>判定型字段出现在 <c>display.*</c>/<c>feedback.*</c> 表。</summary>
        public const string JudgingFieldInPresentationTable = "feel_judging_field_in_presentation_table";

        /// <summary>有手感数据但标定表缺项（阻断装配）。</summary>
        public const string CalibrationMissing = "feel_calibration_missing";

        /// <summary>光环 <c>feel_modifiers</c> 重复表达了已由属性系统承载的字段。</summary>
        public const string ModifierAttributeDuplicate = "feel_modifier_attribute_duplicate";

        /// <summary>档案行标了 <c>maturity: validated</c> 而 <c>feel.validation</c> 里没有覆盖它的记录（档案版本一致、评分各项达标、格子已写明）。</summary>
        public const string ValidatedWithoutRecord = "feel_validated_without_record";

        /// <summary><c>feel.validation</c> 的 <c>profile_ref</c> 指向不存在的档案行（或不是五张档案表之一）。</summary>
        public const string ValidationProfileMissing = "feel_validation_profile_missing";

        /// <summary><c>feel.validation</c> 的 <c>date</c> 不是 yyyy-mm-dd。</summary>
        public const string ValidationDateInvalid = "feel_validation_date_invalid";
    }

    /// <summary>一条手感档案校验问题。</summary>
    public sealed class FeelCheckIssue
    {
        public string Check { get; }

        public string Table { get; }

        public string? RecordKey { get; }

        public string? Field { get; }

        public string Message { get; }

        public FeelCheckIssue(string check, string table, string? recordKey, string? field, string message)
        {
            Check = check ?? throw new ArgumentNullException(nameof(check));
            Table = table ?? throw new ArgumentNullException(nameof(table));
            RecordKey = recordKey;
            Field = field;
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public override string ToString() =>
            "[" + Check + "] " + Table + (RecordKey == null ? "" : "[" + RecordKey + "]") + (Field == null ? "" : "." + Field) + ": " + Message;
    }

    /// <summary>光环（或其它来源）携带的手感修饰集合，供校验器检查（第 7 层来源）。</summary>
    public sealed class FeelModifierSource
    {
        /// <summary>来源所在表（如 <c>skill.aura_def</c>）。</summary>
        public string Table { get; }

        public string RecordKey { get; }

        public IReadOnlyList<FeelWrite> Modifiers { get; }

        public FeelModifierSource(string table, string recordKey, IReadOnlyList<FeelWrite> modifiers)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            RecordKey = recordKey ?? throw new ArgumentNullException(nameof(recordKey));
            Modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
        }
    }

    /// <summary>
    /// 手感档案静态校验（手感设计/05 第 4 节）：纯函数，输入档案集合（与光环修饰来源），输出问题列表。
    /// 数据校验规则（<c>FeelProfileValidationRule</c>）与装配（<see cref="FeelAssembly"/>）共用同一份实现。
    /// <para>
    /// 判断记录（相对量范围的按操作语义）：<c>set</c> 的值必须落在登记范围 [min,max]；<c>multiply</c> 的倍数必须在
    /// [0, <see cref="MaxMultiplier"/>]（负倍数无意义，上限防手滑）；<c>add</c> 的增量绝对值不超过登记范围的跨度
    /// （max−min）；枚举 <c>set</c> 的值必须在取值集合内。登记范围是限幅依据而不是"唯一合法取值"，
    /// 所以多层叠加后的结果越界不是数据错误，由解析器限幅并记录。
    /// </para>
    /// <para>
    /// 判断记录（同层重复写的粒度）：JSON 对象无法承载重复键，所以覆盖写入用列表形状（<c>writes</c>）；
    /// "同层"按行判定——同一行写入列表里同一字段出现两次（不论操作）即错误；标签映射行与它引用的体型原型同处第 3 层，
    /// 二者合并后重复也报错。<b>不同来源</b>（主手与副手、多个标签、多个光环）在同层写同一字段是叠加场景，不算错。
    /// </para>
    /// </summary>
    public static class FeelProfileChecker
    {
        /// <summary><c>multiply</c> 倍数的上限。</summary>
        public const double MaxMultiplier = 10.0;

        /// <summary>
        /// 检查全部分层表。<paramref name="checkPresetValueRanges"/> 为真时同时检查预设取值范围（数据登记表对预设
        /// <c>values</c> 已有原生范围检查，数据校验规则里传 false 避免重复报告；装配时传 true 兜底）。
        /// </summary>
        public static List<FeelCheckIssue> Check(
            FeelProfileSet profiles,
            IEnumerable<FeelModifierSource>? modifierSources = null,
            bool checkPresetValueRanges = true)
        {
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            var issues = new List<FeelCheckIssue>();
            var fields = profiles.Fields;

            // 预设：extends 成环、缺字段、取值范围。
            for (var i = 0; i < profiles.Presets.Count; i++)
            {
                var preset = profiles.Presets[i];
                CheckExtendsCycle(profiles, preset, issues);
                CheckPresetMissing(profiles, preset, issues);
                if (checkPresetValueRanges)
                {
                    for (var k = 0; k < preset.Values.Count; k++)
                    {
                        CheckRange(fields, preset.Table, preset.Id, preset.Values[k], issues);
                    }
                }
            }

            // 体型原型：武器为主字段只允许 multiply。
            for (var i = 0; i < profiles.Archetypes.Count; i++)
            {
                CheckOverlay(fields, profiles.Archetypes[i], FeelLayer.Archetype, issues);
            }

            // 标签映射：同层（第 3 层）合并原型与自身写入后查重复。
            for (var i = 0; i < profiles.TagMaps.Count; i++)
            {
                var row = profiles.TagMaps[i];
                CheckOverlay(fields, row, FeelLayer.TagMap, issues);
                var archetype = profiles.GetArchetype(row.ArchetypeRef);
                if (archetype != null)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (var k = 0; k < archetype.Writes.Count; k++) seen.Add(archetype.Writes[k].Field);
                    for (var k = 0; k < row.Writes.Count; k++)
                    {
                        if (seen.Contains(row.Writes[k].Field))
                        {
                            issues.Add(new FeelCheckIssue(FeelChecks.DuplicateWrite, row.Table, row.Id, row.Writes[k].Field,
                                $"标签映射与其引用的原型 \"{archetype.Id}\" 同处第 3 层，都写了字段 \"{row.Writes[k].Field}\""));
                        }
                    }
                }
            }

            // 武器：主手写入 + 副手写入。
            for (var i = 0; i < profiles.Weapons.Count; i++)
            {
                var weapon = profiles.Weapons[i];
                CheckOverlay(fields, weapon, FeelLayer.Weapon, issues);
                CheckWrites(fields, weapon.Table, weapon.Id, weapon.OffhandWrites, issues);
                for (var k = 0; k < weapon.OffhandWrites.Count; k++)
                {
                    var w = weapon.OffhandWrites[k];
                    if (!fields.TryGet(w.Field, out var def)) continue;
                    if (!def.OffhandStackable)
                    {
                        issues.Add(new FeelCheckIssue(FeelChecks.CompositionViolation, weapon.Table, weapon.Id, w.Field,
                            $"副手只允许写 offhand_stackable 字段，\"{w.Field}\" 不是"));
                    }
                    else if (w.Op != FeelOp.Add && w.Op != FeelOp.Multiply)
                    {
                        issues.Add(new FeelCheckIssue(FeelChecks.CompositionViolation, weapon.Table, weapon.Id, w.Field,
                            $"副手只允许 add/multiply 叠加，\"{w.Field}\" 用了 {FeelProvenanceOps.FromOp(w.Op)}"));
                    }
                }
            }

            // 角色、动作：无合成限制，只查通用项。
            for (var i = 0; i < profiles.Characters.Count; i++) CheckOverlay(fields, profiles.Characters[i], FeelLayer.Character, issues);
            for (var i = 0; i < profiles.Actions.Count; i++) CheckOverlay(fields, profiles.Actions[i], FeelLayer.Action, issues);

            // 光环手感修饰。
            if (modifierSources != null)
            {
                foreach (var source in modifierSources)
                {
                    CheckWrites(fields, source.Table, source.RecordKey, source.Modifiers, issues);
                    for (var k = 0; k < source.Modifiers.Count; k++)
                    {
                        var w = source.Modifiers[k];
                        if (fields.TryGet(w.Field, out var def) && def.AttributeBackedReason != null)
                        {
                            issues.Add(new FeelCheckIssue(FeelChecks.ModifierAttributeDuplicate, source.Table, source.RecordKey, w.Field,
                                $"字段 \"{w.Field}\" 已由属性系统承载（{def.AttributeBackedReason}），不得用 feel_modifiers 重复表达"));
                        }
                    }
                }
            }

            return issues;
        }

        /// <summary>检查一张覆盖表的一行：通用项（操作、范围、重复）加该层的合成来源限制。</summary>
        private static void CheckOverlay(FeelFieldSet fields, FeelRow row, FeelLayer layer, List<FeelCheckIssue> issues)
        {
            CheckWrites(fields, row.Table, row.Id, row.Writes, issues);
            for (var i = 0; i < row.Writes.Count; i++)
            {
                var w = row.Writes[i];
                if (!fields.TryGet(w.Field, out var def)) continue;
                switch (layer)
                {
                    case FeelLayer.Weapon:
                        if (def.Composition == FeelComposition.CharacterPrimary)
                        {
                            issues.Add(new FeelCheckIssue(FeelChecks.CompositionViolation, row.Table, row.Id, w.Field,
                                $"字段 \"{w.Field}\" 的合成来源是角色为主，武器层不得写"));
                        }
                        break;
                    case FeelLayer.Archetype:
                    case FeelLayer.TagMap:
                        if (def.Composition == FeelComposition.WeaponPrimary && w.Op != FeelOp.Multiply)
                        {
                            issues.Add(new FeelCheckIssue(FeelChecks.CompositionViolation, row.Table, row.Id, w.Field,
                                $"字段 \"{w.Field}\" 的合成来源是武器为主，体型层只允许 multiply，实际 {FeelProvenanceOps.FromOp(w.Op)}"));
                        }
                        break;
                }
            }
        }

        /// <summary>通用项：字段已登记、操作在 ops 内、值在范围内、同一行不重复写同一字段。</summary>
        private static void CheckWrites(
            FeelFieldSet fields, string table, string recordKey, IReadOnlyList<FeelWrite> writes,
            List<FeelCheckIssue> issues)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < writes.Count; i++)
            {
                var w = writes[i];
                if (!seen.Add(w.Field))
                {
                    issues.Add(new FeelCheckIssue(FeelChecks.DuplicateWrite, table, recordKey, w.Field,
                        $"同一行对字段 \"{w.Field}\" 写了不止一次（不允许隐式的后写覆盖前写）"));
                }
                if (!fields.TryGet(w.Field, out var def)) continue;
                if (!def.Allows(w.Op))
                {
                    issues.Add(new FeelCheckIssue(FeelChecks.OpNotAllowed, table, recordKey, w.Field,
                        $"字段 \"{w.Field}\" 登记的允许操作是 {def.Ops}，不允许 {FeelProvenanceOps.FromOp(w.Op)}"));
                    continue;
                }
                CheckRange(fields, table, recordKey, w, issues);
            }
        }

        private static void CheckRange(FeelFieldSet fields, string table, string recordKey, FeelWrite w, List<FeelCheckIssue> issues)
        {
            if (!fields.TryGet(w.Field, out var def)) return;
            if (def.IsNumeric)
            {
                if (w.Value.Kind != FeelValueKind.Number) return;
                var v = w.Value.AsNumber();
                var min = def.Min!.Value;
                var max = def.Max!.Value;
                switch (w.Op)
                {
                    case FeelOp.Set:
                        if (v < min || v > max)
                        {
                            issues.Add(new FeelCheckIssue(FeelChecks.ValueOutOfRange, table, recordKey, w.Field,
                                $"set 值 {w.Value} 超出登记范围 [{min},{max}]"));
                        }
                        break;
                    case FeelOp.Multiply:
                        if (v < 0 || v > MaxMultiplier)
                        {
                            issues.Add(new FeelCheckIssue(FeelChecks.ValueOutOfRange, table, recordKey, w.Field,
                                $"multiply 倍数 {w.Value} 超出 [0,{MaxMultiplier}]"));
                        }
                        break;
                    case FeelOp.Add:
                        if (Math.Abs(v) > max - min)
                        {
                            issues.Add(new FeelCheckIssue(FeelChecks.ValueOutOfRange, table, recordKey, w.Field,
                                $"add 增量 {w.Value} 的绝对值超过登记范围跨度 {max - min}"));
                        }
                        break;
                }
            }
            else if (def.Kind == FeelFieldKind.Enum && w.Op == FeelOp.Set && w.Value.Kind == FeelValueKind.Text)
            {
                var text = w.Value.AsText();
                var ok = false;
                for (var i = 0; i < def.EnumValues!.Count; i++)
                {
                    if (string.Equals(def.EnumValues[i], text, StringComparison.Ordinal)) { ok = true; break; }
                }
                if (!ok)
                {
                    issues.Add(new FeelCheckIssue(FeelChecks.ValueOutOfRange, table, recordKey, w.Field,
                        $"枚举值 \"{text}\" 不在登记取值 {string.Join("|", def.EnumValues)} 内"));
                }
            }
        }

        private static void CheckExtendsCycle(FeelProfileSet profiles, FeelRow preset, List<FeelCheckIssue> issues)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { preset.Id };
            var cur = profiles.GetPreset(preset.Extends);
            while (cur != null)
            {
                if (cur.Id == preset.Id)
                {
                    issues.Add(new FeelCheckIssue(FeelChecks.ExtendsCycle, preset.Table, preset.Id, "extends",
                        $"预设 extends 成环（沿 \"{preset.Extends}\" 回到自身）"));
                    return;
                }
                if (!seen.Add(cur.Id)) return; // 环不含本行，由环内成员各自报告。
                cur = profiles.GetPreset(cur.Extends);
            }
        }

        private static void CheckPresetMissing(FeelProfileSet profiles, FeelRow preset, List<FeelCheckIssue> issues)
        {
            var values = profiles.EffectivePresetValues(preset.Id, out _);
            var fields = profiles.Fields;
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i].Optional || !values[i].IsNone) continue;
                issues.Add(new FeelCheckIssue(FeelChecks.PresetMissingField, preset.Table, preset.Id, fields[i].Name,
                    $"预设必须全字段，缺少 \"{fields[i].Name}\"（含 extends 链仍缺）"));
            }
        }
    }
}
