#nullable enable
// TestFirstChanceExceptionLogger（Editor/EditMode 镜像）：与
// Tests/Runtime/TestFirstChanceExceptionLogger.cs 逻辑完全一致，仅命名空间不同——两个测试
// 程序集（Adapter.Unity.Tests.Runtime 覆盖 PlayMode，Adapter.Unity.Tests.Editor 覆盖
// EditMode）是互不引用的独立 asmdef，新增一个第三方共享程序集只为承载几十行诊断代码不值得，
// 见 Runtime 版文件头判断记录（两份实现互为镜像，今后改动需要同步改两份）。完整背景、判断
// 记录（含最初按 AppDomain.FirstChanceException 实现、2026-09-15 实测证伪该方案在本仓库
// Unity 6000.3.23f1 Mono 运行时完全不触发、改为本文件现在的"用例边界 + Application.
// logMessageReceivedThreaded"设计的完整过程），均见 Runtime 版文件头注释与
// architecture/落地计划/排查复盘-2026-09-15-PlayMode-PRES180.md。
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(Adapter.Unity.Tests.Editor.TestFirstChanceExceptionLogger))]

namespace Adapter.Unity.Tests.Editor
{
    /// <summary>EditMode 版用例边界 + 窗口内 Error/Exception 日志记录回调，见 Runtime 版
    /// <see cref="Adapter.Unity.Tests.Runtime.TestFirstChanceExceptionLogger"/> 的 XML 文档与
    /// 本文件头判断记录。</summary>
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
                // 见 Runtime 版文件头"避免递归"判断记录：诊断代码本身的任何失败都静默吞掉。
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
