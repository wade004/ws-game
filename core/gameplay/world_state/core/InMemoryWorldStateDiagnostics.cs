using System.Collections.Generic;

namespace Core.Gameplay.WorldState
{
    /// <summary>
    /// <see cref="IWorldStateDiagnostics"/> 的默认实现：把警告收集到内存列表，不依赖任何引擎
    /// 适配层接口（与 <c>InMemoryCombatDiagnostics</c>/<c>InMemoryPowerDiagnostics</c>/
    /// <c>InMemoryEventDiagnostics</c> 同一惯例）。
    /// </summary>
    public sealed class InMemoryWorldStateDiagnostics : IWorldStateDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message) => _warnings.Add(message);
    }
}
