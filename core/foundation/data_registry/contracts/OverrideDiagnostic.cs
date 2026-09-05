using System;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 一条"多根合并时发生了行覆盖"的诊断记录（框架数据行覆盖语义任务新增，见
    /// <c>DataRegistry</c> 类型级判断记录"覆盖语义"、<c>data/README.md</c>"多根加载与合并规则"）。
    /// 纯诊断信息，不是 <see cref="ValidationIssue"/>（覆盖成功不算警告也不算错误，见该判断记录
    /// "为什么不进 ValidationReport.Issues"）；只能通过 <see cref="IDataRegistry.GetOverrideDiagnostics"/>
    /// 读取。
    /// </summary>
    public readonly struct OverrideDiagnostic
    {
        /// <summary>发生覆盖的表名。</summary>
        public string Table { get; }

        /// <summary>被覆盖/覆盖的那一行的主键（<see cref="DataRecord.Key"/>）。</summary>
        public string RecordKey { get; }

        /// <summary>声明 <c>"override": true</c> 并最终胜出、留在合并结果里的那一行来自哪个数据根
        /// （<see cref="DataTableSource.Location"/>）。</summary>
        public string OverridingLocation { get; }

        /// <summary>被替换掉的那一行来自哪个数据根。</summary>
        public string OverriddenLocation { get; }

        public OverrideDiagnostic(string table, string recordKey, string overridingLocation, string overriddenLocation)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            RecordKey = recordKey ?? throw new ArgumentNullException(nameof(recordKey));
            OverridingLocation = overridingLocation ?? throw new ArgumentNullException(nameof(overridingLocation));
            OverriddenLocation = overriddenLocation ?? throw new ArgumentNullException(nameof(overriddenLocation));
        }

        public override string ToString() =>
            $"{Table}[{RecordKey}]: \"{OverridingLocation}\" 覆盖 \"{OverriddenLocation}\"";
    }
}
