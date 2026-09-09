#nullable enable
// DataHotReloadEditModeTests：F3 新增，验证 Runtime/DataHotReload.cs 的"文件变更 -> 去抖 -> Reload"
// 全链路。刻意不依赖 Play Mode/场景（用 [Test] 而不是 [UnityTest]，见 NUnit 判断记录）——构造一个
// 独立的临时磁盘数据根 + 独立 DataRegistry + 独立 DataHotReload 实例，不经过 GameBootstrap/Shell
// 场景，纯粹验证本组件自身的行为，可在 EditMode 下跑（不需要进入 Play Mode）。
//
// 判断记录（为什么调用公开方法 ProcessPendingChanges 而不是等 Unity 调度 Update()）：EditMode 测试
// 环境（`-testPlatform EditMode`，未进入 Play Mode）下 Unity 不会自动调用 MonoBehaviour.Update()，
// 见 Runtime/DataHotReload.cs 该方法的判断记录；本测试直接驱动同一个公开方法，与 Update() 调用的是
// 完全相同的实现，行为等价。该方法公开而非 internal + InternalsVisibleTo，是因为模板改名后程序集名
// 会变化，硬编码旧程序集名的 InternalsVisibleTo 会失效（同判断记录）。
using System;
using System.IO;
using System.Threading;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using NUnit.Framework;
using UnityEngine;

namespace Game.Template.EditorTests
{
    public sealed class DataHotReloadEditModeTests
    {
        private static string CreateTempDataRoot()
        {
            var dir = Path.Combine(Path.GetTempPath(), "gf_hotreload_editmode_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteStatTable(string root, params (string id, string nameKey)[] rows)
        {
            var rowParts = new string[rows.Length];
            for (var i = 0; i < rows.Length; i++)
            {
                rowParts[i] = "{\"id\":\"" + rows[i].id + "\",\"name_key\":\"" + rows[i].nameKey +
                    "\",\"group\":\"primary\",\"default_base\":0}";
            }
            var json = "{\"table\":\"stat.definition\",\"schema_version\":1,\"rows\":[" + string.Join(",", rowParts) + "]}";
            File.WriteAllText(Path.Combine(root, "stat.definition.json"), json);
        }

        [Test]
        public void FileChange_AfterDebounceWindow_ReloadsTableAndUpdatesRegistry()
        {
            var tempRoot = CreateTempDataRoot();
            GameObject? go = null;
            try
            {
                WriteStatTable(tempRoot, ("stat.a", "l10n.stat.a.name"));

                var catalog = EventCatalog.FromDefinitions(new[]
                {
                    new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                    new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
                });
                var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false });

                // readOnlyContentMode: true + 显式 contentRoot=tempRoot：见 UnityFileSystem 构造函数，
                // ReadText/ListFiles 相对 contentRoot 解析路径；FileSystemDataSource 的 rootDir 传
                // 空字符串，ResolveFullPath("") 直接返回 CurrentRoot（=tempRoot 本身）。
                var fs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: tempRoot);
                var source = new FileSystemDataSource(fs, "");
                var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
                registry.RegisterSchema(StatSchemas.Definition);

                var loadReport = registry.LoadAll();
                Assert.IsFalse(loadReport.IsBlocking, string.Join("; ", loadReport.Issues));
                Assert.AreEqual(1, registry.GetAll("stat.definition").Count, "初次加载应读到 1 条 stat.definition 记录");

                go = new GameObject("TestDataHotReload");
                var hotReload = go.AddComponent<DataHotReload>();
                hotReload.Initialize(registry, bus, new[] { tempRoot });

                // 修改文件：新增一行（覆盖写整份表文件，惯例同真实编辑器/文本工具保存数据表）。
                WriteStatTable(tempRoot, ("stat.a", "l10n.stat.a.name"), ("stat.b", "l10n.stat.b.name"));

                // 真实 FileSystemWatcher 事件是异步投递的（不保证立即到达），加上 300ms 去抖窗口——
                // 轮询驱动 ProcessPendingChanges，给足够的时间但不无限期等待（5s 保底）。
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline && registry.GetAll("stat.definition").Count != 2)
                {
                    Thread.Sleep(50);
                    hotReload.ProcessPendingChanges();
                }

                Assert.AreEqual(2, registry.GetAll("stat.definition").Count,
                    "热重载应在去抖窗口过后，把改动后的表文件重新读进内存（新增的 stat.b 行应出现）");
            }
            finally
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
                try { Directory.Delete(tempRoot, recursive: true); } catch { /* 尽力而为的清理 */ }
            }
        }
    }
}
