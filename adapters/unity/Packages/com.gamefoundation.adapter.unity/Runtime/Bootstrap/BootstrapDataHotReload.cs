#nullable enable
// BootstrapDataHotReload：包内两个引导（GameFoundationBootstrap / FrameworkResidentHost）的开发期数据热重载组件
// （手感落地 M3-C；游戏模板同名职责的 games/_template/Runtime/DataHotReload.cs 在游戏层，包内引导用不到，这里提供包内等价物）。
// 监视各数据根下的 *.json，去抖后调用 DataRegistry.Reload(table)，成功后补发 data.load_completed（失败补发 data.validation_failed）——
// 手感数据（feel.*）的热加载订阅在核心装配里（CarriersFeelSystem 订阅 data.load_completed 换入新档案，手感落地 M2-B），据此随之热更换。
//
// 判断记录（缺省关闭、开发期打开）：引导上的 EnableDataHotReload 缺省 false；打开后组件仍只在 UNITY_EDITOR || DEVELOPMENT_BUILD 下有实现，
// 发布构建下是空壳（Initialize 直接返回、不定义 Update，Unity 不会排每帧回调），同模板 DataHotReload 的双保险。
//
// 判断记录（只用轮询，不建 FileSystemWatcher，与模板的取舍不同）：模板为"事件 + 轮询兜底"两路（FileSystemWatcher 事件投递没有时限承诺，
// 会延迟数百毫秒甚至静默丢事件，模板因此补了轮询兜底）。包内引导没有"事件先到就省一次扫描"的需求：轮询（每 PollInterval 比对每个 *.json 的
// 写入时刻 + 长度）单独就能覆盖 Changed/Created/Deleted/Renamed 四类变化，并且不依赖线程池线程，天然全在主线程，不需要锁；
// 代价是数据根下文件数很大时每个轮询周期枚举一次目录，开发期可接受。去抖窗口 300 ms，同模板（防止编辑器两阶段写入触发两次重载）。
//
// 判断记录（Reload 失败语义）：同模板——DataRegistry.Reload 对信封级错误保持旧数据，但只读查询是全局阻断语义，热重载改坏任何一张表都会让整个
// 数据集在下一次成功重载之前不可读；手感侧的热加载订阅在收到 data.load_completed 时才换入新档案，被拒时保持当前档案（见 CarriersFeelSystem.LastHotReload）。
//
// 判断记录（表名取自文件名）：表文件命名约定 <表名>.json（如 feel.preset.json），监视到变更的文件取不带扩展名的文件名作为表名，与模板一致。
using System;
using System.Collections.Generic;
using Adapter.Unity.Diagnostics;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.IO;
#endif

namespace Adapter.Unity.Bootstrap
{
    /// <summary>见文件头判断记录。引导在数据加载成功后按其 <c>EnableDataHotReload</c> 开关挂载本组件（<c>AddComponent</c> 后立即 <see cref="Initialize"/>）。</summary>
    public sealed class BootstrapDataHotReload : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

        private DataRegistry? _registry;
        private IEventBus? _bus;
        private string? _validationReportFilePath;
        private readonly List<string> _watchRoots = new List<string>();
        private readonly Dictionary<string, (DateTime LastWriteUtc, long Length)> _knownFiles =
            new Dictionary<string, (DateTime, long)>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _pendingChanges = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private DateTime _lastPollUtc = DateTime.MinValue;
#endif

        /// <summary>成功重载（发布了 <c>data.load_completed</c>）的次数累计（诊断/测试用）；发布构建下恒为 0。</summary>
        public int ReloadCount { get; private set; }

        /// <summary>引导侧的统一挂载入口：把各数据根（相对内容根的路径，或绝对路径——<c>Path.Combine</c> 遇到绝对路径取其本身）解析成绝对目录，
        /// 与数据实际来源是同一套解析规则（引导里 <c>FileSystemDataSource</c> 也是相对内容根解析），挂到 <paramref name="host"/> 上并初始化。</summary>
        public static BootstrapDataHotReload Attach(
            GameObject host, DataRegistry registry, IEventBus bus, string contentRootDir, IReadOnlyList<string> datasetRoots, string? validationReportFilePath)
        {
            var absolute = new List<string>(datasetRoots.Count);
            for (var i = 0; i < datasetRoots.Count; i++)
            {
                absolute.Add(System.IO.Path.Combine(contentRootDir, datasetRoots[i]));
            }

            var component = host.AddComponent<BootstrapDataHotReload>();
            component.Initialize(registry, bus, absolute, validationReportFilePath);
            return component;
        }

        /// <param name="registry">引导已完成首次 <c>LoadAll</c> 的同一个 <see cref="DataRegistry"/>（需要具体类：<c>Reload</c> 不在只读视图接口上）。</param>
        /// <param name="bus">补发 <c>data.load_completed</c>/<c>data.validation_failed</c> 的事件总线，与 <paramref name="registry"/> 构造时是同一条。</param>
        /// <param name="watchRoots">要监视的绝对目录列表（引导各数据根的绝对路径，与数据实际来源同一套解析）；不存在的目录跳过。</param>
        /// <param name="validationReportFilePath">引导已解析好的校验报告落盘路径（null = 未配置），与启动校验共用同一个目标文件。</param>
        public void Initialize(DataRegistry registry, IEventBus bus, IReadOnlyList<string> watchRoots, string? validationReportFilePath)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            if (watchRoots == null) throw new ArgumentNullException(nameof(watchRoots));

