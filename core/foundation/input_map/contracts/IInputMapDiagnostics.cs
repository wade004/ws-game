using System;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 模块内部的最小诊断出口（与 hook_registry 的 <c>IHookDiagnostics</c>、event_bus 的
    /// <c>IEventDiagnostics</c> 同一惯例）：记录重绑定冲突等警告，不依赖任何引擎适配层接口。
    /// </summary>
    public interface IInputMapDiagnostics
    {
        void Warn(string message);
    }
}
