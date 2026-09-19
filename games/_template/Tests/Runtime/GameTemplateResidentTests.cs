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
//
// 待办（本文件本次改动，2026-09-19）：`ResidentRunner_DatasetRootOverride_LoadsProbeTable_
// FromOverrideRootOnly` 是本次任务对此前一条假测试（覆盖值=默认值，通过与失败无法区分）的替换，
// 属于 Unity PlayMode 用例，本任务未在此工作树内跑 Unity（见派单说明，PlayMode 门禁由主会话统一
// 跑），未能实机验证；反向确认（去掉覆盖应让断言失败）的具体做法与预期结果见该用例内注释，
// 待主会话在整合时跑引擎门禁一并验证。同等的"覆盖不同根确实加载到不同数据"这条底层机制已在纯
// .NET 侧补了等价回归（`core/foundation/data_registry/tests/GameDatasetRootOverrideEquivalenceTests.cs`），
// 已本地跑通并做过真正的反向确认（临时改断代码再还原，见该文件判断记录）。
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
        private string? _overrideProbeDir;
        private string? _overrideRelativeRoot;

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
            TryDeleteProbeDir(_overrideProbeDir);
            _overrideProbeDir = null;
            yield return null;
        }

        private static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { /* 尽力而为 */ }
        }

        /// <summary>清理 <see cref="ResidentRunner_DatasetRootOverride_LoadsProbeTable_FromOverrideRootOnly"/>
        /// 在真实 StreamingAssets 内容根下临时写出的探针数据目录（含 Unity 可能顺带生成的
        /// 同名 .meta），避免残留污染工作副本；用随机子目录名（见该用例），不会与仓库既有内容
        /// 冲突。</summary>
        private static void TryDeleteProbeDir(string? dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* 尽力而为 */ }
            try { if (File.Exists(dir + ".meta")) File.Delete(dir + ".meta"); } catch { /* 尽力而为 */ }
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

        /// <summary>探针数据表名，只在 <see cref="ResidentRunner_DatasetRootOverride_LoadsProbeTable_FromOverrideRootOnly"/>
        /// 临时写出的覆盖根下存在，仓库任何既有数据根（含默认 "data/game"）下都不存在——用于让
        /// "覆盖是否真的生效"变得可观察、可区分（修复前的用例把覆盖值设成与默认值相同，通过与
        /// 失败无法区分，等于没测，见本文件改动前版本与 CHANGELOG "[Unreleased]" 对应条目）。</summary>
        private const string ProbeTableName = "probe.dataset_root_marker";

        private const string ProbeRecordKey = "probe.dataset_root_marker.value";

        private static string ProbeTableJson(string origin) =>
            "{\"table\": \"" + ProbeTableName + "\", \"schema_version\": 1, \"rows\": " +
            "[{\"id\": \"" + ProbeRecordKey + "\", \"origin\": \"" + origin + "\"}]}";

        [UnityTest]
        public IEnumerator ResidentRunner_DatasetRootOverride_LoadsProbeTable_FromOverrideRootOnly()
        {
            // 有区分力的替换用例（修复前版本见上方类型头一段判断记录引用）：准备一份只存在于覆盖
            // 根、不存在于默认根 "data/game" 的最小探针数据表，断言覆盖生效后 GameBootstrap.Registry
            // 能读到探针记录——如果覆盖没有生效（被忽略、字段写错、装配阶段某处吞掉了覆盖值等），
            // ResidentRunner/GameBootstrap 实际读的仍是默认根，探针表根本不存在，TryGet 必然返回
            // false，断言必然失败，因此本用例具备区分力。
            //
            // 反向确认（AGENTS.md §7 与本任务硬性要求：真正跑一次"去掉覆盖，测试必须失败"）：把下面
            // "GameBootstrap.GameDatasetRootOverride = _overrideRelativeRoot;" 这一行临时改成
            // "GameBootstrap.GameDatasetRootOverride = null;"（或注释掉，等价于"覆盖功能被去
            // 掉/传空"），保持其余代码不变重新跑本用例——预期：探针表在默认根 "data/game" 下不存在，
            // "found" 断言（下方 Assert.IsTrue(found, ...)）会失败、用例报红，证明当前断言确实依赖
            // 覆盖生效才能通过，不是摆设；验证完成后必须改回来，否则本用例会假失败。本任务在此
            // worktree 内不跑 Unity（见派单说明），未能执行这一步，标注为"待主会话在整合时跑引擎
            // 门禁验证"一并核对。
            CleanupStaleGameBootstraps();

            _overrideRelativeRoot = "gf_test_dataset_root_probe_" + Guid.NewGuid().ToString("N");
            var contentRoot = Path.Combine(Application.streamingAssetsPath, "GameFoundation");
            _overrideProbeDir = Path.Combine(contentRoot, _overrideRelativeRoot);
            Directory.CreateDirectory(_overrideProbeDir);
            File.WriteAllText(Path.Combine(_overrideProbeDir, ProbeTableName + ".json"), ProbeTableJson("override_root"));

            GameBootstrap.GameDatasetRootOverride = _overrideRelativeRoot;

            var go = new GameObject("ResidentTestBootstrapOverride");
            go.SetActive(false);
            var bootstrap = go.AddComponent<GameBootstrap>();
            _bootstrapGo = go;
            go.SetActive(true);

            Assert.IsFalse(bootstrap.BootstrapFailed, "覆盖到一个真实存在的数据根，装配不应失败");
            Assert.IsNotNull(bootstrap.LoadReport);
            Assert.AreEqual(0, bootstrap.LoadReport!.ErrorCount,
                "覆盖生效后仍应 0 阻断错误：" + string.Join("; ", bootstrap.LoadReport.Issues));

            var found = bootstrap.Registry.TryGet(ProbeTableName, ProbeRecordKey, out var record);
            Assert.IsTrue(found,
                "覆盖生效时应能读到只存在于覆盖根下的探针表——若为 false，说明覆盖被忽略，实际仍在读默认根 data/game");
            Assert.AreEqual("override_root", record!.GetString("origin"), "探针记录字段应来自覆盖根写入的内容");

            yield return null;
        }

        [UnityTest]
        public IEnumerator ResidentRunner_DatasetRootOverride_Absent_DefaultRootNeverSeesOverrideProbeTable()
        {
            // 对照组（不是反向确认本身，反向确认见上一条用例注释）：不设置覆盖（默认根
            // "data/game"）时，探针表本就不存在于默认根下，TryGet 应返回 false——与上一条用例合起
            // 来构成"覆盖生效 vs 不生效"两种可观察状态的对照，捕获"探针表意外泄漏进默认根/静态字段
            // 跨用例残留导致假阳性"这一类问题。
            CleanupStaleGameBootstraps();
            GameBootstrap.GameDatasetRootOverride = null;

            var go = new GameObject("ResidentTestBootstrapDefault");
            go.SetActive(false);
            var bootstrap = go.AddComponent<GameBootstrap>();
            _bootstrapGo = go;
            go.SetActive(true);

            Assert.IsFalse(bootstrap.BootstrapFailed, "默认数据根装配不应失败");
            var found = bootstrap.Registry.TryGet(ProbeTableName, ProbeRecordKey, out _);
            Assert.IsFalse(found, "默认根 data/game 下不应存在覆盖探针表");

            yield return null;
        }
    }
}
