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
    /// 实现决定，如文件路径）、取文本的委托（延迟到 <see cref="DataRegistry.LoadAll()"/> 真正需要时
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
    }
}
