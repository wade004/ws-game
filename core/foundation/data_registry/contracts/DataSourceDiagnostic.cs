using System;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 一条"数据源枚举/根访问执行期异常隔离"诊断记录（框架调用外部实现不做隔离系列第三条，
    /// 2026-09-19，见 <c>DataRegistry</c> 类型级判断记录"数据源枚举执行期异常隔离"、
    /// <c>core/foundation/data_registry/README.md</c> 同名一节）：某个 <see cref="IDataSource"/>
    /// 在本次 <see cref="IDataRegistry.LoadAll()"/>/<see cref="IDataRegistry.LoadAll(System.Collections.Generic.IReadOnlyList{IDataSource})"/>/
    /// <see cref="IDataRegistry.Reload(string)"/> 期间访问 <see cref="IDataSource.Root"/> 或调用
    /// <see cref="IDataSource.ListTables"/> 时抛出未预期异常——该数据源被整体跳过（其本应提供的全部表
    /// 本次未加载），其余数据源照常处理。纯诊断信息，不是 <see cref="ValidationIssue"/> 本身（对应的
    /// <c>data_source_unavailable</c> Error 级问题已写入 <see cref="ValidationReport.Issues"/>，本类型
    /// 只是同一次失败的结构化快照，供调用方以代码而非解析消息文本的方式判断"哪些数据源不可用"，见
    /// <see cref="IDataRegistry.IsDegraded"/>/<see cref="IDataRegistry.GetUnavailableSources"/>）。
    /// </summary>
    public sealed class DataSourceDiagnostic
    {
        /// <summary>该数据源在本次 <c>sources</c>/<c>_sources</c> 列表中的下标（"根序号"，语义同
        /// <see cref="OverrideDiagnostic.OverridingRootIndex"/>）。</summary>
        public int SourceIndex { get; }

        /// <summary>该数据源的最有辨识度的标识——能取到 <see cref="IDataSource.Root"/> 时用它；
        /// <see cref="IDataSource.Root"/> 访问本身也抛出、或该实现未提供（默认返回 <c>null</c>）时，
        /// 退化为该数据源实例的运行时类型名（<c>GetType().Name</c>），保证本字段恒非空。</summary>
        public string Identifier { get; }

        /// <summary>抛出的异常的类型名（<c>ex.GetType().Name</c>）。</summary>
        public string ExceptionType { get; }

        /// <summary>抛出的异常的 <see cref="Exception.Message"/>。</summary>
        public string ExceptionMessage { get; }

        public DataSourceDiagnostic(int sourceIndex, string identifier, string exceptionType, string exceptionMessage)
        {
            SourceIndex = sourceIndex;
            Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
            ExceptionType = exceptionType ?? throw new ArgumentNullException(nameof(exceptionType));
            ExceptionMessage = exceptionMessage ?? throw new ArgumentNullException(nameof(exceptionMessage));
        }

        public override string ToString() => $"根{SourceIndex}:{Identifier}（{ExceptionType}：{ExceptionMessage}）";
    }
}
