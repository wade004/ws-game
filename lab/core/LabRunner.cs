using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Lab
{
    /// <summary>
    /// 把另一个数据来源的指定表文本在内存里替换掉（不动磁盘）。测试里"故意改一个移动速度值、看基线比较失败、
    /// 不留任何改动"就靠它：覆盖只活在这个对象里，丢弃即消失。
    /// </summary>
    public sealed class TableOverlayDataSource : IDataSource
    {
        private readonly IDataSource _inner;
        private readonly Func<string, string, string?> _rewrite;

        /// <param name="inner">被包装的来源。</param>
        /// <param name="rewrite">参数为（表名，原文本）；返回非空即替换为该文本，返回 null 保持原样。</param>
        public TableOverlayDataSource(IDataSource inner, Func<string, string, string?> rewrite)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _rewrite = rewrite ?? throw new ArgumentNullException(nameof(rewrite));
        }

        public string? Root => _inner.Root;

        public IReadOnlyList<DataTableSource> ListTables()
        {
            var result = new List<DataTableSource>();
            foreach (var table in _inner.ListTables())
            {
                var original = table;
                result.Add(new DataTableSource(original.TableName, original.Location, () =>
                {
                    var text = original.ReadText();
                    return _rewrite(original.TableName, text) ?? text;
                }));
            }

            return result;
        }
    }

    /// <summary>已装载的实验室数据集：数据来源、内容哈希、格子/地形/靶子集目录。</summary>
    public sealed class LabDataset
    {
        public IReadOnlyList<IDataSource> Sources { get; }

        public string Hash { get; }

        public LabCatalog Catalog { get; }

        public LabHostOptions HostOptions { get; }

        private LabDataset(IReadOnlyList<IDataSource> sources, string hash, LabCatalog catalog, LabHostOptions options)
        {
            Sources = sources;
            Hash = hash;
            Catalog = catalog;
            HostOptions = options;
        }

        public static LabDataset Load(IReadOnlyList<IDataSource> sources, LabHostOptions? template = null)
        {
            var options = new LabHostOptions
            {
                DataSources = sources,
                PlayerClassId = template?.PlayerClassId ?? "arch.class.lab_hero",
                PlayerFactionId = template?.PlayerFactionId ?? "fac.player",
                PlayerId = template?.PlayerId ?? "unit.lab_player",
                Seed = template?.Seed ?? 20261002UL,
                MoveAction = template?.MoveAction ?? "input.action.move",
            };
            var probe = LabHost.BuildProbe(options);
            return new LabDataset(sources, LabHost.ComputeDatasetHash(sources), new LabCatalog(probe.Registry), options);
        }
    }

    /// <summary>
    /// 一次运行的入口：给定脚本与格子，产出记录与指纹。
    /// <para>
    /// 判断记录（脚本级数据集）：脚本 meta 声明了额外数据根（换装场景）时，该脚本在"基础数据集 + 额外根"的
    /// 派生数据集上运行（按脚本 id 缓存，装载一次）；没有声明的脚本一律用基础数据集，既有脚本的世界与基线因此不受影响。
    /// 额外根的路径由 <c>extraRootResolver</c> 解析（缺省按进程当前目录，同命令行入口的数据根参数）。
    /// </para>
    /// </summary>
    public sealed class LabRunner
    {
        private readonly Func<string, IDataSource> _extraRootResolver;
        private readonly Dictionary<string, LabDataset> _scriptDatasets = new Dictionary<string, LabDataset>(StringComparer.Ordinal);

        public LabDataset Dataset { get; }

        public MetricRegistry Registry { get; }

        public LabRunner(LabDataset dataset, MetricRegistry? registry = null, Func<string, IDataSource>? extraRootResolver = null)
        {
            Dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            Registry = registry ?? MetricRegistry.CreateDefault();
            _extraRootResolver = extraRootResolver ?? LabDataSources.FromDirectory;
        }

        /// <summary>
        /// 该脚本实际运行的数据集（无扩展数据声明时就是 <see cref="Dataset"/>）。手感场景脚本的数据集还随格子/变体而变：
        /// 目标选择式格子（或显式 <see cref="LabRunVariant.StripTimelines"/>）把额外根技能的 <c>timeline</c> 块剥掉，
        /// 因此同一脚本最多有"保留 / 剥掉"两份派生数据集（各自按脚本 id + 变体键缓存，装载一次）。
        /// </summary>
        public LabDataset DatasetFor(InputScript script, LabScenario? cell = null, LabRunVariant? variant = null)
        {
            var meta = script.Meta;
            if (meta.ExtraDataRoots.Count == 0)
            {
                return Dataset;
            }

            variant ??= LabRunVariant.Default;
            var strip = meta.Feel && variant.EffectiveStrip(cell);
            var key = meta.ScriptId + (strip ? "|strip" : string.Empty);
            if (!_scriptDatasets.TryGetValue(key, out var dataset))
            {
                dataset = LabDataset.Load(LabDataSources.ForScript(Dataset.Sources, meta, _extraRootResolver, strip), Dataset.HostOptions);
                _scriptDatasets[key] = dataset;
            }

            return dataset;
        }

        /// <summary>
        /// 解析格子：先在基础数据集里找（六个平面格子与既有脚本，行为不变）；找不到再到脚本自己的派生数据集里找——空间格子
        /// （<c>side_2d_*</c>/<c>volume_*</c>）的行放在脚本声明的额外数据根里，不进基础数据集（否则所有既有基线的数据集哈希都会变）。
        /// </summary>
        private LabScenario ScenarioFor(InputScript script, string cell) =>
            Dataset.Catalog.HasScenario(cell) ? Dataset.Catalog.GetScenario(cell) : DatasetFor(script).Catalog.GetScenario(cell);

        public LabRecording Record(InputScript script, string cell, LabRunVariant? variant = null)
        {
            var scenario = ScenarioFor(script, cell);
            var dataset = DatasetFor(script, scenario, variant);
            return LabHost.Run(dataset.HostOptions, dataset.Catalog.GetScenario(cell), script, dataset.Catalog, variant);
        }

        public Fingerprint Run(InputScript script, string cell, LabRunVariant? variant = null)
        {
            var scenario = ScenarioFor(script, cell);
            return Fingerprint.Build(Record(script, cell, variant), Registry, DatasetFor(script, scenario, variant).Hash);
        }

        /// <summary>
        /// 带宿主扩展点的记录（引擎宿主、调试覆盖；见 <see cref="LabHostExtension"/>）；<paramref name="extension"/> 为 null 等同无扩展版本。
        /// </summary>
        public LabRecording Record(InputScript script, string cell, LabRunVariant? variant, LabHostExtension? extension)
        {
            var scenario = ScenarioFor(script, cell);
            var dataset = DatasetFor(script, scenario, variant);
            return LabHost.Run(dataset.HostOptions, dataset.Catalog.GetScenario(cell), script, dataset.Catalog, variant, extension);
        }

        /// <summary>带宿主扩展点的指纹（用本运行器的注册表折算度量）。</summary>
        public Fingerprint Run(InputScript script, string cell, LabRunVariant? variant, LabHostExtension? extension)
        {
            var scenario = ScenarioFor(script, cell);
            return Fingerprint.Build(Record(script, cell, variant, extension), Registry, DatasetFor(script, scenario, variant).Hash);
        }

        /// <summary>数据集内容哈希（该脚本在该格子/变体下实际运行的数据集）。</summary>
        public string DatasetHashFor(InputScript script, string cell, LabRunVariant? variant = null) =>
            DatasetFor(script, ScenarioFor(script, cell), variant).Hash;

        /// <summary>该脚本适用的格子：格子的脚本子集为空表示适用全部，否则脚本 id 必须在子集里。</summary>
        public IReadOnlyList<LabScenario> ApplicableCells(InputScript script)
        {
            var result = new List<LabScenario>();
            foreach (var cell in DatasetFor(script).Catalog.Scenarios())
            {
                var subset = cell.ScriptSubset;
                var applicable = subset.Count == 0;
                foreach (var id in subset)
                {
                    if (string.Equals(id, script.Meta.ScriptId, StringComparison.Ordinal))
                    {
                        applicable = true;
                        break;
                    }
                }

                if (applicable)
                {
                    result.Add(cell);
                }
            }

            return result;
        }
    }

    /// <summary>一个格子上的基线比较状态。</summary>
    public enum CellStatus
    {
        Pass,
        Diff,
        BaselineMissing,
        NotRunnable,
    }

    /// <summary>套件里一个（脚本，格子）的结果。</summary>
    public sealed class CellResult
    {
        public string Script { get; }

        public string Cell { get; }

        public CellStatus Status { get; }

        public FingerprintDiff? Diff { get; }

        public string Message { get; }

        /// <summary>脚本期望清单在该格子上的逐条判定（脚本没有期望、或该格子不可运行时为空）。</summary>
        public IReadOnlyList<ExpectationResult> Expectations { get; }

        /// <summary>期望里没有通过的条数（失败或无法判定）。</summary>
        public int ExpectationFailures
        {
            get
            {
                var n = 0;
                foreach (var e in Expectations)
                {
                    if (!e.Ok)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public CellResult(
            string script, string cell, CellStatus status, FingerprintDiff? diff, string message,
            IReadOnlyList<ExpectationResult>? expectations = null)
        {
            Script = script;
            Cell = cell;
            Status = status;
            Diff = diff;
            Message = message;
            Expectations = expectations ?? Array.Empty<ExpectationResult>();
        }
    }

    /// <summary>
    /// 夹具目录约定：<c>&lt;dir&gt;/scripts/&lt;scriptId&gt;.script.json</c> 与
    /// <c>&lt;dir&gt;/baselines/&lt;scriptId&gt;.baseline.json</c>（一个脚本一份基线文件，里面按格子分条目）。
    /// 夹具与基线入库；录制文件与本地产出的指纹落在被忽略的输出目录。
    /// </summary>
    public static class LabFixtures
    {
        public const string ScriptSuffix = ".script.json";
        public const string BaselineSuffix = ".baseline.json";

        public static string ScriptPath(string dir, string scriptId) => Path.Combine(dir, "scripts", scriptId + ScriptSuffix);

        public static string BaselinePath(string dir, string scriptId) => Path.Combine(dir, "baselines", scriptId + BaselineSuffix);

        /// <summary>读取目录下全部标准脚本（按脚本 id 排序）。</summary>
        public static List<InputScript> LoadScripts(string dir)
        {
            var scriptsDir = Path.Combine(dir, "scripts");
            var scripts = new List<InputScript>();
            if (!Directory.Exists(scriptsDir))
            {
                return scripts;
            }

            var files = new List<string>(Directory.GetFiles(scriptsDir, "*" + ScriptSuffix));
            files.Sort(StringComparer.Ordinal);
            foreach (var file in files)
            {
                scripts.Add(InputScript.Parse(File.ReadAllText(file, Encoding.UTF8), file));
            }

            scripts.Sort((a, b) => string.CompareOrdinal(a.Meta.ScriptId, b.Meta.ScriptId));
            return scripts;
        }

        /// <summary>基线文件内容：<c>{formatVersion, script, cells: {格子: {key, groups}}}</c>。</summary>
        public static string SerializeBaseline(string scriptId, IEnumerable<KeyValuePair<string, Fingerprint>> cells)
        {
            var cellsBuilder = new JsonObjectBuilder();
            foreach (var pair in cells)
            {
                cellsBuilder.Add(
                    pair.Key,
                    new JsonObjectBuilder().Add("key", pair.Value.Key).Add("groups", pair.Value.Groups).Build());
            }

            return LabJson.Write(new JsonObjectBuilder()
                .Add("formatVersion", LabJson.Num(Fingerprint.FormatVersion))
                .Add("script", LabJson.Str(scriptId))
                .Add("cells", cellsBuilder.Build())
                .Build());
        }

        public static Dictionary<string, Fingerprint> ParseBaseline(string text, string what)
        {
            var root = LabJson.ParseObject(text, what);
            var format = LabJson.RequireInt(root, "formatVersion", what);
            if (format > Fingerprint.FormatVersion)
            {
                throw new LabFormatException($"{what} 的 formatVersion={format} 高于本内核支持的 {Fingerprint.FormatVersion}");
            }

            var result = new Dictionary<string, Fingerprint>(StringComparer.Ordinal);
            foreach (var pair in LabJson.RequireObject(root, "cells", what))
            {
                if (!(pair.Value is JsonObject cell))
                {
                    throw new LabFormatException($"{what}.cells.{pair.Key} 必须是对象");
                }

                result[pair.Key] = new Fingerprint(
                    LabJson.RequireObject(cell, "key", what + ".cells." + pair.Key),
                    LabJson.RequireObject(cell, "groups", what + ".cells." + pair.Key));
            }

            return result;
        }
    }

    /// <summary>套件：全部标准脚本 × 其适用格子，逐个与基线比较；也提供更新基线与"导出为测试"。</summary>
    public static class LabSuite
    {
        public static List<CellResult> Check(LabRunner runner, string fixturesDir, string? onlyScript = null, string? onlyCell = null)
        {
            var results = new List<CellResult>();
            foreach (var script in LabFixtures.LoadScripts(fixturesDir))
            {
                if (onlyScript != null && !string.Equals(onlyScript, script.Meta.ScriptId, StringComparison.Ordinal))
                {
                    continue;
                }

                ValidateExpectations(runner, script);
                var baselinePath = LabFixtures.BaselinePath(fixturesDir, script.Meta.ScriptId);
                var fingerprints = new Dictionary<string, Fingerprint>(StringComparer.Ordinal);
                Dictionary<string, Fingerprint>? baselines = null;
                if (File.Exists(baselinePath))
                {
                    baselines = LabFixtures.ParseBaseline(File.ReadAllText(baselinePath, Encoding.UTF8), baselinePath);
                }

                foreach (var cell in runner.ApplicableCells(script))
                {
                    if (onlyCell != null && !string.Equals(onlyCell, cell.Cell, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var runnability = cell.CheckRunnable(LabHost.AvailableCapabilities);
                    if (!runnability.Runnable)
                    {
                        results.Add(new CellResult(script.Meta.ScriptId, cell.Cell, CellStatus.NotRunnable, null, runnability.ToString()));
                        continue;
                    }

                    var actual = runner.Run(script, cell.Cell);
                    var expectations = EvaluateExpectations(runner, script, cell.Cell, actual, fingerprints);
                    if (baselines == null || !baselines.TryGetValue(cell.Cell, out var baseline))
                    {
                        results.Add(new CellResult(
                            script.Meta.ScriptId, cell.Cell, CellStatus.BaselineMissing, null,
                            $"没有基线（{baselinePath} 里无格子 {cell.Cell}）", expectations));
                        continue;
                    }

                    var diff = FingerprintComparer.Compare(baseline, actual, runner.Registry);
                    var failed = false;
                    foreach (var e in expectations)
                    {
                        failed |= !e.Ok;
                    }

                    // 基线有差异或期望没全过，该格子都算"有差异"（套件退出码 1、汇总行 diff 计数）；明细分别打印。
                    results.Add(new CellResult(
                        script.Meta.ScriptId, cell.Cell, diff.Ok && !failed ? CellStatus.Pass : CellStatus.Diff, diff, string.Empty, expectations));
                }
            }

            return results;
        }

        /// <summary>
        /// 静态检查脚本期望清单引用的度量组、度量与格子都存在（不跑模拟）；有问题抛 <see cref="LabFormatException"/>
        /// （脚本内容错误，命令行退出码 5）：拼错的度量名或格子名不能让期望悄悄失效。
        /// </summary>
        public static void ValidateExpectations(LabRunner runner, InputScript script)
        {
            if (script.Expectations.Count == 0)
            {
                return;
            }

            var cells = new List<string>();
            foreach (var scenario in runner.DatasetFor(script).Catalog.Scenarios())
            {
                cells.Add(scenario.Cell);
            }

            var problems = ExpectationEvaluator.Validate(script.Expectations, runner.Registry, cells);
            if (problems.Count > 0)
            {
                throw new LabFormatException(
                    $"脚本 {script.Meta.ScriptId} 的期望清单有 {problems.Count} 处引用无效：" + Environment.NewLine + "  "
                    + string.Join(Environment.NewLine + "  ", problems));
            }
        }

        /// <summary>
        /// 判定脚本期望清单在一个格子上的结果：相对关系引用的另一个格子按需运行并缓存（<paramref name="cache"/> 按格子短名存指纹，
        /// 调用方在一个脚本的各格子之间共用同一份缓存，免得重复跑）；另一个格子不存在或不可运行时那条期望记为无法判定。
        /// 期望引用的度量组/度量/格子不存在时，同样逐条给出"无法判定"，不抛异常。
        /// </summary>
        public static List<ExpectationResult> EvaluateExpectations(
            LabRunner runner, InputScript script, string cell, Fingerprint actual, Dictionary<string, Fingerprint> cache) =>
            EvaluateExpectations(runner, script, cell, actual, cache, null);

        /// <summary>
        /// 同上，另允许调用方给出"相对关系引用的另一个格子怎么跑"（<paramref name="runOther"/>，引擎宿主用它在引擎宿主上跑对照格子）；
        /// 为 null 时用 <paramref name="runner"/> 的无头宿主。可运行性检查不变。
        /// </summary>
        public static List<ExpectationResult> EvaluateExpectations(
            LabRunner runner, InputScript script, string cell, Fingerprint actual, Dictionary<string, Fingerprint> cache,
            Func<string, Fingerprint>? runOther)
        {
            if (script.Expectations.Count == 0)
            {
                return new List<ExpectationResult>();
            }

            cache[cell] = actual;
            Fingerprint FingerprintOf(string other)
            {
                if (cache.TryGetValue(other, out var cached))
                {
                    return cached;
                }

                var scenario = runner.Dataset.Catalog.GetScenario(other);
                var runnability = scenario.CheckRunnable(LabHost.AvailableCapabilities);
                if (!runnability.Runnable)
                {
                    throw new LabFormatException(runnability.ToString());
                }

                var fingerprint = runOther != null ? runOther(other) : runner.Run(script, other);
                cache[other] = fingerprint;
                return fingerprint;
            }

            return ExpectationEvaluator.Evaluate(script.Expectations, cell, actual, runner.Registry, FingerprintOf);
        }

        /// <summary>重新跑全部适用且可运行的格子，把指纹写成基线文件（覆盖）。返回写出的文件路径。</summary>
        public static List<string> UpdateBaselines(LabRunner runner, string fixturesDir, string? onlyScript = null)
        {
            var written = new List<string>();
            foreach (var script in LabFixtures.LoadScripts(fixturesDir))
            {
                if (onlyScript != null && !string.Equals(onlyScript, script.Meta.ScriptId, StringComparison.Ordinal))
                {
                    continue;
                }

                written.Add(WriteBaseline(runner, fixturesDir, script, null));
            }

            return written;
        }

        /// <summary>
        /// "导出为测试"（06 第 3.5 节）：把脚本与其在各格子上的指纹落成夹具——脚本文件（带由指纹生成的期望清单）与基线文件。
        /// 脚本按规范 JSON 重写（键序固定、换行统一），因此手写脚本与回放导入脚本入库形态一致。
        /// 期望清单的生成规则见 <see cref="ExpectationExporter"/>；脚本里已有的手写期望（id 不以 <see cref="Expectation.AutoPrefix"/> 开头）
        /// 原样保留在前，之前自动生成的期望整体换成这次新生成的。<paramref name="expect"/> 为空时用缺省取舍，
        /// <paramref name="writeExpectations"/> 为假则不改脚本里的期望（只写脚本与基线）。
        /// </summary>
        public static List<string> ExportAsTest(
            LabRunner runner, InputScript script, string fixturesDir, string? onlyCell = null,
            ExpectationExportOptions? expect = null, bool writeExpectations = true)
        {
            var written = new List<string>();
            var cells = RunCells(runner, script, onlyCell);
            var exported = script;
            if (writeExpectations)
            {
                var merged = new List<Expectation>();
                foreach (var e in script.Expectations)
                {
                    if (!e.Id.StartsWith(Expectation.AutoPrefix, StringComparison.Ordinal))
                    {
                        merged.Add(e);
                    }
                }

                merged.AddRange(ExpectationExporter.FromFingerprints(cells, runner.Registry, expect));
                exported = script.WithExpectations(merged);
            }

            var scriptPath = LabFixtures.ScriptPath(fixturesDir, script.Meta.ScriptId);
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            File.WriteAllText(scriptPath, exported.ToJson(), new UTF8Encoding(false));
            written.Add(scriptPath);
            written.Add(WriteBaseline(fixturesDir, script, cells));
            return written;
        }

        private static List<KeyValuePair<string, Fingerprint>> RunCells(LabRunner runner, InputScript script, string? onlyCell)
        {
            var cells = new List<KeyValuePair<string, Fingerprint>>();
            foreach (var cell in runner.ApplicableCells(script))
            {
                if (onlyCell != null && !string.Equals(onlyCell, cell.Cell, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!cell.CheckRunnable(LabHost.AvailableCapabilities).Runnable)
                {
                    continue;
                }

                cells.Add(new KeyValuePair<string, Fingerprint>(cell.Cell, runner.Run(script, cell.Cell)));
            }

            return cells;
        }

        private static string WriteBaseline(string fixturesDir, InputScript script, List<KeyValuePair<string, Fingerprint>> cells)
        {
            var path = LabFixtures.BaselinePath(fixturesDir, script.Meta.ScriptId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, LabFixtures.SerializeBaseline(script.Meta.ScriptId, cells), new UTF8Encoding(false));
            return path;
        }

        private static string WriteBaseline(LabRunner runner, string fixturesDir, InputScript script, string? onlyCell) =>
            WriteBaseline(fixturesDir, script, RunCells(runner, script, onlyCell));
    }
}
