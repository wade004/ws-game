using System.Collections.Generic;

namespace Core.Gameplay.ProgressionBridge
{
    /// <summary>默认 <see cref="IProgressionBridgeDiagnostics"/> 实现：把警告收进内存列表，不依赖
    /// 任何引擎适配层接口（惯例同 <c>InMemoryRewardDiagnostics</c>/<c>InMemoryWorldStateDiagnostics</c>），
    /// 供 <c>adapters/unity</c> 侧统一诊断转发机制（ADR-0042）轮询、也供测试直接断言。</summary>
    public sealed class InMemoryProgressionBridgeDiagnostics : IProgressionBridgeDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message) => _warnings.Add(message);
    }
}
