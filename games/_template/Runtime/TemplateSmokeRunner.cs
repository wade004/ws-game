#nullable enable
// TemplateSmokeRunner：games/_template 版本的无人值守冒烟入口（对应 adapters/unity 工作台
// Adapter.Unity.Shell.SmokeRunner 在游戏层的等价物，见 13_新游戏接入指南.md 第 7 节验收关卡、
// toolchain/consumer_smoke.ps1 第 10 步）。命令行参数驱动，跑一遍"数据零阻断 → 主菜单 → 新游戏
// （SampleNewGameStarter）→ 进入地图 → 向右移动 1 秒 → 存档到 slot.smoke → 读档 → 退出"，日志格式
// 与退出码约定完全沿用 Adapter.Unity.Shell.SmokeRunner："[GF-SMOKE] step=<name> ok"、
// "[GF-SMOKE] RESULT=OK" + Application.Quit(0)、失败 "[GF-SMOKE] RESULT=FAIL reason=..." +
// Application.Quit(2)、总耗时超过 60 秒 Application.Quit(3)（见 RunWatchdog）——consumer_smoke.ps1/
// check.ps1 未来若要给游戏层加一份等价冒烟步骤，判定逻辑不需要另起一套。
//
// 判断记录（为什么不是直接复用 Adapter.Unity.Shell.SmokeRunner 类型本身，而是新写一个类型——同
// GameBootstrap.cs 顶部"为什么不直接复用 FrameworkResidentHost/ShellRoot"判断记录一脉相承）：
// SmokeRunner 的步骤序列硬编码依赖 Adapter.Unity.Shell.ShellRoot（工作台自己的 Shell 场景组合根，
// 提供 ShowSlots/ShowNewGameSetup 等存档槽/难度选择页面）与 skill.sample_strike（工作台自带示例
// 数据的技能 id，用于"普攻一次"这一步）。本模板走的是完全不同的组合根 GameBootstrap +
// TemplateShellUi（见二者类型顶部判断记录：TemplateShellUi 是"最小闭环"，不做存档槽/难度选择 UI，
// 点新游戏直接用 GameOptions.DefaultDifficultyId 固定难度开局），且模板最小数据集不含任何
// skill.* 表（见 games/_template/data 目录清单——没有 skill.def，"普攻一次"这一步在模板里根本没有
// 对应的技能可放，任务书给模板定的步骤列表也确实没有这一步）。SmokeRunner 不经改造无法直接对模板
// 生效，本类型按同一套日志/退出码/看门狗约定重新实现一遍"模板专属"的步骤序列。
//
// 判断记录（为什么命令行标志不是 "-gf-smoke" 而是 "-gf-smoke-template"，如实记录这处与任务书
// 原始措辞的偏离，以及为什么必须偏离）：Game.Template.asmdef（本类型所在程序集）的 references
// 数组显式列了 "Adapter.Unity"（见该 asmdef 文件），这意味着任何链接了 Game.Template 的独立版
// 编译产物——包括 toolchain/consumer_smoke.ps1 第 9 步构建的独立版、以及本模板自身被复制为新游戏
// 后构建的任何独立版——都会把 Adapter.Unity.Shell.SmokeRunner 所在的整个 Adapter.Unity 程序集一并
// 打进去。Adapter.Unity.Shell.SmokeRunner.TryStart 是一个静态
// [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] 钩子，只要命令行参数里出现它认的那个 token，
// 不管场景里有没有 Adapter.Unity.Shell.ShellRoot 的实例都会执行——本模板场景（GameTemplateShell/
// GameTemplateMap）从未挂过 ShellRoot，若沿用完全相同的 "-gf-smoke" 字符串，两个互不相干的
// SmokeRunner 会在同一个进程里同时响应同一条命令行标志：Adapter.Unity.Shell.SmokeRunner.
// RunSequence 找不到 ShellRoot，轮询最多 600 帧（约 10 秒）后调用 Fail("shell_root_not_found") +
// Application.Quit(2)；本类型完整走一遍步骤序列（含两次等待 InWorld 页面，各自最多轮询 1200 帧）
// 大概率比这 600 帧更晚才跑完，会被那个与本类型代码逻辑毫不相干的"找不到 ShellRoot"失败提前把
// 整个进程终止掉——而且"谁先调用 Application.Quit"是未定义的帧序竞态，不是稳定复现的失败，是
// 偶发的假失败，更难排查。改用不同的标志字符串 "-gf-smoke-template" 彻底避免这个撞车
// （Environment.GetCommandLineArgs().Contains(...) 是精确 token 比较，不是子串匹配，两个钩子从此
// 各自只对精确匹配自己那个标志字符串的命令行参数生效，互不触发）；
// toolchain/consumer_smoke.ps1 第 10 步、games/_template/README.md 已同步改用这个标志。
using System;
using System.Collections;
using System.Linq;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.SaveSystem;
using Presentation.Shell;
using UnityEngine;

