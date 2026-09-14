#nullable enable
// TestFirstChanceExceptionLogger：测试运行期"首个异常记录"回调（排查复盘 2026-09-15 落地，见
// architecture/落地计划/排查复盘-2026-09-15-PlayMode-PRES180.md）。
//
// 背景：一次 PlayMode 全量门禁稳定失败（VerticalSliceTests.PRES180_...）排查耗时约 3.5 小时、
// 约 156 万 token 才查明真因——玩家资源池跨用例未回满，导致 PRES180 的攻击断言（Assert.IsTrue
// (died)）真的失败；但 Unity Test Framework 的 UnityLogCheckDelegatingCommand 在断言异常之后
// 仍会继续执行 CheckLogs，两条 LogAssert.Expect 落空再抛一次 UnexpectedLogMessageException，
// NUnit TestResult 只记录最后一次抛出的异常，真正的断言失败被覆盖，表面症状变成一句跟字体资产
// 有关、实际上毫不相干的警告消息。排查过程里没有任何地方能看到"死亡断言先失败、字体警告异常
// 后覆盖"这条时序——Unity 批处理日志本身不会打印用例边界，更不会打印"哪个用例在哪个时刻抛出了
// 哪个异常"。本文件把用例边界与每个用例窗口内出现的 Error/Exception 级日志主动打进日志，
// 与 toolchain/unity_test_triage.py 的窗口定位（test_first_chance 一级，见该脚本文件头判断
// 记录）配套使用。
//
// 判断记录（本文件最初按 AppDomain.FirstChanceException 实现，2026-09-15 实测证伪、改为
// 本文件现在的设计——这条判断记录本身就是给后来者的重要提醒，避免重蹈覆辙）：最初设计是订阅
// AppDomain.CurrentDomain.FirstChanceException，在异常刚被抛出、尚未被任何 catch 处理、也
// 尚未被 NUnit 自己的日志检查机制覆盖之前就记录下来。但在本仓库实际使用的
// Unity 6000.3.23f1 Editor（Mono 脚本后端）里实测：即使是最简单的
// "try { throw new InvalidOperationException(...); } catch { }"，也不会触发
// AppDomain.CurrentDomain.FirstChanceException 订阅的回调（真实复现：在 RunStarted 订阅、
// TestStarted 里让一个良性异常自己 try/catch 掉，日志里完全没有任何 OnFirstChanceException
// 被调用过的痕迹）——即用 NUnit 真正的 AssertionException 复现同一问题用例也是如此。这不是
// 本文件哪里写错了，是 Unity 这个 Mono 运行时在这个版本上压根不触发该事件（已知的 Mono 嵌入式
// 运行时限制，并非所有 .NET 事件在所有 Mono 版本/配置下都完整实现）。继续按 FirstChanceException
// 设计会是一段"看起来对、实际从不触发"的死代码，比没有更危险（会让人误以为已经有防线）。
//
// 改法（等价机制，同样能解决"日志里没有第一现场"这个根问题，但走的是验证过真实可用的 API）：
// 1. TestStarted/TestFinished 打印用例边界（"Started"/"Finished"），把 Unity 批处理日志里
//    "完全不打印用例名、不打印用例边界"这个已确认的根问题（见 toolchain/unity_test_triage.py
//    文件头判断记录）直接解决——unity_test_triage.py 的 test_first_chance 一级窗口定位现在靠
//    这对边界标记精确切窗口，不再需要靠用例名首次出现位置近似猜测。
// 2. 订阅 UnityEngine.Application.logMessageReceivedThreaded（标准公开 API，不依赖 Mono 对
//    FirstChanceException 的支持，已实测在本环境可靠触发——见本文件头"避免递归"判断记录的
//    验证方式同样适用），把当前用例窗口内出现的 LogType.Error/LogType.Exception 级别日志
//    连同当前用例全名一起打印。真正的 NUnit AssertionException 本身仍然不会流经这条路径
//    （断言失败由 NUnit 内部捕获并记录进 TestResult，不经过 Unity 的日志系统，这一点与
//    FirstChanceException 是否可用无关，是 NUnit/Unity Test Framework 的既有设计），
//    TestFinished 里额外打印 NUnit 记录的最终 ResultState/Message，可能仍是"最后一次"覆盖后
//    的结果（同本文件头背景所述），但配合"Started/Finished"边界，unity_test_triage.py 至少能
//    准确切出这段窗口、看到窗口内真实发生过的 Error/Exception 级日志，不再需要在日志里盲猜。
//
// 判断记录（为什么两个测试程序集（Runtime/Editor）各自都要有一份，不共享一个类型）：
// Adapter.Unity.Tests.Runtime 与 Adapter.Unity.Tests.Editor 是两个独立的 asmdef，互不引用
// （Editor 版的 includePlatforms 限定为 Editor、覆盖 EditMode，Runtime 版覆盖 PlayMode），
// 新增一个第三方共享程序集只为承载几十行诊断代码不值得；两份实现刻意保持逻辑一致（仅命名空间
// 不同），今后改动需要同步改两份，本文件与
// Tests/Editor/TestFirstChanceExceptionLogger.cs 互为镜像。
//
// 判断记录（只挑 LogType.Error/LogType.Exception，不含 Warning）：Warning 级日志在本仓库
// PlayMode 用例里大量出现且多数是预期内的（占位资源退化提示等，见真实 playmode.log 抽样），
// 全部打印只会淹没真正有价值的信息；Error/Exception 级别更可能对应真实问题（未处理的运行期
// 异常、Unity 自身报的错误），信噪比更高。
//
// 判断记录（避免递归/避免诊断代码自己成为新故障点）：日志回调内部只调用 Debug.Log（不是
// Error/Exception 级别，不会重新触发自身过滤条件，不构成递归），并整体包在 try/catch 内，
// 任何失败直接静默吞掉——诊断代码绝不能向外抛出新的异常，也不能让门禁因为诊断代码的缺陷而
// 多出一条新的失败原因。
using System;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(Adapter.Unity.Tests.Runtime.TestFirstChanceExceptionLogger))]

