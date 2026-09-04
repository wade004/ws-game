using System;
using System.Collections.Generic;

namespace Core.Foundation.Expr
{
    /// <summary>记录一条求值期错误：文本消息与可选的原始异常。</summary>
    public readonly struct ExprDiagnosticError
    {
        public string Message { get; }

        public Exception? Exception { get; }

        public ExprDiagnosticError(string message, Exception? exception)
        {
            Message = message;
            Exception = exception;
        }

        public override string ToString() => Message;
    }

    /// <summary>
    /// <see cref="IExprDiagnostics"/> 的内存实现：把警告与错误各自累积到一个列表，
    /// 供测试与调用方在求值结束后检查。
    /// </summary>
    public sealed class ExprDiagnosticsRecorder : IExprDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();
        private readonly List<ExprDiagnosticError> _errors = new List<ExprDiagnosticError>();

        public IReadOnlyList<string> Warnings => _warnings;

        public IReadOnlyList<ExprDiagnosticError> Errors => _errors;

        public bool HasErrors => _errors.Count > 0;

        public void Warn(string message) => _warnings.Add(message);

        public void Error(string message, Exception? exception = null) => _errors.Add(new ExprDiagnosticError(message, exception));
    }
}
