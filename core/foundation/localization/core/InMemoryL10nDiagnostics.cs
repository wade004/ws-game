using System.Collections.Generic;

namespace Core.Foundation.Localization
{
    /// <summary>
    /// <see cref="IL10nDiagnostics"/> 的默认实现：把警告收集到内存列表，不依赖任何引擎适配层
    /// 接口（与 hook_registry 的 <c>InMemoryHookDiagnostics</c> 同一惯例）。
    /// </summary>
    public sealed class InMemoryL10nDiagnostics : IL10nDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message) => _warnings.Add(message);
    }
}
