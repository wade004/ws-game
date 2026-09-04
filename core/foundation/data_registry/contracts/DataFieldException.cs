using System;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// <see cref="DataRecord"/> 的 <c>GetXxx</c> 类型化访问器在字段缺失或类型不符时抛出
    /// （见 11_工程规范与测试.md 第 4 节"运行时契约调用参数非法...调用方必须处理该分支"——
    /// 这里对应"调用方明知字段应当存在却读错类型"，属于编程错误而非正常数据缺失分支，
    /// 正常分支应使用 <c>TryGetXxx</c>）。
    /// </summary>
    public sealed class DataFieldException : Exception
    {
        public string Table { get; }

        public string RecordKey { get; }

        public string Field { get; }

        public DataFieldException(string table, string recordKey, string field, string message)
            : base($"表 \"{table}\" 记录 \"{recordKey}\" 字段 \"{field}\"：{message}")
        {
            Table = table;
            RecordKey = recordKey;
            Field = field;
        }
    }
}
