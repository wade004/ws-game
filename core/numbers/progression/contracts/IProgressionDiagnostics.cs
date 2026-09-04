namespace Core.Numbers.Progression
{
    /// <summary>
    /// 模块内部的最小诊断出口（与 L0 event_bus 的 <c>IEventDiagnostics</c>、localization 的
    /// <c>IL10nDiagnostics</c> 同一惯例）：记录满级后经验被丢弃等警告，不依赖任何引擎适配层
    /// 接口、不抛异常中止流程。
    /// </summary>
    public interface IProgressionDiagnostics
    {
        void Warn(string message);
    }
}
