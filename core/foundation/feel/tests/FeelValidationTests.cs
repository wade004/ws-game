using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Xunit;
using static Tests.Foundation.Feel.FeelTestSupport;

namespace Tests.Foundation.Feel
{
    /// <summary>
    /// 手感设计/05 第 4 节每一条校验检查项的正反用例：每项一条"数据合法时不报"与一条"注入缺陷时报出对应检查名"。
    /// 全部经数据注册表加载（框架手感数据 + 测试标定行为基线，按表名替换为测试数据）。
    /// </summary>
    public class FeelValidationTests
    {
        private static string W(string field, string op, string valueJson) =>
            "{\"field\":\"" + field + "\",\"op\":\"" + op + "\",\"value\":" + valueJson + "}";

        private static string Table(string table, params string[] rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":[" + string.Join(",", rowsJson) + "]}";

        private static string Row(string id, string extra) => "{\"id\":\"" + id + "\"" + (extra.Length > 0 ? "," + extra : "") + "}";

        private static string Writes(params string[] writes) => "\"writes\":[" + string.Join(",", writes) + "]";

        /// <summary>框架手感数据 + 标定为基线，<paramref name="overrides"/> 按表名替换整张表，<paramref name="removed"/> 去掉的表不加载。</summary>
        private static ValidationReport Run(
            Dictionary<string, string>? overrides = null, string[]? removed = null, IEnumerable<TableSchema>? extra = null,
            IEnumerable<(string, string)>? extraTables = null, bool withCalibration = true)
        {
            var tables = FrameworkFeelTables();
            if (withCalibration) tables.Add(("feel.calibration", CalibrationJson));
            if (removed != null) tables.RemoveAll(t => removed.Contains(t.Table));
            if (overrides != null)
            {
                for (var i = 0; i < tables.Count; i++)
                {
                    if (overrides.TryGetValue(tables[i].Table, out var json)) tables[i] = (tables[i].Table, json);
                }
                foreach (var pair in overrides)
                {
                    if (!tables.Any(t => t.Table == pair.Key)) tables.Add((pair.Key, pair.Value));
                }
            }
            if (extraTables != null) tables.AddRange(extraTables);
            return Load(tables, extra).Report;
        }

        private static List<ValidationIssue> Of(ValidationReport report, string check) =>
            report.Issues.Where(i => i.Check == check).ToList();

        private static Dictionary<string, string> Weapons(params string[] rows) =>
            new Dictionary<string, string> { { "feel.weapon", Table("feel.weapon", rows) } };

        private static string WeaponRow(string id, string extra) =>
            Row(id, "\"family\":\"sword_1h\"" + (extra.Length > 0 ? "," + extra : ""));

        // ------------------------------------------------------------------ 基线

        [Fact]
        public void Baseline_FrameworkDataWithCalibration_HasNoIssuesAtAll()
        {
            var report = Run();
            Assert.Equal(0, report.ErrorCount);
            Assert.Equal(0, report.WarningCount);
            foreach (var code in new[]
            {
                FeelChecks.OpNotAllowed, FeelChecks.CompositionViolation, FeelChecks.DuplicateWrite, FeelChecks.ValueOutOfRange,
                FeelChecks.ExtendsCycle, FeelChecks.PresetMissingField, FeelChecks.JudgingFieldInPresentationTable,
                FeelChecks.CalibrationMissing, FeelChecks.ModifierAttributeDuplicate,
            })
            {
                Assert.Empty(Of(report, code));
            }
        }

        // ------------------------------------------------------------------ 操作不在 ops 内

        [Fact]
        public void OpNotAllowed_BoolFieldWithAdd_IsReported()
        {
            var report = Run(Weapons(WeaponRow("feel.weapon.bad_op", Writes(W("trail_enabled", "add", "true")))));
            var found = Of(report, FeelChecks.OpNotAllowed);
            Assert.Single(found);
            Assert.Equal("trail_enabled", found[0].Field);
            Assert.Equal("feel.weapon.bad_op", found[0].RecordKey);
        }

        [Fact]
        public void OpNotAllowed_AllowedOps_AreNotReported()
        {
            var report = Run(Weapons(WeaponRow("feel.weapon.ok_op", Writes(
                W("trail_enabled", "set", "true"), W("attacker_hitstop_ms", "add", "5"), W("stagger_power", "multiply", "1.2")))));
            Assert.Empty(Of(report, FeelChecks.OpNotAllowed));
            Assert.Equal(0, report.ErrorCount);
        }

        // ------------------------------------------------------------------ 合成来源违规

        [Fact]
        public void Composition_WeaponLayerWritingCharacterPrimaryField_IsReported()
        {
            var report = Run(Weapons(WeaponRow("feel.weapon.bad", Writes(W("accel_ms", "set", "100")))));
            var found = Of(report, FeelChecks.CompositionViolation);
            Assert.Single(found);
            Assert.Equal("accel_ms", found[0].Field);
        }

        [Fact]
        public void Composition_ArchetypeSettingWeaponPrimaryField_IsReported_ButMultiplyIsAllowed()
        {
            var bad = Run(new Dictionary<string, string>
            {
                { "feel.archetype", Table("feel.archetype", Row("feel.archetype.bad", Writes(W("attacker_hitstop_ms", "set", "10")))) },
            });
            Assert.Single(Of(bad, FeelChecks.CompositionViolation));

            var good = Run(new Dictionary<string, string>
            {
                { "feel.archetype", Table("feel.archetype", Row("feel.archetype.good", Writes(W("attacker_hitstop_ms", "multiply", "0.9")))) },
            });
            Assert.Empty(Of(good, FeelChecks.CompositionViolation));
            Assert.Equal(0, good.ErrorCount);
        }

        [Fact]
        public void Composition_OffhandWriting_OnlyStackableFieldsWithAddOrMultiply()
        {
            var notStackable = Run(Weapons(WeaponRow("feel.weapon.off1", "\"offhand_writes\":[" + W("stagger_power", "add", "1") + "]")));
            Assert.Single(Of(notStackable, FeelChecks.CompositionViolation));

            var setOp = Run(Weapons(WeaponRow("feel.weapon.off2", "\"offhand_writes\":[" + W("impact_vfx_scale", "set", "2") + "]")));
            Assert.Single(Of(setOp, FeelChecks.CompositionViolation));

            var ok = Run(Weapons(WeaponRow("feel.weapon.off3",
                "\"offhand_writes\":[" + W("impact_vfx_scale", "multiply", "1.1") + "," + W("sfx_sweetener_tier", "add", "1") + "]")));
            Assert.Empty(Of(ok, FeelChecks.CompositionViolation));
            Assert.Equal(0, ok.ErrorCount);
        }

        // ------------------------------------------------------------------ 同层重复写

        [Fact]
        public void DuplicateWrite_SameFieldTwiceInOneRow_IsReported_DifferentFieldsAreNot()
        {
            var bad = Run(Weapons(WeaponRow("feel.weapon.dup", Writes(W("attacker_hitstop_ms", "set", "30"), W("attacker_hitstop_ms", "add", "5")))));
            var found = Of(bad, FeelChecks.DuplicateWrite);
            Assert.Single(found);
            Assert.Equal("attacker_hitstop_ms", found[0].Field);

            var good = Run(Weapons(WeaponRow("feel.weapon.nodup", Writes(W("attacker_hitstop_ms", "set", "30"), W("target_hitstop_ms", "add", "5")))));
            Assert.Empty(Of(good, FeelChecks.DuplicateWrite));
        }

        [Fact]
        public void DuplicateWrite_TagMapAndItsArchetypeShareLayer3_IsReported()
        {
            var arch = Table("feel.archetype", Row("feel.archetype.a", Writes(W("stride_scale", "multiply", "1.1"))));
            var tagBad = Table("feel.tag_map", Row("feel.tag_map.t", "\"tag\":\"size:tall\",\"archetype_ref\":\"feel.archetype.a\"," + Writes(W("stride_scale", "multiply", "1.2"))));
            var bad = Run(new Dictionary<string, string> { { "feel.archetype", arch }, { "feel.tag_map", tagBad } });
            Assert.Single(Of(bad, FeelChecks.DuplicateWrite));

            var tagGood = Table("feel.tag_map", Row("feel.tag_map.t", "\"tag\":\"size:tall\",\"archetype_ref\":\"feel.archetype.a\"," + Writes(W("accel_ms", "multiply", "1.2"))));
            var good = Run(new Dictionary<string, string> { { "feel.archetype", arch }, { "feel.tag_map", tagGood } });
            Assert.Empty(Of(good, FeelChecks.DuplicateWrite));
            Assert.Equal(0, good.ErrorCount);
        }

        // ------------------------------------------------------------------ 相对值超范围

        [Theory]
        [InlineData("set", "99999")]       // 超过 accel_ms 上限 3000
        [InlineData("set", "-1")]
        [InlineData("multiply", "20")]     // 超过倍数上限
        [InlineData("multiply", "-0.5")]
        [InlineData("add", "5000")]        // 增量超过范围跨度
        public void ValueOutOfRange_NumericWrites_AreReported(string op, string value)
        {
            var report = Run(new Dictionary<string, string>
            {
                { "feel.character", Table("feel.character", Row("feel.character.bad", Writes(W("accel_ms", op, value)))) },
            });
            var found = Of(report, FeelChecks.ValueOutOfRange);
            Assert.Single(found);
            Assert.Equal("accel_ms", found[0].Field);
        }

        [Theory]
        [InlineData("set", "0")]
        [InlineData("set", "3000")]        // 边界合法
        [InlineData("multiply", "10")]
        [InlineData("multiply", "0")]
        [InlineData("add", "-3000")]
        [InlineData("add", "3000")]
        public void ValueOutOfRange_BoundaryValues_AreNotReported(string op, string value)
        {
            var report = Run(new Dictionary<string, string>
            {
                { "feel.character", Table("feel.character", Row("feel.character.ok", Writes(W("accel_ms", op, value)))) },
            });
            Assert.Empty(Of(report, FeelChecks.ValueOutOfRange));
            Assert.Equal(0, report.ErrorCount);
        }

        [Fact]
        public void ValueOutOfRange_EnumValueOutsideRegisteredSet_IsReported()
        {
            var report = Run(Weapons(WeaponRow("feel.weapon.enum", Writes(W("impact_class", "set", "\"colossal\"")))));
            Assert.Single(Of(report, FeelChecks.ValueOutOfRange));
        }

        [Fact]
        public void ValueOutOfRange_PresetValue_IsReportedByNativeRangeCheck_AndByCheckerAtAssembly()
        {
            // 数据注册表对预设 values 有原生范围检查（报告里没有 feel_value_out_of_range，而是登记表自己的范围检查）。
            var profiles = FrameworkProfiles();
            var bad = FeelRow.Preset("feel.preset.arpg_responsive", null, profiles.Presets[0].Values
                .Select(v => v.Field == "accel_ms" ? Set("accel_ms", 99999) : v).ToArray());
            var rows = new List<FeelRow> { bad };
            var set = new FeelProfileSet(profiles.Fields, rows, profiles.MotionModeRules, profiles.Calibrations);

            Assert.Contains(FeelProfileChecker.Check(set, null, checkPresetValueRanges: true), i => i.Check == FeelChecks.ValueOutOfRange);
            Assert.DoesNotContain(FeelProfileChecker.Check(set, null, checkPresetValueRanges: false), i => i.Check == FeelChecks.ValueOutOfRange);
            Assert.DoesNotContain(FeelProfileChecker.Check(profiles, null, checkPresetValueRanges: true), i => i.Check == FeelChecks.ValueOutOfRange);
        }

        // ------------------------------------------------------------------ extends 成环

        [Fact]
        public void ExtendsCycle_TwoPresetsExtendingEachOther_IsReported()
        {
            var presets = Table("feel.preset",
                Row("feel.preset.a", "\"extends\":\"feel.preset.b\",\"values\":{}"),
                Row("feel.preset.b", "\"extends\":\"feel.preset.a\",\"values\":{}"));
            var report = Run(new Dictionary<string, string> { { "feel.preset", presets } });
            Assert.NotEmpty(Of(report, FeelChecks.ExtendsCycle));
        }

        [Fact]
        public void ExtendsCycle_PresetExtendingItself_IsReported_ButLinearChainIsNot()
        {
            var selfLoop = Run(new Dictionary<string, string>
            {
                { "feel.preset", Table("feel.preset", Row("feel.preset.a", "\"extends\":\"feel.preset.a\",\"values\":{}")) },
            });
            Assert.NotEmpty(Of(selfLoop, FeelChecks.ExtendsCycle));

            var presetText = FrameworkFeelTables().Single(t => t.Table == "feel.preset").Json;
            var chained = presetText.TrimEnd().TrimEnd('}').TrimEnd().TrimEnd(']').TrimEnd()
                + ",\n{\"id\":\"feel.preset.child\",\"extends\":\"feel.preset.arpg_responsive\",\"values\":{\"accel_ms\":50}}\n]}";
            var report = Run(new Dictionary<string, string> { { "feel.preset", chained } });
            Assert.Empty(Of(report, FeelChecks.ExtendsCycle));
            Assert.Empty(Of(report, FeelChecks.PresetMissingField));
            Assert.Equal(0, report.ErrorCount);
        }

        // ------------------------------------------------------------------ 预设缺字段

        [Fact]
        public void PresetMissingField_PartialPresetWithoutExtends_ListsEveryMissingNonOptionalField()
        {
            var presets = Table("feel.preset", Row("feel.preset.partial", "\"values\":{\"accel_ms\":50}"));
            var report = Run(new Dictionary<string, string> { { "feel.preset", presets } });
            var found = Of(report, FeelChecks.PresetMissingField);

            var required = FeelFields.Default.Fields.Where(f => !f.Optional && f.Name != "accel_ms").Select(f => f.Name).ToHashSet();
            Assert.Equal(required, found.Select(i => i.Field!).ToHashSet());
            Assert.Equal(required.Count, found.Count);
            Assert.DoesNotContain(found, i => i.Field == "accel_ms");
            Assert.DoesNotContain(found, i => FeelFields.Default.Get(i.Field!).Optional);
        }

        [Fact]
        public void PresetMissingField_FullFrameworkPresets_AreNotReported()
        {
            Assert.Empty(Of(Run(), FeelChecks.PresetMissingField));
        }

        // ------------------------------------------------------------------ 判定型字段出现在表现域表

        private static TableSchema DisplayTable(string name, string extraFieldName) => new TableSchema(
            name: name, primaryKey: "id", currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "id"),
                new FieldSchema("label", FieldKind.String, required: false, description: "名称"),
                new FieldSchema(extraFieldName, FieldKind.Number, required: false, description: "测试字段"),
            });

