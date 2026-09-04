// StubPlatform：IPlatform 的最小可用桩实现。
// 用途：测试崩溃上报、剪贴板、语言切换等平台服务的调用是否发生，而不接触真实操作系统 API。
// 与真实实现的差异：崩溃日志写入内存列表（CrashLog）供测试断言，而非任何真实的日志系统；
// 剪贴板是进程内内存字符串；语言默认 "en"，可用 SetLanguage 覆盖。
using System.Collections.Generic;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubPlatform : IPlatform
    {
        private readonly List<string> _crashLog = new List<string>();
        private string? _clipboard;
        private string _language = "en";

        public IReadOnlyList<string> CrashLog => _crashLog;

        public string GetSystemLanguage() => _language;

        public string? GetClipboardText() => _clipboard;

        public void SetClipboardText(string text) => _clipboard = text;

        public void ReportCrash(string context, string details) => _crashLog.Add($"{context}: {details}");

        public string GetPlatformName() => "stub";

        /// <summary>测试用：设置 GetSystemLanguage() 的返回值。</summary>
        public void SetLanguage(string language) => _language = language;
    }
}
