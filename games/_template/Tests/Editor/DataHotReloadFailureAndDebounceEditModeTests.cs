#nullable enable
// DataHotReloadFailureAndDebounceEditModeTests：测试覆盖第四批 T-M48——DataHotReload 此前只有“正常改文件→重载”
// 与“删除 override 回落”两条用例，缺：
//  ① 重载遇到坏数据（非法 JSON / 主键重复 / 必填字段缺失）：不抛异常、整个 registry 进入阻断（类型头判断记录
//     "Reload 失败语义"）、经事件总线补发 DataValidationFailedEvent（不发 LoadCompleted）；
//  ② 失败后“回退”：把文件改回合法内容，下一次重载成功、registry 恢复可读并反映修复后的内容、补发 LoadCompleted；
//  ③ 新增文件：监视根下此前不存在的 *.json 出现，按同一条登记路径触发重载，覆盖生效；
//  ④ 防抖窗口内的多次变更合并成一次重载，且读到的是最终内容（而不是每次写入各重载一次）。
// 同既有两条 EditMode 用例：不依赖 Play Mode/场景，用 [Test] + 手动驱动公开方法 ProcessPendingChanges，
// 真实 FileSystemWatcher + 轮询兜底参与（5 秒保底超时）。
using System;
using System.Collections.Generic;
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
    public sealed class DataHotReloadFailureAndDebounceEditModeTests
    {
        private sealed class Harness : IDisposable
        {
            public readonly string Root = Path.Combine(Path.GetTempPath(), "gf_hotreload_fail_" + Guid.NewGuid().ToString("N"));
            public readonly string TableFile;
            public DataRegistry Registry = null!;
            public EventBus Bus = null!;
            public DataHotReload HotReload = null!;
            public readonly List<DataLoadCompletedEvent> Completed = new List<DataLoadCompletedEvent>();
            public readonly List<DataValidationFailedEvent> Failed = new List<DataValidationFailedEvent>();
            private GameObject? _go;

            public Harness()
            {
                Directory.CreateDirectory(Root);
                TableFile = Path.Combine(Root, "stat.definition.json");
            }

            public static string TableJson(params string[] ids)
            {
                var rows = new string[ids.Length];
                for (var i = 0; i < ids.Length; i++)
                {
                    rows[i] = "{\"id\":\"" + ids[i] + "\",\"name_key\":\"l10n." + ids[i] + ".name\",\"group\":\"primary\",\"default_base\":0}";
                }

                return "{\"table\":\"stat.definition\",\"schema_version\":1,\"rows\":[" + string.Join(",", rows) + "]}";
            }

            public void Start(string initialJson)
            {
                File.WriteAllText(TableFile, initialJson);

                var catalog = EventCatalog.FromDefinitions(new[]
                {
                    new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                    new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
                });
                Bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
                Bus.Subscribe<DataLoadCompletedEvent>(DataRegistryEventKeys.LoadCompleted, e => Completed.Add(e));
                Bus.Subscribe<DataValidationFailedEvent>(DataRegistryEventKeys.ValidationFailed, e => Failed.Add(e));

                var fs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: Root);
                Registry = new DataRegistry(new FileSystemDataSource(fs, ""), Bus, new DataRegistryOptions { FailOnUnknownTable = false });
                Registry.RegisterSchema(StatSchemas.Definition);
                var report = Registry.LoadAll();
                Assert.IsFalse(report.IsBlocking, string.Join("; ", report.Issues));
                // 首次 LoadAll 自己会发 LoadCompleted，不算热重载；清掉以便后面只数热重载补发的事件。
                Completed.Clear();
                Failed.Clear();

                _go = new GameObject("TestDataHotReloadFailure");
                HotReload = _go.AddComponent<DataHotReload>();
                HotReload.Initialize(Registry, Bus, new[] { Root });
            }

            /// <summary>反复驱动 ProcessPendingChanges 直到 <paramref name="done"/> 为真或超时（5 秒）。</summary>
            public bool PumpUntil(Func<bool> done)
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    HotReload.ProcessPendingChanges();
                    if (done())
                    {
                        return true;
                    }

                    Thread.Sleep(25);
                }

                HotReload.ProcessPendingChanges();
                return done();
            }

            public int RecordCountOrMinusOne()
            {
                try
                {
                    return Registry.GetAll("stat.definition").Count;
                }
                catch (InvalidOperationException)
                {
                    return -1;   // 阻断态：禁止读取
                }
            }

            public void Dispose()
            {
                if (_go != null)
                {
                    UnityEngine.Object.DestroyImmediate(_go);
                }

                try { Directory.Delete(Root, recursive: true); } catch { /* 尽力而为的清理 */ }
            }
        }

        // 每种坏数据 -> 构造它的文件内容。规则上都是“热重载把一张表改坏”，行为应一致。
        private static readonly (string Name, string Content)[] BrokenVariants =
        {
            ("非法JSON", "{ this is not json"),
            ("主键重复", Harness.TableJson("stat.dup", "stat.dup")),
            ("必填字段缺失", "{\"table\":\"stat.definition\",\"schema_version\":1,\"rows\":[{\"id\":\"stat.nofield\",\"group\":\"primary\",\"default_base\":0}]}"),
        };

        [Test]
        public void BrokenTable_Reload_DoesNotThrow_BlocksRegistry_PublishesValidationFailedOnly()
        {
            foreach (var (name, content) in BrokenVariants)
            {
                using var h = new Harness();
                h.Start(Harness.TableJson("stat.a"));
                Assert.AreEqual(1, h.RecordCountOrMinusOne(), name + "：前置");

                File.WriteAllText(h.TableFile, content);

                Assert.IsTrue(h.PumpUntil(() => h.Failed.Count >= 1), name + "：去抖窗口过后应补发 DataValidationFailedEvent");
                Assert.Greater(h.Failed[0].ErrorCount, 0, name);
                Assert.IsNotNull(h.Failed[0].Issues, name);
                Assert.Greater(h.Failed[0].Issues.Count, 0, name + "：失败事件应携带问题清单");
                Assert.AreEqual(0, h.Completed.Count, name + "：失败的重载不得发 LoadCompleted");
                Assert.AreEqual(-1, h.RecordCountOrMinusOne(), name + "：校验失败后 registry 全局阻断，禁止读取（类型头判断记录“Reload 失败语义”）");
            }
        }

        [Test]
        public void AfterBrokenReload_RestoringValidContent_RecoversRegistryAndReflectsRepairedRows()
        {
            foreach (var (name, content) in BrokenVariants)
            {
                using var h = new Harness();
                h.Start(Harness.TableJson("stat.a"));

                File.WriteAllText(h.TableFile, content);
                Assert.IsTrue(h.PumpUntil(() => h.Failed.Count >= 1), name + "：先确认进入阻断");
                Assert.AreEqual(-1, h.RecordCountOrMinusOne(), name);

                var repaired = Harness.TableJson("stat.a", "stat.b", "stat.c");
                File.WriteAllText(h.TableFile, repaired);

                Assert.IsTrue(h.PumpUntil(() => h.RecordCountOrMinusOne() == 3),
                    name + "：改回合法内容后下一次重载应成功，registry 恢复可读并反映修复后的 3 行");
                Assert.GreaterOrEqual(h.Completed.Count, 1, name + "：成功的重载应补发 LoadCompleted");
                Assert.AreEqual(0, h.Completed[h.Completed.Count - 1].ErrorCount, name);
                Assert.IsNotNull(h.Registry.Get("stat.definition", "stat.c"), name);
            }
        }

        [Test]
        public void NewFileAppearingInWatchedRoot_TriggersReload_AndOverridesFrameworkRow()
        {
            var root = Path.Combine(Path.GetTempPath(), "gf_hotreload_newfile_" + Guid.NewGuid().ToString("N"));
            var frameworkRoot = Path.Combine(root, "framework");
            var gameRoot = Path.Combine(root, "game");
            Directory.CreateDirectory(frameworkRoot);
            Directory.CreateDirectory(gameRoot);
            GameObject? go = null;
            try
            {
                const double frameworkValue = 1;
                const double overrideValue = 2;
                File.WriteAllText(Path.Combine(frameworkRoot, "stat.definition.json"), TableWithBase(frameworkValue, withOverrideFlag: false));

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
                Assert.AreEqual(frameworkValue, registry.Get("stat.definition", "stat.shared")!.GetNumber("default_base"), "前置：游戏根为空，只有 framework 行");

                go = new GameObject("TestDataHotReloadNewFile");
                var hotReload = go.AddComponent<DataHotReload>();
                hotReload.Initialize(registry, bus, new[] { gameRoot });

                // 监视根下此前不存在的文件：新增。
                File.WriteAllText(Path.Combine(gameRoot, "stat.definition.json"), TableWithBase(overrideValue, withOverrideFlag: true));

                var deadline = DateTime.UtcNow.AddSeconds(5);
                double observed = frameworkValue;
                while (DateTime.UtcNow < deadline)
                {
                    hotReload.ProcessPendingChanges();
                    observed = registry.Get("stat.definition", "stat.shared")!.GetNumber("default_base");
                    if (observed == overrideValue)
                    {
                        break;
                    }

                    Thread.Sleep(25);
                }

                Assert.AreEqual(overrideValue, observed, "新增的 game override 文件应在去抖窗口过后自动触发重载并覆盖 framework 行");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                try { Directory.Delete(root, recursive: true); } catch { /* 尽力而为的清理 */ }
            }
        }

        private static string TableWithBase(double defaultBase, bool withOverrideFlag) =>
            "{\"table\":\"stat.definition\",\"schema_version\":1,\"rows\":[" +
            "{\"id\":\"stat.shared\",\"name_key\":\"l10n.stat.shared\",\"group\":\"primary\",\"default_base\":" +
            defaultBase.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            (withOverrideFlag ? ",\"override\":true" : string.Empty) + "}]}";

        [Test]
        public void RapidEditsInsideDebounceWindow_CoalesceIntoOneReload_WithTheFinalContent()
        {
            using var h = new Harness();
            h.Start(Harness.TableJson("stat.a"));

            // 三次连续写入（每次行数不同，保证长度/mtime 变化都能被轮询兜底看到），间隔远小于 300ms 去抖窗口。
            File.WriteAllText(h.TableFile, Harness.TableJson("stat.a", "stat.b"));
            h.HotReload.ProcessPendingChanges();   // 首次调用会立刻做一轮轮询，登记“待重载”但窗口未到
            Assert.AreEqual(1, h.RecordCountOrMinusOne(), "去抖窗口内不应立即重载");
            Assert.AreEqual(0, h.Completed.Count, "去抖窗口内不应已有重载完成事件");

            File.WriteAllText(h.TableFile, Harness.TableJson("stat.a", "stat.b", "stat.c"));
            File.WriteAllText(h.TableFile, Harness.TableJson("stat.a", "stat.b", "stat.c", "stat.d"));
            const int finalRows = 4;

            Assert.IsTrue(h.PumpUntil(() => h.RecordCountOrMinusOne() == finalRows), "去抖后应读到最终内容（4 行），而不是中间态");
            Assert.AreEqual(1, h.Completed.Count,
                "三次写入落在同一去抖窗口内，应合并成一次重载（看到 4 行时只发出过一次 LoadCompleted）");
            Assert.AreEqual(0, h.Failed.Count);
        }

        [Test]
        public void EditsSeparatedByMoreThanTheDebounceWindow_ReloadEachTime()
        {
            using var h = new Harness();
            h.Start(Harness.TableJson("stat.a"));

            File.WriteAllText(h.TableFile, Harness.TableJson("stat.a", "stat.b"));
            Assert.IsTrue(h.PumpUntil(() => h.RecordCountOrMinusOne() == 2));
            var afterFirst = h.Completed.Count;
            Assert.GreaterOrEqual(afterFirst, 1);

            File.WriteAllText(h.TableFile, Harness.TableJson("stat.a", "stat.b", "stat.c"));
            Assert.IsTrue(h.PumpUntil(() => h.RecordCountOrMinusOne() == 3), "窗口之外的第二次编辑应单独触发第二次重载");
            Assert.Greater(h.Completed.Count, afterFirst);
        }
    }
}
