using System;
using System.Collections.Generic;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// <see cref="IEventDiagnostics"/> 的默认实现：把警告/错误收集到内存列表，
    /// 不依赖任何引擎适配层接口。供测试断言与不需要接入宿主日志系统的场景使用。
    /// </summary>
    public sealed class InMemoryEventDiagnostics : IEventDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();
        private readonly List<EventDiagnosticsErrorRecord> _errors = new List<EventDiagnosticsErrorRecord>();

        public IReadOnlyList<string> Warnings => _warnings;

        public IReadOnlyList<EventDiagnosticsErrorRecord> Errors => _errors;

        public void Warn(string message) => _warnings.Add(message);

        public void Error(string message, Exception? exception) =>
            _errors.Add(new EventDiagnosticsErrorRecord(message, exception));
    }

    /// <summary>单条错误记录：消息与可选的触发异常。</summary>
    public readonly struct EventDiagnosticsErrorRecord
    {
        public string Message { get; }

        public Exception? Exception { get; }

        public EventDiagnosticsErrorRecord(string message, Exception? exception)
        {
            Message = message;
            Exception = exception;
        }
    }
}
