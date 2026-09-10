#nullable enable
// DataHotReloadDeletedOverlayTests：P2-09 根治验收（architecture/落地计划/audit-c9ff301-20260909/
// docs-project/project-findings.md"P2-09：启用双根覆盖热重载时删除 override 不回落"、
// presentation/presentation-findings.md"DataHotReload"一节）——两根数据源存在同表覆盖时，删除
// game override 后，正确 oracle 是 watcher 收到 Deleted 事件、自动触发 DataRegistry.Reload，结果
// 回落到仍存在的 framework 行；不需要任何手工补一次 Reload。同 DataHotReloadEditModeTests 同款
// [Test]（不依赖 Play Mode/场景）与 ProcessPendingChanges 手动驱动惯例。
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
    public sealed class DataHotReloadDeletedOverlayTests
    {
        private static void WriteStatTable(string root, double defaultBase)
        {
            var dir = Path.Combine(root, "stat");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "stat.definition.json"),
                "{\"table\":\"stat.definition\",\"schema_version\":1,\"rows\":[" +
                "{\"id\":\"stat.shared\",\"name_key\":\"l10n.stat.shared\",\"group\":\"primary\",\"default_base\":" +
                defaultBase.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"override\":true}]}");
        }

        [Test]
        public void DeletedGameOverride_AutoReloadsAndFallsBackToFrameworkValue()
        {
            var root = Path.Combine(Path.GetTempPath(), "gf_hotreload_deleted_" + Guid.NewGuid().ToString("N"));
            var frameworkRoot = Path.Combine(root, "framework");
            var gameRoot = Path.Combine(root, "game");
            Directory.CreateDirectory(frameworkRoot);
            Directory.CreateDirectory(gameRoot);
            WriteStatTable(frameworkRoot, 1);
            WriteStatTable(gameRoot, 2);
            GameObject? go = null;

            try
            {
                var catalog = EventCatalog.FromDefinitions(new[]
                {
                    new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                    new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
                });
                var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
                var frameworkSource = new FileSystemDataSource(new UnityFileSystem(true, frameworkRoot), "");
                var gameSource = new FileSystemDataSource(new UnityFileSystem(true, gameRoot), "");
                var registry = new DataRegistry(frameworkSource, bus, new DataRegistryOptions { AllowOverride = true });
                registry.RegisterSchema(StatSchemas.Definition);

                var report = registry.LoadAll(new IDataSource[] { frameworkSource, gameSource });
                Assert.IsFalse(report.IsBlocking, string.Join("; ", report.Issues));
                Assert.AreEqual(2, registry.Get("stat.definition", "stat.shared")!.GetNumber("default_base"),
                    "初次多根加载应由 game override=2 覆盖 framework=1");

                // watcher 只监视 gameRoot（同真实生产场景"游戏数据根"一侧，见 DataHotReload.Initialize
                // watchRoots 参数注释——通常 framework/game 各一个根，本用例只关心删除 game override 这一
                // 半，framework 根始终存在不需要监视）。
                go = new GameObject("TestDataHotReloadDeletedOverlay");
                var hotReload = go.AddComponent<DataHotReload>();
                hotReload.Initialize(registry, bus, new[] { gameRoot });
                File.Delete(Path.Combine(gameRoot, "stat", "stat.definition.json"));

                var deadline = DateTime.UtcNow.AddSeconds(5);
                double afterDelete;
                while (true)
                {
                    Thread.Sleep(50);
                    hotReload.ProcessPendingChanges();
                    afterDelete = registry.Get("stat.definition", "stat.shared")!.GetNumber("default_base");
                    if (afterDelete == 1 || DateTime.UtcNow >= deadline)
                    {
                        break;
                    }
                }

                Debug.Log($"[DataHotReloadDeletedOverlayTests] before=2;after_delete={afterDelete}");
                Assert.AreEqual(1, afterDelete,
                    "正确 oracle：watcher 订阅 Deleted 后，删除 game override 应在去抖窗口过后自动回落到" +
                    "仍存在的 framework 行，不需要任何手工补一次 Reload");

                // 独立幂等 oracle：手工再触发一次 Reload（同真实“文件已删除、之后又发生一次编辑器保存”
                // 场景）应保持同一个结果，不因重复处理而产生副作用。
                var fallbackReport = registry.Reload("stat.definition");
                Assert.IsFalse(fallbackReport.IsBlocking, string.Join("; ", fallbackReport.Issues));
                var manualReload = registry.Get("stat.definition", "stat.shared")!.GetNumber("default_base");
                Assert.AreEqual(1, manualReload, "手工 Reload 应保持幂等，仍为 framework 值 1");
            }
            finally
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
                try { Directory.Delete(root, recursive: true); } catch { /* 尽力而为的清理 */ }
            }
        }
    }
}
