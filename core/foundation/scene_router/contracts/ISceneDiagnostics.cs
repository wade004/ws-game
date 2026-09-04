using System;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// 模块内部的最小诊断出口（与 hook_registry 的 <c>IHookDiagnostics</c>、event_bus 的
    /// <c>IEventDiagnostics</c> 同一惯例）：记录加载失败等场景路由自身关注的诊断信息，
    /// 不要求依赖任何引擎适配层接口。默认实现 <see cref="InMemorySceneDiagnostics"/> 只把
    /// 消息收集到内存列表。
    /// </summary>
    public interface ISceneDiagnostics
    {
        /// <summary>记一条警告（不中止流程）。</summary>
        void Warn(string message);

        /// <summary>记一条错误，可选携带触发它的异常。</summary>
        void Error(string message, Exception? exception);
    }
}
