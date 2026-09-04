namespace Core.Foundation.Localization
{
    /// <summary>
    /// 模块内部的最小诊断出口（与 hook_registry 的 <c>IHookDiagnostics</c>、input_map 的
    /// <c>IInputMapDiagnostics</c> 同一惯例）：记录缺失文本键、缺失变量等警告，不依赖任何
    /// 引擎适配层接口。
    /// </summary>
    public interface IL10nDiagnostics
    {
        void Warn(string message);
    }
}
