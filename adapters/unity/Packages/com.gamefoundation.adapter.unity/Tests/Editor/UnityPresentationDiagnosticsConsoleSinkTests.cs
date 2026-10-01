#nullable enable
// UnityPresentationDiagnosticsConsoleSinkTests：测试覆盖第四批 T-L15/T-M47——UnityPresentationDiagnosticsConsoleSink
// 是诊断转发链路“最后一步真正写到引擎控制台”的唯一一行胶水（类型头判断记录：恒用 Debug.LogWarning，不用
// LogError，否则 Unity Test Framework 会把未预期的 LogError 判成用例失败）。dotnet test 侧只能用录制 sink
// 验证去抖/开关逻辑，这一层必须在真实引擎里确认：
//  ① Warn(message) 确实产出一条 LogType.Warning、文本原样；
//  ② 经 DiagnosticsHub 转发时，warnings 与 errors 两路最终都落成 Warning（没有任何一路升级成 Error）；
//  ③ Instance 是单例且就是 IPresentationDiagnosticsConsoleSink。
// 失败模式的判定靠 Unity Test Framework 自身：若实现误用 Debug.LogError，下面任何一条用例都会因
// “Unhandled log message”而失败，无需额外断言。
using Adapter.Unity.Diagnostics;
using Adapter.Unity.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class UnityPresentationDiagnosticsConsoleSinkTests
    {
        [Test]
        public void Instance_IsSingleton_AndImplementsTheConsoleSinkContract()
        {
            Assert.AreSame(UnityPresentationDiagnosticsConsoleSink.Instance, UnityPresentationDiagnosticsConsoleSink.Instance);
            Assert.IsInstanceOf<IPresentationDiagnosticsConsoleSink>(UnityPresentationDiagnosticsConsoleSink.Instance);
        }

        [Test]
        public void Warn_WritesExactlyOneWarningWithTheMessageVerbatim()
        {
            const string message = "[Probe.Source] 探针消息 with 'quotes' and 中文";
            LogAssert.Expect(LogType.Warning, message);

            UnityPresentationDiagnosticsConsoleSink.Instance.Warn(message);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Warn_CalledTwiceWithSameText_WritesTwoWarnings_NoDeduplicationAtThisLayer()
        {
            // 去重/上限是调用方 PresentationDiagnosticsConsoleGate 的职责（dotnet test 侧已覆盖），本层不做。
            const string message = "[Probe.Repeat] same text";
            LogAssert.Expect(LogType.Warning, message);
            LogAssert.Expect(LogType.Warning, message);

            UnityPresentationDiagnosticsConsoleSink.Instance.Warn(message);
            UnityPresentationDiagnosticsConsoleSink.Instance.Warn(message);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ViaDiagnosticsHub_WarningsAndErrorsBothBecomeEngineWarnings_NeverErrors()
        {
            var warnings = new System.Collections.Generic.List<string> { "w-one" };
            var errors = new System.Collections.Generic.List<string> { "e-one" };
            var hub = new DiagnosticsHub(UnityPresentationDiagnosticsConsoleSink.Instance);
            hub.Register("Probe.Hub", warnings, errors);

            LogAssert.Expect(LogType.Warning, "[Probe.Hub] w-one");
            LogAssert.Expect(LogType.Warning, "[Probe.Hub][error] e-one");

            hub.Pump();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ViaDiagnosticsHub_ExceptionAwareErrors_AlsoBecomeEngineWarnings_WithTypeNameInText()
        {
            var warnings = new System.Collections.Generic.List<string>();
            var errors = new System.Collections.Generic.List<(string Message, System.Exception? Exception)>
            {
                ("boom-message", new System.InvalidOperationException("inner-detail")),
            };
            var hub = new DiagnosticsHub(UnityPresentationDiagnosticsConsoleSink.Instance);
            hub.Register("Probe.ExcHub", warnings, ExceptionAwareErrorProjection.From(errors, e => e.Message, e => e.Exception));

            // 带异常的条目文本含类型全名与消息（堆栈因未真正抛出而为空），用正则匹配头部即可。
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                @"^\[Probe\.ExcHub\]\[error\] boom-message —— System\.InvalidOperationException: inner-detail"));

            hub.Pump();

            LogAssert.NoUnexpectedReceived();
        }
    }
}
