using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Xunit;
using static Tests.Foundation.Feel.FeelTestSupport;

namespace Tests.Foundation.Feel
{
    /// <summary>
    /// 成熟度（手感设计/05 第 8 节、06 第 6 节，ADR-0146）：<c>maturity: validated</c> 必须有 <c>feel.validation</c> 记录覆盖；
    /// 覆盖 = 记录指向该行 + 档案版本一致 + 评分各项不低于升级门槛 + 格子非空。
    /// 复现：没有记录的 validated 行被报错；不变量：框架自带数据永远是 experimental、框架数据根不带任何验证记录，门槛边界按常量判定。
    /// </summary>
    public class FeelMaturityTests
    {
        private const string Character = "feel.character.mat_test";

        private static string CharacterRow(string maturity, int version) =>
            "{\"table\":\"feel.character\",\"schema_version\":1,\"rows\":[{\"id\":\"" + Character + "\",\"maturity\":\"" + maturity
            + "\",\"profile_version\":" + version + ",\"writes\":[]}]}";

        private static string Record(string id, string profileRef, int version, string cell, double move, double turn, double hit, double combo,
            string date = "2026-10-04") =>
            "{\"id\":\"" + id + "\",\"profile_ref\":\"" + profileRef + "\",\"profile_version\":" + version + ",\"cell\":\"" + cell
            + "\",\"data_root_version\":\"1.0.0\",\"pose_set_version\":\"pose-1\",\"device\":\"pc_keyboard_mouse\",\"scores\":{\"move\":"
            + move.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"turn\":" + turn.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"hit_weight\":" + hit.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"combo\":"
            + combo.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},\"date\":\"" + date + "\"}";

        private static string Validation(params string[] records) =>
            "{\"table\":\"feel.validation\",\"schema_version\":1,\"rows\":[" + string.Join(",", records) + "]}";

        private static (IDataRegistry Registry, ValidationReport Report) LoadGame(string characterJson, string? validationJson)
        {
            var tables = FrameworkFeelTables();
            tables.Add(("feel.calibration", CalibrationJson));
            tables.Add(("feel.character", characterJson));
            if (validationJson != null) tables.Add(("feel.validation", validationJson));
            return Load(tables);
        }

        private static List<ValidationIssue> Of(ValidationReport report, string check) =>
            report.Issues.Where(i => i.Check == check).ToList();

        private static double Threshold => FeelMaturity.ScoreThreshold;

        // ------------------------------------------------------------------ 复现

        [Fact]
        public void ValidatedRow_WithoutAnyRecord_IsAnError()
        {
            var (_, report) = LoadGame(CharacterRow("validated", 1), null);

            var issues = Of(report, FeelChecks.ValidatedWithoutRecord);
            Assert.Single(issues);
            Assert.Equal(ValidationSeverity.Error, issues[0].Severity);
            Assert.Equal(Character, issues[0].RecordKey);
        }

        [Fact]
        public void ValidatedRow_WithCoveringRecord_PassesAndLedgerNamesTheCell()
        {
            var (registry, report) = LoadGame(CharacterRow("validated", 1),
                Validation(Record("feel.validation.a", Character, 1, "2_5d_action", Threshold, Threshold + 0.5, 5, Threshold)));

            Assert.Empty(Of(report, FeelChecks.ValidatedWithoutRecord));
            Assert.Equal(0, report.ErrorCount);
            var ledger = FeelValidationLedger.FromRegistry(registry);
            Assert.Equal(new[] { "2_5d_action" }, ledger.ValidatedCells(Character, 1));
            Assert.True(ledger.IsValidatedIn(Character, 1, "2_5d_action"));
            Assert.False(ledger.IsValidatedIn(Character, 1, "3d_action"));
        }

        [Fact]
        public void ExperimentalRow_NeedsNoRecord()
        {
            var (_, report) = LoadGame(CharacterRow("experimental", 1), null);
            Assert.Empty(Of(report, FeelChecks.ValidatedWithoutRecord));
            Assert.Equal(0, report.ErrorCount);
        }

        // ------------------------------------------------------------------ 覆盖的四个条件

        [Fact]
        public void ProfileVersionBump_MakesTheOldRecordStale()
        {
            var (_, report) = LoadGame(CharacterRow("validated", 2),
                Validation(Record("feel.validation.a", Character, 1, "2_5d_action", 5, 5, 5, 5)));

            var issues = Of(report, FeelChecks.ValidatedWithoutRecord);
            Assert.Single(issues);
            Assert.Contains("已过期", issues[0].Message);
        }

