using System;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// 模块内部的最小诊断出口：收集存档读写过程中的警告与错误（备份回退、损坏文件跳过、
    /// 缺失段跳过、迁移失败等，见本模块 README"诊断"一节）。与 event_bus 的
    /// <c>IEventDiagnostics</c>、hook_registry 的 <c>IHookDiagnostics</c> 同一惯例：刻意不
    /// 要求依赖任何引擎适配层接口，默认实现 <see cref="InMemorySaveDiagnostics"/> 只把消息
    /// 收集到内存列表。
    /// </summary>
    public interface ISaveDiagnostics
    {
        /// <summary>记一条警告（不中止流程）。</summary>
        void Warn(string message);

        /// <summary>记一条错误，可选携带触发它的异常。</summary>
        void Error(string message, Exception? exception);
    }
}
