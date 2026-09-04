using System.Text;
using Core.Foundation.Common;

namespace Core.Foundation.Rng
{
    /// <summary>
    /// 由主种子与流 <see cref="Id"/> 派生某条随机流初始状态的确定性算法（见 rng/README.md
    /// 设计要点）。只依赖入参本身，不使用 <c>string.GetHashCode()</c>（.NET Core 下逐进程随机化，
    /// 会破坏"同种子同结果"的确定性要求）、不使用任何系统随机/时间源。
    /// </summary>
    internal static class SeedDerivation
    {
        // SplitMix64 的固定黄金比例增量常量（Vigna 公开算法的标准取值）。
        private const ulong SplitMix64Increment = 0x9E3779B97F4A7C15UL;

        // FNV-1a 64 位的标准偏移基与素数。
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        // 全零状态的兜底替换值：xoshiro256** 若初始状态全零会永远困在全零输出，需要避免。
        private const ulong ZeroStateFallback = 0x9E3779B97F4A7C15UL;

        /// <summary>由 <paramref name="masterSeed"/> 与 <paramref name="stream"/> 派生该流的初始状态。</summary>
        public static RngStreamState DeriveInitialState(ulong masterSeed, Id stream)
        {
            var seed = masterSeed ^ Fnv1a64(stream.Value);

            var smState = seed;
            var s0 = SplitMix64(ref smState);
            var s1 = SplitMix64(ref smState);
            var s2 = SplitMix64(ref smState);
            var s3 = SplitMix64(ref smState);

            if (s0 == 0 && s1 == 0 && s2 == 0 && s3 == 0)
            {
                s0 = ZeroStateFallback;
            }

            return new RngStreamState(s0, s1, s2, s3);
        }

        /// <summary>SplitMix64：以 <paramref name="state"/> 为种子状态（原地推进），产出下一个 64 位值。</summary>
        private static ulong SplitMix64(ref ulong state)
        {
            state += SplitMix64Increment;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>FNV-1a 64 位哈希，作用于 <paramref name="value"/> 的 UTF-8 字节序列。</summary>
        private static ulong Fnv1a64(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var hash = FnvOffsetBasis;
            for (var i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= FnvPrime;
            }

            return hash;
        }
    }
}
