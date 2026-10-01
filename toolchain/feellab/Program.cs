using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Core.Foundation.DataRegistry;
using Lab;

namespace Toolchain.FeelLab
{
    /// <summary>
    /// 手感实验室无头宿主命令行（06 第 2 节"无头宿主命令行"，形态同 <c>toolchain/simrunner</c>）：
    /// <list type="bullet">
    /// <item><c>run</c>：数据根 + 格子 + 脚本 → 指纹文件（可选同时写完整记录、与基线比较）；</item>
    /// <item><c>suite</c>：全部标准脚本 × 适用格子逐个与基线比较（<c>--update-baseline</c> 重写基线）；</item>
    /// <item><c>export-test</c>：脚本 + 指纹 → 夹具（脚本文件 + 基线文件）；</item>
    /// <item><c>list</c>：列出格子及其在无头宿主上的可运行状态；</item>
    /// <item><c>invariants</c>：跨格子不变量（ADR-0122 决定 4）：平面组合逻辑组一致、动作式（剥 timeline + 经典预设）= 目标选择式、
    /// 经典预设下手感装配透明；不比较基线，不改任何文件。</item>
    /// </list>
    /// <para>
    /// 退出码：0 全部通过；1 至少一个格子基线比较有差异；2 命令行参数错误；3 缺基线（无差异但有格子没有基线）；
    /// 4 有格子不可运行（预留空间模型或缺能力，不静默跳过）；5 数据/脚本内容错误（装配失败、文件格式非法）。
    /// 同时出现多种情况时取优先级：差异（1）&gt; 缺基线（3）&gt; 不可运行（4）。
    /// </para>
    /// <para>
    /// 判断记录（输出落点）：指纹与记录默认写到 <c>lab/out/</c>（已被 <c>.gitignore</c> 忽略）；入库的只有
    /// <c>lab/fixtures/</c> 下的脚本与基线（<c>AGENTS.md</c> 第 4 节"生成物不入库"）。
    /// </para>
    /// </summary>
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitDiff = 1;
        private const int ExitArgs = 2;
        private const int ExitBaselineMissing = 3;
        private const int ExitNotRunnable = 4;
        private const int ExitDataError = 5;

        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }
            catch (IOException)
            {
                // 标准输出被重定向到不支持设置编码的目标时可能抛出，退化为调用方已设置的编码（惯例同 simrunner）。
            }

            if (args.Length == 0)
            {
                Console.Error.WriteLine(Usage());
                return ExitArgs;
            }

            try
            {
                var command = args[0];
                var options = Options.Parse(args, 1);
                switch (command)
                {
                    case "run": return Run(options);
                    case "suite": return Suite(options);
                    case "export-test": return ExportTest(options);
                    case "list": return List(options);
                    case "invariants": return Invariants(options);
                    default:
                        Console.Error.WriteLine($"未知命令：{command}");
                        Console.Error.WriteLine(Usage());
                        return ExitArgs;
                }
            }
            catch (ArgException ex)
            {
                Console.Error.WriteLine("参数错误：" + ex.Message);
                Console.Error.WriteLine(Usage());
                return ExitArgs;
            }
            catch (LabFormatException ex)
            {
                Console.Error.WriteLine("内容错误：" + ex.Message);
                return ExitDataError;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine("装配失败：" + ex.Message);
                return ExitDataError;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine("文件错误：" + ex.Message);
                return ExitDataError;
            }
        }

        private static string Usage() =>
            "用法：\n" +
            "  feellab run --script <file> --cell <格子> [--out <dir>] [--record] [--baseline <fixtures dir>]\n" +
            "  feellab suite [--fixtures <dir>] [--script <id>] [--cell <格子>] [--update-baseline]\n" +
            "  feellab export-test --script <file> [--fixtures <dir>] [--cell <格子>]\n" +
            "  feellab list\n" +
            "  feellab invariants [--fixtures <dir>] [--script <id>]\n" +
            "公共参数：--framework-root <dir>（默认 data/_framework）  --data-root <dir>（可重复，默认 data/_lab）\n" +
            "默认夹具目录 lab/fixtures，默认输出目录 lab/out。";

        private static LabRunner CreateRunner(Options o)
        {
            var sources = new List<IDataSource>();
            try
            {
                sources.Add(LabDataSources.FromDirectory(o.FrameworkRoot));
                foreach (var root in o.DataRoots)
                {
                    sources.Add(LabDataSources.FromDirectory(root));
                }
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new ArgException(ex.Message);
            }

            return new LabRunner(LabDataset.Load(sources));
        }

        private static int Run(Options o)
        {
            var scriptPath = o.Require("script");
            var cell = o.Require("cell");
            var runner = CreateRunner(o);
            var script = InputScript.Parse(File.ReadAllText(scriptPath, Encoding.UTF8), scriptPath);

            LabRecording recording;
            try
            {
                recording = runner.Record(script, cell);
            }
            catch (LabCellNotRunnableException ex)
            {
                Console.Error.WriteLine("不可运行：" + ex.Runnability);
                return ExitNotRunnable;
            }

            var fingerprint = Fingerprint.Build(
                recording, runner.Registry, runner.DatasetFor(script, runner.Dataset.Catalog.GetScenario(cell)).Hash);
            var outDir = o.Get("out") ?? Path.Combine("lab", "out");
            Directory.CreateDirectory(outDir);
            var fpPath = Path.Combine(outDir, $"{script.Meta.ScriptId}.{cell}.fingerprint.json");
            File.WriteAllText(fpPath, fingerprint.ToJson(), new UTF8Encoding(false));
            Console.WriteLine($"指纹：{fpPath}");
            if (o.Has("record"))
            {
                var recPath = Path.Combine(outDir, $"{script.Meta.ScriptId}.{cell}.recording.json");
                File.WriteAllText(recPath, RecordingWriter.ToJson(recording), new UTF8Encoding(false));
                Console.WriteLine($"记录：{recPath}");
            }

            var fixtures = o.Get("baseline");
            if (fixtures == null)
            {
                return ExitOk;
            }

            var baselinePath = LabFixtures.BaselinePath(fixtures, script.Meta.ScriptId);
            if (!File.Exists(baselinePath))
            {
                Console.Error.WriteLine($"缺基线：{baselinePath}");
                return ExitBaselineMissing;
            }

            var baselines = LabFixtures.ParseBaseline(File.ReadAllText(baselinePath, Encoding.UTF8), baselinePath);
            if (!baselines.TryGetValue(cell, out var baseline))
            {
                Console.Error.WriteLine($"缺基线：{baselinePath} 里没有格子 {cell}");
                return ExitBaselineMissing;
            }

            var diff = FingerprintComparer.Compare(baseline, fingerprint, runner.Registry);
            Console.Write(diff.Format());
            Console.WriteLine(diff.Ok ? "基线比较：通过" : "基线比较：有差异");
            return diff.Ok ? ExitOk : ExitDiff;
        }

        private static int Suite(Options o)
        {
            var fixtures = o.Get("fixtures") ?? Path.Combine("lab", "fixtures");
            var runner = CreateRunner(o);
            var onlyScript = o.Get("script");
            var onlyCell = o.Get("cell");

            if (o.Has("update-baseline"))
            {
                foreach (var path in LabSuite.UpdateBaselines(runner, fixtures, onlyScript))
                {
                    Console.WriteLine($"已写基线：{path}");
                }

                return ExitOk;
            }

            var results = LabSuite.Check(runner, fixtures, onlyScript, onlyCell);
            if (results.Count == 0)
            {
                Console.Error.WriteLine($"没有可比较的（脚本，格子）：夹具目录 {fixtures} 下无脚本或过滤条件无匹配");
                return ExitArgs;
            }

            int pass = 0, diff = 0, missing = 0, notRunnable = 0;
            foreach (var r in results)
            {
                switch (r.Status)
                {
                    case CellStatus.Pass:
                        pass++;
                        Console.WriteLine($"通过   {r.Script} @ {r.Cell}");
                        break;
                    case CellStatus.Diff:
                        diff++;
                        Console.WriteLine($"差异   {r.Script} @ {r.Cell}");
                        Console.Write(r.Diff!.Format());
                        break;
                    case CellStatus.BaselineMissing:
                        missing++;
                        Console.WriteLine($"缺基线 {r.Script} @ {r.Cell}：{r.Message}");
                        break;
                    default:
                        notRunnable++;
                        Console.WriteLine($"不可运行 {r.Script} @ {r.Cell}：{r.Message}");
                        break;
                }
            }

            Console.WriteLine($"合计 {results.Count}：通过 {pass}，差异 {diff}，缺基线 {missing}，不可运行 {notRunnable}");

            // 判断记录：门禁（PowerShell）按系统代码页解码本进程的标准输出，中文汇总行会失配；
            // 另输出一行纯 ASCII 的机器可读汇总，格式变动须同步 toolchain/_gate_line_heavy.ps1 的 feel_lab_suite 解析。
            Console.WriteLine($"RESULT total={results.Count} pass={pass} diff={diff} missing={missing} not_runnable={notRunnable}");
            if (diff > 0)
            {
                return ExitDiff;
            }

            if (missing > 0)
            {
                return ExitBaselineMissing;
            }

            return notRunnable > 0 ? ExitNotRunnable : ExitOk;
        }

        private static int ExportTest(Options o)
        {
            var scriptPath = o.Require("script");
            var fixtures = o.Get("fixtures") ?? Path.Combine("lab", "fixtures");
            var runner = CreateRunner(o);
            var script = InputScript.Parse(File.ReadAllText(scriptPath, Encoding.UTF8), scriptPath);
            foreach (var path in LabSuite.ExportAsTest(runner, script, fixtures, o.Get("cell")))
            {
                Console.WriteLine($"已写夹具：{path}");
            }

            return ExitOk;
        }

        private static int Invariants(Options o)
        {
            var fixtures = o.Get("fixtures") ?? Path.Combine("lab", "fixtures");
            var runner = CreateRunner(o);
            var onlyScript = o.Get("script");
            var scripts = new List<InputScript>();
            foreach (var script in LabFixtures.LoadScripts(fixtures))
            {
                if (onlyScript == null || string.Equals(onlyScript, script.Meta.ScriptId, StringComparison.Ordinal))
                {
                    scripts.Add(script);
                }
            }

            if (scripts.Count == 0)
            {
                Console.Error.WriteLine($"没有可检查的脚本：夹具目录 {fixtures} 下无脚本或过滤条件无匹配");
                return ExitArgs;
            }

            var results = LabInvariants.Check(runner, scripts);
            int pass = 0, fail = 0;
            var byInvariant = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
            foreach (var r in results)
            {
                if (!byInvariant.TryGetValue(r.Invariant, out var counts))
                {
                    counts = new int[2];
                    byInvariant[r.Invariant] = counts;
                }

                if (r.Ok)
                {
                    pass++;
                    counts[0]++;
                }
                else
                {
                    fail++;
                    counts[1]++;
                    Console.WriteLine(r.ToString());
                }
            }

            foreach (var pair in byInvariant)
            {
                Console.WriteLine($"不变量 {pair.Key}：通过 {pair.Value[0]}，不一致 {pair.Value[1]}");
            }

            Console.WriteLine($"合计 {results.Count}：通过 {pass}，不一致 {fail}");
            // 机器可读汇总（纯 ASCII，同 suite 的 RESULT 行约定）。
            Console.WriteLine($"RESULT invariants total={results.Count} pass={pass} fail={fail}");
            return fail > 0 ? ExitDiff : ExitOk;
        }

        private static int List(Options o)
        {
            var runner = CreateRunner(o);
            var anyNotRunnable = false;
            foreach (var cell in runner.Dataset.Catalog.Scenarios())
            {
                var r = cell.CheckRunnable(LabHost.AvailableCapabilities);
                Console.WriteLine(r.ToString());
                anyNotRunnable |= !r.Runnable;
            }

            return anyNotRunnable ? ExitNotRunnable : ExitOk;
        }

        private sealed class ArgException : Exception
        {
            public ArgException(string message)
                : base(message)
            {
            }
        }

        private sealed class Options
        {
            private static readonly HashSet<string> Flags = new HashSet<string>(StringComparer.Ordinal)
            {
                "record", "update-baseline",
            };

            private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly HashSet<string> _flags = new HashSet<string>(StringComparer.Ordinal);

            public string FrameworkRoot { get; private set; } = Path.Combine("data", "_framework");

            public List<string> DataRoots { get; } = new List<string>();

            public static Options Parse(string[] args, int start)
            {
                var o = new Options();
                for (var i = start; i < args.Length; i++)
                {
                    var a = args[i];
                    if (!a.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgException($"无法识别的参数：{a}");
                    }

                    var name = a.Substring(2);
                    if (Flags.Contains(name))
                    {
                        o._flags.Add(name);
                        continue;
                    }

                    if (i + 1 >= args.Length)
                    {
                        throw new ArgException($"参数 --{name} 缺少值");
                    }

                    var value = args[++i];
                    switch (name)
                    {
                        case "framework-root": o.FrameworkRoot = value; break;
                        case "data-root": o.DataRoots.Add(value); break;
                        default: o._values[name] = value; break;
                    }
                }

                if (o.DataRoots.Count == 0)
                {
                    o.DataRoots.Add(Path.Combine("data", "_lab"));
                }

                return o;
            }

            public bool Has(string name) => _flags.Contains(name);

            public string? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;

            public string Require(string name) => Get(name) ?? throw new ArgException($"缺少必需参数 --{name}");
        }
    }
}
