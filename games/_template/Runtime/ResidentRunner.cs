#nullable enable
// ResidentRunner：反馈第 68 条对应的"长驻可交互运行入口"落地（ADR-0040 决策 2）。与
// TemplateSmokeRunner（"-gf-smoke-template"，跑完固定序列即退出）并列新增，不取代——两者共享同
// 一套 GameBootstrap/TemplateShellUi 装配与场景加载路径（都靠场景自身的正常加载流程触发
// GameBootstrap.Ensure()，不重新发明一套组合根），只是对外暴露不同的生命周期承诺：
// TemplateSmokeRunner 是"跑完即退"（门禁/CI 需要确定性退出码，不能让进程无限期占用），本类型是
// "进入可交互状态后保持运行，直到外部主动结束它"，供任意外部工具（CI 脚本、内容作者自己的工具、
// 任何第三方联调工具）以长驻子进程方式驱动/观察——不专为某一类工具定制协议（ADR-0040"总原则"，
// 把"编辑器"换成"任意外部工具"这句话依然成立）。
//
// 判断记录（就绪信号为什么选"就绪文件"而不是只在日志打一行）：仓库现有 TemplateSmokeRunner/
// Adapter.Unity.Shell.SmokeRunner 的可观察约定都是"往 Debug.Log 打一行加固定前缀的文本，外部靠
// -logFile 输出的日志文件事后整份读取、grep 这一行"（见 TemplateSmokeRunner.cs 头注释）。这套
// 约定对"跑完即退"入口够用（外部反正要等进程退出、退出后一次性读完整份日志），但对"长驻"入口不
// 够：Unity 日志文件的具体刷盘时机未做任何承诺，外部工具若要"轮询判断是否已就绪"，除了反复重新
// 打开整份日志文件搜索该行之外没有更廉价的手段，且无法区分"还没写到这一行"与"这次运行装配已经
// 失败，走的是失败分支，永远不会写这一行"。改用"就绪文件"：确认进入可交互状态后用
// File.WriteAllText 直接写一个小文件（创建/覆盖对文件系统而言是原子可见的一次性动作），外部工具
// 只需要 File.Exists 轮询这一个路径，不需要解析任何文本、不需要关心 Unity 日志缓冲策略；仍保留一
// 行 Debug.Log 输出（前缀 "[GF-RESIDENT]"，与既有 "[GF-SMOKE]" 同一命名习惯）供人工盯着日志窗口
// 时也能看到，两种观测手段并存、互不依赖，启动时把两个路径都打进日志，外部工具即使没有预先读过
// 文档也能从日志里发现约定路径。
//
// 判断记录（受控退出为什么选"停止文件轮询"而不是标准中断信号 Ctrl+C/SIGINT）：ADR-0040 决策 2
// 把"响应标准中断信号"列为示例之一而非强制要求，具体载体留给落地期确定。Unity 独立版在
// -batchmode 下对 Windows 控制台 CTRL_C_EVENT/CTRL_BREAK_EVENT 的处理不受托管代码控制（引擎原生
// 层直接终止进程，不保证跑到本类型的任何清理代码），尤其当调用方通过
// System.Diagnostics.Process/PowerShell Start-Process 以分离控制台方式启动子进程时，Ctrl+C 事件
// 能否可靠传达到子进程本身就不确定——这与"受控"（外部可预期地触发一次可控收尾）的契约意图相悖：
// 一旦信号没送达，或送达后进程直接被引擎原生层杀掉、不经过任何托管代码，效果等同于强杀，不满足
// ADR"非强杀"的要求。改用与就绪信号对称的"文件轮询"：外部工具在自己选定的时机创建停止文件，本
// 类型每隔固定时间检测一次该文件是否存在，检测到后在托管代码里完整走一遍收尾（打印结束日志、
// 删除停止文件避免残留、以约定退出码调用 Application.Quit），不依赖任何操作系统信号语义在这套
// headless 批处理环境下的可靠性。
//
// 判断记录（退出码为什么复用 TemplateSmokeRunner 的分级而不新造一套）：ADR-0040 决策 2 要求"复用
// 既有批处理入口已有的退出码分级约定……不新造一套独立的退出码语义"——本类型只区分二态：0=受控
// 退出成功（检测到停止文件）；2=启动失败（含装配失败、迟迟未能进入可交互状态两类阻断态），与
// TemplateSmokeRunner"0=OK，2=FAIL"的失败码完全一致。不复用 TemplateSmokeRunner 的"3=看门狗超时"
// ——那是该类型自己"整段冒烟流程必须在固定时限内跑完"这一契约的专属语义，本类型故意不设总运行时
// 长上限（核心承诺就是"保持运行直到外部主动结束"，引入一个总超时会与这条承诺矛盾），只在"进入
// 可交互状态之前"这一启动阶段设了有限轮询上限，超出算启动失败，归入退出码 2，不需要单独的第三个
// 退出码。
//
// 命令行标志：
//   -gf-resident                          启用本类型（同 TemplateSmokeRunner 不传任何参数时完全
//                                          不介入，不新建任何 GameObject/组件）。
//   -gf-content-root=<相对路径>            可选，覆盖 GameBootstrap.GameDatasetRootOverride
//                                          （默认不覆盖，即 "data/game"）。
//   -gf-ready-file=<绝对路径>              可选，默认 <persistentDataPath>/gf_resident_ready.txt。
//   -gf-stop-file=<绝对路径>               可选，默认 <persistentDataPath>/gf_resident_stop.txt。
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Presentation.Shell;
using UnityEngine;

