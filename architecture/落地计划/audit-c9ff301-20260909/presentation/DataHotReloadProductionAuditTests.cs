#nullable enable
// 外部审计副本专用探针：验证 DataHotReload 不只是独立组件可构造，而是由
// GameBootstrap 成功装配后真实监视 StreamingAssets 的 data/game，并能把文件变更
// 送到同一 DataRegistry。该文件不回写冻结仓既有测试。
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.Template;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Template.Tests
{
    public sealed class DataHotReloadProductionAuditTests
    {
        private const string ShellSceneName = "GameTemplateShell";
        private const string TablePath = "GameFoundation/data/game/stat/stat.definition.json";
        private const string ExistingStatId = "stat.move_speed";

        private static IEnumerator LoadShellScene()
        {
            SceneManager.LoadScene(ShellSceneName);
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameBootstrap_ChangedGameTable_IsReloadedByProductionHotReload()
        {
            yield return LoadShellScene();

            var bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "模板场景应由真实 GameBootstrap 装配世界");
            Assert.IsFalse(bootstrap!.BootstrapFailed, "生产入口装配不应失败");
            Assert.IsTrue(bootstrap.Options.EnableDataHotReload, "该探针需要模板默认开启热重载");
            Assert.IsNotNull(UnityEngine.Object.FindFirstObjectByType<DataHotReload>(),
                "GameBootstrap 成功 LoadAll 后应挂载 DataHotReload 组件");

            var tablePath = Path.Combine(Application.streamingAssetsPath, "GameFoundation/data/game/stat/stat.definition.json");
            Assert.IsTrue(File.Exists(tablePath), "生产入口实际监视的 data/game/stat.definition.json 应存在");
            var original = File.ReadAllText(tablePath);
            var before = bootstrap.Registry.Get("stat.definition", ExistingStatId);
            Assert.IsNotNull(before, "初次 LoadAll 应读到模板 stat.move_speed");
            Assert.AreEqual(5, before!.GetNumber("default_base"), "测试前应保留模板原始 default_base=5");

            try
            {
                var marker = "\"default_base\": 5";
                Assert.IsTrue(original.Contains(marker, StringComparison.Ordinal), "测试表应有 stat.move_speed 的原始 default_base=5");
                File.WriteAllText(tablePath, original.Replace(marker, "\"default_base\": 7", StringComparison.Ordinal));

                var deadline = Time.realtimeSinceStartup + 8f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    var current = bootstrap.Registry.Get("stat.definition", ExistingStatId);
                    if (current != null && current.GetNumber("default_base") == 7)
                    {
                        break;
                    }
                    yield return null;
                }

                var after = bootstrap.Registry.Get("stat.definition", ExistingStatId);
                Assert.IsNotNull(after, "真实 GameBootstrap 热重载后应仍能查询已有记录");
                Assert.AreEqual(7, after!.GetNumber("default_base"),
                    "真实 GameBootstrap 挂载的 DataHotReload 应在去抖后 Reload 改动表并更新 Registry");
            }
            finally
            {
                File.WriteAllText(tablePath, original);
            }

            var restoreDeadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < restoreDeadline)
            {
                var current = bootstrap.Registry.Get("stat.definition", ExistingStatId);
                if (current != null && current.GetNumber("default_base") == 5)
                {
                    break;
                }
                yield return null;
            }
            var restored = bootstrap.Registry.Get("stat.definition", ExistingStatId);
            Assert.IsNotNull(restored, "探针结束前应恢复外部副本 StreamingAssets");
            Assert.AreEqual(5, restored!.GetNumber("default_base"),
                "探针结束前应恢复外部副本 StreamingAssets，避免污染后续测试");
        }
    }
}