        [Theory]
        [InlineData("display.test_x")]
        [InlineData("feedback.test_x")]
        public void JudgingFieldInPresentationTable_SchemaDeclaringJudgingFieldName_IsReported(string table)
        {
            var report = Run(
                extra: new[] { DisplayTable(table, "accel_ms") },
                extraTables: new[] { (table, Table(table, Row(table + ".one", "\"label\":\"x\""))) });
            var found = Of(report, FeelChecks.JudgingFieldInPresentationTable);
            Assert.NotEmpty(found);
            Assert.All(found, i => Assert.Equal(table, i.Table));
            Assert.Contains(found, i => i.Field == "accel_ms");
        }

        [Fact]
        public void JudgingFieldInPresentationTable_RecordCarryingJudgingKey_IsReported()
        {
            var report = Run(
                extra: new[] { DisplayTable("display.test_y", "scale") },
                extraTables: new[] { ("display.test_y", Table("display.test_y", Row("display.test_y.one", "\"label\":\"x\",\"hit_stun_ms\":120"))) });
            var found = Of(report, FeelChecks.JudgingFieldInPresentationTable);
            Assert.Contains(found, i => i.Field == "hit_stun_ms" && i.RecordKey == "display.test_y.one");
        }

        [Fact]
        public void JudgingFieldInPresentationTable_PresentingNamesAndOtherDomains_AreNotReported()
        {
            // 呈现型字段名（stride_scale）出现在表现域表合法；判定型字段名出现在非表现域表（feel.*）合法。
            var report = Run(
                extra: new[] { DisplayTable("display.test_z", "stride_scale") },
                extraTables: new[] { ("display.test_z", Table("display.test_z", Row("display.test_z.one", "\"label\":\"x\""))) });
            Assert.Empty(Of(report, FeelChecks.JudgingFieldInPresentationTable));
            Assert.Equal(0, report.ErrorCount);
        }

