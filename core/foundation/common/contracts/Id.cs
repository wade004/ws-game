using System;
using System.Text.RegularExpressions;

namespace Core.Foundation.Common
{
    /// <summary>
    /// 全架构统一的逻辑标识类型（见 00_架构总则.md 第 4.1 节、04_数据与内容管线.md 第 2.1 节）。
    /// 格式固定为小写点分的 "领域.名称"："^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$"，
    /// 例如 "skill.fireball"、"item.iron_sword"、"quest.find_the_missing_child"。
    /// 构造时校验格式，非法格式一律在构造期抛出 ArgumentException，不允许携带非法值流入运行时。
    /// </summary>
    public readonly struct Id : IEquatable<Id>, IComparable<Id>
    {
        private static readonly Regex FormatRegex = new Regex(
            "^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>原始字符串值，格式固定为 "领域.名称"（可含多段，如 "a.b.c"）。</summary>
        public string Value { get; }

        public Id(string value)
        {
            if (!IsValidFormat(value))
            {
                throw new ArgumentException(
                    $"Id 格式非法：\"{value ?? "<null>"}\"，应满足 ^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$（全小写点分领域.名称）",
                    nameof(value));
            }

            Value = value;
        }

        /// <summary>领域段：Value 中第一个 '.' 之前的部分。</summary>
        public string Domain
        {
            get
            {
                var dotIndex = Value.IndexOf('.');
                return dotIndex < 0 ? Value : Value.Substring(0, dotIndex);
            }
        }

        /// <summary>按格式规则解析；非法格式抛出 ArgumentException。</summary>
        public static Id Parse(string value) => new Id(value);

        /// <summary>按格式规则尝试解析；非法格式返回 false 且不抛异常。</summary>
        public static bool TryParse(string? value, out Id id)
        {
            if (IsValidFormat(value))
            {
                id = new Id(value!);
                return true;
            }

            id = default;
            return false;
        }

        /// <summary>仅校验格式，不构造实例。</summary>
        public static bool IsValidFormat(string? value)
        {
            return !string.IsNullOrEmpty(value) && FormatRegex.IsMatch(value);
        }

        public bool Equals(Id other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is Id other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? string.Empty;

        public int CompareTo(Id other) => string.CompareOrdinal(Value, other.Value);

        public static bool operator ==(Id left, Id right) => left.Equals(right);

        public static bool operator !=(Id left, Id right) => !left.Equals(right);
    }
}
