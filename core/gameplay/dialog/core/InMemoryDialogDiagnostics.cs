using System.Collections.Generic;

namespace Core.Gameplay.Dialog
{
    /// <summary>默认 <see cref="IDialogDiagnostics"/> 实现：把警告收进内存列表，供测试断言。</summary>
    public sealed class InMemoryDialogDiagnostics : IDialogDiagnostics
    {
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message) => _warnings.Add(message);
    }
}