        // ------------------------------------------------------------------ 标定缺项

        [Fact]
        public void CalibrationMissing_FeelDataWithoutCalibrationRow_IsReported()
        {
            var report = Run(withCalibration: false);
            var found = Of(report, FeelChecks.CalibrationMissing);
            Assert.Single(found);
            Assert.Equal("feel.calibration", found[0].Table);
            Assert.True(report.ErrorCount >= 1);
        }

        [Fact]
        public void CalibrationMissing_WithCalibrationRow_OrWithNoFeelDataAtAll_IsNotReported()
        {
            Assert.Empty(Of(Run(), FeelChecks.CalibrationMissing));

            // 没有任何 feel.* 数据：规则静默（既有行为不变）。
            var (_, empty) = Load(Array.Empty<(string, string)>());
            Assert.Empty(empty.Issues.Where(i => i.Check.StartsWith("feel_", StringComparison.Ordinal)));
            Assert.Equal(0, empty.ErrorCount);
        }

        [Fact]
        public void CalibrationMissing_OnlyMotionModeRules_StillRequiresCalibration()
        {
            var tables = FrameworkFeelTables().Where(t => t.Table == "feel.motion_mode_rules").ToList();
            var report = Load(tables).Report;
            Assert.Single(Of(report, FeelChecks.CalibrationMissing));
        }

