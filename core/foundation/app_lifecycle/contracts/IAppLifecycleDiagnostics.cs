using System;

namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// 模块内部的最小诊断出口：收集非法转移尝试等信息（见本模块 README"诊断"一节）。
    /// 与 event_bus 的 <c>IEventDiagnostics</c>、hook_registry 的 <c>IHookDiagnostics</c>
    /// 同一惯例：刻意不要求依赖任何引擎适配层接口，默认实现
    /// <see cref="InMemoryAppLifecycleDiagnostics"/> 只把消息收集到内存列表。
    /// </summary>
    public interface IAppLifecycleDiagnostics
    {
        /// <summary>记一条警告（不中止流程），例如一次被拒绝的非法转移尝试。</summary>
        void Warn(string message);

        /// <summary>记一条错误，可选携带触发它的异常，用于不可恢复的内部一致性问题。</summary>
        void Error(string message, Exception? exception);
    }
}
