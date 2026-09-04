using System.Collections.Generic;

namespace Core.Numbers.PowerSet
{
    /// <summary>
    /// <see cref="IPowerDiagnostics"/> 的默认实现：把警告收集到内存列表，不依赖任何引擎
    /// 适配层接口（与 <c>InMemoryEventDiagnostics</c>、<c>InMemoryHookDiagnostics</c> 同一惯例）。
    /// </summary>
    public sealed class InMemoryPowerDiagnostics : IPowerDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message) => _warnings.Add(message);
    }
}
