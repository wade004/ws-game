using System;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// Expr 求值期诊断出口（见 04 第 6.4 节）：引用对象暂缺按默认值处理并记一条警告；
    /// 宿主 <c>Query</c> 内部异常记一条错误，整个表达式判定为 <c>false</c>，不向调用方抛出。
    /// </summary>
    public interface IExprDiagnostics
    {
        void Warn(string message);

        void Error(string message, Exception? exception = null);
    }
}
