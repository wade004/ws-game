using System;
using System.Collections.Generic;
using System.Globalization;

namespace Core.Foundation.Feel
{
    /// <summary>手感值的种类。</summary>
    public enum FeelValueKind
    {
        /// <summary>无值（可选字段未设置）。</summary>
        None,

        /// <summary>数值（<c>Number</c>/<c>Int</c> 字段统一用双精度承载，整数字段取整数值）。</summary>
        Number,

        /// <summary>布尔。</summary>
        Bool,

        /// <summary>文本（枚举取值、Id、曲线引用等）。</summary>
        Text,

        /// <summary>文本列表（列表字段，整体替换，显式 <c>remove</c> 删元素）。</summary>
        List,
    }

    /// <summary>
    /// 手感字段的取值（不可变值类型）：解析器的输入输出统一用它承载，避免装箱与字典枚举顺序依赖。
    /// 相等比较：同种类且内容逐位相同（数值按 <c>double.Equals</c>，列表逐元素有序比较）。
    /// </summary>
    public readonly struct FeelValue : IEquatable<FeelValue>
    {
        private readonly double _number;
        private readonly string? _text;
        private readonly IReadOnlyList<string>? _list;

        public FeelValueKind Kind { get; }

        private FeelValue(FeelValueKind kind, double number, string? text, IReadOnlyList<string>? list)
        {
            Kind = kind;
            _number = number;
            _text = text;
            _list = list;
        }

        public static FeelValue None => default;

        public static FeelValue Of(double number)
        {
            if (!double.IsFinite(number)) throw new ArgumentException("手感数值必须是有限数", nameof(number));
            return new FeelValue(FeelValueKind.Number, number, null, null);
        }

        public static FeelValue Of(bool value) => new FeelValue(FeelValueKind.Bool, value ? 1.0 : 0.0, null, null);

        public static FeelValue Of(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return new FeelValue(FeelValueKind.Text, 0.0, text, null);
        }

        public static FeelValue OfList(IReadOnlyList<string> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var copy = new string[items.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = items[i] ?? throw new ArgumentException("列表元素不能为 null", nameof(items));
            }
            return new FeelValue(FeelValueKind.List, 0.0, null, copy);
        }

        public bool IsNone => Kind == FeelValueKind.None;

        public double AsNumber()
        {
            if (Kind != FeelValueKind.Number) throw new InvalidOperationException($"手感值不是数值（实际 {Kind}）");
            return _number;
        }

        public bool AsBool()
        {
            if (Kind != FeelValueKind.Bool) throw new InvalidOperationException($"手感值不是布尔（实际 {Kind}）");
            return _number != 0.0;
        }

        public string AsText()
        {
            if (Kind != FeelValueKind.Text) throw new InvalidOperationException($"手感值不是文本（实际 {Kind}）");
            return _text!;
        }

        public IReadOnlyList<string> AsList()
        {
            if (Kind != FeelValueKind.List) throw new InvalidOperationException($"手感值不是列表（实际 {Kind}）");
            return _list!;
        }

        public bool Equals(FeelValue other)
        {
            if (Kind != other.Kind) return false;
            switch (Kind)
            {
                case FeelValueKind.None: return true;
                case FeelValueKind.Number:
                case FeelValueKind.Bool: return _number.Equals(other._number);
                case FeelValueKind.Text: return string.Equals(_text, other._text, StringComparison.Ordinal);
                case FeelValueKind.List:
                    if (_list!.Count != other._list!.Count) return false;
                    for (var i = 0; i < _list.Count; i++)
                    {
                        if (!string.Equals(_list[i], other._list[i], StringComparison.Ordinal)) return false;
                    }
                    return true;
                default: return false;
            }
        }

        public override bool Equals(object? obj) => obj is FeelValue other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = (int)Kind;
                switch (Kind)
                {
                    case FeelValueKind.Number:
                    case FeelValueKind.Bool: h = (h * 397) ^ _number.GetHashCode(); break;
                    case FeelValueKind.Text: h = (h * 397) ^ StringComparer.Ordinal.GetHashCode(_text!); break;
                    case FeelValueKind.List:
                        for (var i = 0; i < _list!.Count; i++) h = (h * 397) ^ StringComparer.Ordinal.GetHashCode(_list[i]);
                        break;
                }
                return h;
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case FeelValueKind.None: return "none";
                case FeelValueKind.Number: return _number.ToString("R", CultureInfo.InvariantCulture);
                case FeelValueKind.Bool: return _number != 0.0 ? "true" : "false";
                case FeelValueKind.Text: return _text!;
                case FeelValueKind.List: return "[" + string.Join(",", _list!) + "]";
                default: return "?";
            }
        }

        public static bool operator ==(FeelValue left, FeelValue right) => left.Equals(right);

        public static bool operator !=(FeelValue left, FeelValue right) => !left.Equals(right);
    }
}
