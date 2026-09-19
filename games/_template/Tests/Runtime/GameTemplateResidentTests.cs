#nullable enable
// GameTemplateResidentTests：`Runtime/ResidentRunner.cs`（反馈第 68 条"长驻可交互运行入口"，
// ADR-0040 决策 2）在编辑器 PlayMode 下的可跑通性回归。
//
// 判断记录（不经 GameTemplateShell 场景/TemplateShellUi，改用独立未激活 GameObject 装配，惯例同
// PRES118_TemplateContractTests.BuildInactiveBootstrap 判断记录）：ResidentRunner 只被动等待
// GameBootstrap 出现并进入 MainMenu 页，不主动驱动 Shell 状态——正常独立版路径下这一步由
// TemplateShellUi.Awake() 隐式调用 Presentation.Shell.Start()+ReturnToMainMenu() 完成；本文件不
// 经场景加载，改为独立实例上手工调用 Presentation.Shell.Start() 到达 MainMenu，避免与同一
// PlayMode 批次里 GameTemplateSmokeTests（尤其其冒烟用例跑完后停留在 InWorld，从不
// ReturnToMainMenu）共用同一个 DontDestroyOnLoad 单例 GameBootstrap 时的跨用例状态残留。
//
// 判断记录（为什么在编辑器 PlayMode 测试里放心跑到 ResidentRunner 内部的 Application.Quit 那一
// 步）：同 GameTemplateSmokeTests.TemplateSmokeRunner_InEditor_CompletesSequenceSuccessfully
// 判断记录——Application.Quit 在编辑器内（无论手动 Play 还是 -runTests 批处理 PlayMode）是纯粹
// 空操作，不会真的中断测试进程/退出 Play 模式，只在真正独立版构建产物里才生效；本用例因此可以
// 完整验证"就绪文件被写出 -> 创建停止文件 -> Succeeded 转为 true"这一整条链路，只是不能验证真实
// 进程退出码本身（那需要构建独立版，见 games/_template/README.md"长驻可交互运行"一节，由
// consumer_smoke.ps1 或主会话手工验证，本用例不覆盖）。
using System;
using System.Collections;
using System.IO;
using Game.Template;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Template.Tests
{
    public sealed class GameTemplateResidentTests
    {
        private GameObject? _bootstrapGo;
        private GameObject? _runnerGo;
        private string? _readyFilePath;
        private string? _stopFilePath;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_runnerGo != null)
            {
                UnityEngine.Object.Destroy(_runnerGo);
                _runnerGo = null;
            }
            if (_bootstrapGo != null)
            {
                UnityEngine.Object.Destroy(_bootstrapGo);
                _bootstrapGo = null;
            }
            GameBootstrap.GameDatasetRootOverride = null;
            TryDeleteFile(_readyFilePath);
            TryDeleteFile(_stopFilePath);
            yield return null;
        }

        private static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { /* 尽力而为 */ }
        }

        /// <summary>惯例同 PRES118_TemplateContractTests.CleanupStaleGameBootstraps：构建独立实例
        /// 之前先同步销毁场景里任何残留的 GameBootstrap 实例。</summary>
        private static void CleanupStaleGameBootstraps()
        {
            foreach (var stale in UnityEngine.Object.FindObjectsByType<GameBootstrap>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }
        }

        /// <summary>独立装配一个已到达 MainMenu 页的 GameBootstrap（不经场景/TemplateShellUi），
        /// 惯例同 PRES118_TemplateContractTests.BuildInactiveBootstrap + 该文件用例里手工调用
        /// Presentation.Shell.Start() 的写法。</summary>
        private GameBootstrap BuildBootstrapAtMainMenu()
        {
            CleanupStaleGameBootstraps();

            var go = new GameObject("ResidentTestBootstrap");
            go.SetActive(false);
            var bootstrap = go.AddComponent<GameBootstrap>();
            _bootstrapGo = go;
            go.SetActive(true);

            Assert.IsFalse(bootstrap.BootstrapFailed, "默认 GameOptions 下 GameBootstrap 不应装配失败");
            bootstrap.Presentation.Shell.Start();
            Assert.AreEqual(global::Presentation.Shell.ShellPage.MainMenu, bootstrap.Presentation.Shell.Page,
                "手工 Start() 后应停在主菜单页（同 TemplateShellUi.Awake 隐式触发的效果）");
            return bootstrap;
        }

        /// <summary>惯例同 <see cref="ResidentRunner.Configure"/> 判断记录：GameObject 必须先
        /// SetActive(false) 再 AddComponent，避免 AddComponent 在 active 对象上同步触发 Awake 时
        /// _readyFilePath/_stopFilePath 仍是默认值。</summary>
        private ResidentRunner BuildInactiveResidentRunner(string readyFilePath, string stopFilePath)
        {
            var go = new GameObject("ResidentTestRunner");
            go.SetActive(false);
            var runner = go.AddComponent<ResidentRunner>();
            runner.Configure(readyFilePath, stopFilePath);
            _runnerGo = go;
            return runner;
        }

        [UnityTest]
        public IEnumerator ResidentRunner_AfterMainMenu_WritesReadySignal_ThenStopFile_EndsRunSuccessfully()
        {
            BuildBootstrapAtMainMenu();

            var tempDir = Path.Combine(Application.temporaryCachePath, "gf_resident_tests_" + Guid.NewGuid().ToString("N"));
            _readyFilePath = Path.Combine(tempDir, "ready.txt");
            _stopFilePath = Path.Combine(tempDir, "stop.txt");

            var runner = BuildInactiveResidentRunner(_readyFilePath, _stopFilePath);
            runner.gameObject.SetActive(true);

            // 1) 就绪：ResidentRunner 应在有限帧数内发现 MainMenu 已可见并写出就绪文件。
            var readyDeadline = Time.realtimeSinceStartup + 15f;
            while (!runner.ReadySignalWritten && Time.realtimeSinceStartup < readyDeadline)
            {
                yield return null;
            }
            Assert.IsTrue(runner.ReadySignalWritten, "ResidentRunner 应在保底时限内写出就绪信号");
            Assert.IsFalse(runner.IsFinished, "写出就绪信号后应继续保持运行，不应自行结束");
            Assert.IsTrue(File.Exists(_readyFilePath), "就绪文件应当真的落盘，供外部工具轮询");

            // 2) 长驻：再等几帧，确认在没有停止文件的情况下不会自行退出（核心契约：保持运行直到
            //    外部主动结束）。
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
            Assert.IsFalse(runner.IsFinished, "没有停止文件时 ResidentRunner 不应自行结束");

            // 3) 受控退出：外部工具创建停止文件后，应在有限时间内检测到并走受控退出路径。
            Directory.CreateDirectory(Path.GetDirectoryName(_stopFilePath)!);
            File.WriteAllText(_stopFilePath, "stop");

            var stopDeadline = Time.realtimeSinceStartup + 5f;
            while (!runner.IsFinished && Time.realtimeSinceStartup < stopDeadline)
            {
                yield return null;
            }
            Assert.IsTrue(runner.IsFinished, "创建停止文件后 ResidentRunner 应在保底时限内结束");
            Assert.IsTrue(runner.Succeeded, "检测到停止文件应走受控退出成功路径（RESULT=OK），不是失败路径");
            Assert.IsFalse(File.Exists(_stopFilePath), "受控退出应清理掉停止文件，避免下次启动误触发");
        }

        [UnityTest]
        public IEnumerator ResidentRunner_ContentRootOverride_SameAsDefault_StillBootstrapsCleanly()
        {
            // ADR-0040 决策 2：验证 GameBootstrap.GameDatasetRootOverride 覆盖生效（不是被忽略的死
            // 配置项）。用与默认值相同的路径覆盖，只验证"覆盖被读取并使用后装配依然成功、0 阻断
            // 错误"，不引入新的模板数据集夹具——真正加载"另一份不同内容"的端到端验证属于需要真实
            // Unity 独立版构建 + 一份替代数据集的场景，留给主会话按 README"长驻可交互运行"一节列出
            // 的清单手工验证。
            CleanupStaleGameBootstraps();
            GameBootstrap.GameDatasetRootOverride = "data/game";

            var go = new GameObject("ResidentTestBootstrapOverride");
            go.SetActive(false);
            var bootstrap = go.AddComponent<GameBootstrap>();
            _bootstrapGo = go;
            go.SetActive(true);

            Assert.IsFalse(bootstrap.BootstrapFailed, "覆盖为与默认值相同的数据根，装配不应失败");
            Assert.IsNotNull(bootstrap.LoadReport);
            Assert.AreEqual(0, bootstrap.LoadReport!.ErrorCount,
                "覆盖生效后仍应 0 阻断错误：" + string.Join("; ", bootstrap.LoadReport.Issues));

            yield return null;
        }
    }
}
