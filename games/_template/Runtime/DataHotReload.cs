#nullable enable
// DataHotReload：开发期数据热重载标准实现（F3 新增，ADR-0018 决策 5/ADR-0019 决策 4 附带交付，见
// architecture/13_新游戏接入指南.md 第 4 节"开发期数据热重载"新增行、games/_template/README.md
// "复制为新游戏：改哪几处"/"口味配置项"、editor/docs/编辑器产品文档.md 4.6 节）：监视框架数据根与
// 游戏数据根下的 *.json 文件变更，去抖后调用 DataRegistry.Reload(table) 把改动后的内容重新读进
// 内存，不需要重启 Unity 编辑器/独立版进程即可在编辑数据表后立刻看到效果，供编辑器（ADR-0018 决策
// 1/2 的独立消费方项目）与本模板直连的开发工作流共用同一套语义。
//
// 判断记录（编译条件）：仅 UNITY_EDITOR || DEVELOPMENT_BUILD 下编译为有效实现——发布（Release/
// Shipping）构建下本类型是一具空壳：Initialize 直接返回、不创建 FileSystemWatcher、不定义
// Update()（MonoBehaviour 未定义某个消息方法时 Unity 根本不会把它排进每帧回调队列，不是"定义了但
// 什么也不做"的空转，是真正的零调用开销）。GameOptions.EnableDataHotReload 默认 true——即便游戏层
// 忘记在发布构建前把它关掉，只要构建本身不是编辑器/不勾选 Development Build，本类型仍然保持空壳，
// 双重保险，不依赖游戏层记得手动关闭开关。GameBootstrap 无条件持有并调用本类型（不用 #if 包裹调用
// 点），两种编译形态下方法签名完全一致，只是方法体不同，调用方不需要关心当前处于哪种编译条件。
//
// 判断记录（去抖 + 主线程投递）：System.IO.FileSystemWatcher 的事件在线程池线程上触发，不能在那里
// 直接调用 DataRegistry.Reload（DataRegistry 无内部锁，不是线程安全类型，见其类型注释"无反射、无
// LINQ 热路径、无线程"的既有设计前提）。改法：事件处理器只做一件事——把"表名 -> 最近一次变更时刻
// （UTC）"记进一个用 lock 保护的字典；真正的 Reload 调用挪到 MonoBehaviour.Update()（Unity 主线程）
// 里，每帧检查哪些表的最近一次变更已经过去 >= 300ms（去抖窗口，防止编辑器/文本工具保存文件时的多次
// 写入事件——例如"截断再写入"两阶段——把同一次编辑触发两次重载），到期的表才真正调用 Reload。
//
// 判断记录（Reload 失败语义，核实结论）：DataRegistry.Reload(table) 对信封级错误（JSON 解析失败、
// 主键重复等——在真正建出新记录之前就失败）确实保持该表旧数据不变（新记录尚未落进内部字典就已经
// 提前返回）；但 DataRegistry 的只读查询遵循"全局阻断"语义（EnsureReadable：_blocked 为 true 时
// Get/GetAll/Query 一律抛异常，不区分是哪张表触发的阻断，见该类型 RunValidationAndBuildReport 判断
// 记录）——热重载改坏一张表（不论是信封级错误还是字段级校验错误）都会让整个 registry（不止这一张
// 表）在下一次成功的 Reload/LoadAll 把 _blocked 重新算成 false 之前，全部表都无法读取，这是需要在
// games/_template/README.md 里明确记录的限制，不是"只冻结出问题的那一张表，其它照常"。
//
// 判断记录（事件发出）：DataRegistry.Reload 本身不发 data.load_completed/data.validation_failed
// （只有 LoadAll(Core) 会发，见该类型源码——Reload 的实现里完全没有触碰 _bus），本组件因此在
// Reload 成功后自行经事件总线补发一次 DataLoadCompletedEvent（tableCount/recordCount/errorCount/
// warningCount，与 LoadAll 发出的同一事件形状一致，供表现层/编辑器联调监听、不需要区分"这次事件
// 是首次加载发的还是热重载发的"）；失败（阻断）时对称补发 DataValidationFailedEvent，行为与
// LoadAll 阻断态下的既有约定一致。
using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.IO;
#endif

