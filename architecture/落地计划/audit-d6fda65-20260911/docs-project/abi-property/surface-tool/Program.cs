namespace Toolchain.AbiSurface
{
    /// <summary>
    /// 命令行入口：<c>dump --out &lt;file&gt; &lt;dll&gt; [&lt;dll&gt;...]</c> 与
    /// <c>compare &lt;baseline.txt&gt; &lt;current.txt&gt; [--allowlist &lt;file&gt;] [--out &lt;report&gt;]</c>。
    /// 见 toolchain/abi_probe.ps1 集成点与 toolchain/tests/test_abi_surface_compare.py。
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* 非交互式重定向输出等场景可能不支持设置，忽略。 */ }
            try
            {
                if (args.Length == 0)
                {
                    Console.Error.WriteLine(Usage);
                    return 64;
                }
                switch (args[0])
                {
                    case "dump":
                        return RunDump(args);
                    case "compare":
                        return RunCompare(args);
                    default:
                        Console.Error.WriteLine("未知子命令：" + args[0]);
                        Console.Error.WriteLine(Usage);
                        return 64;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("AbiSurface 执行失败：" + ex);
                return 70;
            }
        }

        private const string Usage =
            "用法：\n" +
            "  AbiSurface dump --out <file> <dll> [<dll>...]\n" +
            "  AbiSurface compare <baseline.txt> <current.txt> [--allowlist <file>] [--out <report>]";

        private static int RunDump(string[] args)
        {
            string? outPath = null;
            var dlls = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--out")
                {
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--out 缺少参数"); return 64; }
                    outPath = args[++i];
                }
                else
                {
                    dlls.Add(args[i]);
                }
            }
            if (outPath == null || dlls.Count == 0)
            {
                Console.Error.WriteLine("dump 需要 --out <file> 与至少一个 <dll>");
                Console.Error.WriteLine(Usage);
                return 64;
            }

            var lines = SurfaceDumper.Dump(dlls);
            var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(outPath, lines);
            Console.WriteLine("[abi_surface] dump 完成：" + lines.Count + " 行 -> " + outPath);
            return 0;
        }

        private static int RunCompare(string[] args)
        {
            string? baselinePath = null;
            string? currentPath = null;
            string? allowlistPath = null;
            string? reportPath = null;
            var positional = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--allowlist")
                {
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--allowlist 缺少参数"); return 64; }
                    allowlistPath = args[++i];
                }
                else if (args[i] == "--out")
                {
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--out 缺少参数"); return 64; }
                    reportPath = args[++i];
                }
                else
                {
                    positional.Add(args[i]);
                }
            }
            if (positional.Count < 2)
            {
                Console.Error.WriteLine("compare 需要 <baseline.txt> <current.txt>");
                Console.Error.WriteLine(Usage);
                return 64;
            }
            baselinePath = positional[0];
            currentPath = positional[1];

            if (!File.Exists(baselinePath)) { Console.Error.WriteLine("基线文件不存在：" + baselinePath); return 64; }
            if (!File.Exists(currentPath)) { Console.Error.WriteLine("当前文件不存在：" + currentPath); return 64; }

            var baselineLines = File.ReadAllLines(baselinePath);
            var currentLines = File.ReadAllLines(currentPath);
            var allowlist = new List<AllowlistEntry>();
            if (allowlistPath != null)
            {
                if (!File.Exists(allowlistPath)) { Console.Error.WriteLine("allowlist 文件不存在：" + allowlistPath); return 64; }
                allowlist = SurfaceCompareLogic.ParseAllowlist(File.ReadAllLines(allowlistPath));
            }

            var result = SurfaceCompareLogic.Compare(baselineLines, currentLines, allowlist);

            var report = BuildReport(result, baselinePath, currentPath, allowlistPath);
            Console.WriteLine(report);
            if (reportPath != null)
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(reportPath, report);
            }

            return result.HasBreaks ? 2 : 0;
        }

        private static string BuildReport(CompareResult result, string baselinePath, string currentPath, string? allowlistPath)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[abi_surface] compare baseline=" + baselinePath + " current=" + currentPath);
            if (allowlistPath != null) sb.AppendLine("[abi_surface] allowlist=" + allowlistPath);
            sb.AppendLine("[abi_surface] breaks=" + result.Breaks.Count + " allowed=" + result.Allowed.Count + " additions=" + result.Additions.Count);
            if (result.Breaks.Count > 0)
            {
                sb.AppendLine("[abi_surface] BREAKING（未在 allowlist 放行）：");
                foreach (var b in result.Breaks)
                {
                    sb.AppendLine("  [" + b.Category + "] " + b.Line);
                    sb.AppendLine("    原因：" + b.Detail);
                }
            }
            if (result.Allowed.Count > 0)
            {
                sb.AppendLine("[abi_surface] ALLOWED（allowlist 放行）：");
                foreach (var a in result.Allowed)
                {
                    sb.AppendLine("  [" + a.Category + "] " + a.Line);
                    sb.AppendLine("    " + a.Detail);
                }
            }
            if (result.Additions.Count > 0)
            {
                sb.AppendLine("[abi_surface] 新增（不计入破坏）：" + result.Additions.Count + " 行");
            }
            if (result.Widened.Count > 0)
            {
                sb.AppendLine("[abi_surface] 可见性放宽（不计入破坏）：" + result.Widened.Count + " 行");
                foreach (var w in result.Widened)
                {
                    sb.AppendLine("  [" + w.Category + "] " + w.Line);
                    sb.AppendLine("    " + w.Detail);
                }
            }
            sb.AppendLine(result.HasBreaks ? "[abi_surface] RESULT=BREAKING" : "[abi_surface] RESULT=OK");
            return sb.ToString();
        }
    }
}
