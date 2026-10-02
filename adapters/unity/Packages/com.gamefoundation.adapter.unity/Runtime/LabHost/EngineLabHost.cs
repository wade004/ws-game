#nullable enable
// EngineLabHost：实验室引擎宿主的门面（手感设计/06 第 4 节"引擎宿主"）。
//
// 它把内核的 LabRunner（脚本、格子、度量、指纹定义）与引擎舞台（EngineLabStage）接在一起：
//   · 同一份实验室数据集、同一批脚本与格子，不另写一套定义；
//   · 跑出的逻辑组指纹与无头宿主逐字节一致（RunHeadless 给出无头一侧的同一文本，跨宿主不变量比较二者）；
//   · 另带引擎侧表现组（engine 组）度量；
//   · 脚本的期望清单也在引擎宿主上判定（含相对关系引用的对照格子，也在引擎宿主上跑）；
//   · 覆盖层（手感档案字段覆盖）经内核的覆盖扩展接入，不改源数据。
// 判断记录（可选组件）：本程序集 autoReferenced 为 false，只有显式引用它的测试/编辑器工具才会带上它与内核 DLL；不使用实验室的游戏完全不受影响。
using System;
using System.Collections.Generic;
using System.IO;
using Core.Foundation.DataRegistry;
using Lab;
using UnityEngine;

namespace Adapter.Unity.LabHost
{
    /// <summary>一次引擎宿主运行的结果。</summary>
    public sealed class EngineLabRun
    {
        public InputScript Script { get; }

        public string Cell { get; }

        public LabRecording Recording { get; }

        /// <summary>完整指纹（含引擎侧 engine 组）。</summary>
        public Fingerprint Fingerprint { get; }

        /// <summary>无头格式的逻辑组指纹文本（默认注册表、仅逻辑类度量）；与 <see cref="EngineLabHost.RunHeadlessLogic"/> 逐字节比较。</summary>
        public string LogicProjection { get; }

        public EngineRecording Engine => Recording.Engine!;

        /// <summary>本次施加的输入噪声记录（没有噪声为 null）；可存下来供回放。</summary>
        public InputNoiseRecord? NoiseRecord { get; }

        /// <summary>实际送进宿主的脚本（带噪时是带噪脚本）。</summary>
        public InputScript EffectiveScript { get; }

        public EngineLabRun(
            InputScript script, InputScript effective, string cell, LabRecording recording, Fingerprint fingerprint,
            string logicProjection, InputNoiseRecord? noise)
        {
            Script = script;
            EffectiveScript = effective;
            Cell = cell;
            Recording = recording;
            Fingerprint = fingerprint;
            LogicProjection = logicProjection;
            NoiseRecord = noise;
        }
    }

    public sealed class EngineLabHost
    {
        private readonly string _repoRoot;
        private readonly LabRunner _engineRunner;
        private readonly LabRunner _headlessRunner;

        /// <summary>仓库根（含 <c>data/_lab</c> 与 <c>lab/fixtures</c>）。</summary>
        public string RepoRoot => _repoRoot;

        /// <summary>夹具目录（<c>lab/fixtures</c>，含脚本与基线）。</summary>
        public string FixturesDir => Path.Combine(_repoRoot, "lab", "fixtures");

        /// <summary>引擎宿主用的运行器（注册表带 engine 组）。</summary>
        public LabRunner Runner => _engineRunner;

        /// <summary>无头宿主用的运行器（默认注册表，与既有基线一致）。</summary>
        public LabRunner HeadlessRunner => _headlessRunner;

        public MetricRegistry Registry => _engineRunner.Registry;

        public EngineLabHost(string repoRoot)
        {
            _repoRoot = repoRoot;
            var sources = new List<IDataSource>
            {
                LabDataSources.FromDirectory(Path.Combine(repoRoot, "data", "_framework")),
                LabDataSources.FromDirectory(Path.Combine(repoRoot, "data", "_lab")),
            };
            Func<string, IDataSource> resolver = path =>
                LabDataSources.FromDirectory(Path.IsPathRooted(path) ? path : Path.Combine(repoRoot, path));
            var dataset = LabDataset.Load(sources);
            _engineRunner = new LabRunner(dataset, MetricRegistry.CreateWithEngine(), resolver);
            _headlessRunner = new LabRunner(dataset, MetricRegistry.CreateDefault(), resolver);
        }

