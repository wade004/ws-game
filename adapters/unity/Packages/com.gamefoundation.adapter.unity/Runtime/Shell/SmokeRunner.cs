#nullable enable
// SmokeRunner：缺口 3（独立版无人值守冒烟）。命令行参数 "-gf-smoke" 驱动，跑一遍
// "主菜单 → 新游戏（默认难度）→ 进入地图 → 向右移动 1 秒 → 普攻一次 → 存档到 slot.smoke →
// 读档 → 退出"，每步 Debug.Log("[GF-SMOKE] step=<name> ok")，成功以
// "[GF-SMOKE] RESULT=OK" + Application.Quit(0) 收尾；任一步失败
// "[GF-SMOKE] RESULT=FAIL reason=..." + Application.Quit(2)；总耗时超过 60 秒
// Application.Quit(3)（见 RunWatchdog）。
//
// 判断记录（为什么直接调用 Presentation.Shell/Gameplay API，不模拟鼠标点击 UI 按钮）：
// ShellRoot.cs 类型顶部已有的判断记录（"注入意图"测试哲学）指出 ShellRoot 的按钮 OnClick 回调
// 本身就是直接调用 Framework.Presentation.Shell 的同一批方法，测试代码（ShellFlowTests/
// VerticalSliceTests）因此也一律走这条路径而不模拟点击——本类型沿用同一惯例，用同一套
// Presentation.Shell.IShellHost/GameplayAssembly API 编排冒烟步骤，理由相同：这是"真实点击" 与
// "程序化驱动"共享的同一条调用路径，不是另一条需要额外验证是否等价的旁路。移动/普攻分别复用
// VerticalSliceTests.cs 已验证过的
// Gameplay.Carriers.Movement.Request(MoveRequest.InDirection(...))/FrameworkResidentHost.CastSkill
// 两个公开方法。
//
// 判断记录（为什么用 Application.Quit 而不是其它退出手段）：勘察 UnityWindow.cs 发现
// IWindow.Create 会把 HandleWantsToQuit 挂到 Application.wantsToQuit，未经 Destroy() 直接调
// Application.Quit() 会被挡下（返回 false 阻止默认退出）；但 adapters/unity 全仓库勘察确认
// UnityEngineHost 从未调用过 Window.Create（IWindow.Create/Destroy 目前没有任何调用方接入 Shell
// 运行时路径，只在 UnityWindowTests.cs 里被单独测试），因此 Application.wantsToQuit 从未被挂钩，
// 本类型可以直接调用 Application.Quit(exitCode) 退出且不会被拦截；若未来有代码开始调用
// Window.Create，需要改走 UnityEngineHost.Window.Destroy() 触发的真正退出路径。
//
// 判断记录（为什么不加 -nographics）：包 README"独立版无头冒烟"一节已实测记录
// -nographics 强制 NullGfxDevice 时 UiRoot/InputSystemUIInputModule 依赖的渲染/输入子系统"可能
// 无法正确初始化"，本类型不依赖任何鼠标/键盘物理事件（见上一条判断记录，全走程序化 API 调用），
// 理论上可以带 -nographics 跑；但任务书明确要求命令行不加该参数（允许弹窗），本类型不对此做
// 额外适配，按任务书原样执行。
using System;
using System.Collections;
using System.Linq;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.SaveSystem;
using Presentation.Shell;
using UnityEngine;

namespace Adapter.Unity.Shell
{
    public sealed class SmokeRunner : MonoBehaviour
    {
        private const string CommandLineFlag = "-gf-smoke";
        private const float TotalTimeoutSeconds = 60f;
        private const string SmokeSlotId = "slot.smoke";
        private const string AttackSkillId = "skill.sample_strike";
        private const string LogPrefix = "[GF-SMOKE]";

        private bool _finished;

