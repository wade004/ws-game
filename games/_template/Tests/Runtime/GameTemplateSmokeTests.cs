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
    /// <summary>装配级清理（PRES-118-CAMERA 根治，见 PRES118_TemplateContractTests.cs 判断记录）：
    /// 本类型此前的实现只删除真实 <c>Application.persistentDataPath/saves</c> 目录下
    /// "game.template." 前缀的顶层文件——这与 <see cref="Adapter.Unity.Tests.Runtime.
    /// GlobalPlayModeTestSetup"/> 判断记录 2 已经定位并根治过的缺陷是同一类问题（"只清一个前缀
    /// 不够"）：同一台开发机上，<c>toolchain/consumer_smoke.ps1</c>/<c>check.ps1</c> 的
    /// <c>-gf-smoke</c>/<c>-gf-smoke-discrete</c> 步骤反复构建并运行独立版 Player，这些 Player
    /// 进程不经过任何 PlayMode 测试装配级 setup，直接对真实 <c>Application.persistentDataPath/
    /// saves</c>（本工程 companyName/productName 为默认值"DefaultCompany/unity"，是这台机器上
    /// 很多未改过默认名的 Unity 工程共用的同一个物理目录）写入 "game.sample.*"/裸 "slot.*"
    /// 前缀的存档槽，跨越"第十八轮审核"这样的多轮本地重跑不断累积、从未被清理；一旦这些残留
    /// 加上本次运行内新建的槽把 <see cref="Core.Foundation.SaveSystem.SaveSystemOptions.MaxSlots"/>
    /// （默认 20，且 <see cref="Core.Foundation.SaveSystem.SaveSystem.Save"/> 对"目标槽不存在"的
    /// 计数不分前缀，见该方法源码）顶满，<see cref="PRES118_TemplateContractTests.
    /// GameBootstrap_NewGame_CameraFollowsPlayer_AfterInWorld"/> 这类首次调用
    /// <c>RequestNewGame</c> 新建槽的用例就会稳定命中 <c>SaveFailureReason.SlotLimitReached</c>
    /// 而返回 false——真实复现：本地一次全新 check.ps1 全量门禁跑到这一步时，
    /// <c>%LOCALAPPDATA%Low\DefaultCompany\unity\saves</c> 下恰好已有 20 个非本模板前缀的历史
    /// 遗留文件，与本用例代码逻辑、与 PRES-118-CAMERA/SFX/VIEW 三项改动均无关，是模板测试套件
    /// 自身的装配级隔离缺口，因此在本轮一并根治，采用与 <c>GlobalPlayModeTestSetup</c> 完全相同的
    /// 手法：不再对真实目录做前缀删除，改为把
    /// <see cref="Adapter.Unity.EngineAdapter.UnityFileSystem.UserDataRootOverride"/> 重定向到
    /// 本套件专属的子目录（与 <c>GlobalPlayModeTestSetup</c> 用的 "_playmode_tests" 同级但另起
    /// 一个名字，避免两套装配级 setup 在同一次 -runTests 调用里各自打印的诊断日志/目录名混淆——
    /// 二者按 NUnit 装配顺序先后运行、不会同时持有覆盖，互不冲突），运行结束后整体删除并把覆盖
    /// 复位为 null；真实玩家存档目录自此对本模板测试套件也完全不可见。回归测试见
    /// GlobalTemplateTestSetupTests（同目录）。</summary>
    [SetUpFixture]
    public sealed class GlobalTemplateTestSetup
    {
        /// <summary>专属测试用户数据根，相对真实 <c>Application.persistentDataPath</c> 的子目录，
        /// 与 <see cref="Adapter.Unity.Tests.Runtime.GlobalPlayModeTestSetup.TestUserDataRoot"/>
        /// 用途相同但目录名不同（见本类型判断记录）。internal 可见性供
        /// GlobalTemplateTestSetupTests 回归测试读取。</summary>
        internal static string TestUserDataRoot => Path.Combine(Application.persistentDataPath, "_playmode_tests_template");

        [OneTimeSetUp]
        public void ClearTemplateSaveSlotsBeforeAnyTestRuns()
        {
            var testRoot = TestUserDataRoot;
            Adapter.Unity.EngineAdapter.UnityFileSystem.UserDataRootOverride = testRoot;

            var savesDir = Path.Combine(testRoot, "saves");
            var removed = ClearAllSaveArtifacts(savesDir);
            Debug.Log($"[GlobalTemplateTestSetup] 已把用户数据根重定向到测试专属目录：{testRoot}" +
                      $"（预清理存档子目录={savesDir}，删除文件数={removed}），真实存档目录未被触碰。");
        }

        /// <summary>递归清空 <paramref name="savesDir"/> 下的全部文件。判断记录（为什么本类型不直接
        /// 调用 <see cref="Adapter.Unity.Tests.Runtime.GlobalPlayModeTestSetup.ClearAllSaveArtifacts"/>
        /// 复用同一份实现，而是就地复制一份等价逻辑）：该方法是 internal，且其所在 asmdef
        /// "Adapter.Unity.Tests.Runtime" 本身不在 <c>Game.Template.Tests.asmdef</c> 的 references
        /// 数组里（本模板测试套件只引用生产装配 "Adapter.Unity"，不应该为了复用几行文件系统清理
        /// 代码而额外引入对方测试程序集这一跨装配耦合）；逻辑与命名均与该方法保持一致，方便对照，
        /// 回归测试见 GlobalTemplateTestSetupTests（同目录）。</summary>
        internal static int ClearAllSaveArtifacts(string savesDir)
        {
            if (!Directory.Exists(savesDir))
            {
                return 0;
            }

            var removed = 0;
            foreach (var file in Directory.GetFiles(savesDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch
                {
                    // 尽力而为，同 GlobalPlayModeTestSetup 同款判断记录。
                }
            }

            return removed;
        }

        [OneTimeTearDown]
        public void RestoreUserDataRootAfterAllTestsRun()
        {
            var testRoot = TestUserDataRoot;
            Adapter.Unity.EngineAdapter.UnityFileSystem.UserDataRootOverride = null;

            if (Directory.Exists(testRoot))
            {
                try
                {
                    Directory.Delete(testRoot, recursive: true);
                }
                catch
                {
                    // 尽力而为，同 GlobalPlayModeTestSetup 同款判断记录：清理失败不阻断测试运行结束。
                }
            }

            Debug.Log($"[GlobalTemplateTestSetup] 已恢复用户数据根覆盖为 null，已清理测试专属目录：{testRoot}");
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

        /// <summary>GP-PRES-08 收口回归（architecture/落地计划/audit-20260907/gameplay-presentation.md）：
        /// 主菜单标题与"新游戏"按钮文案此前硬编码中文字符串，绕开了 09 第 7.3 节"面向玩家文案一律
        /// 经本地化表"的铁律。本用例断言两处渲染文本都与经 L10n.Text 查到的对应 key 一致——标题用
        /// l10n.shell.template_main_menu_title（新增），按钮沿用 shell_menu_definition 已登记的
        /// l10n.shell.template_new_game，并断言两个 key 在数据表里确有真实文案（HasText），不是
        /// 靠 MissingKeyPolicy 的占位兜底侥幸凑巧相等。</summary>
        [UnityTest]
        public IEnumerator TemplateShellUi_MainMenuTexts_ComeFromL10n_NotHardcoded()
        {
            yield return LoadShellScene();

            var shellUi = Object.FindFirstObjectByType<TemplateShellUi>();
            Assert.IsNotNull(shellUi, "Shell 场景应挂有 TemplateShellUi");
            var l10n = shellUi!.Bootstrap.Presentation.L10n;

            var titleKey = new Core.Foundation.Common.Id("l10n.shell.template_main_menu_title");
            var newGameKey = new Core.Foundation.Common.Id("l10n.shell.template_new_game");
            Assert.IsTrue(l10n.HasText(titleKey), "l10n.shell.template_main_menu_title 应当在 data/game/l10n/l10n.text.json 里有真实登记，不是靠占位兜底");
            Assert.IsTrue(l10n.HasText(newGameKey), "l10n.shell.template_new_game 应当在 data/game/l10n/l10n.text.json 里有真实登记");

            var titleLabel = Object.FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None)
                .FirstOrDefault(t => t.name == "Title");
            Assert.IsNotNull(titleLabel, "主菜单应有一个名为 Title 的 TextMeshProUGUI");
            Assert.AreEqual(l10n.Text(titleKey), titleLabel!.text, "标题文案应当来自 L10n.Text(l10n.shell.template_main_menu_title)，不是硬编码字符串");

            // UiWidgets.CreateButton 把按钮文案落在名为 "Label" 的子物体上（见该方法源码），按钮
            // 根物体名为 "Entry_{index}"（TemplateShellUi.BuildMainMenu，本模板只登记了一条
            // new_game 入口，因此固定是 "Entry_0"）。
            var newGameButtonLabel = Object.FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None)
                .FirstOrDefault(t => t.name == "Label" && t.transform.parent != null && t.transform.parent.name.StartsWith("Entry_"));
            Assert.IsNotNull(newGameButtonLabel, "\"新游戏\"入口按钮应有一个名为 Label 的子 TextMeshProUGUI 承载文案");
            Assert.AreEqual(l10n.Text(newGameKey), newGameButtonLabel!.text, "按钮文案应当来自 L10n.Text(l10n.shell.template_new_game)，不是硬编码字符串");
        }

        /// <summary>验证 <c>-gf-smoke-template</c> 冒烟序列本身在编辑器内可跑通（见
        /// <see cref="TemplateSmokeRunner"/> 类型头注释）：不经由命令行标志触发
        /// （<c>-runTests</c> 跑 PlayMode 测试的命令行不会带这个标志，见该类型
        /// <c>TryStart</c> 静态钩子的判定条件），改为测试代码自己
        /// <c>AddComponent&lt;TemplateSmokeRunner&gt;()</c>，惯例同该类型 <c>IsFinished</c>/
        /// <c>Succeeded</c> 属性注释——两个属性正是为本用例公开的。
        /// <para>
        /// 判断记录（为什么在编辑器 PlayMode 测试里放心跑到 <c>Application.Quit</c> 那一步，
        /// 不会真的中断测试进程/退出 Play 模式）：Unity 官方行为——<c>Application.Quit</c> 在
        /// 编辑器内（无论是手动 Play 还是 <c>-runTests</c> 批处理 PlayMode）是纯粹的空操作，
        /// 既不会退出 Play 模式也不会关闭编辑器进程，只在真正的独立版构建产物里才生效；
        /// 这也是为什么 <c>check.ps1</c>/<c>consumer_smoke.ps1</c> 的 <c>-gf-smoke</c>/
        /// <c>-gf-smoke-template</c> 冒烟步骤都必须先构建独立版再运行，不能直接在 PlayMode
        /// 测试里验证"退出码"本身——本用例只验证"序列本身能跑通并打印 RESULT=OK"，不验证
        /// 退出码语义（那是 <c>toolchain/consumer_smoke.ps1</c> 第 10 步的职责，见该脚本）。
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TemplateSmokeRunner_InEditor_CompletesSequenceSuccessfully()
        {
            yield return LoadShellScene();

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "场景里应当有且仅有一个 GameBootstrap 实例");
            Assert.IsFalse(bootstrap!.BootstrapFailed, "默认 GameOptions 下 GameBootstrap 不应装配失败");

            var runnerGo = new GameObject("TestTemplateSmokeRunner");
            var runner = runnerGo.AddComponent<TemplateSmokeRunner>();

            // 冒烟序列内部两处"等待 InWorld 页面"的轮询上限各 1200 帧、外加"移动 1 秒"，实测在
            // PlayMode 测试环境下远快于看门狗的 60 秒实时上限；这里给一个比看门狗更宽松的保底
            // （65 秒实时），避免真正卡死时把本用例挂到 Unity 测试框架自己的默认超时上，报出的
            // 失败原因更明确（下面的 Assert.Fail 消息），而不是一个含糊的框架级超时。
            var guardDeadline = Time.realtimeSinceStartup + 65f;
            while (!runner.IsFinished && Time.realtimeSinceStartup < guardDeadline)
            {
                yield return null;
            }

            Assert.IsTrue(runner.IsFinished, "TemplateSmokeRunner 冒烟序列应在保底时限内跑完（成功或失败）");
            Assert.IsTrue(runner.Succeeded, "TemplateSmokeRunner 冒烟序列应成功跑完（RESULT=OK），失败原因见上方 Console 日志");

            Object.Destroy(runnerGo);
        }
    }
}
