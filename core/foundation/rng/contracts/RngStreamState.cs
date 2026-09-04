using System;
using System.Globalization;

namespace Core.Foundation.Rng
{
    /// <summary>
    /// 单条随机流的内部状态快照（xoshiro256** 的 256 位状态，见 rng/README.md 设计要点）。
    /// 值类型、可直接相等比较；<see cref="ToString"/>/<see cref="Parse"/>/<see cref="TryParse"/>
    /// 提供稳定的十六进制文本形式，供存档系统序列化到
    /// 10_存档与持久化.md 第 2.4 节 rng 段的 <c>stream_states</c> 字段。
    /// </summary>
    public readonly struct RngStreamState : IEquatable<RngStreamState>
    {
        /// <summary>文本形式的段分隔符。</summary>
        private const char Separator = '-';

        public ulong S0 { get; }
        public ulong S1 { get; }
        public ulong S2 { get; }
        public ulong S3 { get; }

        public RngStreamState(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }

        /// <summary>稳定文本形式："s0-s1-s2-s3"，每段为 16 位十六进制、小写、定长补零。</summary>
        public override string ToString()
        {
            // "x16" 十六进制格式不受区域文化影响（无千分位、固定小写字母 a-f），无需显式传入 CultureInfo。
            return S0.ToString("x16") + Separator + S1.ToString("x16") + Separator +
                   S2.ToString("x16") + Separator + S3.ToString("x16");
        }

        /// <summary>按 <see cref="ToString"/> 的文本形式解析；非法格式抛出 <see cref="FormatException"/>。</summary>
        public static RngStreamState Parse(string text)
        {
            if (!TryParse(text, out var state))
            {
                throw new FormatException(
                    $"RngStreamState 文本格式非法：\"{text ?? "<null>"}\"，应为 \"s0-s1-s2-s3\"" +
                    "（4 段 16 位十六进制、以 '-' 分隔）");
            }

            return state;
        }

        /// <summary>按 <see cref="ToString"/> 的文本形式尝试解析；非法格式返回 false 且不抛异常。</summary>
        public static bool TryParse(string? text, out RngStreamState state)
        {
            state = default;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var parts = text.Split(Separator);
            if (parts.Length != 4)
            {
                return false;
            }

            if (!TryParseSegment(parts[0], out var s0) ||
                !TryParseSegment(parts[1], out var s1) ||
                !TryParseSegment(parts[2], out var s2) ||
                !TryParseSegment(parts[3], out var s3))
            {
                return false;
            }

            state = new RngStreamState(s0, s1, s2, s3);
            return true;
        }

        private static bool TryParseSegment(string segment, out ulong value)
        {
            return ulong.TryParse(
                segment,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out value);
        }

        public bool Equals(RngStreamState other)
        {
            return S0 == other.S0 && S1 == other.S1 && S2 == other.S2 && S3 == other.S3;
        }

        public override bool Equals(object? obj) => obj is RngStreamState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + S0.GetHashCode();
                hash = hash * 31 + S1.GetHashCode();
                hash = hash * 31 + S2.GetHashCode();
                hash = hash * 31 + S3.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(RngStreamState left, RngStreamState right) => left.Equals(right);

        public static bool operator !=(RngStreamState left, RngStreamState right) => !left.Equals(right);
    }
}
