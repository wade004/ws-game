using System;
using System.Collections.Generic;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// <see cref="ISceneDiagnostics"/> 的默认实现：把警告/错误收集到内存列表，不依赖任何
    /// 引擎适配层接口（与 hook_registry 的 <c>InMemoryHookDiagnostics</c> 同一惯例）。
    /// </summary>
    public sealed class InMemorySceneDiagnostics : ISceneDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();
        private readonly List<SceneDiagnosticsErrorRecord> _errors = new List<SceneDiagnosticsErrorRecord>();

        public IReadOnlyList<string> Warnings => _warnings;

        public IReadOnlyList<SceneDiagnosticsErrorRecord> Errors => _errors;

        public void Warn(string message) => _warnings.Add(message);

        public void Error(string message, Exception? exception) =>
            _errors.Add(new SceneDiagnosticsErrorRecord(message, exception));
    }

    /// <summary>单条错误记录：消息与可选的触发异常。</summary>
    public readonly struct SceneDiagnosticsErrorRecord
    {
        public string Message { get; }

        public Exception? Exception { get; }

        public SceneDiagnosticsErrorRecord(string message, Exception? exception)
        {
            Message = message;
            Exception = exception;
        }
    }
}
