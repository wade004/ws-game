using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// 测试覆盖梳理 T-H7（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节）：基线比对契约用例。
    /// <para>
    /// 判断记录（本文件钉的是"门禁契约"，不是新行为）：<see cref="BaselineDiff.ToText"/> 首行
    /// <c>added=&lt;n&gt;</c> 是 <c>toolchain/_gate_line_heavy.ps1</c>"数值仿真基线比对"步骤识别
    /// <c>Added</c> 差异的文本接口（AGENTS.md 第 4 节，一次潜伏 4 天事故的根治点）；<see cref="SimBaseline"/>
    /// 的解析/序列化与 <see cref="BaselineComparer"/> 的状态分类是该门禁的数据基础。既有
    /// <c>BaselineComparerTests</c> 覆盖"同种子重跑全 Same""改数据出现 Exceeded""容差表"，本文件补
    /// <c>Added</c>/<c>Removed</c> 正向断言、文本/JSON 输出格式与顺序确定性、<c>DatasetChanged</c>、
    /// <see cref="SimBaseline.Parse"/> 的异常类型、超过 2^53 的种子往返、指纹与加载顺序无关。
    /// 所有期望由规则（<see cref="BaselineComparer.ResolveTolerance"/> 与状态判定规则）现算，不写死裸数。
    /// 报告一律用 <c>WithRuns(2)</c> 的 coverage 场景（约 1 秒）。
    /// </para>
    /// </summary>
    public class BaselineContractTests
    {
        private static SimReport BuildCoverageReport(ulong worldSeed = 1)
        {
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: worldSeed);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_coverage_all")).WithRuns(2);
            return SimReport.FromCoverageReport(
                CoverageSimulation.Run(scenario, world.AnchorTable!, dataSources), scenario, world.Registry, "test");
        }

        private static SimBaseline BaselineWith(SimReport report, IReadOnlyDictionary<string, double> stats, string? fingerprint = null) =>
            new SimBaseline(
                SimReport.SchemaVersion, report.ScenarioId, report.Kind, fingerprint ?? report.DatasetFingerprint,
                report.GeneratedWithVersion, report.Seed, stats);

        private static Dictionary<string, double> StatsOf(SimReport report) =>
            report.Stats.ToDictionary(s => s.Path, s => s.Value, StringComparer.Ordinal);

        /// <summary>取一批"有限、绝对值足够大"的统计量路径（按 Ordinal 排序），用于构造受控扰动。</summary>
        private static List<string> FinitePaths(SimReport report, int count)
        {
            var paths = report.Stats
                .Where(s => double.IsFinite(s.Value) && Math.Abs(s.Value) > 1e-3)
                .Select(s => s.Path)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Take(count)
                .ToList();
            Assert.Equal(count, paths.Count);
            return paths;
        }

        // ---- Added / Removed 正向断言 ---------------------------------------------------------

        [Fact]
        public void Compare_BaselineMissingOneStat_ReportsExactlyOneAdded_NonBlocking()
        {
            var report = BuildCoverageReport();
            var missing = FinitePaths(report, 1)[0];
            var stats = StatsOf(report);
            stats.Remove(missing);

            var diff = BaselineComparer.Compare(report, BaselineWith(report, stats));

            Assert.Equal(1, diff.AddedCount);
            Assert.Equal(0, diff.RemovedCount);
            Assert.Equal(0, diff.ExceededCount);
            Assert.Equal(report.Stats.Count - 1, diff.SameCount);
            var row = Assert.Single(diff.Rows, r => r.Status == BaselineDiffStatus.Added);
            Assert.Equal(missing, row.Path);
            Assert.Null(row.BaselineValue);
            Assert.Equal(report.Stats.Single(s => s.Path == missing).Value, row.CurrentValue);
            // 契约：Added 单独不算 HasBlockingDifference（simrunner 退出码据此为 0），
            // 门禁靠 ToText 首行 added=<n> 另行拦截——见本类型判断记录。
            Assert.False(diff.HasBlockingDifference);
        }

        [Fact]
        public void Compare_BaselineHasExtraStat_ReportsExactlyOneRemoved_Blocking()
        {
            var report = BuildCoverageReport();
            const string ghostPath = "coverage.creature.creature.ghost_not_in_report.ttk_seconds";
            var stats = StatsOf(report);
            Assert.DoesNotContain(ghostPath, stats.Keys);
            stats[ghostPath] = 42.5;

            var diff = BaselineComparer.Compare(report, BaselineWith(report, stats));

            Assert.Equal(1, diff.RemovedCount);
            Assert.Equal(0, diff.AddedCount);
            Assert.Equal(report.Stats.Count, diff.SameCount);
            var row = Assert.Single(diff.Rows, r => r.Status == BaselineDiffStatus.Removed);
            Assert.Equal(ghostPath, row.Path);
            Assert.Equal(42.5, row.BaselineValue);
            Assert.Null(row.CurrentValue);
            Assert.True(diff.HasBlockingDifference);
        }

        // ---- ToText 首行契约 ------------------------------------------------------------------

        [Fact]
        public void ToText_FirstLine_PinsAddedCountFormat_ForGate()
        {
            var report = BuildCoverageReport();
            var paths = FinitePaths(report, 3);
            var stats = StatsOf(report);
            stats.Remove(paths[0]);
            stats.Remove(paths[1]);
            stats["coverage.ghost.a"] = 1.0;

            var diff = BaselineComparer.Compare(report, BaselineWith(report, stats));
            Assert.Equal(2, diff.AddedCount);
            Assert.Equal(1, diff.RemovedCount);

            var firstLine = diff.ToText().Split('\n')[0].TrimEnd('\r');
            var expected =
                $"scenario={report.ScenarioId.Value} kind={report.Kind} dataset_changed=false " +
                $"same={diff.SameCount.ToString(CultureInfo.InvariantCulture)} within=0 exceeded=0 " +
                $"added={diff.AddedCount.ToString(CultureInfo.InvariantCulture)} removed=1";
            Assert.Equal(expected, firstLine);

            // 门禁脚本按 "added=(\d+)" 逐字段解析首行：字段名、等号、十进制无分隔符整数都不得变。
            var match = System.Text.RegularExpressions.Regex.Match(firstLine, @"\badded=(\d+)\b");
            Assert.True(match.Success);
            Assert.Equal(diff.AddedCount, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void ToText_AddedZero_FirstLineStillCarriesAddedZero()
        {
            var report = BuildCoverageReport();
            var diff = BaselineComparer.Compare(report, SimBaseline.FromReport(report));

            var firstLine = diff.ToText().Split('\n')[0].TrimEnd('\r');
            Assert.Contains(" added=0 ", firstLine + " ");
            Assert.EndsWith("removed=0", firstLine);
        }

        // ---- 顺序确定性 -----------------------------------------------------------------------

        [Fact]
        public void ToJsonAndSortedRows_OrderIsDeterministic_IndependentOfBaselineInsertionOrder()
        {
            var report = BuildCoverageReport();
            var paths = FinitePaths(report, 4);
            var current = StatsOf(report);

            // 构造四种状态各一条：Exceeded、Within、Added（基线里缺）、Removed（基线里多），其余 Same。
            var perturbed = new Dictionary<string, double>(current, StringComparer.Ordinal);
            var options = new BaselineCompareOptions();
            var bandwidths = report.Bandwidths;

            // Exceeded：基线取当前值的两倍加一，偏差 = c+1，取 tolerance 上界 0.5*(2c+1) 比较仍必大于。
            perturbed[paths[0]] = current[paths[0]] * 2 + Math.Sign(current[paths[0]]);
            // Within：基线取当前值 × (1 - 0.1 × 该路径容差比例)，偏差必落在 (eps, tolerance] 内。
            var baseForWithin = current[paths[1]];
            var tolWithin = BaselineComparer.ResolveTolerance(paths[1], baseForWithin, bandwidths, options);
            perturbed[paths[1]] = baseForWithin + tolWithin * 0.1;
            perturbed.Remove(paths[2]); // Added
            perturbed["coverage.ghost.removed"] = 7.0; // Removed

            var baselineA = BaselineWith(report, perturbed);
            // 同内容、相反的插入顺序。
            var reversed = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var kv in perturbed.Reverse()) reversed[kv.Key] = kv.Value;
            var baselineB = BaselineWith(report, reversed);

            var diffA = BaselineComparer.Compare(report, baselineA);
            var diffB = BaselineComparer.Compare(report, baselineB);

            Assert.Equal(diffA.ToJson(), diffB.ToJson());
            Assert.Equal(diffA.ToText(), diffB.ToText());
            Assert.Equal(diffA.ToJson(), BaselineComparer.Compare(report, baselineA).ToJson());

            Assert.Equal(1, diffA.ExceededCount);
            Assert.Equal(1, diffA.WithinCount);
            Assert.Equal(1, diffA.AddedCount);
            Assert.Equal(1, diffA.RemovedCount);

            // SortedRows 规则：状态秩 Exceeded < Removed < Added < Within < Same；同秩按 |偏差| 降序，再按 path Ordinal。
            static int Rank(BaselineDiffStatus s) => s switch
            {
                BaselineDiffStatus.Exceeded => 0,
                BaselineDiffStatus.Removed => 1,
                BaselineDiffStatus.Added => 2,
                BaselineDiffStatus.Within => 3,
                _ => 4,
            };

            var sorted = diffA.SortedRows();
            Assert.Equal(diffA.Rows.Count, sorted.Count);
            Assert.Equal(
                new[] { BaselineDiffStatus.Exceeded, BaselineDiffStatus.Removed, BaselineDiffStatus.Added, BaselineDiffStatus.Within },
                sorted.Take(4).Select(r => r.Status).ToArray());
            for (var i = 1; i < sorted.Count; i++)
            {
                var prev = sorted[i - 1];
                var cur = sorted[i];
                Assert.True(Rank(prev.Status) <= Rank(cur.Status), $"第 {i} 行状态秩倒序：{prev.Status} -> {cur.Status}");
                if (prev.Status == cur.Status)
                {
                    var prevDev = prev.AbsDeviation ?? 0.0;
                    var curDev = cur.AbsDeviation ?? 0.0;
                    Assert.True(
                        prevDev > curDev || (prevDev == curDev && string.CompareOrdinal(prev.Path, cur.Path) <= 0),
                        $"第 {i} 行同状态内顺序违规：{prev.Path}({prevDev}) -> {cur.Path}({curDev})");
                }
            }

            // ToJson 的 rows 数组按 path Ordinal 升序（Rows 本身的顺序，不是 SortedRows），且 *_count 字段与计数一致。
            var json = (JsonObject)JsonReader.Parse(diffA.ToJson());
            var rows = (JsonArray)json["rows"];
            var jsonPaths = rows.Select(r => ((JsonString)((JsonObject)r)["path"]).Value).ToList();
            Assert.Equal(jsonPaths.OrderBy(p => p, StringComparer.Ordinal).ToList(), jsonPaths);
            Assert.Equal(diffA.AddedCount, (int)((JsonNumber)json["added_count"]).Value);
            Assert.Equal(diffA.RemovedCount, (int)((JsonNumber)json["removed_count"]).Value);
            Assert.Equal(diffA.ExceededCount, (int)((JsonNumber)json["exceeded_count"]).Value);
            Assert.Equal(diffA.WithinCount, (int)((JsonNumber)json["within_count"]).Value);
            Assert.Equal(diffA.SameCount, (int)((JsonNumber)json["same_count"]).Value);
        }

        // ---- DatasetChanged ------------------------------------------------------------------

        [Fact]
        public void Compare_BaselineWithDifferentFingerprint_DatasetChangedTrue_ButNotBlocking()
        {
            var report = BuildCoverageReport();
            var stats = StatsOf(report);
            var differentFingerprint = report.DatasetFingerprint == "0000000000000000" ? "ffffffffffffffff" : "0000000000000000";

            var diff = BaselineComparer.Compare(report, BaselineWith(report, stats, differentFingerprint));

            Assert.True(diff.DatasetChanged);
            Assert.False(diff.HasBlockingDifference);
            Assert.Equal(report.Stats.Count, diff.SameCount);
            Assert.Contains(" dataset_changed=true ", diff.ToText().Split('\n')[0]);
            var json = (JsonObject)JsonReader.Parse(diff.ToJson());
            Assert.True(((JsonBool)json["dataset_changed"]).Value);

            var sameFingerprint = BaselineComparer.Compare(report, BaselineWith(report, stats));
            Assert.False(sameFingerprint.DatasetChanged);
        }

        // ---- SimBaseline.Parse 异常类型 -------------------------------------------------------

        private const string ValidBaselineJson =
            "{\"schema_version\":1,\"scenario_id\":\"sim.scenario.x\",\"kind\":\"arena\",\"dataset_fingerprint\":\"abc\"," +
            "\"generated_with_version\":\"1.0.0\",\"seed\":7,\"stats\":{\"a\":1.5}}";

        [Fact]
        public void Parse_ValidMinimalJson_Succeeds()
        {
            var baseline = SimBaseline.Parse(ValidBaselineJson);
            Assert.Equal("sim.scenario.x", baseline.ScenarioId.Value);
            Assert.Equal("arena", baseline.Kind);
            Assert.Equal(7UL, baseline.Seed);
            Assert.Equal(1.5, baseline.Stats["a"]);
        }

        [Theory]
        [InlineData("{bad")]
        [InlineData("")]
        [InlineData("{\"schema_version\":1,")]
        public void Parse_MalformedJson_ThrowsJsonParseException(string text)
        {
            Assert.Throws<JsonParseException>(() => SimBaseline.Parse(text));
        }

        [Theory]
        [InlineData("schema_version")]
        [InlineData("scenario_id")]
        [InlineData("kind")]
        [InlineData("dataset_fingerprint")]
        [InlineData("generated_with_version")]
        [InlineData("seed")]
        public void Parse_MissingRequiredField_ThrowsKeyNotFoundException(string field)
        {
            var obj = (JsonObject)JsonReader.Parse(ValidBaselineJson);
            var builder = new JsonObjectBuilder();
            foreach (var kv in obj)
            {
                if (kv.Key != field) builder.Add(kv.Key, kv.Value);
            }
            var text = JsonWriter.Write(builder.Build());

            Assert.Throws<KeyNotFoundException>(() => SimBaseline.Parse(text));
        }

        [Theory]
        [InlineData("schema_version", "\"one\"")]
        [InlineData("scenario_id", "5")]
        [InlineData("kind", "null")]
        [InlineData("dataset_fingerprint", "[]")]
        [InlineData("generated_with_version", "1")]
        [InlineData("seed", "true")]
        [InlineData("seed", "[]")]
        [InlineData("seed", "\"abc\"")]
        [InlineData("seed", "\"-7\"")]
        public void Parse_WrongFieldType_ThrowsInvalidCastException(string field, string replacementLiteral)
        {
            var text = ValidBaselineJson.Replace(
                ExtractMember(ValidBaselineJson, field), "\"" + field + "\":" + replacementLiteral);

            Assert.Throws<InvalidCastException>(() => SimBaseline.Parse(text));
        }

        [Fact]
        public void Parse_TopLevelNotObject_ThrowsInvalidCastException()
        {
            Assert.Throws<InvalidCastException>(() => SimBaseline.Parse("[1,2,3]"));
        }

        [Fact]
        public void Parse_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => SimBaseline.Parse(null!));
        }

        [Fact]
        public void Parse_StatsMissingOrNonNumeric_MissingIsEmpty_NonNumericBecomesNaN()
        {
            var noStats = ValidBaselineJson.Replace(",\"stats\":{\"a\":1.5}", "");
            Assert.Empty(SimBaseline.Parse(noStats).Stats);

            var nonNumeric = ValidBaselineJson.Replace("\"a\":1.5", "\"a\":null,\"b\":\"text\",\"c\":2");
            var stats = SimBaseline.Parse(nonNumeric).Stats;
            Assert.True(double.IsNaN(stats["a"]));
            Assert.True(double.IsNaN(stats["b"]));
            Assert.Equal(2.0, stats["c"]);
        }

        /// <summary>取 <c>"field":value</c> 的原文片段（value 只支持本文件基线样例里出现的标量形态）。</summary>
        private static string ExtractMember(string json, string field)
        {
            var start = json.IndexOf("\"" + field + "\":", StringComparison.Ordinal);
            Assert.True(start >= 0, field);
            var i = start + field.Length + 3;
            if (json[i] == '"')
            {
                i = json.IndexOf('"', i + 1) + 1;
            }
            else
            {
                while (i < json.Length && json[i] != ',' && json[i] != '}') i++;
            }
            return json.Substring(start, i - start);
        }

        // ---- 大种子往返 ----------------------------------------------------------------------

        private static void AssertSeedRoundTrips(ulong seed)
        {
            var baseline = new SimBaseline(
                SimReport.SchemaVersion, new Id("sim.scenario.x"), "arena", "abc", "1.0.0", seed,
                new Dictionary<string, double>(StringComparer.Ordinal) { ["a"] = 1.0 });

            var parsed = SimBaseline.Parse(baseline.ToJson());

            Assert.Equal(seed, parsed.Seed);
        }

        /// <summary>double 能精确表示的种子（含边界 2^53）往返必须逐位一致。</summary>
        [Theory]
        [InlineData(0UL)]
        [InlineData(20260916UL)]
        [InlineData(4294967296UL)]            // 2^32
        [InlineData(9007199254740992UL)]      // 2^53
        public void SeedWithinDoublePrecision_RoundTripsThroughBaselineJson_Exactly(ulong seed)
        {
            AssertSeedRoundTrips(seed);
        }

        /// <summary>缺陷修复回归（2026-10-01 探针暴露、已修）：<c>SimBaseline.ToJson</c> 此前用
        /// <c>new JsonNumber(Seed)</c>（ulong 隐式转 double）写种子，超过 2^53 的种子不能往返（2^53+1 读回成 2^53，
        /// <c>ulong.MaxValue</c> 读回成 0）。现 <c>ToJson</c> 把种子写成十进制字符串，<c>Parse</c> 逐位精确读回。</summary>
        [Theory]
        [InlineData(9007199254740993UL)]      // 2^53 + 1
        [InlineData(18446744073709551615UL)]  // ulong.MaxValue
        public void SeedAboveDoublePrecision_RoundTripsThroughBaselineJson_Exactly(ulong seed)
        {
            AssertSeedRoundTrips(seed);
        }

        /// <summary>新写法：种子以十进制字符串落盘（不再是 JSON 数字），这是大种子逐位精确的前提。</summary>
        [Fact]
        public void ToJson_WritesSeedAsDecimalString()
        {
            var baseline = new SimBaseline(
                SimReport.SchemaVersion, new Id("sim.scenario.x"), "arena", "abc", "1.0.0", ulong.MaxValue,
                new Dictionary<string, double>(StringComparer.Ordinal) { ["a"] = 1.0 });

            var obj = (JsonObject)JsonReader.Parse(baseline.ToJson());

            Assert.Equal(ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.IsType<JsonString>(obj["seed"]).Value);
        }

        /// <summary>旧基线兼容：仓库里已入库的基线（种子是 JSON 数字）无需重写，<c>Parse</c> 仍按数字读回。</summary>
        [Theory]
        [InlineData(0UL)]
        [InlineData(7UL)]
        [InlineData(20260916UL)]
        [InlineData(9007199254740992UL)]      // 2^53，旧写法仍能精确表示的上界
        public void Parse_LegacyNumericSeed_StillParses(ulong seed)
        {
            var legacyJson = ValidBaselineJson.Replace(
                "\"seed\":7", "\"seed\":" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var parsed = SimBaseline.Parse(legacyJson);

            Assert.Equal(seed, parsed.Seed);
        }

        /// <summary>新旧两种写法读出同一个种子（字符串写法与数字写法等价）。</summary>
        [Fact]
        public void Parse_StringSeed_EqualsNumericSeed()
        {
            var asString = ValidBaselineJson.Replace("\"seed\":7", "\"seed\":\"7\"");

            Assert.Equal(SimBaseline.Parse(ValidBaselineJson).Seed, SimBaseline.Parse(asString).Seed);
        }

        // ---- 数据集指纹与加载顺序无关 --------------------------------------------------------

        private sealed class ReversedTableOrderSource : IDataSource
        {
            private readonly IDataSource _inner;

            public ReversedTableOrderSource(IDataSource inner)
            {
                _inner = inner;
            }

            public string? Root => _inner.Root;

            public IReadOnlyList<DataTableSource> ListTables() => _inner.ListTables().Reverse().ToList();
        }

        private static IDataRegistryView BuildEmbeddedRegistry(IReadOnlyList<IDataSource> sources)
        {
            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = sources,
                Seed = 1,
                FileSystem = new Adapters.Stub.StubFileSystem(),
                MapId = SimTestWorldFactory.EmbeddedMapId,
                PlayerId = SimTestWorldFactory.PlayerId,
                PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                GameId = SimTestWorldFactory.EmbeddedGameId,
                StepSeconds = SimTestWorldFactory.StepSeconds,
                FailOnUnknownTable = false,
            });
            return world.Registry;
        }

        [Fact]
        public void ComputeDatasetFingerprint_IsIndependentOfTableEnumerationOrder()
        {
            var straight = BuildEmbeddedRegistry(SimTestWorldFactory.BuildEmbeddedDataSources());
            var reversedSources = SimTestWorldFactory.BuildEmbeddedDataSources()
                .Select(s => (IDataSource)new ReversedTableOrderSource(s))
                .ToList();
            var reversed = BuildEmbeddedRegistry(reversedSources);

            // 前置：两个注册表确实装了同一批表（否则比较无意义）。
            Assert.Equal(
                straight.Tables.OrderBy(t => t, StringComparer.Ordinal).ToList(),
                reversed.Tables.OrderBy(t => t, StringComparer.Ordinal).ToList());
            Assert.True(straight.Tables.Count > 10);

            Assert.Equal(
                SimReport.ComputeDatasetFingerprint(straight),
                SimReport.ComputeDatasetFingerprint(reversed));
        }

        [Fact]
        public void ComputeDatasetFingerprint_ChangesWhenAnyRecordChanges()
        {
            var straight = BuildEmbeddedRegistry(SimTestWorldFactory.BuildEmbeddedDataSources());
            var baselineFingerprint = SimReport.ComputeDatasetFingerprint(straight);

            // 覆盖 arch.class.sim_warrior 的力量基础值（同 BaselineComparerTests 的覆盖做法），指纹必须变。
            var sources = new List<IDataSource>(SimTestWorldFactory.BuildEmbeddedDataSources())
            {
                BuildStrengthOverrideSource(),
            };
            var changed = BuildEmbeddedRegistry(sources);

            Assert.NotEqual(baselineFingerprint, SimReport.ComputeDatasetFingerprint(changed));
            // 指纹格式：16 位小写十六进制。
            Assert.Matches("^[0-9a-f]{16}$", baselineFingerprint);
        }

        private static IDataSource BuildStrengthOverrideSource()
        {
            const string json = @"{
  ""table"": ""arch.class"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""arch.class.sim_warrior"",
      ""override"": true,
      ""name_key"": ""l10n.class.sim_warrior.name"",
      ""primary_stat"": ""stat.strength"",
      ""base_stats"": {
        ""stat.strength"": 200,
        ""stat.agility"": 6,
        ""stat.intellect"": 4,
        ""stat.stamina"": 150
      },
      ""power_types"": [
        ""arch.power.health"",
        ""arch.power.sim_fury""
      ],
      ""skill_book_ref"": ""skill.book.sim_warrior"",
      ""talent_tree_ref"": ""arch.talent_tree.sim_warrior"",
      ""level_curve_ref"": ""prog.level_curve.sim_warrior"",
      ""derivation_overrides"": [
        {
          ""stat"": ""stat.attack_power"",
          ""source"": ""stat.strength"",
          ""coefficient"": 1.0
        }
      ]
    }
  ]
}";
            var fs = new Adapters.Stub.StubFileSystem();
            fs.WriteTextAtomic("override/arch/arch.class.json", json);
            return new FileSystemDataSource(fs, "override");
        }
    }
}