        // ------------------------------------------------------------------ 光环 feel_modifiers 与属性重复

        private static TableSchema AuraSchema() => new TableSchema(
            name: FeelTables.AuraDefTable, primaryKey: "id", currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "id"),
                FeelSchemas.WriteArray(FeelTables.AuraFeelModifiersField, "光环手感修饰"),
            });

        private static ValidationReport RunAura(string rowExtra) => Run(
            extra: new[] { AuraSchema() },
            extraTables: new[] { (FeelTables.AuraDefTable, Table(FeelTables.AuraDefTable, Row("skill.aura_def.test", rowExtra))) });

        [Theory]
        [InlineData("walk_speed_ratio")]
        [InlineData("phase_scale.active")]
        public void ModifierAttributeDuplicate_AttributeBackedField_IsReported(string field)
        {
            var report = RunAura("\"" + FeelTables.AuraFeelModifiersField + "\":[" + W(field, "multiply", "0.8") + "]");
            var found = Of(report, FeelChecks.ModifierAttributeDuplicate);
            Assert.Single(found);
            Assert.Equal(field, found[0].Field);
            Assert.Equal("skill.aura_def.test", found[0].RecordKey);
        }

        [Fact]
        public void ModifierAttributeDuplicate_NonAttributeField_IsNotReported_AndOtherChecksStillApply()
        {
            var ok = RunAura("\"" + FeelTables.AuraFeelModifiersField + "\":[" + W("stride_scale", "multiply", "0.9") + "]");
            Assert.Empty(Of(ok, FeelChecks.ModifierAttributeDuplicate));
            Assert.Equal(0, ok.ErrorCount);

            // 光环写非法操作同样走通用检查。
            var badOp = RunAura("\"" + FeelTables.AuraFeelModifiersField + "\":[" + W("trail_enabled", "add", "true") + "]");
            Assert.Single(Of(badOp, FeelChecks.OpNotAllowed));
        }

        // ------------------------------------------------------------------ 规则登记元数据

        [Fact]
        public void ValidationRules_AreErrorSeverity_AndSilentWhenNoFeelData()
        {
            var rules = new IValidationRule[] { new FeelProfileValidationRule(FeelFields.Default), new FeelHalfIsolationRule(FeelFields.Default), new FeelCalibrationRule() };
            Assert.All(rules, r => Assert.Equal(ValidationSeverity.Error, r.DefaultSeverity));
            Assert.Equal(3, rules.Select(r => r.RuleId).Distinct().Count());
        }
    }
}