namespace Game.Template
{
    /// <summary>见文件头判断记录。<see cref="GameBootstrap"/> 在数据加载完成后按
    /// <see cref="GameOptions.EnableDataHotReload"/> 挂载本组件（<c>AddComponent&lt;DataHotReload&gt;()</c>
    /// 后立即调用 <see cref="Initialize"/>）。</summary>
    public sealed class DataHotReload : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(300);

        private DataRegistry? _registry;
        private IEventBus? _bus;
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly object _lock = new object();

        /// <summary>表名 -&gt; 最近一次收到变更事件的 UTC 时刻；<see cref="Update"/> 每帧检查是否已
        /// 超过 <see cref="DebounceWindow"/>，到期才真正 Reload（见文件头判断记录"去抖 + 主线程
        /// 投递"）。</summary>
        private readonly Dictionary<string, DateTime> _pendingChanges = new Dictionary<string, DateTime>(StringComparer.Ordinal);
#endif

        /// <param name="registry">与 <see cref="GameBootstrap"/> 已完成首次 <c>LoadAll</c> 的同一个
        /// <see cref="DataRegistry"/> 实例（需要具体类而不只是 <see cref="IDataRegistryView"/>——
        /// <see cref="DataRegistry.Reload(string)"/> 不在 <see cref="IDataRegistryView"/> 接口上，
        /// 见 08 号任务书约束"不改 IDataRegistry/IDataRegistryView 接口签名"）。</param>
        /// <param name="bus">用于补发 <c>data.load_completed</c>/<c>data.validation_failed</c> 的
        /// 事件总线，与 <paramref name="registry"/> 构造时使用的是同一条。</param>
        /// <param name="watchRoots">要监视的绝对文件系统目录列表（通常是框架数据根与游戏数据根各一
        /// 个，见 <see cref="GameBootstrap"/> 调用点如何从 <c>UnityFileSystem.GetContentRootDir()</c>
        /// 拼出绝对路径）；不存在的目录会被跳过，不报错（游戏层可能暂时只有其中一个根）。</param>
        public void Initialize(DataRegistry registry, IEventBus bus, IReadOnlyList<string> watchRoots)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            if (watchRoots == null) throw new ArgumentNullException(nameof(watchRoots));

            _registry = registry;
            _bus = bus;

            for (var i = 0; i < watchRoots.Count; i++)
            {
                var root = watchRoots[i];
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    continue;
                }

                FileSystemWatcher watcher;
                try
                {
                    watcher = new FileSystemWatcher(root, "*.json")
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    };
                }
                catch (Exception ex) when (ex is ArgumentException || ex is IOException)
                {
                    Debug.LogWarning($"[DataHotReload] 无法监视数据根 \"{root}\"，本次跳过（不影响其它数据根）：{ex.Message}");
                    continue;
                }

                watcher.Changed += OnFileEvent;
                watcher.Created += OnFileEvent;
                watcher.Renamed += OnFileEvent;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
