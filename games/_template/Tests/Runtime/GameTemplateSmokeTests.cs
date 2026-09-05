#nullable enable
// GameTemplateSmokeTests：模板最小验收（见 games/_template/README.md"七步立项对照表"第 7 步
// "跑验收"）——GameBootstrap 用默认 GameOptions 启动 → 数据零阻断错误 → 主菜单可见。
// 在工作台（adapters/unity）里跑：games/_template 通过 Packages/manifest.json 的 file: 引用接入
// 工作台 Unity 工程，见该文件顶部说明；场景由 Game.Template.EditorTools.GameSceneBuilder 预先生成
// 为 Assets/Framework/Scenes/GameTemplateShell.unity（已加入 Build Settings）。
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
    /// <summary>装配级清理：存档槽会累积在跨运行持久化的真实目录里（见
    /// Adapter.Unity.Tests.Runtime.GlobalPlayModeTestSetup 同款判断记录），本模板测试用固定的
    /// "game.template." 前缀槽位 id，同样的道理在此清理一次，避免多次本地重跑累积到
    /// SaveSystemOptions.MaxSlots 上限。</summary>
    [SetUpFixture]
    public sealed class GlobalTemplateTestSetup
    {
        private const string TemplateSlotPrefix = "game.template.";

        [OneTimeSetUp]
        public void ClearTemplateSaveSlotsBeforeAnyTestRuns()
        {
            var savesDir = Path.Combine(Application.persistentDataPath, "saves");
            if (!Directory.Exists(savesDir))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(savesDir).Where(f => Path.GetFileName(f).StartsWith(TemplateSlotPrefix)))
            {
                try { File.Delete(file); } catch { /* 尽力而为，见 GlobalPlayModeTestSetup 同款判断记录 */ }
            }
        }
    }

    public sealed class GameTemplateSmokeTests
    {
        private const string ShellSceneName = "GameTemplateShell";

        private static IEnumerator LoadShellScene()
        {
            // 判断记录（装配级隔离）：本套件与工作台既有的 Adapter.Unity.Tests.Runtime 套件共用
            // 同一次 -runTests 调用（同一 Unity 子进程），若排在重负载用例（如涉及 Aura 周期 tick
            // 的战斗竖切测试）之后运行，偶发观察到上一条用例场景卸载后仍有一次迟到的异步日志在
            // 本用例执行窗口内触发、被 Unity 测试框架计入本用例失败（"Unhandled log message"，
            // 与本用例代码路径完全无关——本模板最小闭环不生成任何战斗/光环实体）。先多等两帧
            // 让上一条用例的收尾回调有机会先跑完，降低这种跨用例日志误记的概率（这是缓解，不是
            // 根治——根治需要改动既有测试套件的收尾逻辑，不在本任务写入范围内）。
            yield return null;
            yield return null;
            SceneManager.LoadScene(ShellSceneName);
            // 判断记录：仅等一帧不保证旧场景的 MonoBehaviour 完全卸载/新场景 Awake 已跑完，惯例同
            // Adapter.Unity.Tests.Runtime.VerticalSliceTests.LoadShellScene（多等两次
            // WaitForFixedUpdate）。
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameBootstrap_DefaultGameOptions_LoadsWithZeroBlockingErrors()
        {
            yield return LoadShellScene();

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "场景里应当有且仅有一个 GameBootstrap 实例（经 TemplateShellUi.Awake -> GameBootstrap.Ensure 创建）");
            Assert.IsFalse(bootstrap!.BootstrapFailed, "默认 GameOptions 下 GameBootstrap 不应装配失败");
            Assert.IsNotNull(bootstrap.LoadReport, "装配成功时应留有本次加载的校验报告");
            Assert.AreEqual(0, bootstrap.LoadReport!.ErrorCount, "模板自带最小数据集 + 框架级数据表合并加载应 0 阻断错误：" + string.Join("; ", bootstrap.LoadReport.Issues));
            Assert.IsFalse(bootstrap.LoadReport.IsBlocking);
        }

        [UnityTest]
        public IEnumerator TemplateShellUi_AfterLoad_MainMenuIsVisible()
        {
            yield return LoadShellScene();

            var shellUi = Object.FindFirstObjectByType<TemplateShellUi>();
            Assert.IsNotNull(shellUi, "Shell 场景应挂有 TemplateShellUi（见 Editor/GameSceneBuilder.BuildShellScene）");
            Assert.IsFalse(shellUi!.Bootstrap.BootstrapFailed);

            // 主菜单可见：ShellHost 在 Bootstrap/TemplateShellUi.Awake 里已 Start()+ReturnToMainMenu()
            // 一次，默认口味配置（未点击任何按钮）下应停在 MainMenu 页。
            Assert.AreEqual(Presentation.Shell.ShellPage.MainMenu, shellUi.Bootstrap.Presentation.Shell.Page,
                "默认启动后 Shell 应停在主菜单页");
        }
    }
}
