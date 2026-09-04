namespace Presentation.Ui
{
    /// <summary>
    /// UI 框架自用的最小诊断出口（惯例同仓库其它模块的 <c>I*Diagnostics</c>，例如
    /// <c>Core.Rules.Skill.ISkillDiagnostics</c>）：本模块不依赖任何具体日志/控制台实现，
    /// 只约定"记一条警告"这一最小能力，供 <see cref="IUiDataSource.Query"/> 遇到未知/非法路径时
    /// 记一条诊断（见 09_表现层.md 第 7.2 节、任务书"未知路径返回 null 并记诊断一次"）。
    /// </summary>
    public interface IUiDiagnostics
    {
        void Warn(string message);
    }

    /// <summary>
    /// 内存态默认实现：只把消息追加进一个列表，供测试断言、也供没有接入具体日志系统的调用方
    /// 兜底使用（惯例同 <c>Core.Rules.Skill.InMemorySkillDiagnostics</c>）。
    /// </summary>
    public sealed class InMemoryUiDiagnostics : IUiDiagnostics
    {
        private readonly System.Collections.Generic.List<string> _warnings = new System.Collections.Generic.List<string>();

        public System.Collections.Generic.IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message) => _warnings.Add(message ?? string.Empty);
    }
}
