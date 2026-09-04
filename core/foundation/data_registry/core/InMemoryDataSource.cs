using System;
using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// <see cref="IDataSource"/> 的内存实现：测试用，直接持有一组"表名 → JSON 文本"，不接触
    /// 任何文件系统。<see cref="Add"/> 支持链式调用，便于测试一次性搭好一组表。
    /// </summary>
    public sealed class InMemoryDataSource : IDataSource
    {
        private readonly List<DataTableSource> _tables = new List<DataTableSource>();

        /// <summary>登记一张表；<paramref name="location"/> 省略时用 <c>"memory://{tableName}"</c>
        /// 作为定位信息（只用于错误消息展示，不影响加载逻辑）。</summary>
        public InMemoryDataSource Add(string tableName, string jsonText, string? location = null)
        {
            if (string.IsNullOrEmpty(tableName)) throw new ArgumentException("表名不能为空", nameof(tableName));
            if (jsonText == null) throw new ArgumentNullException(nameof(jsonText));

            var loc = location ?? $"memory://{tableName}";
            _tables.Add(new DataTableSource(tableName, loc, () => jsonText));
            return this;
        }

        public IReadOnlyList<DataTableSource> ListTables() => _tables;
    }
}
