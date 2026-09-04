using System;
using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>严重级别：<see cref="Error"/> 一律阻断（见 04 第 5 节"任何一项失败即视为本次
    /// 内容变更不可合入"）；<see cref="Warning"/> 是否阻断取决于
    /// <see cref="DataRegistryOptions.Strictness"/>。</summary>
    public enum ValidationSeverity
    {
        Warning,
        Error,
    }

    /// <summary>
    /// 一条校验问题：严重级别 + 定位信息（表/记录主键/字段，后两者可选）+ 检查项名
    /// （固定取值集合见 04 第 5 节：<c>envelope</c>、<c>schema_version</c>、<c>primary_key</c>、
    /// <c>required_field</c>、<c>field_type</c>、<c>reference_integrity</c>、<c>text_key_exists</c>、
    /// <c>expr_parsable</c>，以及 <see cref="IValidationRule"/> 扩展点自行命名的检查项）+ 消息。
    /// </summary>
    public readonly struct ValidationIssue
    {
        public ValidationSeverity Severity { get; }

        public string Table { get; }

        public string? RecordKey { get; }

        public string? Field { get; }

        public string Check { get; }

        public string Message { get; }

        public ValidationIssue(
            ValidationSeverity severity,
            string table,
            string check,
            string message,
            string? recordKey = null,
            string? field = null)
        {
            Severity = severity;
            Table = table ?? throw new ArgumentNullException(nameof(table));
            Check = check ?? throw new ArgumentNullException(nameof(check));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            RecordKey = recordKey;
            Field = field;
        }

        public override string ToString()
        {
            var loc = RecordKey == null ? Table : (Field == null ? $"{Table}[{RecordKey}]" : $"{Table}[{RecordKey}].{Field}");
            return $"[{Severity}] {Check} {loc}: {Message}";
        }
    }

    /// <summary>
    /// 一次加载/校验的完整报告（见 04 第 4 节 <c>loadAll()</c>/<c>validate()</c> 返回值）。
    /// <see cref="IsBlocking"/> 由构造时传入的严格级别与 <see cref="ErrorCount"/>/
    /// <see cref="WarningCount"/> 计算：存在 Error 一律阻断；<see cref="DataRegistryStrictness.WarningsBlock"/>
    /// 下存在 Warning 也阻断。
    /// </summary>
    public sealed class ValidationReport
    {
        public IReadOnlyList<ValidationIssue> Issues { get; }

        public int ErrorCount { get; }

        public int WarningCount { get; }

        public bool IsBlocking { get; }

        public ValidationReport(IReadOnlyList<ValidationIssue> issues, DataRegistryStrictness strictness)
        {
            Issues = issues ?? throw new ArgumentNullException(nameof(issues));

            int errors = 0, warnings = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == ValidationSeverity.Error) errors++;
                else warnings++;
            }
            ErrorCount = errors;
            WarningCount = warnings;
            IsBlocking = errors > 0 || (strictness == DataRegistryStrictness.WarningsBlock && warnings > 0);
        }
    }
}
