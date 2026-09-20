using System.Collections.Generic;

namespace Core.Carriers.Creature
{
    /// <summary><see cref="ICreatureDiagnostics"/> 的默认实现：把警告/错误各自收集到内存列表（惯例同
    /// <c>core/carriers/gobj</c> 的 <c>InMemoryGobjDiagnostics</c>）。</summary>
    public sealed class InMemoryCreatureDiagnostics : ICreatureDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _errors = new List<string>();

        public IReadOnlyList<string> Warnings => _warnings;

        public IReadOnlyList<string> Errors => _errors;

        public void Warn(string message) => _warnings.Add(message);

        public void Error(string message) => _errors.Add(message);
    }
}
