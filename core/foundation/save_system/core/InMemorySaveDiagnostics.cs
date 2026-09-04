using System;
using System.Collections.Generic;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// <see cref="ISaveDiagnostics"/> 的默认实现：把警告/错误收集到内存列表，不依赖任何
    /// 引擎适配层接口。供测试断言与不需要接入宿主日志系统的场景使用（与 hook_registry 的
    /// <c>InMemoryHookDiagnostics</c> 同一惯例）。
    /// </summary>
    public sealed class InMemorySaveDiagnostics : ISaveDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();
        private readonly List<SaveDiagnosticsErrorRecord> _errors = new List<SaveDiagnosticsErrorRecord>();

        public IReadOnlyList<string> Warnings => _warnings;

        public IReadOnlyList<SaveDiagnosticsErrorRecord> Errors => _errors;

        public void Warn(string message) => _warnings.Add(message);

        public void Error(string message, Exception? exception) =>
            _errors.Add(new SaveDiagnosticsErrorRecord(message, exception));
    }

    /// <summary>单条错误记录：消息与可选的触发异常。</summary>
    public readonly struct SaveDiagnosticsErrorRecord
    {
        public string Message { get; }

        public Exception? Exception { get; }

        public SaveDiagnosticsErrorRecord(string message, Exception? exception)
        {
            Message = message;
            Exception = exception;
        }
    }
}