        /// <summary>命令行不带 "-gf-smoke" 时本类型完全不介入（不新建任何 GameObject/组件），
        /// 正常游戏/编辑器/既有 PlayMode 测试运行路径不受影响。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void TryStart()
        {
            if (!Environment.GetCommandLineArgs().Contains(CommandLineFlag, StringComparer.Ordinal))
            {
                return;
            }

            var go = new GameObject("SmokeRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<SmokeRunner>();
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
            // 1) 等 ShellRoot 出现（Shell.unity 是 Build Settings 第 0 位启动场景，
            //    RuntimeInitializeLoadType.AfterSceneLoad 时其 Awake 通常已经跑完，这里仍轮询
            //    一段时间保底，避免时序假设有误时直接空引用）。
            ShellRoot? shell = null;
            var findGuard = 600;
            while (shell == null && findGuard-- > 0)
            {
                shell = FindFirstObjectByType<ShellRoot>();
                if (shell == null) yield return null;
            }
            if (shell == null) { Fail("shell_root_not_found"); yield break; }
            if (shell.Framework.BootstrapFailed) { Fail("bootstrap_failed"); yield break; }

            yield return WaitForPage(shell, ShellPage.MainMenu, "boot_main_menu");
            if (_finished) yield break;

            shell.Framework.Presentation.Shell.ShowSlots();
            yield return WaitForPage(shell, ShellPage.SaveSlots, "show_slots");
            if (_finished) yield break;

            shell.Framework.Presentation.Shell.ShowNewGameSetup();
            yield return WaitForPage(shell, ShellPage.NewGameSetup, "show_new_game_setup");
            if (_finished) yield break;

            var tierId = ResolveDefaultDifficulty(shell);
            if (tierId == null) { Fail("no_difficulty_tier_registered"); yield break; }

            var newGameOk = shell.Framework.Presentation.Shell.NewGame(new Id(SmokeSlotId + "_new"), tierId.Value, archetypeId: null);
            if (!newGameOk) { Fail("new_game_rejected"); yield break; }

            yield return WaitForPage(shell, ShellPage.InWorld, "new_game_enter_world", maxFrames: 1200);
            if (_finished) yield break;

            // 2) 向右移动 1 秒（每帧提交一次移动意图，同 VerticalSliceTests.cs 已验证过的调用方式）。
            var moveElapsed = 0f;
            while (moveElapsed < 1f)
            {
                if (_finished) yield break;
                shell.Framework.Gameplay.Carriers.Movement.Request(MoveRequest.InDirection(shell.Framework.PlayerId, new Vec2(1, 0)));
                yield return null;
                moveElapsed += Time.unscaledDeltaTime;
            }
            Log("move_right_1s");

            // 3) 普攻一次（skill.sample_strike，同 GameFoundationBootstrap/FrameworkResidentHost
            //    "普攻"绑定的同一个技能 id）；多等两个固定步，让 cast 意图真正被 WorldSim.Tick 结算。
            shell.Framework.CastSkill(new Id(AttackSkillId));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            if (_finished) yield break;
            Log("attack_once");

            // 4) 存档到 slot.smoke
            var saveResult = shell.Framework.Presentation.Shell.OverwriteSlot(new Id(SmokeSlotId), playTimeSeconds: null, displaySummary: null);
            if (!saveResult.Success) { Fail($"save_failed_{saveResult.Reason}"); yield break; }
            Log("save_slot_smoke");

            // 5) 读档
            var loadResult = shell.Framework.Presentation.Shell.LoadGame(new Id(SmokeSlotId));
            if (loadResult.Status != LoadStatus.Loaded && loadResult.Status != LoadStatus.LoadedFromBackup)
            {
                Fail($"load_failed_{loadResult.Status}");
                yield break;
            }
            yield return WaitForPage(shell, ShellPage.InWorld, "load_slot_smoke", maxFrames: 1200);
            if (_finished) yield break;

            // 6) 退出（见类型顶部"为什么用 Application.Quit"判断记录）
            _finished = true;
            Debug.Log($"{LogPrefix} RESULT=OK");
            Application.Quit(0);
        }

        /// <summary>默认难度：diff.tier 表 sort_weight 最小的一档（与 ShellRoot.BuildNewGameSetup
        /// 同一份数据源，见该方法 foreach 遍历同一个表；本方法不硬编码具体档位 id，避免与示例数据
        /// 文件内容耦合）。</summary>
        private static Id? ResolveDefaultDifficulty(ShellRoot shell)
        {
            Id? best = null;
            long bestWeight = long.MaxValue;
            foreach (var record in shell.Framework.Registry.GetAll("diff.tier"))
            {
                var weight = record.TryGetInt("sort_weight", out var w) ? w : 0;
                if (best == null || weight < bestWeight)
                {
                    best = record.GetId("id");
                    bestWeight = weight;
                }
            }
            return best;
        }

        private IEnumerator WaitForPage(ShellRoot shell, ShellPage page, string stepName, int maxFrames = 300)
        {
            var guard = maxFrames;
            while (!_finished && shell.Framework.Presentation.Shell.Page != page && guard-- > 0)
            {
                yield return null;
            }
            if (_finished) yield break;

            if (shell.Framework.Presentation.Shell.Page != page)
            {
                Fail($"{stepName}_timeout_page={shell.Framework.Presentation.Shell.Page}");
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
