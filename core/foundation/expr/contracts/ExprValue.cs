using System;
using System.Globalization;
using Core.Foundation.Common;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// Expr 求值结果的判别联合：Bool | Int(long) | Number(double) | String | Id（见 04 第 6.3 节）。
    /// 只读值类型，通过静态工厂构造，按 <see cref="Kind"/> 访问对应的 As* 属性；
    /// 访问与 Kind 不符的 As* 属性抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    public readonly struct ExprValue : IEquatable<ExprValue>
    {
        public ExprValueKind Kind { get; }

        private readonly bool _boolValue;
        private readonly long _intValue;
        private readonly double _numberValue;
        private readonly string? _stringValue;
        private readonly Id _idValue;

        private ExprValue(ExprValueKind kind, bool boolValue, long intValue, double numberValue, string? stringValue, Id idValue)
        {
            Kind = kind;
            _boolValue = boolValue;
            _intValue = intValue;
            _numberValue = numberValue;
            _stringValue = stringValue;
            _idValue = idValue;
        }

        public static ExprValue OfBool(bool value) => new ExprValue(ExprValueKind.Bool, value, 0, 0, null, default);

        public static ExprValue OfInt(long value) => new ExprValue(ExprValueKind.Int, false, value, 0, null, default);

        public static ExprValue OfNumber(double value) => new ExprValue(ExprValueKind.Number, false, 0, value, null, default);

        public static ExprValue OfString(string value) =>
            new ExprValue(ExprValueKind.String, false, 0, 0, value ?? throw new ArgumentNullException(nameof(value)), default);

        public static ExprValue OfId(Id value) => new ExprValue(ExprValueKind.Id, false, 0, 0, null, value);

        public bool AsBool => Kind == ExprValueKind.Bool
            ? _boolValue
            : throw new InvalidOperationException($"ExprValue 不是 Bool：实际为 {Kind}");

        public long AsInt => Kind == ExprValueKind.Int
            ? _intValue
            : throw new InvalidOperationException($"ExprValue 不是 Int：实际为 {Kind}");

        public double AsNumber => Kind == ExprValueKind.Number
            ? _numberValue
            : throw new InvalidOperationException($"ExprValue 不是 Number：实际为 {Kind}");

        public string AsString => Kind == ExprValueKind.String
            ? _stringValue!
            : throw new InvalidOperationException($"ExprValue 不是 String：实际为 {Kind}");

        public Id AsId => Kind == ExprValueKind.Id
            ? _idValue
            : throw new InvalidOperationException($"ExprValue 不是 Id：实际为 {Kind}");

        /// <summary>是否为数值类型（Int 或 Number）——二者互相可比较，见 04 第 6.3 节。</summary>
        public bool IsNumeric => Kind == ExprValueKind.Int || Kind == ExprValueKind.Number;

        /// <summary>Int/Number 统一提升为 double；其余类型抛异常。</summary>
        public double ToDouble()
        {
            switch (Kind)
            {
                case ExprValueKind.Int: return _intValue;
                case ExprValueKind.Number: return _numberValue;
                default: throw new InvalidOperationException($"ExprValue 不是数值类型：实际为 {Kind}");
            }
        }

        public bool Equals(ExprValue other)
        {
            if (Kind != other.Kind) return false;

            switch (Kind)
            {
                case ExprValueKind.Bool: return _boolValue == other._boolValue;
                case ExprValueKind.Int: return _intValue == other._intValue;
                case ExprValueKind.Number: return _numberValue.Equals(other._numberValue);
                case ExprValueKind.String: return string.Equals(_stringValue, other._stringValue, StringComparison.Ordinal);
                case ExprValueKind.Id: return _idValue.Equals(other._idValue);
                default: return false;
            }
        }

        public override bool Equals(object? obj) => obj is ExprValue other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                switch (Kind)
                {
                    case ExprValueKind.Bool: return (hash * 397) ^ _boolValue.GetHashCode();
                    case ExprValueKind.Int: return (hash * 397) ^ _intValue.GetHashCode();
                    case ExprValueKind.Number: return (hash * 397) ^ _numberValue.GetHashCode();
                    case ExprValueKind.String: return (hash * 397) ^ (_stringValue == null ? 0 : StringComparer.Ordinal.GetHashCode(_stringValue));
                    case ExprValueKind.Id: return (hash * 397) ^ _idValue.GetHashCode();
                    default: return hash;
                }
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case ExprValueKind.Bool: return _boolValue ? "true" : "false";
                case ExprValueKind.Int: return _intValue.ToString(CultureInfo.InvariantCulture);
                case ExprValueKind.Number:
                {
                    var s = _numberValue.ToString("R", CultureInfo.InvariantCulture);
                    if (s.IndexOf('.') < 0 && s.IndexOf('e') < 0 && s.IndexOf('E') < 0)
                    {
                        s += ".0";
                    }
                    return s;
                }
                case ExprValueKind.String: return _stringValue ?? string.Empty;
                case ExprValueKind.Id: return _idValue.ToString();
                default: return string.Empty;
            }
        }

        public static bool operator ==(ExprValue left, ExprValue right) => left.Equals(right);

        public static bool operator !=(ExprValue left, ExprValue right) => !left.Equals(right);
    }
}