namespace Game.Template
{
    public sealed class ResidentRunner : MonoBehaviour
    {
        private const string CommandLineFlag = "-gf-resident";
        private const string ContentRootArgPrefix = "-gf-content-root=";
        private const string ReadyFileArgPrefix = "-gf-ready-file=";
        private const string StopFileArgPrefix = "-gf-stop-file=";
        private const string LogPrefix = "[GF-RESIDENT]";

        /// <summary>停止文件轮询间隔（实时秒），不需要逐帧检测——长驻场景对响应延迟不敏感，
        /// 降低轮询频率对性能友好。</summary>
        private const float StopPollIntervalSeconds = 0.25f;

        /// <summary>等待进入可交互状态（主菜单可见）的轮询帧数上限，惯例同
        /// <see cref="TemplateSmokeRunner"/> 对 <c>InWorld</c> 页面的等待写法；超出判启动失败
        /// （退出码 2），不是无限等待。</summary>
        private const int ReadyGuardFrames = 600;

        private string _readyFilePath = "";
        private string _stopFilePath = "";
        private bool _finished;

        /// <summary>本次运行是否已经结束（无论成功收尾还是启动失败）。公开而非 internal：同
        /// <see cref="TemplateSmokeRunner.IsFinished"/> 判断记录，本模板未声明任何
        /// <c>InternalsVisibleTo</c>，PlayMode 测试需要在编辑器内轮询这个属性。</summary>
        public bool IsFinished => _finished;

        /// <summary>只在检测到停止文件、走受控退出路径时为真；启动失败路径下保持 false，惯例同
        /// <see cref="TemplateSmokeRunner.Succeeded"/>。</summary>
        public bool Succeeded { get; private set; }

        /// <summary>就绪文件是否已经写出（即已经进入可交互状态），供测试/外部代码直接读取，不必
        /// 反过来用 <see cref="File.Exists"/> 轮询同一个进程自己已经知道的状态。</summary>
        public bool ReadySignalWritten { get; private set; }

        /// <summary>本次运行实际使用的就绪文件路径（命令行未显式指定时是默认路径），供测试断言。</summary>
        public string ReadyFilePath => _readyFilePath;

        /// <summary>本次运行实际使用的停止文件路径，供测试断言/外部工具在未读文档时从日志之外的
        /// 另一渠道获知（<see cref="Configure"/> 场景）。</summary>
        public string StopFilePath => _stopFilePath;

        /// <summary>命令行不带 <see cref="CommandLineFlag"/> 时本类型完全不介入，正常游戏/编辑器/
        /// 既有 PlayMode 测试运行路径不受影响，惯例同 <see cref="TemplateSmokeRunner.TryStart"/>。
        /// 本钩子额外做一件 <see cref="TemplateSmokeRunner"/> 不需要做的事：在任何场景 Awake 之前
        /// （<c>BeforeSceneLoad</c>，早于 <c>AfterSceneLoad</c>）把 <c>-gf-content-root</c> 覆盖值
        /// 写进 <see cref="GameBootstrap.GameDatasetRootOverride"/>——必须在 GameBootstrap.Bootstrap()
        /// 读取该字段之前完成，见该字段判断记录。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyContentRootOverrideBeforeSceneLoad()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains(CommandLineFlag, StringComparer.Ordinal))
            {
                return;
            }

            var contentRootArg = args.FirstOrDefault(a => a.StartsWith(ContentRootArgPrefix, StringComparison.Ordinal));
            if (contentRootArg != null)
            {
                GameBootstrap.GameDatasetRootOverride = contentRootArg.Substring(ContentRootArgPrefix.Length);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void TryStart()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains(CommandLineFlag, StringComparer.Ordinal))
            {
                return;
            }

            var go = new GameObject("ResidentRunner");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<ResidentRunner>();
            runner._readyFilePath = ResolvePathArg(args, ReadyFileArgPrefix, DefaultReadyFilePath());
            runner._stopFilePath = ResolvePathArg(args, StopFileArgPrefix, DefaultStopFilePath());
        }

