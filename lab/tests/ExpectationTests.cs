using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Core.Foundation.Common.Json;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 脚本期望清单（手感设计 06 第 3.1 节 <c>expectations</c>）与"导出为测试"产出期望（第 3.5 节）：
    /// 格式与版本、解析诊断、运行期判定与失败诊断、套件与命令行接线、导出生成与再导出。
    /// 期望值由度量声明的比较规则（允差类别）与真实指纹在用例里算出，不写死裸数。
    /// </summary>
    public sealed class ExpectationTests : IDisposable
    {
        private readonly string _temp = Path.Combine(Path.GetTempPath(), "lab_expect_" + Guid.NewGuid().ToString("N"));

        public ExpectationTests()
        {
            Directory.CreateDirectory(_temp);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_temp, true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响用例结论。
            }
        }

        // ---------- 辅助 ----------

        private static Expectation Eq(string id, string metric, JsonValue value, params string[] cells) =>
            new Expectation(id, cells, new MetricSelector(metric), ExpectOp.Eq, value, null, null, null, null, string.Empty);

        private static Expectation Op(string id, string metric, ExpectOp op, double value, params string[] cells) =>
            new Expectation(id, cells, new MetricSelector(metric), op, LabJson.Num(value), null, null, null, null, string.Empty);

        private static Expectation Between(string id, string metric, double? min, double? max, params string[] cells) =>
            new Expectation(id, cells, new MetricSelector(metric), ExpectOp.Between, null, min, max, null, null, string.Empty);

        private static Expectation Versus(string id, string metric, ExpectOp op, string otherCell, string otherMetric, params string[] cells) =>
            new Expectation(
                id, cells, new MetricSelector(metric), op, null, null, null, null,
                new ExpectVersus(otherCell, new MetricSelector(otherMetric)), string.Empty);

        private static List<ExpectationResult> Evaluate(InputScript script, string cell, IEnumerable<Expectation> expectations)
        {
            var runner = LabTestSupport.Runner;
            var cache = new Dictionary<string, Fingerprint>(StringComparer.Ordinal);
            var withExpectations = script.WithExpectations(expectations.ToList());
            var actual = runner.Run(withExpectations, cell);
            return LabSuite.EvaluateExpectations(runner, withExpectations, cell, actual, cache);
        }

        private static double Num(Fingerprint fp, string path)
        {
            var dot = path.IndexOf('.');
            return ((JsonNumber)((JsonObject)fp.Groups[path.Substring(0, dot)])[path.Substring(dot + 1)]).Value;
        }

        private string Fixtures(string name) => Path.Combine(_temp, name);

        private static void CopyBaseline(string scriptId, string fixtures)
        {
            var target = LabFixtures.BaselinePath(fixtures, scriptId);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(LabFixtures.BaselinePath(LabTestSupport.FixturesDir, scriptId), target);
        }

        private static void WriteScript(InputScript script, string fixtures)
        {
            var path = LabFixtures.ScriptPath(fixtures, script.Meta.ScriptId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, script.ToJson(), new UTF8Encoding(false));
        }

        // ---------- 格式与版本 ----------

        [Fact]
        public void Script_WithExpectations_WritesVersion4_AndRoundTripsByteIdentically_ButLegacyVersionsStayReadable()
        {
            var kill = LabTestSupport.Script("feel_kill");
            var withExpect = kill.WithExpectations(new[]
            {
                Eq("kills", "attack.kills", LabJson.Num(1)),
                Between("band", "movement.speed_steady", 0, 6, "2d_action"),
                Versus("vs", "hitstop.started_ticks_player", ExpectOp.Gt, "2d_targeted", "hitstop.started_ticks_player", "2d_action"),
            });
            Assert.Equal(InputScript.ExpectFormatVersion, withExpect.EffectiveFormatVersion);
            var text = withExpect.ToJson();
            Assert.Contains("\"formatVersion\": " + InputScript.ExpectFormatVersion, text);
            Assert.Equal(text, InputScript.Parse(text).ToJson());
            Assert.Equal(3, InputScript.Parse(text).Expectations.Count);

            // 没有期望的脚本仍按原格式版本写，序列化文本与加入期望能力之前逐字相同（版本 1 / 2 / 3 各取一个）。
            foreach (var id in new[] { "move_tap", "equip_cycle", "feel_melee" })
            {
                var legacy = LabTestSupport.Script(id);
                Assert.Empty(legacy.Expectations);
                Assert.True(legacy.EffectiveFormatVersion < InputScript.ExpectFormatVersion);
                Assert.DoesNotContain("expectations", legacy.ToJson());
            }

            // 旧格式文本（版本 1，没有 expectations 键）照常读取；更高版本照旧拒绝。
            var oldText = "{\"formatVersion\":1,\"meta\":{\"scriptId\":\"x\",\"scriptVersion\":1,\"tickRate\":50,\"frameRateCap\":60,\"durationTicks\":5},\"events\":[]}";
            Assert.Empty(InputScript.Parse(oldText).Expectations);
            var tooNew = "{\"formatVersion\":" + (InputScript.MaxSupportedFormatVersion + 1) + ",\"meta\":{},\"events\":[]}";
            Assert.Throws<LabFormatException>(() => InputScript.Parse(tooNew));
        }

        [Fact]
        public void ExpectationsAreNotPartOfTheScriptIdentity_AddingThemKeepsTheBaselineKeyAndFingerprint()
        {
            // 不变量：期望清单不改脚本的行为身份——同一脚本带不带期望，指纹（键与全部度量）逐字相同，基线因此不失效。
            var runner = LabTestSupport.Runner;
            var plain = LabTestSupport.Script("feel_melee");
            var with = plain.WithExpectations(new[] { Op("any", "attack.damage_events", ExpectOp.Ge, 0) });
            var a = runner.Run(plain, "2d_action");
            var b = runner.Run(with, "2d_action");
            Assert.Equal(LabJson.Write(a.Key), LabJson.Write(b.Key));
            Assert.Equal(
                a.Project(runner.Registry, MetricClass.Logic, MetricClass.Presentation),
                b.Project(runner.Registry, MetricClass.Logic, MetricClass.Presentation));
        }

        [Theory]
        [InlineData("{\"metric\":\"a.b\",\"op\":\"nope\",\"value\":1}", "op 未知")]
        [InlineData("{\"metric\":\"a.b\",\"op\":\"eq\",\"value\":1,\"valu\":2}", "未知字段")]
        [InlineData("{\"metric\":\"a\",\"op\":\"eq\",\"value\":1}", "组.度量")]
        [InlineData("{\"metric\":\"a.b\",\"op\":\"eq\"}", "value 或 versus")]
        [InlineData("{\"metric\":\"a.b\",\"op\":\"eq\",\"value\":1,\"versus\":{\"metric\":\"a.c\"}}", "value 或 versus")]
        [InlineData("{\"metric\":\"a.b\",\"op\":\"between\"}", "min 或 max")]
        [InlineData("{\"metric\":\"a.b\",\"op\":\"between\",\"min\":5,\"max\":1}", "大于 max")]
        [InlineData("{\"metric\":\"a.b\",\"op\":\"ge\",\"value\":\"x\"}", "必须是数值")]
        [InlineData("{\"metric\":\"a.b\",\"op\":\"eq\",\"value\":1,\"tolerance\":1}", "tolerance 只用于 approx")]
        [InlineData("{\"metric\":\"a.b\",\"at\":0,\"agg\":\"sum\",\"op\":\"eq\",\"value\":1}", "at 与 agg")]
        [InlineData("{\"metric\":\"a.b\",\"agg\":\"median\",\"op\":\"eq\",\"value\":1}", "agg 未知")]
        [InlineData("{\"metric\":\"a.b\",\"op\":\"in\",\"value\":1}", "必须是数组")]
        public void MalformedExpectation_IsRejectedWithAPathedDiagnostic(string expectation, string expectedFragment)
        {
            var text = "{\"formatVersion\":4,\"meta\":{\"scriptId\":\"x\",\"scriptVersion\":1,\"tickRate\":50,\"frameRateCap\":60,\"durationTicks\":5},"
                + "\"events\":[],\"expectations\":[" + expectation + "]}";
            var ex = Assert.Throws<LabFormatException>(() => InputScript.Parse(text, "demo.script.json"));
            Assert.Contains("demo.script.json.expectations[0]", ex.Message);
            Assert.Contains(expectedFragment, ex.Message);
        }

        [Fact]
        public void DuplicateExpectationIds_AreRejected()
        {
            var text = "{\"formatVersion\":4,\"meta\":{\"scriptId\":\"x\",\"scriptVersion\":1,\"tickRate\":50,\"frameRateCap\":60,\"durationTicks\":5},\"events\":[],"
                + "\"expectations\":[{\"id\":\"same\",\"metric\":\"a.b\",\"op\":\"eq\",\"value\":1},{\"id\":\"same\",\"metric\":\"a.b\",\"op\":\"eq\",\"value\":2}]}";
            var ex = Assert.Throws<LabFormatException>(() => InputScript.Parse(text));
            Assert.Contains("id 重复", ex.Message);
        }

        // ---------- 运行期判定 ----------

        [Fact]
        public void EveryOperator_AgreesWithAnIndependentComputation_OnARealFingerprint()
        {
            // 不变量：对真实指纹里的每个数值度量，每个运算符的判定都等于直接用数值比较算出的结果（对期望值取 实际 - 1、实际、实际 + 1）。
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("feel_combo3");
            var fp = runner.Run(script, "2d_action");
            var checkedMetrics = 0;
            foreach (var group in runner.Registry.Groups)
            {
                if (!fp.Groups.TryGetValue(group.Name, out var gv))
                {
                    continue;
                }

                foreach (var spec in group.Specs)
                {
                    var raw = ((JsonObject)gv)[spec.Name];
                    if (!(raw is JsonNumber n) || spec.Class == MetricClass.RealTime)
                    {
                        continue;
                    }

                    checkedMetrics++;
                    var actual = n.Value;
                    foreach (var delta in new[] { -1.0, 0.0, 1.0 })
                    {
                        var want = actual + delta;
                        var metric = group.Name + "." + spec.Name;
                        var results = Evaluate(script, "2d_action", new[]
                        {
                            Op("eq", metric, ExpectOp.Eq, want),
                            Op("ne", metric, ExpectOp.Ne, want),
                            Op("lt", metric, ExpectOp.Lt, want),
                            Op("le", metric, ExpectOp.Le, want),
                            Op("gt", metric, ExpectOp.Gt, want),
                            Op("ge", metric, ExpectOp.Ge, want),
                            Between("btw", metric, want, want + 1),
                            Between("lo", metric, want, null),
                            Between("hi", metric, null, want),
                            new Expectation("apx", null, new MetricSelector(metric), ExpectOp.Approx, LabLabel(want), null, null, 0.5, null, string.Empty),
                        });
                        var byId = results.ToDictionary(r => r.Expectation.Id, r => r.Ok);
                        Assert.Equal(actual == want, byId["eq"]);
                        Assert.Equal(actual != want, byId["ne"]);
                        Assert.Equal(actual < want, byId["lt"]);
                        Assert.Equal(actual <= want, byId["le"]);
                        Assert.Equal(actual > want, byId["gt"]);
                        Assert.Equal(actual >= want, byId["ge"]);
                        Assert.Equal(actual >= want && actual <= want + 1, byId["btw"]);
                        Assert.Equal(actual >= want, byId["lo"]);
                        Assert.Equal(actual <= want, byId["hi"]);
                        Assert.Equal(Math.Abs(actual - want) <= 0.5, byId["apx"]);
                    }
                }
            }

            Assert.True(checkedMetrics > 20, "应覆盖真实指纹里的大量数值度量");
        }

        private static JsonNumber LabLabel(double value) => LabJson.Num(value);

        [Fact]
        public void FailedExpectation_ReportsWhatWasRequiredAndWhatWasMeasured()
        {
            var script = LabTestSupport.Script("feel_kill");
            var fp = LabTestSupport.Runner.Run(script, "2d_action");
            var kills = Num(fp, "attack.kills");

            var results = Evaluate(script, "2d_action", new[]
            {
                Op("kills_too_many", "attack.kills", ExpectOp.Eq, kills + 1),
                Between("kills_band", "attack.kills", kills + 1, kills + 5),
                Eq("reaction_text", "reaction.reaction_counts", LabJson.Str("Stagger:9")),
            });

            Assert.All(results, r => Assert.Equal(ExpectStatus.Fail, r.Status));
            Assert.Contains("要求 == " + LabJson.Fmt(kills + 1), results[0].Message);
            Assert.Contains("实际 " + LabJson.Fmt(kills), results[0].Message);
            Assert.Contains("差 -1", results[0].Message);
            Assert.Contains("低于下界", results[1].Message);
            Assert.Contains("[" + LabJson.Fmt(kills + 1), results[1].Message);
            Assert.Contains("\"Stagger:9\"", results[2].Message);
            Assert.Contains("\"Death:1\"", results[2].Message);

            // 整条诊断带上期望 id、格子与度量名，在套件输出里能直接定位。
            Assert.Contains("[kills_too_many] 2d_action attack.kills", results[0].ToString());
        }

        [Fact]
        public void CrossCellRelation_ComparesAnotherCellsMetric_WithScaleAndOffset()
        {
            // 动作式格子的攻击方顿帧 tick 数（事实）大于目标选择式格子（经典预设无顿帧）；比例与偏移按 other × scale + offset 换算。
            var script = LabTestSupport.Script("feel_kill");
            var runner = LabTestSupport.Runner;
            var action = Num(runner.Run(script, "2d_action"), "hitstop.started_ticks_player");
            var targeted = Num(runner.Run(script, "2d_targeted"), "hitstop.started_ticks_player");
            Assert.True(action > targeted);

            var ok = Versus("more_freeze", "hitstop.started_ticks_player", ExpectOp.Gt, "2d_targeted", "hitstop.started_ticks_player", "2d_action");
            var wrongWay = Versus("less_freeze", "hitstop.started_ticks_player", ExpectOp.Le, "2d_targeted", "hitstop.started_ticks_player", "2d_action");
            var scaled = new Expectation(
                "scaled", new[] { "2d_action" }, new MetricSelector("hitstop.started_ticks_player"), ExpectOp.Eq, null, null, null, null,
                new ExpectVersus("2d_targeted", new MetricSelector("hitstop.started_ticks_player"), 2.0, action), string.Empty);
            var results = Evaluate(script, "2d_action", new[] { ok, wrongWay, scaled });
            Assert.True(results[0].Ok);
            Assert.False(results[1].Ok);
            Assert.Contains("2d_targeted:hitstop.started_ticks_player", results[1].Message);
            Assert.Contains("实际 " + LabJson.Fmt(action), results[1].Message);
            Assert.True(results[2].Ok, "targeted × 2 + action 恒等于 action（targeted 为 0 时）：" + results[2]);
        }

        [Fact]
        public void IndexAndAggregateSelectors_ReduceArrayMetrics()
        {
            var script = LabTestSupport.Script("feel_combo3");
            var fp = LabTestSupport.Runner.Run(script, "2d_action");
            var totals = ((JsonArray)((JsonObject)fp.Groups["actiontl"])["action_total_ticks"]).Select(v => ((JsonNumber)v).Value).ToList();
            Assert.True(totals.Count >= 3);

            Expectation Sel(string id, int? at, string agg, ExpectOp op, double value) =>
                new Expectation(id, null, new MetricSelector("actiontl.action_total_ticks", at, agg), op, LabJson.Num(value), null, null, null, null, string.Empty);

            var results = Evaluate(script, "2d_action", new[]
            {
                Sel("first", 0, string.Empty, ExpectOp.Eq, totals[0]),
                Sel("last", -1, string.Empty, ExpectOp.Eq, totals[totals.Count - 1]),
                Sel("len", null, "len", ExpectOp.Eq, totals.Count),
                Sel("sum", null, "sum", ExpectOp.Eq, totals.Sum()),
                Sel("min", null, "min", ExpectOp.Eq, totals.Min()),
                Sel("max", null, "max", ExpectOp.Eq, totals.Max()),
                Sel("oob", totals.Count + 3, string.Empty, ExpectOp.Eq, 0),
            });
            Assert.All(results.Take(6), r => Assert.True(r.Ok, r.ToString()));
            Assert.Equal(ExpectStatus.Error, results[6].Status);
            Assert.Contains("越界", results[6].Message);
        }

        [Fact]
        public void InAndContainsOperators_WorkOnTextAndArrays()
        {
            var script = LabTestSupport.Script("feel_combo3");
            var results = Evaluate(script, "2d_action", new[]
            {
                new Expectation("in_ok", null, new MetricSelector("reaction.hit_results"), ExpectOp.In, new JsonArray(new JsonValue[] { LabJson.Str("Miss:1"), LabJson.Str("Hit:3") }), null, null, null, null, string.Empty),
                new Expectation("in_bad", null, new MetricSelector("reaction.hit_results"), ExpectOp.In, new JsonArray(new JsonValue[] { LabJson.Str("Miss:1") }), null, null, null, null, string.Empty),
                new Expectation("contains_text", null, new MetricSelector("reaction.reaction_counts"), ExpectOp.Contains, LabJson.Str("Knockback"), null, null, null, null, string.Empty),
                new Expectation("contains_missing", null, new MetricSelector("reaction.reaction_counts"), ExpectOp.Contains, LabJson.Str("Death"), null, null, null, null, string.Empty),
                new Expectation("contains_array", null, new MetricSelector("actiontl.action_total_ticks"), ExpectOp.Contains, LabJson.Num(((JsonNumber)((JsonArray)((JsonObject)LabTestSupport.Runner.Run(script, "2d_action").Groups["actiontl"])["action_total_ticks"])[0]).Value), null, null, null, null, string.Empty),
            });
            Assert.True(results[0].Ok);
            Assert.False(results[1].Ok);
            Assert.True(results[2].Ok);
            Assert.False(results[3].Ok);
            Assert.True(results[4].Ok);
        }

        [Fact]
        public void Unjudgeable_Expectations_AreErrorsWithDiagnostics_NotSilentPasses()
        {
            var script = LabTestSupport.Script("feel_kill");
            var results = Evaluate(script, "2d_action", new[]
            {
                Op("typo_group", "attak.kills", ExpectOp.Eq, 1),
                Op("typo_metric", "attack.kil", ExpectOp.Eq, 1),
                Op("text_as_number", "reaction.reaction_counts", ExpectOp.Ge, 1),
                Versus("bad_other_cell", "attack.kills", ExpectOp.Eq, "9d_nowhere", "attack.kills"),
            });
            Assert.All(results, r => Assert.Equal(ExpectStatus.Error, r.Status));
            Assert.Contains("度量组 attak 不存在", results[0].Message);
            Assert.Contains("kills", results[1].Message); // 列出该组已有的度量名
            Assert.Contains("数值", results[2].Message);
            Assert.Contains("9d_nowhere", results[3].Message);

            // 条件度量组（手感组）只在 feel 脚本里出现：旧脚本上断言它给出明确的"没有该组"诊断。
            var legacy = LabTestSupport.Script("move_tap");
            var onLegacy = Evaluate(legacy, "2d_action", new[] { Op("feel_group", "hitstop.started_ticks_player", ExpectOp.Eq, 0) });
            Assert.Equal(ExpectStatus.Error, onLegacy[0].Status);
            Assert.Contains("没有度量组 hitstop", onLegacy[0].Message);
        }

        [Fact]
        public void CellFilter_RestrictsWhereAnExpectationApplies()
        {
            var script = LabTestSupport.Script("feel_kill");
            var fp = LabTestSupport.Runner.Run(script, "2d_targeted");
            var only = Op("only_action", "attack.kills", ExpectOp.Eq, 999, "2d_action");
            Assert.Empty(ExpectationEvaluator.Evaluate(new[] { only }, "2d_targeted", fp, LabTestSupport.Runner.Registry, c => fp));
            Assert.Single(ExpectationEvaluator.Evaluate(new[] { only }, "2d_action", fp, LabTestSupport.Runner.Registry, c => fp));
        }

        // ---------- 套件与命令行接线 ----------

        [Fact]
        public void Suite_ExpectationFailure_MarksTheCellAsDiff_EvenWhenTheBaselineMatches()
        {
            var fixtures = Fixtures("suite_fail");
            var kill = LabTestSupport.Script("feel_kill");
            CopyBaseline("feel_kill", fixtures);

            // 通过的期望：格子仍是 Pass，且结果里带着期望判定。
            WriteScript(kill.WithExpectations(new[] { Op("kills", "attack.kills", ExpectOp.Eq, 1) }), fixtures);
            var ok = LabSuite.Check(LabTestSupport.Runner, fixtures, "feel_kill", "2d_action");
            Assert.Single(ok);
            Assert.Equal(CellStatus.Pass, ok[0].Status);
            Assert.Single(ok[0].Expectations);
            Assert.Equal(0, ok[0].ExpectationFailures);

            // 失败的期望：基线逐项一致，但格子算"有差异"，逐条诊断在结果里。
            WriteScript(kill.WithExpectations(new[] { Op("kills", "attack.kills", ExpectOp.Eq, 2) }), fixtures);
            var bad = LabSuite.Check(LabTestSupport.Runner, fixtures, "feel_kill", "2d_action");
            Assert.Equal(CellStatus.Diff, bad[0].Status);
            Assert.True(bad[0].Diff!.Ok, "基线本身没有差异");
            Assert.Equal(1, bad[0].ExpectationFailures);
            Assert.Contains("[kills] 2d_action attack.kills", bad[0].Expectations[0].ToString());
        }

        [Fact]
        public void Suite_StaticallyInvalidExpectations_AreScriptContentErrors()
        {
            var fixtures = Fixtures("suite_invalid");
            CopyBaseline("feel_kill", fixtures);
            var kill = LabTestSupport.Script("feel_kill");
            WriteScript(kill.WithExpectations(new[] { Op("typo", "attack.kils", ExpectOp.Eq, 1, "2d_acton") }), fixtures);
            var ex = Assert.Throws<LabFormatException>(() => LabSuite.Check(LabTestSupport.Runner, fixtures, "feel_kill"));
            Assert.Contains("度量组 attack 没有度量 kils", ex.Message);
            Assert.Contains("2d_acton", ex.Message);
        }

        [Fact]
        public void CommittedScriptsWithExpectations_PassOnEveryCellTheyApplyTo()
        {
            // 随仓库入库的示范期望（若干既有脚本）在六个格子上全部通过；至少有一份带跨格子相对关系、一份带下标/聚合选择器。
            var withExpectations = LabTestSupport.AllScripts().FindAll(s => s.Expectations.Count > 0);
            Assert.True(withExpectations.Count >= 5);
            Assert.Contains(withExpectations, s => s.Expectations.Any(e => e.Versus != null && e.Versus.Cell.Length > 0));
            Assert.Contains(withExpectations, s => s.Expectations.Any(e => e.Subject.At.HasValue || e.Subject.Agg.Length > 0));
            foreach (var script in withExpectations)
            {
                var results = LabSuite.Check(LabTestSupport.Runner, LabTestSupport.FixturesDir, script.Meta.ScriptId);
                Assert.Equal(LabTestSupport.Runner.ApplicableCells(script).Count, results.Count); // 空间脚本适用十个格子，其余六个
                foreach (var r in results)
                {
                    Assert.True(r.Status == CellStatus.Pass, $"{r.Script} @ {r.Cell}：{string.Join("；", r.Expectations.Where(e => !e.Ok).Select(e => e.ToString()))}");
                    Assert.True(r.Expectations.Count > 0 || script.Expectations.All(e => e.Cells.Count > 0 && !e.Cells.Contains(r.Cell)));
                }
            }
        }

        // ---------- 导出为测试 ----------

        private static List<KeyValuePair<string, Fingerprint>> Cells(string scriptId)
        {
            var script = LabTestSupport.Script(scriptId);
            return LabTestSupport.AllCells.Select(c => new KeyValuePair<string, Fingerprint>(c, LabTestSupport.Runner.Run(script, c))).ToList();
        }

        [Fact]
        public void Export_GeneratesExpectations_ThatPassOnTheSameRun_ForEveryFixtureScriptAndCell()
        {
            // 不变量：从一次运行导出的期望，在同一次运行上必然全部通过（逻辑类精确、表现类带度量声明的允差、实时类用倍率上限）。
            var runner = LabTestSupport.Runner;
            foreach (var script in LabTestSupport.AllScripts())
            {
                var cells = new List<KeyValuePair<string, Fingerprint>>();
                foreach (var cell in runner.ApplicableCells(script))
                {
                    cells.Add(new KeyValuePair<string, Fingerprint>(cell.Cell, runner.Run(script, cell.Cell)));
                }

                var generated = ExpectationExporter.FromFingerprints(cells, runner.Registry, new ExpectationExportOptions { IncludeRealTime = true });
                Assert.NotEmpty(generated);
                Assert.All(generated, e => Assert.StartsWith(Expectation.AutoPrefix, e.Id));
                Assert.Equal(generated.Count, generated.Select(e => e.Id).Distinct().Count());
                foreach (var cell in cells)
                {
                    var results = ExpectationEvaluator.Evaluate(generated, cell.Key, cell.Value, runner.Registry, c => cells.First(x => x.Key == c).Value);
                    var failures = results.Where(r => !r.Ok).Select(r => r.ToString()).ToList();
                    Assert.True(failures.Count == 0, $"{script.Meta.ScriptId} @ {cell.Key}：{string.Join("；", failures)}");
                }
            }
        }

        [Fact]
        public void Export_UsesTheMetricsDeclaredToleranceByClass_AndMergesEqualCells()
        {
            var runner = LabTestSupport.Runner;
            var cells = Cells("feel_kill");
            var generated = ExpectationExporter.FromFingerprints(cells, runner.Registry, new ExpectationExportOptions { IncludeRealTime = true });
            var byId = generated.ToDictionary(e => e.Id);

            // 逻辑类数值：精确；全部格子取值相同的度量合并成不带 cells 的一条。
            var kills = byId["auto.attack.kills"];
            Assert.Equal(ExpectOp.Eq, kills.Op);
            Assert.Empty(kills.Cells);
            Assert.Equal(Num(cells[0].Value, "attack.kills"), ((JsonNumber)kills.Value!).Value);

            // 取值随格子不同的度量按取值分组、带 cells：动作式三格与目标选择式三格各一条。
            var freeze = generated.Where(e => e.Subject.Metric == "hitstop.started_ticks_player").ToList();
            Assert.Equal(2, freeze.Count);
            var groupedCells = freeze.Select(e => string.Join("+", e.Cells.OrderBy(c => c, StringComparer.Ordinal))).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var expectedGroups = new[]
            {
                string.Join("+", LabTestSupport.AllCells.Where(c => c.EndsWith("_action")).OrderBy(c => c, StringComparer.Ordinal)),
                string.Join("+", LabTestSupport.AllCells.Where(c => c.EndsWith("_targeted")).OrderBy(c => c, StringComparer.Ordinal)),
            }.OrderBy(s => s, StringComparer.Ordinal).ToList();
            Assert.Equal(expectedGroups, groupedCells);
            foreach (var e in freeze)
            {
                Assert.Equal(Num(cells.First(c => e.Cells.Contains(c.Key)).Value, "hitstop.started_ticks_player"), ((JsonNumber)e.Value!).Value);
            }

            // 表现类数值：approx，容差 = 度量声明的绝对允差（不小于下限）；实时类：le，上限 = 比较器的倍率上限规则。
            foreach (var group in runner.Registry.Groups)
            {
                foreach (var spec in group.Specs)
                {
                    var id = Expectation.AutoPrefix + group.Name + "." + spec.Name;
                    if (!byId.TryGetValue(id, out var e))
                    {
                        continue;
                    }

                    if (spec.Class == MetricClass.Presentation && e.Value is JsonNumber)
                    {
                        Assert.Equal(ExpectOp.Approx, e.Op);
                        Assert.Equal(Math.Max(spec.Amount, 1e-6), e.Tolerance!.Value);
                    }

                    if (spec.Class == MetricClass.RealTime)
                    {
                        Assert.Equal(ExpectOp.Le, e.Op);
                        var measured = ((JsonNumber)((JsonObject)cells[0].Value.Groups[group.Name])[spec.Name]).Value;
                        Assert.Equal(MetricSink.Round(Math.Max(measured * spec.Amount, spec.Floor)), ((JsonNumber)e.Value!).Value);
                    }
                }
            }

            // 默认不导出实时类（随机器抖动）；空值（空文本、空数组、-1 哨兵）不导出。
            var defaults = ExpectationExporter.FromFingerprints(cells, runner.Registry);
            Assert.DoesNotContain(defaults, e => e.Subject.Metric.StartsWith("performance.frame_ms", StringComparison.Ordinal));
            Assert.DoesNotContain(defaults, e => e.Subject.Metric == "response.move_response_ticks");
            Assert.DoesNotContain(defaults, e => e.Subject.Metric == "inputbuf.drop_events");

            // 只导出指定组。
            var onlyAttack = ExpectationExporter.FromFingerprints(
                cells, runner.Registry, new ExpectationExportOptions { Groups = { "attack" } });
            Assert.All(onlyAttack, e => Assert.Equal("attack", e.Subject.Group));
        }

        [Fact]
        public void ExportAsTest_WritesScriptWithExpectationsAndBaseline_AndTheFixtureSetPassesTheSuite()
        {
            var runner = LabTestSupport.Runner;
            var fixtures = Fixtures("export");
            var script = LabTestSupport.Script("feel_melee");
            var written = LabSuite.ExportAsTest(runner, script, fixtures);
            Assert.Equal(2, written.Count);

            var exported = InputScript.Parse(File.ReadAllText(written[0], Encoding.UTF8));
            Assert.Equal(InputScript.ExpectFormatVersion, exported.EffectiveFormatVersion);
            Assert.NotEmpty(exported.Expectations);

            // 导出的夹具（脚本带期望 + 基线）在套件里全部通过，期望清单逐格判定过。
            var results = LabSuite.Check(runner, fixtures, "feel_melee");
            Assert.Equal(6, results.Count);
            Assert.All(results, r => Assert.Equal(CellStatus.Pass, r.Status));
            Assert.All(results, r => Assert.True(r.Expectations.Count > 0));

            // 可编辑：改动一条导出的期望（把精确值改错），套件就以清晰诊断指出这一条。
            var edited = exported.Expectations.ToList();
            var victim = edited.First(e => e.Op == ExpectOp.Eq && e.Value is JsonNumber);
            var index = edited.IndexOf(victim);
            edited[index] = new Expectation(
                victim.Id, victim.Cells, victim.Subject, ExpectOp.Eq, LabJson.Num(((JsonNumber)victim.Value!).Value + 7),
                null, null, null, null, string.Empty);
            WriteScript(exported.WithExpectations(edited), fixtures);
            var afterEdit = LabSuite.Check(runner, fixtures, "feel_melee");
            Assert.Contains(afterEdit, r => r.Status == CellStatus.Diff && r.Expectations.Any(e => !e.Ok && e.Expectation.Id == victim.Id));
        }

        [Fact]
        public void ExportAsTest_ReExport_ReplacesAutoGeneratedExpectationsAndKeepsHandWrittenOnes()
        {
            var runner = LabTestSupport.Runner;
            var fixtures = Fixtures("reexport");
            var hand = Op("hand_written", "attack.damage_events", ExpectOp.Ge, 1);
            var script = LabTestSupport.Script("feel_melee").WithExpectations(new[] { hand });

            var first = InputScript.Parse(File.ReadAllText(LabSuite.ExportAsTest(runner, script, fixtures)[0], Encoding.UTF8));
            Assert.Equal("hand_written", first.Expectations[0].Id);
            var autoCount = first.Expectations.Count(e => e.Id.StartsWith(Expectation.AutoPrefix, StringComparison.Ordinal));
            Assert.Equal(first.Expectations.Count - 1, autoCount);

            // 再导出一次（以上次导出的脚本为输入）：自动生成的整体重生成（条数不变、不叠加），手写的原样保留在前。
            var second = InputScript.Parse(File.ReadAllText(LabSuite.ExportAsTest(runner, first, fixtures)[0], Encoding.UTF8));
            Assert.Equal(first.Expectations.Count, second.Expectations.Count);
            Assert.Equal(first.ToJson(), second.ToJson());

            // 只写脚本与基线、不动期望：手写与自动生成的原样保留。
            var keep = InputScript.Parse(File.ReadAllText(LabSuite.ExportAsTest(runner, first, fixtures, null, null, writeExpectations: false)[0], Encoding.UTF8));
            Assert.Equal(first.ToJson(), keep.ToJson());

            // 脚本没有期望、又不要期望：导出的脚本仍是旧格式版本（不带期望键）。
            var noExpectDir = Fixtures("noexpect");
            var plain = InputScript.Parse(File.ReadAllText(LabSuite.ExportAsTest(runner, LabTestSupport.Script("move_tap"), noExpectDir, null, null, writeExpectations: false)[0], Encoding.UTF8));
            Assert.Empty(plain.Expectations);
            Assert.Equal(LabTestSupport.Script("move_tap").EffectiveFormatVersion, plain.EffectiveFormatVersion);
        }
    }
}
