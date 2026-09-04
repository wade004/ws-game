using System;

namespace Core.Foundation.HookRegistry
{
    /// <summary>
    /// 模块内部的最小诊断出口：收集回调执行过程中的警告与错误（未声明挂载点、
    /// AllowMultiple=false 时重复注册、回调抛出异常被隔离等，见本模块 README"诊断"一节）。
    /// 与 event_bus 的 <c>IEventDiagnostics</c> 同一惯例：刻意不要求依赖任何引擎适配层接口，
    /// 默认实现 <see cref="InMemoryHookDiagnostics"/> 只把消息收集到内存列表。
    /// </summary>
    public interface IHookDiagnostics
    {
        /// <summary>记一条警告（不中止流程）。</summary>
        void Warn(string message);

        /// <summary>记一条错误，可选携带触发它的异常。</summary>
        void Error(string message, Exception? exception);
    }
}