        /// <summary>从工程位置向上找仓库根（环境变量 <c>GF_LAB_ROOT</c> 优先）；找不到抛 <see cref="DirectoryNotFoundException"/>。</summary>
        public static string LocateRepoRoot()
        {
            var env = Environment.GetEnvironmentVariable("GF_LAB_ROOT");
            if (!string.IsNullOrEmpty(env) && IsRepoRoot(env!))
            {
                return Path.GetFullPath(env!);
            }

            var dir = new DirectoryInfo(Application.dataPath);
            while (dir != null)
            {
                if (IsRepoRoot(dir.FullName))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("找不到实验室仓库根（需要同时含 data/_lab 与 lab/fixtures）；可用环境变量 GF_LAB_ROOT 指定。");
        }

        private static bool IsRepoRoot(string path) =>
            Directory.Exists(Path.Combine(path, "data", "_lab")) && Directory.Exists(Path.Combine(path, "lab", "fixtures"));

        public static EngineLabHost Open() => new EngineLabHost(LocateRepoRoot());

        public List<InputScript> LoadScripts() => LabFixtures.LoadScripts(FixturesDir);

        /// <summary>脚本适用的格子短名（不含不可运行的）。</summary>
        public List<string> RunnableCells(InputScript script)
        {
            var cells = new List<string>();
            foreach (var cell in _engineRunner.ApplicableCells(script))
            {
                if (cell.CheckRunnable(Lab.LabHost.AvailableCapabilities).Runnable)
                {
                    cells.Add(cell.Cell);
                }
            }

            return cells;
        }

        /// <summary>在引擎宿主上跑一个脚本格子：同一个内核宿主循环 + 舞台扩展（+ 可选覆盖层）。</summary>
        public EngineLabRun Run(
            InputScript script, string cell, EngineLabOptions? options = null, OverrideSet? overrides = null, LabRunVariant? variant = null)
        {
            options ??= new EngineLabOptions();
            var effective = script;
            InputNoiseRecord? record = null;
            if (options.NoiseReplay != null)
            {
                effective = InputNoiseModel.Replay(script, options.NoiseReplay);
                record = options.NoiseReplay;
            }
            else if (options.Noise != null && !options.Noise.IsNone)
            {
                effective = options.Noise.Apply(script, out var applied);
                record = applied;
            }

            var stage = new EngineLabStage(options);
            try
            {
                LabHostExtension extension = overrides == null
                    ? stage
                    : new CompositeLabHostExtension(stage, new OverrideExtension(overrides));
                var recording = _engineRunner.Record(effective, cell, variant, extension);
                var fingerprint = Fingerprint.Build(recording, _engineRunner.Registry, _engineRunner.DatasetHashFor(effective, cell, variant));
                var logic = fingerprint.Project(_headlessRunner.Registry, MetricClass.Logic);
                return new EngineLabRun(script, effective, cell, recording, fingerprint, logic, record);
            }
            finally
            {
                stage.Dispose();
            }
        }

        /// <summary>在无头宿主上跑同一个脚本格子（可带覆盖层），返回无头格式的逻辑组指纹文本。</summary>
        public string RunHeadlessLogic(InputScript script, string cell, OverrideSet? overrides = null, LabRunVariant? variant = null)
        {
            var fingerprint = RunHeadless(script, cell, overrides, variant);
            return fingerprint.Project(_headlessRunner.Registry, MetricClass.Logic);
        }

        public Fingerprint RunHeadless(InputScript script, string cell, OverrideSet? overrides = null, LabRunVariant? variant = null)
        {
            LabHostExtension? extension = overrides == null ? null : new OverrideExtension(overrides);
            return _headlessRunner.Run(script, cell, variant, extension);
        }

        /// <summary>
        /// 在引擎宿主上判定脚本的期望清单：当前格子取 <paramref name="run"/> 的指纹，相对关系引用的对照格子也在引擎宿主上跑（同一套选项）。
        /// 脚本没有期望时返回空列表。
        /// </summary>
        public List<ExpectationResult> JudgeExpectations(
            EngineLabRun run, EngineLabOptions? options = null, OverrideSet? overrides = null, LabRunVariant? variant = null)
        {
            var cache = new Dictionary<string, Fingerprint>(StringComparer.Ordinal);
            return LabSuite.EvaluateExpectations(
                _engineRunner, run.Script, run.Cell, run.Fingerprint, cache,
                other => Run(run.Script, other, options, overrides, variant).Fingerprint);
        }
    }
}