#else
            // 空壳：见文件头判断记录"编译条件"。
            _ = registry;
            _ = bus;
            _ = watchRoots;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>FileSystemWatcher 回调，线程池线程上触发——只登记"哪张表、什么时候"，不在这里
        /// 直接触碰 <see cref="DataRegistry"/>（见文件头判断记录）。</summary>
        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            var tableName = Path.GetFileNameWithoutExtension(e.Name);
            if (string.IsNullOrEmpty(tableName))
            {
                return;
            }

            lock (_lock)
            {
                _pendingChanges[tableName] = DateTime.UtcNow;
            }
        }

        private void Update() => ProcessPendingChanges();

        /// <summary>手动触发一次"检查去抖到期→Reload"的处理，供测试与编辑器/无头宿主在不依赖
        /// Unity 帧回调（<c>Update()</c>）的场合手动驱动一轮去抖处理——例如
        /// <c>Tests/Editor/DataHotReloadEditModeTests.cs</c> 所在的 EditMode 测试环境（未进入 Play
        /// Mode）下 Unity 不会自动调用 MonoBehaviour 的 <c>Update()</c>，需要直接驱动本方法。生产
        /// 路径下 <see cref="Update"/> 每帧调用的正是同一个方法，两条路径共享同一份实现、不重复
        /// 状态。
        /// <para>判断记录（为何公开而非 <c>InternalsVisibleTo</c>）：模板改名后程序集名会变化
        /// （见 <c>games/_template/README.md</c>"复制为新游戏：改哪几处"），届时
        /// <c>[assembly: InternalsVisibleTo("Game.Template.EditorTests")]</c> 这类硬编码旧程序集名的
        /// 声明会失效；与 <see cref="TemplateSmokeRunner.IsFinished"/> 公开而非 internal 的既有判断
        /// 一致，改为公开方法即可在改名后继续被测试/编辑器/无头宿主调用，不依赖需要同步维护的
        /// 程序集名白名单。</para></summary>
        public void ProcessPendingChanges()
        {
            if (_registry == null)
            {
                return;
            }

            List<string>? ready = null;
            lock (_lock)
            {
                if (_pendingChanges.Count == 0)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                foreach (var kv in _pendingChanges)
                {
                    if (now - kv.Value >= DebounceWindow)
                    {
                        ready ??= new List<string>();
                        ready.Add(kv.Key);
                    }
                }

                if (ready != null)
                {
                    for (var i = 0; i < ready.Count; i++)
                    {
                        _pendingChanges.Remove(ready[i]);
                    }
                }
            }

            if (ready == null)
            {
                return;
            }

            for (var i = 0; i < ready.Count; i++)
            {
                ReloadTable(ready[i]);
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
                Debug.LogError($"[DataHotReload] 重载表 \"{table}\" 时抛出异常，已忽略本次变更：{ex}");
                return;
            }

            if (report.IsBlocking)
            {
                var issueText = string.Join("; ", System.Linq.Enumerable.Select(report.Issues, i => i.ToString()));
                Debug.LogError($"[DataHotReload] 热重载 \"{table}\" 失败（{report.ErrorCount} 个错误、{report.WarningCount} 个警告），已保留错误列表；" +
                    "注意：DataRegistry 的只读查询是全局阻断的，整个数据集（不止这一张表）在下一次成功的重载/加载之前都无法读取，见类型头判断记录\"Reload 失败语义\"。问题清单：" + issueText);
                _bus!.PublishImmediate(new DataValidationFailedEvent(report.ErrorCount, report.WarningCount));
                return;
            }

            var tables = _registry!.Tables;
            var recordCount = 0;
            for (var i = 0; i < tables.Count; i++)
            {
                recordCount += _registry.GetAll(tables[i]).Count;
            }

            _bus!.PublishImmediate(new DataLoadCompletedEvent(tables.Count, recordCount, report.ErrorCount, report.WarningCount));

            Debug.Log($"[DataHotReload] 热重载 \"{table}\" 成功（{_registry.GetAll(table).Count} 条记录）");
        }

        private void OnDestroy()
        {
            for (var i = 0; i < _watchers.Count; i++)
            {
                _watchers[i].EnableRaisingEvents = false;
                _watchers[i].Changed -= OnFileEvent;
                _watchers[i].Created -= OnFileEvent;
                _watchers[i].Renamed -= OnFileEvent;
                _watchers[i].Dispose();
            }
            _watchers.Clear();
        }
#endif
    }
}
