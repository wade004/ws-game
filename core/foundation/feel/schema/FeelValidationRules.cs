using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Feel
{
    /// <summary>
    /// 手感档案校验规则（手感设计/05 第 4 节）：操作不在 ops 内、违反合成来源、同层重复写、相对量越界、
    /// extends 成环、预设缺字段、光环 <c>feel_modifiers</c> 与属性重复。纯委托给 <see cref="FeelProfileChecker"/>
    /// （与装配共用同一份实现）。预设取值范围已由数据登记表原生范围检查报告，这里不重复。
    /// </summary>
    public sealed class FeelProfileValidationRule : IValidationRule
    {
        private readonly Func<IDataRegistryView, FeelFieldSet> _fields;

        public FeelProfileValidationRule(FeelFieldSet fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            _fields = _ => fields;
        }

        /// <summary>字段登记在校验时才按注册表取（游戏自有字段的扩展位，见 <see cref="FeelSchemas.RegisterAll"/>）。</summary>
        internal FeelProfileValidationRule(Func<IDataRegistryView, FeelFieldSet> fieldsForView)
        {
            _fields = fieldsForView ?? throw new ArgumentNullException(nameof(fieldsForView));
        }

        public string RuleId => nameof(FeelProfileValidationRule);

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public bool NonEscalatable => false;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var fields = _fields(view);
            var profiles = FeelProfileSet.FromRegistry(view, fields);
            var sources = new List<FeelModifierSource>();
            if (view.TryGetAll(FeelTables.AuraDefTable, out var auras))
            {
                for (var i = 0; i < auras.Count; i++)
                {
                    var writes = FeelWriteParser.ParseWrites(auras[i].Raw, FeelTables.AuraFeelModifiersField, fields);
                    if (writes.Count > 0) sources.Add(new FeelModifierSource(FeelTables.AuraDefTable, auras[i].Key, writes));
                }
            }
            if (!profiles.HasAnyData && sources.Count == 0) yield break;

            var found = FeelProfileChecker.Check(profiles, sources, checkPresetValueRanges: false);
            for (var i = 0; i < found.Count; i++)
            {
                var f = found[i];
                yield return new ValidationIssue(ValidationSeverity.Error, f.Table, f.Check, f.Message, f.RecordKey, f.Field)
                    .WithRuleId(RuleId);
            }
        }
    }

    /// <summary>
    /// 半属隔离的数据侧检查：判定型手感字段不得出现在 <c>display.*</c>/<c>feedback.*</c> 表（手感设计/05 第 4 节）。
    /// 既扫表的字段登记（含子结构与变体分支的字段名），也扫记录里出现的键名（递归），命中任一判定型字段名即错误。
    /// </summary>
    public sealed class FeelHalfIsolationRule : IValidationRule
    {
        private readonly Func<IDataRegistryView, FeelFieldSet> _fields;
        private HashSet<string> _judging = new HashSet<string>(StringComparer.Ordinal);

        public FeelHalfIsolationRule(FeelFieldSet fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            _fields = _ => fields;
        }

        /// <summary>字段登记在校验时才按注册表取（游戏自有字段的扩展位，见 <see cref="FeelSchemas.RegisterAll"/>）。</summary>
        internal FeelHalfIsolationRule(Func<IDataRegistryView, FeelFieldSet> fieldsForView)
        {
            _fields = fieldsForView ?? throw new ArgumentNullException(nameof(fieldsForView));
        }

        public string RuleId => nameof(FeelHalfIsolationRule);

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public bool NonEscalatable => false;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var fields = _fields(view);
            var judging = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i].Half == FeelHalf.Judging) judging.Add(fields[i].Name);
            }
            _judging = judging;

            var tables = view.Tables;
            for (var t = 0; t < tables.Count; t++)
            {
                var table = tables[t];
                if (!IsPresentationTable(table)) continue;

                var schema = view.GetSchema(table);
                if (schema != null)
                {
                    var hits = new List<string>();
                    for (var i = 0; i < schema.Fields.Count; i++) WalkSchema(schema.Fields[i], schema.Fields[i].Name, hits, 0);
                    for (var i = 0; i < hits.Count; i++)
                    {
                        yield return Issue(table, null, hits[i], $"判定型手感字段 \"{hits[i]}\" 出现在表现域表 \"{table}\" 的字段登记里");
                    }
                }

                if (!view.TryGetAll(table, out var records)) continue;
                for (var r = 0; r < records.Count; r++)
                {
                    var hits = new List<string>();
                    WalkJson(records[r].Raw, string.Empty, hits, 0);
                    for (var i = 0; i < hits.Count; i++)
                    {
                        yield return Issue(table, records[r].Key, hits[i], $"判定型手感字段 \"{hits[i]}\" 出现在表现域表 \"{table}\" 的记录里");
                    }
                }
            }
        }

        private static bool IsPresentationTable(string table) =>
            table.StartsWith("display.", StringComparison.Ordinal) || table.StartsWith("feedback.", StringComparison.Ordinal);

        private ValidationIssue Issue(string table, string? key, string field, string message) =>
            new ValidationIssue(ValidationSeverity.Error, table, FeelChecks.JudgingFieldInPresentationTable, message, key, field)
                .WithRuleId(RuleId);

        private void WalkSchema(FieldSchema field, string path, List<string> hits, int depth)
        {
            if (depth > 16) return;
            if (_judging.Contains(field.Name)) hits.Add(path);
            if (field.Fields != null)
            {
                for (var i = 0; i < field.Fields.Count; i++) WalkSchema(field.Fields[i], path + "." + field.Fields[i].Name, hits, depth + 1);
            }
            var item = field.Item;
            if (item != null && item.Name.Length > 0 && item.Name[0] != '<') WalkSchema(item, path + "[]", hits, depth + 1);
            else if (item != null) WalkSchemaChildren(item, path + "[]", hits, depth + 1);
            var variants = field.Variants;
            if (variants != null)
            {
                if (variants.CommonFields != null)
                {
                    for (var i = 0; i < variants.CommonFields.Count; i++) WalkSchema(variants.CommonFields[i], path + "." + variants.CommonFields[i].Name, hits, depth + 1);
                }
                foreach (var pair in variants.Cases)
                {
                    for (var i = 0; i < pair.Value.Count; i++) WalkSchema(pair.Value[i], path + "." + pair.Value[i].Name, hits, depth + 1);
                }
            }
        }

        private void WalkSchemaChildren(FieldSchema holder, string path, List<string> hits, int depth)
        {
            if (depth > 16) return;
            if (holder.Fields != null)
            {
                for (var i = 0; i < holder.Fields.Count; i++) WalkSchema(holder.Fields[i], path + "." + holder.Fields[i].Name, hits, depth + 1);
            }
            var variants = holder.Variants;
            if (variants != null)
            {
                if (variants.CommonFields != null)
                {
                    for (var i = 0; i < variants.CommonFields.Count; i++) WalkSchema(variants.CommonFields[i], path + "." + variants.CommonFields[i].Name, hits, depth + 1);
                }
                foreach (var pair in variants.Cases)
                {
                    for (var i = 0; i < pair.Value.Count; i++) WalkSchema(pair.Value[i], path + "." + pair.Value[i].Name, hits, depth + 1);
                }
            }
        }

        private void WalkJson(JsonValue value, string path, List<string> hits, int depth)
        {
            if (depth > 32) return;
            if (value is JsonObject obj)
            {
                for (var i = 0; i < obj.Count; i++)
                {
                    var key = obj[i].Key;
                    var childPath = path.Length == 0 ? key : path + "." + key;
                    if (_judging.Contains(key)) hits.Add(childPath);
                    WalkJson(obj[i].Value, childPath, hits, depth + 1);
                }
            }
            else if (value is JsonArray arr)
            {
                for (var i = 0; i < arr.Count; i++) WalkJson(arr[i], path + "[" + i + "]", hits, depth + 1);
            }
        }
    }

    /// <summary>
    /// 标定表缺项（手感设计/05 第 4 节，阻断装配）：存在任何手感数据（含运动模式规则与标签映射）而
    /// <c>feel.calibration</c> 没有任何行。没有手感数据时静默。
    /// </summary>
    public sealed class FeelCalibrationRule : IValidationRule
    {
        public string RuleId => nameof(FeelCalibrationRule);

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public bool NonEscalatable => false;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var calibrationRows = view.TryGetAll(FeelTables.Calibration, out var cal) ? cal.Count : 0;
            if (calibrationRows > 0) yield break;

            var others = 0;
            for (var i = 0; i < FeelTables.All.Count; i++)
            {
                var table = FeelTables.All[i];
                if (table == FeelTables.Calibration || table == FeelTables.Validation) continue;
                if (view.TryGetAll(table, out var rows)) others += rows.Count;
            }
            if (others == 0) yield break;

            yield return new ValidationIssue(ValidationSeverity.Error, FeelTables.Calibration, FeelChecks.CalibrationMissing,
                $"存在 {others} 行 feel.* 数据但 feel.calibration 没有任何行：标定表缺项会阻断装配（不用隐式缺省），请为这款游戏补一行标定")
                .WithRuleId(RuleId);
        }
    }

    /// <summary>
    /// 成熟度校验（手感设计/05 第 8 节，ADR-0146）：档案行标 <c>maturity: validated</c> 必须在 <c>feel.validation</c> 里有覆盖它的记录
    /// （指向该行、档案版本一致、评分各项不低于升级门槛、格子已写明），否则报错；验证记录自身的 <c>profile_ref</c> 必须指向存在的档案行、
    /// <c>date</c> 必须是 yyyy-mm-dd。框架自带的档案行永远是 experimental，所以框架数据在没有验证记录时天然无法标 validated。
    /// 没有任何档案行与验证记录时静默。
    /// </summary>
    public sealed class FeelMaturityRule : IValidationRule
    {
        public string RuleId => nameof(FeelMaturityRule);

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public bool NonEscalatable => false;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var ledger = FeelValidationLedger.FromRegistry(view);

            // 档案行（含版本），按"表id"索引，供验证记录核对 profile_ref。
            var profiles = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var t = 0; t < FeelTables.Profiles.Count; t++)
            {
                var table = FeelTables.Profiles[t];
                if (!view.TryGetAll(table, out var rows)) continue;
                for (var i = 0; i < rows.Count; i++) profiles[rows[i].Key] = FeelMaturity.ProfileVersionOf(rows[i]);
            }

            for (var t = 0; t < FeelTables.Profiles.Count; t++)
            {
                var table = FeelTables.Profiles[t];
                if (!view.TryGetAll(table, out var rows)) continue;
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (!FeelMaturity.IsMarkedValidated(row)) continue;
                    var version = FeelMaturity.ProfileVersionOf(row);
                    if (ledger.Covering(row.Key, version).Count > 0) continue;

                    var near = new List<string>();
                    for (var r = 0; r < ledger.Records.Count; r++)
                    {
                        var rec = ledger.Records[r];
                        if (rec.ProfileRef != row.Key) continue;
                        if (rec.ProfileVersion != version) near.Add(rec.Id + "（档案版本 " + rec.ProfileVersion + "，当前 " + version + "，已过期）");
                        else if (!rec.MeetsThreshold) near.Add(rec.Id + "（评分未达升级门槛 " + FeelMaturity.ScoreThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）");
                    }

                    yield return new ValidationIssue(ValidationSeverity.Error, table, FeelChecks.ValidatedWithoutRecord,
                        "档案行标了 validated，但 feel.validation 里没有覆盖它的记录（需要：profile_ref 指向本行、profile_version 等于本行当前版本 " + version
                        + "、评分各项不低于升级门槛、格子已写明）；框架自带的档案行只能是 experimental"
                        + (near.Count == 0 ? string.Empty : "。已有但不覆盖的记录：" + string.Join("；", near)),
                        row.Key, "maturity").WithRuleId(RuleId);
                }
            }

            if (!view.TryGetAll(FeelTables.Validation, out var records)) yield break;
            for (var i = 0; i < records.Count; i++)
            {
                var rec = records[i];
                if (rec.TryGetString("profile_ref", out var profileRef) && !profiles.ContainsKey(profileRef))
                {
                    yield return new ValidationIssue(ValidationSeverity.Error, FeelTables.Validation, FeelChecks.ValidationProfileMissing,
                        "profile_ref \"" + profileRef + "\" 不是任何 feel.preset/archetype/weapon/character/action 行", rec.Key, "profile_ref")
                        .WithRuleId(RuleId);
                }

                if (rec.TryGetString("date", out var date) && !IsIsoDate(date))
                {
                    yield return new ValidationIssue(ValidationSeverity.Error, FeelTables.Validation, FeelChecks.ValidationDateInvalid,
                        "date \"" + date + "\" 不是 yyyy-mm-dd", rec.Key, "date").WithRuleId(RuleId);
                }
            }
        }

        private static bool IsIsoDate(string text)
        {
            if (text.Length != 10 || text[4] != '-' || text[7] != '-') return false;
            for (var i = 0; i < text.Length; i++)
            {
                if (i == 4 || i == 7) continue;
                if (text[i] < '0' || text[i] > '9') return false;
            }

            var month = (text[5] - '0') * 10 + (text[6] - '0');
            var day = (text[8] - '0') * 10 + (text[9] - '0');
            return month >= 1 && month <= 12 && day >= 1 && day <= 31;
        }
    }
}
