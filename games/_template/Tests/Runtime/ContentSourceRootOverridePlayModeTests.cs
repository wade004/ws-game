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

        private GameBootstrap BuildIsolatedBootstrap()
        {
            CleanupStaleGameBootstrap();

            var go = new GameObject("ContentSourceRootOverrideTest_GameBootstrap");
            go.SetActive(false); // 推迟 Awake：环境变量必须先设置好，见调用方。
            var bootstrap = go.AddComponent<GameBootstrap>();
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
                try { Directory.Delete(overrideRoot, recursive: true); } catch { /* 尽力而为的清理 */ }
            }

            yield return null;
        }
    }
}
