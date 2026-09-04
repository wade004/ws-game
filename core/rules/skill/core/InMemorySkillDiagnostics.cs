using System.Collections.Generic;

namespace Core.Rules.Skill
{
    /// <summary><see cref="ISkillDiagnostics"/> 的默认实现：把警告/错误各自收集到内存列表。</summary>
    public sealed class InMemorySkillDiagnostics : ISkillDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _errors = new List<string>();

        public IReadOnlyList<string> Warnings => _warnings;

        public IReadOnlyList<string> Errors => _errors;

        public void Warn(string message) => _warnings.Add(message);

        public void Error(string message) => _errors.Add(message);
    }
}
