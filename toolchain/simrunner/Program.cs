using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Sim;

namespace Toolchain.SimRunner
{
    /// <summary>
    /// T-N6-6（ADR-0035 决策 5、计划书 §12 验收 5）：数值仿真的命令行入口——对给定数据根跑
    /// <c>sim.scenario</c> 场景（<c>--scenario &lt;id&gt;|all</c>），把结果写成统一信封
    /// <see cref="SimReport"/>（<c>*.report.json</c>），可选与既往基线（<see cref="SimBaseline"/>）比对
    /// 输出差异（<c>*.diff.txt</c>/<c>*.diff.json</c>），或用 <c>--update-baseline</c> 覆写基线。
    /// <para>
    /// 判断记录（不复用 <c>toolchain/validator</c> 的装配代码）：本工具与 <c>toolchain/validator</c>
    /// 是并列的两个独立控制台工程（各自 <c>OutputType=Exe</c>），互不引用；两者都需要"对磁盘目录跑
    /// <c>DataRegistry.LoadAll</c>"这一段基础设施（<c>DiskFileSystem</c>），复制这一份不含任何校验/
    /// 解析业务逻辑的最小适配层，不构成"与 core 内校验逻辑重复实现两套判断"（11 第 2 节 T2-12 明文
    /// 禁止的是重复实现字段级/引用完整性/Expr 规则本身，不是这类零业务逻辑的文件系统适配代码）。
    /// </para>
    /// <para>
    /// 判断记录（退出码 3 独立于 0/1/2）：任务书原文把"无基线且未传 <c>--update-baseline</c>"单独编为
    /// 退出码 3，而不是归进 2（参数错误）或 1（存在差异）——这不是参数本身写错（<c>--baseline-dir</c>
    /// 与 <c>--scenario</c>/<c>--data-root</c> 等参数的语法/存在性检查已经在参数解析阶段用 2 拦住），
    /// 也不是"跑出来的结果不通过"（根本没有跑比对），是一种"调用方的意图无法达成"的第三种状态——
    /// 调用方明确要求比对（传了 <c>--baseline-dir</c>）但没有东西可比，必须显式选择"这次就当作首次
    /// 建立基线"（<c>--update-baseline</c>）或者先手动确认基线该长什么样，不能静默当作通过。这与
    /// 回放回归"比对失败本身不说明对错，只说明结果变了"（<c>core/gameplay/tests/Replay/README.md</c>
    /// "如何更新基线"一节）是同一原理在命令行工具层面的落地：本工具不会在缺基线时自动创建它，必须
    /// 调用方显式传 <c>--update-baseline</c> 确认这是一次有意的建立/更新。
    /// </para>
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }
            catch (IOException)
            {
                // 标准输出被重定向到不支持设置编码的目标时可能抛出，退化为调用方已设置的编码，
                // 惯例同 toolchain/validator/Program.cs。
            }

            if (args.Length == 0 || !string.Equals(args[0], "run", StringComparison.Ordinal))
            {
                Console.Error.WriteLine(UsageText());
                return 2;
            }

            return RunCommand(args.Skip(1).ToArray());
        }

        private static string UsageText() =>
            "用法：dotnet run --project toolchain/simrunner -- run --scenario <id>|all " +
            "--framework-root <dir> --data-root <dir> [--data-root <dir2> ...] --out <dir> " +
            "[--baseline-dir <dir>] [--update-baseline] [--json] [--runs <n>] [--version <str>]";

        private static int RunCommand(string[] args)
        {
            string? scenarioArg = null;
            string? frameworkRoot = null;
            var dataRoots = new List<string>();
            string? outDir = null;
            string? baselineDir = null;
            var updateBaseline = false;
            var jsonOutput = false;
            int? runsOverride = null;
            var version = "unknown";

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--scenario":
                        if (!TryTakeValue(args, ref i, out scenarioArg)) return ArgError("--scenario 需要一个参数（场景短 id 或 all）");
                        break;

                    case "--framework-root":
                        if (!TryTakeValue(args, ref i, out frameworkRoot)) return ArgError("--framework-root 需要一个目录参数");
                        break;

                    case "--data-root":
                        if (!TryTakeValue(args, ref i, out var dataRoot)) return ArgError("--data-root 需要一个目录参数");
                        dataRoots.Add(dataRoot!);
                        break;

                    case "--out":
                        if (!TryTakeValue(args, ref i, out outDir)) return ArgError("--out 需要一个目录参数");
                        break;

                    case "--baseline-dir":
                        if (!TryTakeValue(args, ref i, out baselineDir)) return ArgError("--baseline-dir 需要一个目录参数");
                        break;

                    case "--update-baseline":
                        updateBaseline = true;
                        break;

                    case "--json":
                        jsonOutput = true;
                        break;

                    case "--runs":
                        if (!TryTakeValue(args, ref i, out var runsText) ||
                            !int.TryParse(runsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var runsValue) ||
                            runsValue <= 0)
                        {
                            return ArgError("--runs 需要一个正整数参数");
                        }
                        runsOverride = runsValue;
                        break;

                    case "--version":
                        if (!TryTakeValue(args, ref i, out var versionValue)) return ArgError("--version 需要一个参数");
                        version = versionValue!;
                        break;

                    default:
                        return ArgError($"未知参数 \"{args[i]}\"");
                }
            }

            if (scenarioArg == null) return ArgError("缺少必填参数 --scenario <id>|all");
            if (string.IsNullOrEmpty(frameworkRoot)) return ArgError("缺少必填参数 --framework-root <dir>");
            if (dataRoots.Count == 0) return ArgError("缺少必填参数 --data-root <dir>（可重复传入）");
            if (string.IsNullOrEmpty(outDir)) return ArgError("缺少必填参数 --out <dir>");
            if (updateBaseline && baselineDir == null) return ArgError("--update-baseline 需要同时指定 --baseline-dir <dir>");

            try
            {
                return Execute(scenarioArg, frameworkRoot!, dataRoots, outDir!, baselineDir, updateBaseline, jsonOutput, runsOverride, version);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DirectoryNotFoundException)
            {
                Console.Error.WriteLine("数据装载阻断：" + ex.Message);
                return 2;
            }
        }

        private static bool TryTakeValue(string[] args, ref int i, out string? value)
        {
            if (i + 1 >= args.Length)
            {
                value = null;
                return false;
            }

            value = args[++i];
            return true;
        }

        private static int ArgError(string message)
        {
            Console.Error.WriteLine("参数错误：" + message);
            Console.Error.WriteLine(UsageText());
            return 2;
        }

        private static int Execute(
            string scenarioArg, string frameworkRoot, List<string> dataRoots, string outDir, string? baselineDir,
            bool updateBaseline, bool jsonOutput, int? runsOverride, string version)
        {
            Directory.CreateDirectory(outDir);

            var fs = new DiskFileSystem();
            var dataSources = new List<IDataSource> { BuildSource(fs, frameworkRoot) };
            dataSources.AddRange(dataRoots.Select(root => BuildSource(fs, root)));

            var bootstrap = DiscoverBootstrapPlayerClass(dataSources);
            if (bootstrap == null)
            {
                Console.Error.WriteLine("数据装载阻断：给定的数据根内未找到任何 sim.scenario 行，无法发现场景。");
                return 2;
            }

            var probeWorld = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = dataSources,
                PlayerClassId = bootstrap.Value.ClassId,
                PlayerLevel = bootstrap.Value.Level,
                FailOnUnknownTable = false,
            });

            if (probeWorld.ScenarioCatalog == null || probeWorld.AnchorTable == null)
            {
                Console.Error.WriteLine("数据装载阻断：数据根未同时提供 sim.scenario 与 sim.anchor 数据。");
                return 2;
            }

            var catalog = probeWorld.ScenarioCatalog;
            var anchors = probeWorld.AnchorTable;
            var registry = probeWorld.Registry;

            List<ScenarioDef> scenarios;
            if (string.Equals(scenarioArg, "all", StringComparison.OrdinalIgnoreCase))
            {
                scenarios = catalog.All.ToList();
            }
            else
            {
                var id = scenarioArg.StartsWith("sim.scenario.", StringComparison.Ordinal)
                    ? new Id(scenarioArg)
                    : new Id("sim.scenario." + scenarioArg);
                if (!catalog.TryGet(id, out var scenario))
                {
                    Console.Error.WriteLine($"参数错误：sim.scenario 未登记 id={id.Value}");
                    return 2;
                }
                scenarios = new List<ScenarioDef> { scenario };
            }

            if (scenarios.Count == 0)
            {
                Console.Error.WriteLine("参数错误：数据根未登记任何 sim.scenario 行。");
                return 2;
            }

            var anyExceededOrRemoved = false;
            var anyMissingBaselineWithoutUpdate = false;

            foreach (var original in scenarios.OrderBy(s => s.Id.Value, StringComparer.Ordinal))
            {
                var scenario = runsOverride.HasValue ? original.WithRuns(runsOverride.Value) : original;
                var shortId = ShortScenarioId(scenario.Id);

                SimReport report = scenario.Kind switch
                {
                    ScenarioKind.Arena => SimReport.FromArenaReport(
                        ArenaSimulation.Run(scenario, anchors, dataSources), scenario, registry, version, runsOverride),
                    ScenarioKind.Growth => SimReport.FromGrowthReport(
                        GrowthSimulation.Run(scenario, anchors, dataSources), scenario, registry, version, runsOverride),
                    ScenarioKind.Coverage => SimReport.FromCoverageReport(
                        CoverageSimulation.Run(scenario, anchors, dataSources), scenario, registry, version, runsOverride),
                    _ => throw new InvalidOperationException($"未知场景 kind：{scenario.Kind}"),
                };

                File.WriteAllText(Path.Combine(outDir, shortId + ".report.json"), report.ToJson());

                var exceeded = 0;
                var added = 0;
                var removed = 0;
                var resultText = "PASS";

                if (updateBaseline)
                {
                    // 参数解析阶段已保证 updateBaseline == true 时 baselineDir != null。
                    Directory.CreateDirectory(baselineDir!);
                    var baselinePath = Path.Combine(baselineDir!, shortId + ".json");
                    var newBaseline = SimBaseline.FromReport(report);
                    var existedBefore = File.Exists(baselinePath);
                    var previousStatCount = existedBefore ? SimBaseline.Parse(File.ReadAllText(baselinePath)).Stats.Count : 0;

                    Console.WriteLine(existedBefore
                        ? $"更新基线：{baselinePath}（覆盖既有 {previousStatCount} 条统计量 -> {newBaseline.Stats.Count} 条统计量）"
                        : $"新建基线：{baselinePath}（{newBaseline.Stats.Count} 条统计量）");

                    File.WriteAllText(baselinePath, newBaseline.ToJson());
                }
                else if (baselineDir != null)
                {
                    var baselinePath = Path.Combine(baselineDir, shortId + ".json");
                    if (!File.Exists(baselinePath))
                    {
                        Console.Error.WriteLine($"缺少基线：{baselinePath}（未传 --update-baseline，无法比对）");
                        anyMissingBaselineWithoutUpdate = true;
                        resultText = "FAIL";
                    }
                    else
                    {
                        var baseline = SimBaseline.Parse(File.ReadAllText(baselinePath));
                        var diff = BaselineComparer.Compare(report, baseline);
                        exceeded = diff.ExceededCount;
                        added = diff.AddedCount;
                        removed = diff.RemovedCount;
                        resultText = diff.HasBlockingDifference ? "FAIL" : "PASS";
                        if (diff.HasBlockingDifference)
                        {
                            anyExceededOrRemoved = true;
                        }

                        File.WriteAllText(Path.Combine(outDir, shortId + ".diff.txt"), diff.ToText());
                        File.WriteAllText(Path.Combine(outDir, shortId + ".diff.json"), diff.ToJson());

                        if (jsonOutput)
                        {
                            Console.WriteLine(diff.ToJson());
                        }
                    }
                }

                Console.WriteLine(
                    $"scenario={scenario.Id.Value} kind={report.Kind} stats={report.Stats.Count} " +
                    $"exceeded={exceeded} added={added} removed={removed} result={resultText}");
            }

            string finalResult;
            int exitCode;
            if (anyExceededOrRemoved)
            {
                finalResult = "FAIL";
                exitCode = 1;
            }
            else if (anyMissingBaselineWithoutUpdate)
            {
                finalResult = "FAIL";
                exitCode = 3;
            }
            else
            {
                finalResult = "OK";
                exitCode = 0;
            }

            Console.WriteLine($"RESULT={finalResult}");
            return exitCode;
        }

        private static string ShortScenarioId(Id id)
        {
            const string prefix = "sim.scenario.";
            return id.Value.StartsWith(prefix, StringComparison.Ordinal) ? id.Value.Substring(prefix.Length) : id.Value;
        }

        private static IDataSource BuildSource(DiskFileSystem fs, string root)
        {
            var full = Path.IsPathRooted(root) ? root : Path.Combine(Directory.GetCurrentDirectory(), root);
            full = Path.GetFullPath(full).Replace('\\', '/');
            if (!Directory.Exists(full))
            {
                throw new DirectoryNotFoundException($"目录不存在：{full}");
            }

            return new FileSystemDataSource(fs, full);
        }

        private readonly struct BootstrapPlayerClass
        {
            public Id ClassId { get; }
            public int Level { get; }

            public BootstrapPlayerClass(Id classId, int level)
            {
                ClassId = classId;
                Level = level;
            }
        }

        /// <summary>判断记录：<see cref="HeadlessWorldBuilder.Build"/> 要求一个真实存在于数据源里的
        /// <c>PlayerClassId</c>（否则 <c>Core.Rules.Assembly.RulesAssembly.RegisterUnit</c> 内部
        /// <c>Archetypes.ApplyTo</c> 会因"未知职业"抛异常），但本工具在真正拿到
        /// <see cref="ScenarioCatalog"/> 之前恰恰不知道该传哪个职业——<see cref="AnchorTable"/>/
        /// <see cref="ScenarioCatalog"/> 本身只依赖 <c>sim.anchor</c>/<c>sim.scenario</c> 两张表的数据
        /// 行，与"玩家职业是否合法"无关，为了拿到它们而先构造一个完整可玩的 <see cref="HeadlessWorld"/>，
        /// 是这层 API 形状带来的鸡生蛋问题。本方法直接扫描全部数据源的 <c>sim.scenario</c> 表文件文本
        /// （<see cref="IDataSource.ListTables"/> + <see cref="DataTableSource.ReadText"/>，不经过
        /// <see cref="Core.Foundation.DataRegistry.DataRegistry"/>），取第一行的
        /// <c>player.class_id</c>/<c>player.level</c> 作探测用的职业/等级——这只是为"装配一次探测世界"
        /// 这一引导步骤找一个真实存在的职业，不含任何校验/解析业务逻辑，不与 core 内任何判断重复；
        /// 真正的场景解析仍然全部经由 <see cref="ScenarioCatalog"/>（本方法产出的探测世界的
        /// <see cref="HeadlessWorld.ScenarioCatalog"/>）完成。</summary>
        private static BootstrapPlayerClass? DiscoverBootstrapPlayerClass(IReadOnlyList<IDataSource> dataSources)
        {
            foreach (var source in dataSources)
            {
                foreach (var table in source.ListTables())
                {
                    if (!string.Equals(table.TableName, "sim.scenario", StringComparison.Ordinal)) continue;

                    if (JsonReader.Parse(table.ReadText()) is not JsonObject root) continue;
                    if (!root.TryGetValue("rows", out var rowsVal) || rowsVal is not JsonArray rows || rows.Count == 0) continue;
                    if (rows[0] is not JsonObject firstRow) continue;
                    if (!firstRow.TryGetValue("player", out var playerVal) || playerVal is not JsonObject player) continue;
                    if (!player.TryGetValue("class_id", out var classIdVal) || classIdVal is not JsonString classIdStr) continue;

                    var level = player.TryGetValue("level", out var levelVal) && levelVal is JsonNumber levelNum
                        ? (int)levelNum.Value
                        : 1;
                    return new BootstrapPlayerClass(new Id(classIdStr.Value), level);
                }
            }

            return null;
        }
    }
}
