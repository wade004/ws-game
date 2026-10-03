#nullable enable
// ContentSourceRootOverridePlayModeTests：消费方反馈第 70 条根治——验证 GameBootstrap 真正按
// ContentSourceRootOverride（环境变量入口）接线：① 指定不存在的目录时装配显式失败，不静默回退；
// ② 指定存在的目录时，DataHotReload 监视的目录确实改成了覆盖后的内容根，而不是默认部署副本根。
//
// 判断记录（不复用共享场景 GameTemplateShell，改用独立 GameObject + SetActive(false) 先注入）：
// 惯例同 adapters/unity 包 SharedBootstrapDiscreteTests.BuildInactiveBootstrapWithDiscreteOverlay
// ——GameBootstrap 是 DontDestroyOnLoad 单例（见该类型 Ensure()/_instance 字段），若复用
// GameTemplateSmokeTests 已加载的共享场景 GameTemplateShell，本类型需要的"进程环境变量覆盖内容根"
// 必须在 GameBootstrap.Awake() 首次运行前生效，而共享场景在整个 PlayMode 批处理运行期间只会
// Bootstrap 一次（Ensure() 幂等），后加载的用例拿到的是已经装配好的旧实例，改环境变量不会重新触发
// Awake。本类型因此在每条用例开头同步销毁任何残留的 GameBootstrap 实例（DestroyImmediate 立即
// 触发 OnDestroy 把静态 _instance 复位为 null），再新建一个未激活的 GameObject 设置好环境变量后
// SetActive(true) 触发真正的 Awake/Bootstrap()，跑完后同样在 TearDown 里销毁，不影响同一次
// -runTests 调用里其它套件（包括 GameTemplateSmokeTests 自己）后续再次触发 Ensure() 时重新走一遍
// 正常装配流程。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Adapter.Unity.EngineAdapter;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Template.Tests
{
    public sealed class ContentSourceRootOverridePlayModeTests
    {
        private GameObject? _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Environment.SetEnvironmentVariable(ContentSourceRootOverride.EnvironmentVariable, null);

            if (_go != null)
            {
                UnityEngine.Object.Destroy(_go);
                _go = null;
            }

            yield return null;
        }

        /// <summary>见文件头判断记录：同步销毁场景里任何残留的 <see cref="GameBootstrap"/> 实例，
        /// 保证接下来新建的实例会真正重新走一遍 Awake/Bootstrap()（读取本用例刚设置的环境变量），
        /// 而不是被 Ensure() 的幂等检查拦下、直接返回旧实例。</summary>
        private static void CleanupStaleGameBootstrap()
        {
            foreach (var stale in UnityEngine.Object.FindObjectsByType<GameBootstrap>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }
        }

        /// <summary>
        /// 先同步销毁本用例的 <see cref="GameBootstrap"/>（连带其上的 <see cref="DataHotReload"/>，其 OnDestroy 会
        /// 关闭并 Dispose 全部 <c>FileSystemWatcher</c>），再删除被监视的临时内容根——次序不能反。
        /// 判断记录（清理次序，2026-10-03 门禁连续三次被偶发红挡住的根因）：DataHotReload 对覆盖后的内容根建了带
        /// 子目录的 FileSystemWatcher；Unity 的 Mono 实现靠后台线程 DefaultWatcher 周期性扫描该目录，若用例在
        /// finally 里先 Directory.Delete、等 TearDown 才销毁 GameObject，后台线程会扫到正在被删除的目录，抛
        /// DirectoryNotFoundException，Unity 把它记成未处理 Error，用例随之失败（约 1/7 概率，与断言无关）。
        /// 做成唯一入口，避免有人把两步写反；不靠重试、不靠 LogAssert.Ignore 掩盖。EditMode 同类用例
        /// （DataHotReloadEditModeTests 等）一直是先 DestroyImmediate 再删目录，本方法让 PlayMode 与之一致。
        /// </summary>
        private void DestroyBootstrapThenDeleteDirectory(string directory)
        {
            if (_go != null)
            {
                UnityEngine.Object.DestroyImmediate(_go);
                _go = null;
            }

            try { Directory.Delete(directory, recursive: true); } catch { /* 尽力而为的清理 */ }
        }

        private GameBootstrap BuildIsolatedBootstrap(Action<GameBootstrap>? configureBeforeAwake = null)
        {
            CleanupStaleGameBootstrap();

            var go = new GameObject("ContentSourceRootOverrideTest_GameBootstrap");
            go.SetActive(false); // 推迟 Awake：环境变量必须先设置好，见调用方。
            var bootstrap = go.AddComponent<GameBootstrap>();
            configureBeforeAwake?.Invoke(bootstrap); // 手感落地 M2-A：需要在 Awake 前改 GameOptions（如打开 FeelOptions）的用例用。
            _go = go;
            go.SetActive(true); // 触发 Awake -> Bootstrap()，此时环境变量已经生效。
            return bootstrap;
        }

        private static string CopyDefaultContentRootToTemp()
        {
            var defaultContentRoot = new UnityFileSystem(readOnlyContentMode: true).GetContentRootDir();
            Assert.IsTrue(Directory.Exists(defaultContentRoot),
                "默认部署副本根应当存在（工作台已跑过 build.ps1 -SyncContent /场景已生成），否则本用例无法验证\"覆盖后仍能正常装配\"");

            var tempRoot = Path.Combine(Path.GetTempPath(), "gf_content_root_override_" + Guid.NewGuid().ToString("N"));
            CopyDirectoryRecursive(defaultContentRoot, tempRoot);
            return tempRoot;
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = filePath.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var destPath = Path.Combine(destDir, relative);
                var destSubDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destSubDir) && !Directory.Exists(destSubDir))
                {
                    Directory.CreateDirectory(destSubDir);
                }
                File.Copy(filePath, destPath, overwrite: true);
            }
        }

        [UnityTest]
        public IEnumerator GameBootstrap_ContentRootOverride_NonexistentDirectory_FailsBootstrapWithClearError()
        {
            var missingDir = Path.Combine(Path.GetTempPath(), "gf_content_root_missing_" + Guid.NewGuid().ToString("N"));
            Assert.IsFalse(Directory.Exists(missingDir), "测试前置条件：该目录不应存在");
            Environment.SetEnvironmentVariable(ContentSourceRootOverride.EnvironmentVariable, missingDir);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("内容源目录不存在", System.Text.RegularExpressions.RegexOptions.Singleline));

            var bootstrap = BuildIsolatedBootstrap();

            Assert.IsTrue(bootstrap.BootstrapFailed,
                "指定不存在的内容源目录应导致装配显式失败，而不是静默回退到默认部署副本根（消费方反馈第 70 条 AGENTS.md 判断记录）");

            yield return null;
        }

        [UnityTest]
        public IEnumerator GameBootstrap_ContentRootOverride_ExistingDirectory_LoadsSuccessfully_AndHotReloadWatchesOverrideRoot()
        {
            var overrideRoot = CopyDefaultContentRootToTemp();
            try
            {
                Environment.SetEnvironmentVariable(ContentSourceRootOverride.EnvironmentVariable, overrideRoot);

                var bootstrap = BuildIsolatedBootstrap();

                Assert.IsFalse(bootstrap.BootstrapFailed,
                    "内容源目录覆盖为一份完整拷贝（框架级 + 游戏数据两个数据根齐全）时，装配不应失败");
                Assert.IsNotNull(bootstrap.LoadReport);
                Assert.IsFalse(bootstrap.LoadReport!.IsBlocking,
                    "覆盖后的内容根应能正常加载数据集，不应产生阻断错误：" + string.Join("; ", bootstrap.LoadReport.Issues));

                Assert.IsTrue(bootstrap.RuntimeOptions.EnableDataHotReload, "默认口味配置下 EnableDataHotReload 应为 true");
                var hotReload = bootstrap.GetComponent<DataHotReload>();
                Assert.IsNotNull(hotReload, "EnableDataHotReload 默认 true，应挂载 DataHotReload 组件");

                var watchRootsField = typeof(DataHotReload).GetField("_watchRoots", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(watchRootsField,
                    "DataHotReload 应有私有字段 _watchRoots（仅 UNITY_EDITOR || DEVELOPMENT_BUILD 下存在，PlayMode 测试在编辑器内跑，该符号应已定义）");
                var watchRoots = (List<string>)watchRootsField!.GetValue(hotReload)!;

                Assert.Greater(watchRoots.Count, 0, "应当至少监视到框架/游戏两个数据根之一");
                foreach (var root in watchRoots)
                {
                    Assert.IsTrue(root.StartsWith(overrideRoot, StringComparison.Ordinal),
                        $"热重载监视根 \"{root}\" 应当以覆盖后的内容源目录 \"{overrideRoot}\" 为前缀，而不是默认部署副本根——" +
                        "这正是消费方反馈第 70 条描述的缺口（热重载只能监视部署副本），本用例验证接线之后已改为监视内容源目录。");
                }
            }
            finally
            {
                DestroyBootstrapThenDeleteDirectory(overrideRoot);
            }

            yield return null;
        }

        /// <summary>
        /// 手感落地 M2-A：开发期热重载对手感数据同样生效——开手感、运行中改框架手感根里 <c>feel.preset</c> 的一个字段，
        /// <see cref="DataHotReload"/> 在 Reload 之后补发 <c>data.load_completed</c>，核心装配据此换入新档案，单位读到新值。
        /// 期望值由编辑本身算出（旧值 + 增量），不写死裸数。
        /// 判断记录：手感档案的热加载订阅在核心装配里（<c>CarriersFeelSystem</c> 订阅 <c>data.load_completed</c> 换入新档案，手感落地 M2-B），
        /// 本用例经由真实的 <c>DataHotReload</c> 与引擎引导走完整链路验证。
        /// </summary>
        [UnityTest]
        public IEnumerator GameBootstrap_FeelOn_HotReloadOfFeelPreset_UnitReadsNewValue_AndLoadCompletedIsPublished()
        {
            var overrideRoot = CopyDefaultContentRootToTemp();
            try
            {
                Environment.SetEnvironmentVariable(ContentSourceRootOverride.EnvironmentVariable, overrideRoot);
                var bootstrap = BuildIsolatedBootstrap(b =>
                    b.Options.FeelOptions = new Core.Carriers.Assembly.CarriersFeelOptions { CalibrationId = "feel.calibration.framework_default" });

                Assert.IsFalse(bootstrap.BootstrapFailed, "开手感的内容根覆盖装配不应失败");
                var feel = bootstrap.Gameplay!.Feel;
                Assert.IsNotNull(feel, "开手感：玩法装配应带手感系统");
                var hotReload = bootstrap.GetComponent<DataHotReload>();
                Assert.IsNotNull(hotReload, "EnableDataHotReload 默认 true，应挂载 DataHotReload 组件");

                // 数据加载完成事件计数（热重载 Reload 之后由 DataHotReload 补发，见该类型判断记录"事件发出"）。
                var loadCompleted = 0;
                var bus = (Core.Foundation.EventBus.IEventBus)typeof(GameBootstrap)
                    .GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bootstrap)!;
                bus.Subscribe(Core.Foundation.DataRegistry.DataRegistryEventKeys.LoadCompleted, _ => loadCompleted++);

                var field = Core.Foundation.Feel.FeelFieldNames.BufferMs;
                var before = feel!.Resolver.ResolveJudging(bootstrap.PlayerId).GetNumber(field);
                var expected = before + 80;

                // 改框架手感根（监视目录）里 arpg_responsive 档（框架缺省标定的基础档，文件里第一行）的 buffer_ms。
                var presetPath = Path.Combine(overrideRoot, GameOptions.FeelDatasetRoot.Replace('/', Path.DirectorySeparatorChar), "feel", "feel.preset.json");
                var original = File.ReadAllText(presetPath);
                // 只改第一处（文件里第一个 buffer_ms 属于 arpg_responsive 档，框架缺省标定的基础档）。
                var edited = new System.Text.RegularExpressions.Regex(@"""buffer_ms""\s*:\s*[0-9.]+").Replace(
                    original, "\"buffer_ms\": " + expected.ToString(System.Globalization.CultureInfo.InvariantCulture), 1);
                Assert.AreNotEqual(original, edited, "测试前置条件：手感预设文件应被改写");
                File.WriteAllText(presetPath, edited);

                var deadline = Time.realtimeSinceStartup + 10f;
                var after = before;
                while (Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    hotReload.ProcessPendingChanges();
                    after = feel.Resolver.ResolveJudging(bootstrap.PlayerId).GetNumber(field);
                    if (after != before) break;
                }

                Debug.Log($"[HotReloadFeel] buffer_ms before={before} expected={expected} after={after} loadCompleted={loadCompleted}");
                Assert.AreEqual(expected, after, 1e-9, "热重载后单位应读到新的缓冲窗口毫秒（不重启）");
                Assert.GreaterOrEqual(loadCompleted, 1, "DataHotReload 应在 Reload 之后发布 data.load_completed（手感热加载依赖它）");
            }
            finally
            {
                DestroyBootstrapThenDeleteDirectory(overrideRoot);
            }
        }
    }
}
