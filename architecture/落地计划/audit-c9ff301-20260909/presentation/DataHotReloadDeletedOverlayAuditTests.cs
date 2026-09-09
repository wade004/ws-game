#nullable enable
// 外部审计副本专用探针：两根数据源存在同表覆盖时，删除 game override 后，
// DataRegistry.Reload 的正确 oracle 是回到 framework 行；当前 DataHotReload
// 只订阅 Changed/Created/Renamed，因此文件删除不会触发该 Reload。
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
    public sealed class DataHotReloadDeletedOverlayAuditTests
    {
        private static void WriteStatTable(string root, double defaultBase)
        {
            var dir = Path.Combine(root, "stat");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "stat.definition.json"),
                "{\"table\":\"stat.definition\",\"schema_version\":1,\"rows\":[" +
                "{\"id\":\"stat.shared\",\"name_key\":\"l10n.stat.shared\",\"group\":\"primary\",\"default_base\":" +
                defaultBase.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"override\":true}]}" );
        }

        [Test]
        public void DeletedGameOverride_IsNotObservedByCurrentWatcher_ButManualReloadFallsBack()
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

                go = new GameObject("TestDataHotReloadDeletedOverlay");
                var hotReload = go.AddComponent<DataHotReload>();
                hotReload.Initialize(registry, bus, new[] { gameRoot });
                File.Delete(Path.Combine(gameRoot, "stat", "stat.definition.json"));

                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                    hotReload.ProcessPendingChanges();
                }

                var afterDelete = registry.Get("stat.definition", "stat.shared")!.GetNumber("default_base");
                Debug.Log($"[DataHotReloadDeletedOverlayAudit] before=2;after_delete={afterDelete}");
                Assert.AreEqual(2, afterDelete,
                    "当前 watcher 未订阅 Deleted，删除 game override 后旧内存覆盖会继续保留");

                var fallbackReport = registry.Reload("stat.definition");
                Assert.IsFalse(fallbackReport.IsBlocking, string.Join("; ", fallbackReport.Issues));
                var manualFallback = registry.Get("stat.definition", "stat.shared")!.GetNumber("default_base");
                Debug.Log($"[DataHotReloadDeletedOverlayAudit] manual_reload_fallback={manualFallback}");
                Assert.AreEqual(1, manualFallback,
                    "独立 oracle：显式 Reload 后应回落到仍存在的 framework 行");
            }
            finally
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }
}
