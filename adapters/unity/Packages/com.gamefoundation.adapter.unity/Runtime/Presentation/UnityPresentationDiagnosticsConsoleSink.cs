#nullable enable
// PresentationDiagnosticsConsoleForwarding.cs 的唯一"薄胶水"部分：把纯逻辑侧
// IPresentationDiagnosticsConsoleSink 接到真正的引擎控制台。除了这一处 UnityEngine.Debug 调用，
// 本文件不包含任何去重/上限/开关逻辑（全部在同目录 PresentationDiagnosticsConsoleForwarding.cs，
// 已在 dotnet test 侧验证）。
//
// 判断记录（级别映射，Warning 而不是 Error）：见 PresentationDiagnosticsConsoleForwarding.cs 顶部
// 判断记录——恒用 Debug.LogWarning，不使用 Debug.LogError（Unity Test Framework 会让未预期的
// LogError 直接判 PlayMode 测试失败，288 条用例里多条会命中资源缺失路径）。待主会话跑 Unity 引擎
// 门禁确认：本文件引用 UnityEngine.Debug，无法用 dotnet test 验证，只能在真实 Unity 批处理测试
// （EditMode/PlayMode）里确认调用不抛异常、确实写入了 Unity 控制台/日志文件。
using UnityEngine;

namespace Adapter.Unity.Presentation
{
    /// <summary><see cref="IPresentationDiagnosticsConsoleSink"/> 的 Unity 实现：直接转发
    /// <see cref="Debug.LogWarning(object)"/>。不做任何去重/开关判断——这些已经在调用方
    /// （<see cref="PresentationDiagnosticsConsoleGate"/>）完成，本类型只负责"最后一步真正写到
    /// 控制台"。</summary>
    public sealed class UnityPresentationDiagnosticsConsoleSink : IPresentationDiagnosticsConsoleSink
    {
        public static readonly UnityPresentationDiagnosticsConsoleSink Instance = new UnityPresentationDiagnosticsConsoleSink();

        public void Warn(string message) => Debug.LogWarning(message);
    }
}
