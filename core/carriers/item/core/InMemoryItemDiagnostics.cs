using System.Collections.Generic;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <see cref="IItemDiagnostics"/> 的默认实现：把警告收集到内存列表，不依赖任何引擎适配层接口
    /// （与 <c>InMemoryPowerDiagnostics</c>/<c>InMemoryEventDiagnostics</c> 同一惯例）。
    /// </summary>
    public sealed class InMemoryItemDiagnostics : IItemDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message) => _warnings.Add(message);
    }
}