            _registry = registry;
            _bus = bus;
            _validationReportFilePath = validationReportFilePath;

            for (var i = 0; i < watchRoots.Count; i++)
            {
                var root = watchRoots[i];
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    continue;
                }

                _watchRoots.Add(root);
                // 基线必须在这里同步扫一次：推迟到第一次轮询会把"首次加载时已存在的文件"误判成新出现，白白触发一次多余的 Reload。
                foreach (var path in SafeEnumerateJsonFiles(root))
                {
                    if (TryGetFileState(path, out var state))
                    {
                        _knownFiles[path] = state;
                    }
                }
            }
#else
            _ = registry;
            _ = bus;
            _ = watchRoots;
            _ = validationReportFilePath;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update() => ProcessPendingChanges();

        /// <summary>手动驱动一轮"轮询 -> 去抖到期 -> Reload"。生产路径下 <see cref="Update"/> 每帧调用的就是本方法；公开是为了让测试与无 Unity 帧回调的
        /// 宿主能手动驱动（同模板 <c>DataHotReload.ProcessPendingChanges</c> 的取舍：公开而不是 InternalsVisibleTo）。</summary>
        public void ProcessPendingChanges()
        {
            if (_registry == null)
            {
                return;
            }

            Poll();

            List<string>? ready = null;
            var now = DateTime.UtcNow;
            foreach (var kv in _pendingChanges)
            {
                if (now - kv.Value >= DebounceWindow)
                {
                    (ready ??= new List<string>()).Add(kv.Key);
                }
            }

            if (ready == null)
            {
                return;
            }

            for (var i = 0; i < ready.Count; i++)
            {
                _pendingChanges.Remove(ready[i]);
                ReloadTable(ready[i]);
            }
        }

        private void Poll()
        {
            var now = DateTime.UtcNow;
            if (now - _lastPollUtc < PollInterval)
            {
                return;
            }

            _lastPollUtc = now;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _watchRoots.Count; i++)
            {
                foreach (var path in SafeEnumerateJsonFiles(_watchRoots[i]))
                {
                    seen.Add(path);
                    if (!TryGetFileState(path, out var state))
                    {
                        continue; // 列目录与取状态之间被删/移走：下一轮要么看到已消失，要么看到稳定存在。
                    }

                    if (!_knownFiles.TryGetValue(path, out var prev) || prev.LastWriteUtc != state.LastWriteUtc || prev.Length != state.Length)
                    {
                        _knownFiles[path] = state;
                        MarkPending(path);
                    }
                }
            }

            List<string>? disappeared = null;
            foreach (var known in _knownFiles.Keys)
            {
                if (!seen.Contains(known))
                {
                    (disappeared ??= new List<string>()).Add(known);
                }
            }

            if (disappeared != null)
            {
                for (var i = 0; i < disappeared.Count; i++)
                {
                    _knownFiles.Remove(disappeared[i]);
                    MarkPending(disappeared[i]);
                }
            }
        }

        private void MarkPending(string path)
        {
            var table = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(table))
            {
                _pendingChanges[table] = DateTime.UtcNow;
            }
        }

        private static IEnumerable<string> SafeEnumerateJsonFiles(string root)
        {
            try
            {
                return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static bool TryGetFileState(string path, out (DateTime LastWriteUtc, long Length) state)
        {
            try
            {
                var info = new FileInfo(path);
                state = (info.LastWriteTimeUtc, info.Length);
                return true;
            }
            catch (IOException)
            {
                state = default;
                return false;
            }
        }

        private void ReloadTable(string table)
        {
            ValidationReport report;
            try
            {
                report = _registry!.Reload(table);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BootstrapDataHotReload] 重载表 \"{table}\" 时抛出异常，已忽略本次变更：{ex}");
                return;
            }

            ValidationReportFileOutlet.WriteIfConfigured(_validationReportFilePath, ValidationReportTriggerSource.HotReload, report.Issues, table);

            if (report.IsBlocking)
            {
                // 同模板（ADR-0046/ADR-0042 决策 4）：Unity 侧诊断一律不产生 LogError。
                Debug.LogWarning($"[BootstrapDataHotReload] 热重载 \"{table}\" 失败（{report.ErrorCount} 个错误、{report.WarningCount} 个警告）：" +
                    string.Join("; ", System.Linq.Enumerable.Select(report.Issues, i => i.ToString())));
                _bus!.PublishImmediate(new DataValidationFailedEvent(report.ErrorCount, report.WarningCount, report.Issues));
                return;
            }

            var tables = _registry!.Tables;
            var recordCount = 0;
            for (var i = 0; i < tables.Count; i++)
            {
                recordCount += _registry.GetAll(tables[i]).Count;
            }

            ReloadCount++;
            _bus!.PublishImmediate(new DataLoadCompletedEvent(tables.Count, recordCount, report.ErrorCount, report.WarningCount));
        }
#endif
    }
}
