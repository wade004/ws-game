using System;
using System.Collections.Generic;

namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// <see cref="IAppLifecycleDiagnostics"/> 的默认实现：把警告/错误收集到内存列表，
    /// 不依赖任何引擎适配层接口（与 event_bus 的 <c>InMemoryEventDiagnostics</c>、
    /// hook_registry 的 <c>InMemoryHookDiagnostics</c> 同一惯例）。
    /// </summary>
    public sealed class InMemoryAppLifecycleDiagnostics : IAppLifecycleDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();
        private readonly List<AppLifecycleDiagnosticsErrorRecord> _errors = new List<AppLifecycleDiagnosticsErrorRecord>();

        public IReadOnlyList<string> Warnings => _warnings;

        public IReadOnlyList<AppLifecycleDiagnosticsErrorRecord> Errors => _errors;

        public void Warn(string message) => _warnings.Add(message);

        public void Error(string message, Exception? exception) =>
            _errors.Add(new AppLifecycleDiagnosticsErrorRecord(message, exception));
    }

    /// <summary>单条错误记录：消息与可选的触发异常。</summary>
    public readonly struct AppLifecycleDiagnosticsErrorRecord
    {
        public string Message { get; }

        public Exception? Exception { get; }

        public AppLifecycleDiagnosticsErrorRecord(string message, Exception? exception)
        {
            Message = message;
            Exception = exception;
        }
    }
}
