namespace Core.Foundation.Common.Json
{
    /// <summary>
    /// <see cref="JsonWriter.Write"/> 的格式化选项。默认值产出人类可读、2 空格缩进、
    /// LF 换行、非 ASCII 字符原样输出（不转义成 <c>\uXXXX</c>）的文本，匹配
    /// <c>data/README.md</c>"编码与格式"一节（UTF-8 无 BOM、缩进 2 空格、行尾 LF）。
    /// </summary>
    public sealed class JsonWriterOptions
    {
        /// <summary>每级缩进的空格数；0 表示不缩进（各 token 之间仍以单个空格分隔，见 <see cref="JsonWriter"/>）。</summary>
        public int Indent { get; set; } = 2;

        /// <summary>换行符文本，默认 <c>"\n"</c>（LF，不用 CRLF，呼应 data/README.md 编码约定）。</summary>
        public string NewLine { get; set; } = "\n";

        /// <summary>是否把非 ASCII 字符转义成 <c>\uXXXX</c>；默认 false（原样输出 UTF-8 字符）。</summary>
        public bool EscapeNonAscii { get; set; }

        public static readonly JsonWriterOptions Default = new JsonWriterOptions();
    }
}
