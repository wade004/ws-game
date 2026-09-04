using System;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// 模块内部的最小诊断出口：收集派发过程中的警告与错误（严格模式下遇到未登记事件、
    /// 类型化订阅遇到类型不匹配、超过 <see cref="EventBusOptions.MaxDispatchPasses"/>、
    /// 订阅者处理事件时抛出异常等，见本模块 README"诊断"一节）。
    ///
    /// 刻意不要求 <see cref="EventBus"/> 依赖 <c>IPlatform</c>（引擎适配层 L-1 接口）：
    /// event_bus 是全架构最基础的 L0 模块之一，若把 L-1 的具体接口类型直接写进
    /// <see cref="IEventBus"/>/<see cref="EventBus"/> 的必需依赖，会让"事件总线"这个最基础的
    /// 通道反过来要求调用方总是先备好一个引擎适配层实现才能用，抬高不必要的耦合；默认实现
    /// <see cref="InMemoryEventDiagnostics"/> 只把消息收集到内存列表，由调用方（测试、宿主）
    /// 决定如何进一步呈现或上报。若确实需要接到 <c>IPlatform</c>，见可选适配器
    /// <see cref="PlatformEventDiagnostics"/>（对接 <c>IPlatform.ReportCrash</c>，仅 Error 级别）。
    /// </summary>
    public interface IEventDiagnostics
    {
        /// <summary>记一条警告（不中止流程）。</summary>
        void Warn(string message);

        /// <summary>记一条错误，可选携带触发它的异常。</summary>
        void Error(string message, Exception? exception);
    }
}
