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

        /// <summary>由 <paramref name="masterSeed"/> 与 <paramref name="stream"/> 派生该流的初始状态。</summary>
        public static RngStreamState DeriveInitialState(ulong masterSeed, Id stream)
        {
            var seed = masterSeed ^ Fnv1a64(stream.Value);

            var smState = seed;
            var s0 = SplitMix64(ref smState);
            var s1 = SplitMix64(ref smState);
            var s2 = SplitMix64(ref smState);
            var s3 = SplitMix64(ref smState);

            // 判断记录（收口遗留修复 A8，删除全零回退分支）：xoshiro256** 的全零状态是不动点，需要避开；但此处
            // 数学上不可达，故不再保留"全零则替换 s0"的死分支。SplitMix64 的终结变换是 64 位上的双射且
            // 0 ↦ 0，所以某次输出为 0 当且仅当其推进后的内部状态恰为 0；四次输出对应的内部状态是
            // seed+g、seed+2g、seed+3g、seed+4g（g 为奇数常量，mod 2^64 下相邻两项必不相等），
            // 四个值不可能同时为 0，因此 (s0,s1,s2,s3) 恒不全零。万一将来改了派生算法导致全零，
            // RngStreamState 构造函数会抛 ArgumentException，不会静默产出坏流。
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
