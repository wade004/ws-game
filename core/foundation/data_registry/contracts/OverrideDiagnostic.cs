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

        /// <summary>
        /// 消费方反馈第三批第 22 条（2026-09-10，见
        /// architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md 第 22 条）：胜出行所在
        /// 数据根在本次 <see cref="IDataRegistry.LoadAll(System.Collections.Generic.IReadOnlyList{IDataSource})"/>
        /// 传入的 <c>sources</c> 列表中的下标（0 基，"根序号"，与 <c>toolchain/validator</c>
        /// <c>--data-root</c> 重复参数的声明顺序一致）；<c>-1</c> 表示未知（旧构造重载、或加载期
        /// 无法确定根序号的场景，如单根 <see cref="IDataRegistry.LoadAll()"/> 不产生覆盖诊断——
        /// 覆盖只可能发生在多根合并时）。</summary>
        public int OverridingRootIndex { get; }

        /// <summary>被替换行所在数据根的根序号，语义同 <see cref="OverridingRootIndex"/>。</summary>
        public int OverriddenRootIndex { get; }

        /// <summary>
        /// 消费方反馈第三批第 22 条：胜出行相对其所在数据根的路径（把 <see cref="OverridingLocation"/>
        /// 开头与该根自身路径重合的部分裁掉）——多根加载时绝对路径通常很长且各根前缀不同，人类
        /// 阅读/跨机器对比时相对路径更直观。数据根本身未暴露可裁剪前缀（如 <see cref="IDataSource"/>
        /// 的默认实现，见该接口 <c>Root</c> 成员判断记录）时，退化为与 <see cref="OverridingLocation"/>
        /// 相同的值——不是"缺失"，只是裁剪不出更短的相对形式。</summary>
        public string OverridingRelativePath { get; }

        /// <summary>被替换行相对其所在数据根的路径，语义同 <see cref="OverridingRelativePath"/>。</summary>
        public string OverriddenRelativePath { get; }

        public OverrideDiagnostic(string table, string recordKey, string overridingLocation, string overriddenLocation)
            : this(table, recordKey, overridingLocation, overriddenLocation, -1, overridingLocation, -1, overriddenLocation)
        {
        }

        /// <summary>消费方反馈第三批第 22 条新增的完整构造：绝对路径字段保留（<paramref name="overridingLocation"/>/
        /// <paramref name="overriddenLocation"/>），额外带上根序号与相对路径。既有四参数构造保持不变
        /// （新增字段落在默认值：根序号 <c>-1</c>，相对路径退化为绝对路径本身）。</summary>
        public OverrideDiagnostic(
            string table, string recordKey,
            string overridingLocation, string overriddenLocation,
            int overridingRootIndex, string overridingRelativePath,
            int overriddenRootIndex, string overriddenRelativePath)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            RecordKey = recordKey ?? throw new ArgumentNullException(nameof(recordKey));
            OverridingLocation = overridingLocation ?? throw new ArgumentNullException(nameof(overridingLocation));
            OverriddenLocation = overriddenLocation ?? throw new ArgumentNullException(nameof(overriddenLocation));
            OverridingRootIndex = overridingRootIndex;
            OverridingRelativePath = overridingRelativePath ?? throw new ArgumentNullException(nameof(overridingRelativePath));
            OverriddenRootIndex = overriddenRootIndex;
            OverriddenRelativePath = overriddenRelativePath ?? throw new ArgumentNullException(nameof(overriddenRelativePath));
        }

        public override string ToString() =>
            $"{Table}[{RecordKey}]: \"根{OverridingRootIndex}:{OverridingRelativePath}\" 覆盖 \"根{OverriddenRootIndex}:{OverriddenRelativePath}\"";
    }
}