        private static string ResolvePathArg(string[] args, string prefix, string fallback)
        {
            var match = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
            return match != null ? match.Substring(prefix.Length) : fallback;
        }

        private static string DefaultReadyFilePath() => Path.Combine(Application.persistentDataPath, "gf_resident_ready.txt");

        private static string DefaultStopFilePath() => Path.Combine(Application.persistentDataPath, "gf_resident_stop.txt");

        /// <summary>供测试/高级调用方在 <c>AddComponent</c> 之后手工指定路径（正常独立版路径由
        /// <see cref="TryStart"/> 按命令行参数/默认值自动设置，不需要调用本方法）。必须在本
        /// GameObject 变为 active（<see cref="Awake"/> 执行）之前调用——<c>AddComponent</c> 若在
        /// active 的 GameObject 上会立即同步触发 Awake，因此调用方应先 <c>SetActive(false)</c> 再
        /// <c>AddComponent</c>+<see cref="Configure"/>，最后 <c>SetActive(true)</c>，惯例见
        /// <c>games/_template/Tests/Runtime/GameTemplateResidentTests.cs</c>。</summary>
        public void Configure(string readyFilePath, string stopFilePath)
        {
            _readyFilePath = readyFilePath;
            _stopFilePath = stopFilePath;
        }

        private void Awake()
        {
            // 判断记录：停止/就绪文件若在本次启动前就已残留（例如上一次运行异常退出、外部工具没
            // 清理），会导致本次刚进入等待循环就立即"检测到"停止信号，或让外部工具误判"还没就绪"
            // 却读到上一次运行遗留的就绪文件——先各删一次，保证这两个文件只可能来自本次运行期间的
            // 真实状态，不会被历史残留误触发/误判。
            TryDeleteFile(_stopFilePath);
            TryDeleteFile(_readyFilePath);
            StartCoroutine(RunSequence());
        }

        private IEnumerator RunSequence()
        {
            GameBootstrap? bootstrap = null;
            var findGuard = ReadyGuardFrames;
            while (bootstrap == null && findGuard-- > 0)
            {
                bootstrap = FindFirstObjectByType<GameBootstrap>();
                if (bootstrap == null) yield return null;
            }
            if (bootstrap == null) { Fail("game_bootstrap_not_found"); yield break; }
            if (bootstrap.BootstrapFailed) { Fail("bootstrap_failed"); yield break; }

            // 就绪 = 已进入可交互状态：主菜单可见（同 TemplateSmokeRunner 步骤 2），玩家/外部工具
            // 此时已经可以点击"新游戏"等操作——不要求走到更深的流程，保持"就绪"这条契约尽量宽松、
            // 通用，不与任何具体游戏内容耦合。
            var pageGuard = ReadyGuardFrames;
            while (bootstrap.Presentation.Shell.Page != ShellPage.MainMenu && pageGuard-- > 0)
            {
                yield return null;
            }
            if (bootstrap.Presentation.Shell.Page != ShellPage.MainMenu)
            {
                Fail("main_menu_timeout");
                yield break;
            }

            WriteReadySignal();

            while (!_finished)
            {
                if (File.Exists(_stopFilePath))
                {
                    Succeed();
                    yield break;
                }
                yield return new WaitForSecondsRealtime(StopPollIntervalSeconds);
            }
        }

        private void WriteReadySignal()
        {
            try
            {
                var dir = Path.GetDirectoryName(_readyFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir!);
                }
                File.WriteAllText(_readyFilePath, DateTime.UtcNow.ToString("O"));
                ReadySignalWritten = true;
            }
            catch (Exception ex)
            {
                // 判断记录：写就绪文件失败（如目录权限问题）不应让本类型崩溃/中断长驻循环——本次
                // 运行仍然是可交互的，只是外部工具这一种观测手段失效；如实打印错误，仍继续走完
                // 停止轮询循环（人工盯日志窗口/直接操作游戏仍然可用）。
                Debug.LogError($"{LogPrefix} 写就绪文件失败：{_readyFilePath}：{ex}");
            }
            Debug.Log($"{LogPrefix} status=ready ready_file={_readyFilePath} stop_file={_stopFilePath}");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 尽力而为，同仓库既有清理惯例（如 GlobalTemplateTestSetup.ClearAllSaveArtifacts）。
            }
        }

        private void Succeed()
        {
            if (_finished) return;
            _finished = true;
            Succeeded = true;
            TryDeleteFile(_stopFilePath);
            Debug.Log($"{LogPrefix} RESULT=OK reason=stop_file_detected");
            Application.Quit(0);
        }

        private void Fail(string reason)
        {
            if (_finished) return;
            _finished = true;
            Debug.LogError($"{LogPrefix} RESULT=FAIL reason={reason}");
            Application.Quit(2);
        }
    }
}