        [Fact]
        public void ScoreThresholdBoundary_AtThresholdCovers_JustBelowDoesNot_AndEveryDimensionCounts()
        {
            var below = Threshold - 0.1;
            foreach (var weak in new[] { "move", "turn", "hit_weight", "combo" })
            {
                var scores = new Dictionary<string, double> { ["move"] = 5, ["turn"] = 5, ["hit_weight"] = 5, ["combo"] = 5 };
                scores[weak] = below;
                var (_, report) = LoadGame(CharacterRow("validated", 1),
                    Validation(Record("feel.validation.a", Character, 1, "2_5d_action", scores["move"], scores["turn"], scores["hit_weight"], scores["combo"])));
                var issues = Of(report, FeelChecks.ValidatedWithoutRecord);
                Assert.Single(issues);
                Assert.Contains("评分未达升级门槛", issues[0].Message);
            }

            var (_, atThreshold) = LoadGame(CharacterRow("validated", 1),
                Validation(Record("feel.validation.a", Character, 1, "2_5d_action", Threshold, Threshold, Threshold, Threshold)));
            Assert.Empty(Of(atThreshold, FeelChecks.ValidatedWithoutRecord));
        }

        [Fact]
        public void Ledger_CoversPerCell_AndOnlyCountsRecordsOfTheCurrentVersionAboveThreshold()
        {
            var (registry, _) = LoadGame(CharacterRow("validated", 3), Validation(
                Record("feel.validation.b", Character, 3, "3d_action", 4, 4, 4, 4),
                Record("feel.validation.a", Character, 3, "2_5d_action", 5, 5, 5, 5),
                Record("feel.validation.old", Character, 2, "top_down", 5, 5, 5, 5),
                Record("feel.validation.weak", Character, 3, "side_scroll", 5, 5, 3, 5)));
            var ledger = FeelValidationLedger.FromRegistry(registry);

            Assert.Equal(new[] { "2_5d_action", "3d_action" }, ledger.ValidatedCells(Character, 3));
            Assert.Equal(new[] { "feel.validation.a", "feel.validation.b" }, ledger.Covering(Character, 3).Select(r => r.Id).ToArray());
            Assert.False(ledger.IsValidatedIn(Character, 3, "top_down"));
            Assert.False(ledger.IsValidatedIn(Character, 3, "side_scroll"));
            Assert.Equal(new[] { "top_down" }, ledger.ValidatedCells(Character, 2));
        }

        // ------------------------------------------------------------------ 记录自身的校验

        [Fact]
        public void RecordWithUnknownProfileRef_AndBadDate_AreReported()
        {
            var (_, report) = LoadGame(CharacterRow("experimental", 1), Validation(
                Record("feel.validation.ghost", "feel.character.nope", 1, "2_5d_action", 5, 5, 5, 5),
                Record("feel.validation.baddate", Character, 1, "2_5d_action", 5, 5, 5, 5, date: "2026/10/04")));

            Assert.Single(Of(report, FeelChecks.ValidationProfileMissing));
            Assert.Single(Of(report, FeelChecks.ValidationDateInvalid));
        }

        [Fact]
        public void ScoreOutsideTheOneToFiveRange_IsRejectedBySchema()
        {
            var (_, report) = LoadGame(CharacterRow("experimental", 1),
                Validation(Record("feel.validation.a", Character, 1, "2_5d_action", 6, 5, 5, 5)));
            Assert.True(report.ErrorCount > 0);
        }

        // ------------------------------------------------------------------ 不变量：框架数据永远 experimental

        private static IEnumerable<string> FrameworkFeelFiles()
        {
            var data = Path.Combine(FindRepoRoot(), "data");
            foreach (var root in Directory.GetDirectories(data))
            {
                var feel = Path.Combine(root, "feel");
                if (!Directory.Exists(feel)) continue;
                foreach (var file in Directory.GetFiles(feel, "*.json")) yield return file;
            }
        }

        [Fact]
        public void FrameworkDataRoots_NeverShipValidatedRows_NorValidationRecords()
        {
            var files = FrameworkFeelFiles().ToList();
            Assert.NotEmpty(files);
            foreach (var file in files)
            {
                var text = File.ReadAllText(file);
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, @"""maturity""\s*:\s*""validated"""), file);
                Assert.NotEqual("feel.validation", Path.GetFileNameWithoutExtension(file));
            }
        }

        [Fact]
        public void FrameworkProfileRows_AreAllExperimental_AfterLoading()
        {
            var (registry, report) = LoadFrameworkWithCalibration();
            Assert.Equal(0, report.ErrorCount);
            var count = 0;
            for (var t = 0; t < FeelTables.Profiles.Count; t++)
            {
                if (!registry.TryGetAll(FeelTables.Profiles[t], out var rows)) continue;
                foreach (var row in rows)
                {
                    count++;
                    Assert.False(FeelMaturity.IsMarkedValidated(row), row.Key);
                }
            }
            Assert.True(count > 0);
            Assert.Empty(FeelValidationLedger.FromRegistry(registry).Records);
        }

        [Fact]
        public void FeelTables_ListsTheValidationTable_AndProfilesExcludeNonProfileTables()
        {
            Assert.Contains(FeelTables.Validation, FeelTables.All);
            Assert.DoesNotContain(FeelTables.Validation, FeelTables.Profiles);
            Assert.DoesNotContain(FeelTables.Calibration, FeelTables.Profiles);
        }
    }
}
