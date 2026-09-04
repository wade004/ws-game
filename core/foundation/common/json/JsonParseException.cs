using System;

namespace Core.Foundation.Common.Json
{
    /// <summary>
    /// <see cref="JsonReader.Parse"/> 解析失败时抛出的异常：语法错误、非法转义、未闭合字符串/
    /// 容器、尾随逗号、注释、NaN/Infinity、多余内容、嵌套深度超限、重复键等。
    /// </summary>
    public sealed class JsonParseException : Exception
    {
        /// <summary>出错所在行号，从 1 起。</summary>
        public int Line { get; }

        /// <summary>出错所在列号（同一行内的字符序号），从 1 起。</summary>
        public int Column { get; }

        public JsonParseException(int line, int column, string message)
            : base($"第 {line} 行第 {column} 列：{message}")
        {
            Line = line;
            Column = column;
        }
    }
}