namespace Game.Template
{
    public sealed class TemplateSmokeRunner : MonoBehaviour
    {
        private const string CommandLineFlag = "-gf-smoke-template";
        private const float TotalTimeoutSeconds = 60f;

        // 判断记录（为什么不是 "slot.smoke"——与工作台 Adapter.Unity.Shell.SmokeRunner.SmokeSlotId
        // 字面相同的写法）：本类型与该类型都会被同一个 -runTests PlayMode 测试进程装进同一个
        // persistentDataPath（见 games/_template/Tests/Runtime/GameTemplateSmokeTests.cs
        // "TemplateSmokeRunner_InEditor_CompletesSequenceSuccessfully"用例——本模板测试套件与工作台
        // 自己的测试套件通过 Packages/manifest.json 的 testables 数组共用同一次 -runTests 调用，见
        // adapters/unity/Packages/manifest.json 判断记录）。存档文件路径只按 slotId 区分，不按
        // SaveSystemOptions.GameId 区分（见 core/foundation/save_system 判断记录：GameId 只记录在
        // 存档元数据里，不参与路径拼接），沿用完全相同的 "slot.smoke" 会让两套互不相干的冒烟各自
        // 覆盖对方留下的存档文件，虽不构成功能性 bug（都是幂等覆写），但绕开了本模板既有的
        // "game.template." 前缀清理惯例（见 GlobalTemplateTestSetup.ClearTemplateSaveSlotsBeforeAnyTestRuns
        // 只清理这个前缀），长期反复本地重跑会在 SaveSystemOptions.MaxSlots 上限之外再多占用两个
        // 不会被清理的槽位。改用 "game.template." 前缀（与 TemplateShellUi.DefaultSaveSlotId/
        // TemplateAutoStart.AutoStartSlotId 同一惯例）后完全避免这两个问题。
        private const string SmokeSlotId = "game.template.slot_smoke";
        private const string LogPrefix = "[GF-SMOKE]";

        private bool _finished;

        /// <summary>本次冒烟是否已经跑完（无论成功还是失败）；<see cref="Succeeded"/> 才区分
        /// 结果。公开而非 internal：本模板未声明任何 <c>InternalsVisibleTo</c>，
        /// <c>Tests/Runtime/Game.Template.Tests.asmdef</c> 是独立程序集，
        /// <c>Tests/Runtime/GameTemplateSmokeTests.cs</c> 需要在编辑器内轮询这两个属性验证冒烟
        /// 序列本身可跑通（见该文件"TemplateSmokeRunner_InEditor_CompletesSequenceSuccessfully"
        /// 用例），不经由命令行标志触发（PlayMode 测试跑 Unity 的命令行不会带
        /// <see cref="CommandLineFlag"/>，见 <see cref="TryStart"/>），改为测试代码自己
        /// <c>AddComponent</c>，惯例同本类型 <see cref="TryStart"/> 对正常独立版路径的做法。</summary>
        public bool IsFinished => _finished;

        /// <summary>只在跑完全部步骤、打印 <c>RESULT=OK</c> 之后才为真；<see cref="Fail"/>
        /// 路径下保持 false。</summary>
        public bool Succeeded { get; private set; }

