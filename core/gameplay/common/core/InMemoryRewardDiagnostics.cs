using System.Collections.Generic;

namespace Core.Gameplay.Common
{
    /// <summary>默认 <see cref="IRewardDiagnostics"/> 实现：把警告收进内存列表，供测试断言
    /// （惯例同 <c>core/gameplay/world_state</c> 的 <c>InMemoryWorldStateDiagnostics</c>）。</summary>
    public sealed class InMemoryRewardDiagnostics : IRewardDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message) => _warnings.Add(message);
    }
}
