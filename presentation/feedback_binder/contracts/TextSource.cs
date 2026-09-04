using System;
using Core.Foundation.Common;

namespace Presentation.FeedbackBinder.Contracts
{
    public enum TextSourceKind
    {
        /// <summary>取事件字段（<c>event.&lt;field&gt;</c>，经 <c>IExprReadableEvent.TryGetField</c>），
        /// 格式化为文本。</summary>
        Field,

        /// <summary>取一个本地化文本键（<c>l10n.text</c>），不做任何字段替换。</summary>
        Literal,

        /// <summary>专用简写：等价于 <c>field:amount</c>，但额外声明"这是一个数值，参与
        /// FeedbackOptions.NumberFormat 取整格式化与飘字合并"（见 09 第 6.2、6.3 节）。</summary>
        Amount,
    }

    /// <summary>
    /// <c>feedback.binding.actions[].params.text_source</c>（见 09_表现层.md 第 6.1 节
    /// <c>field:&lt;name&gt;|literal:&lt;text_key&gt;|amount</c>）的判别联合。
    /// </summary>
    public readonly struct TextSource : IEquatable<TextSource>
    {
        public TextSourceKind Kind { get; }

        /// <summary><see cref="TextSourceKind.Field"/> 的事件字段名；其余 Kind 为 null。</summary>
        public string? FieldName { get; }

        /// <summary><see cref="TextSourceKind.Literal"/> 的本地化文本键；其余 Kind 为 null。</summary>
        public Id? TextKey { get; }

        private TextSource(TextSourceKind kind, string? fieldName, Id? textKey)
        {
            Kind = kind;
            FieldName = fieldName;
            TextKey = textKey;
        }

        public static TextSource Field(string fieldName) =>
            new TextSource(TextSourceKind.Field, fieldName ?? throw new ArgumentNullException(nameof(fieldName)), null);

        public static TextSource Literal(Id textKey) => new TextSource(TextSourceKind.Literal, null, textKey);

        public static readonly TextSource Amount = new TextSource(TextSourceKind.Amount, "amount", null);

        /// <summary>解析数据表里的 <c>text_source</c> 文本（<c>"field:&lt;name&gt;"</c>/
        /// <c>"literal:&lt;text_key&gt;"</c>/<c>"amount"</c>），供 <c>FeedbackRule.FromRecord</c> 使用。</summary>
        public static TextSource Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("text_source 不能为空", nameof(text));
            }

            if (text == "amount")
            {
                return Amount;
            }

            if (text.StartsWith("field:", StringComparison.Ordinal))
            {
                return Field(text.Substring("field:".Length));
            }

            if (text.StartsWith("literal:", StringComparison.Ordinal))
            {
                return Literal(new Id(text.Substring("literal:".Length)));
            }

            throw new ArgumentException($"无法解析的 text_source：\"{text}\"（应为 field:<name>/literal:<text_key>/amount）", nameof(text));
        }

        public bool Equals(TextSource other) =>
            Kind == other.Kind && FieldName == other.FieldName && Nullable.Equals(TextKey, other.TextKey);

        public override bool Equals(object? obj) => obj is TextSource other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind;
                hash = (hash * 397) ^ (FieldName?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (TextKey?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
