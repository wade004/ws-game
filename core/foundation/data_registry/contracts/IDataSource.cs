using System;
using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>供 <see cref="DataTableSource.ReadText"/> 延迟读取一张表的原始 JSON 文本；
    /// 抛异常表示读取失败（如文件不存在），由 <see cref="DataRegistry"/> 转换成一条
    /// <c>envelope</c> 校验错误，不让异常直接冒泡给调用方。</summary>
    public delegate string TextProvider();

    /// <summary>
    /// 一张待加载表的定位信息：表名、来源位置（供错误信息定位，含义由具体 <see cref="IDataSource"/>
    /// 实现决定，如文件路径）、取文本的委托（延迟到 <c>DataRegistry.LoadAll</c> 真正需要时
    /// 才读取，避免一次性把全部表读进内存）。
    /// </summary>
    public sealed class DataTableSource
    {
        public string TableName { get; }

        public string Location { get; }

        public TextProvider ReadText { get; }

        public DataTableSource(string tableName, string location, TextProvider readText)
        {
            if (string.IsNullOrEmpty(tableName)) throw new ArgumentException("表名不能为空", nameof(tableName));
            TableName = tableName;
            Location = location ?? throw new ArgumentNullException(nameof(location));
            ReadText = readText ?? throw new ArgumentNullException(nameof(readText));
        }
    }

    /// <summary>
    /// 数据来源契约（见 04 第 4 节 DataRegistry 接口周边"数据从哪里来"）：只负责列出全部候选表
    /// 及其取文本方式，不负责解析/校验——那些是 <see cref="DataRegistry"/> 的职责。
    /// </summary>
    public interface IDataSource
    {
        IReadOnlyList<DataTableSource> ListTables();

        /// <summary>
        /// 消费方反馈第三批第 22 条（2026-09-10，见
        /// architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md 第 22 条）：本数据源自身的
        /// "根"标识——当且仅当它是 <see cref="DataTableSource.Location"/> 的前缀时，
        /// <c>Core.Foundation.DataRegistry.DataRegistry</c> 用它裁出相对路径（见
        /// <see cref="OverrideDiagnostic.OverridingRelativePath"/>）。带默认实现（恒返回
        /// <c>null</c>，表示"不提供根标识，退化为用完整 <see cref="DataTableSource.Location"/>
        /// 本身作为相对路径"）新增，不要求已有 <see cref="IDataSource"/> 实现方必须提供，不构成
        /// "公开 API 表面"意义上的破坏性变更。<see cref="FileSystemDataSource"/> 显式覆盖为构造时
        /// 传入的根目录。</summary>
        string? Root => null;
    }
}