        /// <summary>命令行不带 <see cref="CommandLineFlag"/> 时本类型完全不介入（不新建任何
        /// GameObject/组件），正常游戏/编辑器/既有 PlayMode 测试运行路径不受影响。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void TryStart()
        {
            if (!Environment.GetCommandLineArgs().Contains(CommandLineFlag, StringComparer.Ordinal))
            {
                return;
            }

            var go = new GameObject("TemplateSmokeRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<TemplateSmokeRunner>();
        }

        private void Awake()
        {
            StartCoroutine(RunWatchdog());
            StartCoroutine(RunSequence());
        }

        private IEnumerator RunWatchdog()
        {
            yield return new WaitForSecondsRealtime(TotalTimeoutSeconds);
            Fail("timeout", quitCode: 3);
        }

        private IEnumerator RunSequence()
        {
            // 1) 等 GameBootstrap 出现并完成装配（GameTemplateShell.unity 是 Build Settings 第 0 位
            //    启动场景，见 Editor/GameSceneBuilder.BuildShellScene insertFirst:true；
            //    TemplateShellUi.Awake -> GameBootstrap.Ensure() 在场景加载时同步完成，本类型的
            //    AfterSceneLoad 钩子通常已经晚于那次 Awake，这里仍轮询一段时间保底，惯例同
            //    Adapter.Unity.Shell.SmokeRunner.RunSequence 对 ShellRoot 的等待）。
            GameBootstrap? bootstrap = null;
            var findGuard = 600;
            while (bootstrap == null && findGuard-- > 0)
            {
                bootstrap = FindFirstObjectByType<GameBootstrap>();
                if (bootstrap == null) yield return null;
            }
            if (bootstrap == null) { Fail("game_bootstrap_not_found"); yield break; }
            if (bootstrap.BootstrapFailed) { Fail("bootstrap_failed"); yield break; }
            Log("data_zero_blocking");

            // 2) 主菜单可见（TemplateShellUi.Awake 已经 Start()+ReturnToMainMenu() 过一次）。
            yield return WaitForPage(bootstrap, ShellPage.MainMenu, "main_menu");
            if (_finished) yield break;

            // 3) 新游戏（TemplateShellUi 的"开始游戏"按钮走的同一条 GameBootstrap.RequestNewGame ->
            //    Presentation.Shell.NewGame -> SampleNewGameStarter 调用链，见 GameBootstrap.cs/
            //    SampleNewGameStarter.cs 判断记录）。难度用 GameOptions.DefaultDifficultyId（省略
            //    difficultyId 参数的默认行为），不做存档槽/难度选择 UI（模板最小闭环本就没有这些
            //    页面，见 TemplateShellUi.cs 顶部判断记录）。
            var newGameOk = bootstrap.RequestNewGame(new Id(SmokeSlotId + "_new"));
            if (!newGameOk) { Fail("new_game_rejected"); yield break; }

            yield return WaitForPage(bootstrap, ShellPage.InWorld, "new_game_enter_world", maxFrames: 1200);
            if (_finished) yield break;

            // 4) 向右移动 1 秒（每帧提交一次移动意图，惯例同
            //    Adapter.Unity.Shell.SmokeRunner.RunSequence 对应步骤）。
            var moveElapsed = 0f;
            while (moveElapsed < 1f)
            {
                if (_finished) yield break;
                bootstrap.Gameplay.Carriers.Movement.Request(MoveRequest.InDirection(bootstrap.PlayerId, new Vec2(1, 0)));
                yield return null;
                moveElapsed += Time.unscaledDeltaTime;
            }
            Log("move_right_1s");

            // 5) 存档到 slot.smoke
            var saveResult = bootstrap.Presentation.Shell.OverwriteSlot(new Id(SmokeSlotId), playTimeSeconds: null, displaySummary: null);
            if (!saveResult.Success) { Fail($"save_failed_{saveResult.Reason}"); yield break; }
            Log("save_slot_smoke");

            // 6) 读档
            var loadResult = bootstrap.Presentation.Shell.LoadGame(new Id(SmokeSlotId));
            if (loadResult.Status != LoadStatus.Loaded && loadResult.Status != LoadStatus.LoadedFromBackup)
            {
                Fail($"load_failed_{loadResult.Status}");
                yield break;
            }
            yield return WaitForPage(bootstrap, ShellPage.InWorld, "load_slot_smoke", maxFrames: 1200);
            if (_finished) yield break;

            // 7) 退出（本类型不涉及 IWindow.Create/Destroy，惯例同 Adapter.Unity.Shell.SmokeRunner
            //    类型顶部"为什么用 Application.Quit"判断记录：直接调用不会被 Application.wantsToQuit
            //    拦截）。
            _finished = true;
            Succeeded = true;
            Debug.Log($"{LogPrefix} RESULT=OK");
            Application.Quit(0);
        }

        private IEnumerator WaitForPage(GameBootstrap bootstrap, ShellPage page, string stepName, int maxFrames = 300)
        {
            var guard = maxFrames;
            while (!_finished && bootstrap.Presentation.Shell.Page != page && guard-- > 0)
            {
                yield return null;
            }
            if (_finished) yield break;

            if (bootstrap.Presentation.Shell.Page != page)
            {
                Fail($"{stepName}_timeout_page={bootstrap.Presentation.Shell.Page}");
                yield break;
            }
            Log(stepName);
        }

        private static void Log(string step) => Debug.Log($"{LogPrefix} step={step} ok");

        private void Fail(string reason, int quitCode = 2)
        {
            if (_finished) return;
            _finished = true;
            Debug.LogError($"{LogPrefix} RESULT=FAIL reason={reason}");
            Application.Quit(quitCode);
        }
    }
}