namespace Adapter.Unity.Tests.Runtime
{
    /// <summary>测试运行期"用例边界 + 窗口内 Error/Exception 日志"记录回调：<c>RunStarted</c>
    /// 订阅 <see cref="Application.logMessageReceivedThreaded"/>，<c>TestStarted</c>/
    /// <c>TestFinished</c> 打印用例边界（<c>[TestFirstChance] Started/Finished: ...</c>），
    /// 窗口内出现的 <see cref="LogType.Error"/>/<see cref="LogType.Exception"/> 级别日志额外
    /// 打印一条归属当前用例的记录（<c>[TestFirstChance] LogDuringTest: ...</c>）。
    /// <c>RunFinished</c> 时退订。与 toolchain/unity_test_triage.py 的窗口定位第一级
    /// （<c>test_first_chance</c>）配套，见该脚本文件头判断记录与本文件顶部判断记录（含
    /// AppDomain.FirstChanceException 方案已实测不可行的记录）。</summary>
    public sealed class TestFirstChanceExceptionLogger : ITestRunCallback
    {
        private static volatile string? s_CurrentTestFullName;

        public void RunStarted(ITest testsToRun)
        {
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
        }

        public void RunFinished(ITestResult testResults)
        {
            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
            s_CurrentTestFullName = null;
        }

        public void TestStarted(ITest test)
        {
            // 只关心叶子用例（实际的测试方法），跳过 fixture/assembly 级的容器节点——
            // ITestRunCallback 的 TestStarted/TestFinished 会在测试树每一层都触发一次
            // （assembly -> fixture -> method），只在方法这一层打边界，避免日志被容器层的
            // 冗余边界行淹没。
            if (test.IsSuite)
            {
                return;
            }

            s_CurrentTestFullName = test.FullName;
            SafeLog($"[TestFirstChance] Started: {test.FullName}");
        }

        public void TestFinished(ITestResult result)
        {
            if (result.Test.IsSuite)
            {
                return;
            }

            SafeLog($"[TestFirstChance] Finished: {result.FullName} result={result.ResultState} message={FirstLine(result.Message)}");

            if (s_CurrentTestFullName == result.FullName)
            {
                s_CurrentTestFullName = null;
            }
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            try
            {
                if (type != LogType.Error && type != LogType.Exception)
                {
                    return;
                }

                string? currentTest = s_CurrentTestFullName;
                if (currentTest == null)
                {
                    return;
                }

                Debug.Log($"[TestFirstChance] LogDuringTest: {currentTest}: {type}: {FirstLine(condition)}");
            }
            catch
            {
                // 见文件头"避免递归"判断记录：诊断代码本身的任何失败都静默吞掉。
            }
        }

        private static void SafeLog(string message)
        {
            try
            {
                Debug.Log(message);
            }
            catch
            {
                // 同上：诊断代码本身的任何失败都静默吞掉，不能让门禁因为这里出错而多一条失败原因。
            }
        }

        private static string FirstLine(string? message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            int newlineIndex = message.IndexOfAny(new[] { '\r', '\n' });
            return newlineIndex >= 0 ? message.Substring(0, newlineIndex) : message;
        }
    }
}
