using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.Rng
{
    /// <summary>
    /// <see cref="IRngHost"/> 的默认实现：按 <see cref="Id"/> 分流懒创建 xoshiro256** 生成器
    /// （见 rng/README.md 设计要点），每条流的初始状态由 <see cref="SeedDerivation"/> 从主种子
    /// 与流 <see cref="Id"/> 确定性派生。无锁、无线程、不读取任何系统随机源或系统时间。
    /// </summary>
    public sealed class RngHost : IRngHost
    {
        // SortedDictionary 按 Id 的 IComparable(序数比较) 顺序维护键，Streams 属性据此天然有序。
        private readonly SortedDictionary<Id, Xoshiro256StarStar> _streams = new SortedDictionary<Id, Xoshiro256StarStar>();

        private ulong _masterSeed;

        public RngHost(ulong masterSeed)
        {
            _masterSeed = masterSeed;
        }

        public double Next(Id stream)
        {
            var generator = GetOrCreateStream(stream);
            var x = generator.Next();

            // 取高 53 位构造 [0,1) 区间的 double：53 位是 double 尾数精度上限，
            // 保证均匀分布且不会因舍入产生 1.0 边界值。
            return (x >> 11) * (1.0 / (1UL << 53));
        }

        public int NextInt(Id stream, int min, int max)
        {
            if (min > max)
            {
                throw new ArgumentException(
                    $"min({min}) 不能大于 max({max})：NextInt 要求闭区间 [min, max] 合法", nameof(min));
            }

            if (min == max)
            {
                // 单点区间不消耗随机数，行为对调用方透明且天然确定。
                return min;
            }

            var generator = GetOrCreateStream(stream);

            // 区间元素个数：[min, max] 闭区间共 (max - min + 1) 个整数；用 long 中间量避免
            // (max - min) 在 int 运算下溢出，结果转 ulong 时最大为 2^32，安全落入 ulong 范围。
            var range = (ulong)((long)max - (long)min) + 1UL;
            var offset = NextBoundedUlong(generator, range);

            return (int)((long)min + (long)offset);
        }

        public RngStreamState GetStreamState(Id stream)
        {
            var generator = GetOrCreateStream(stream);
            return generator.GetState();
        }

        public void SetStreamState(Id stream, RngStreamState state)
        {
            if (_streams.TryGetValue(stream, out var generator))
            {
                generator.SetState(state);
            }
            else
            {
                _streams[stream] = new Xoshiro256StarStar(state);
            }
        }

        public IReadOnlyList<Id> Streams => new List<Id>(_streams.Keys);

        public ulong MasterSeed => _masterSeed;

        public void Reset(ulong masterSeed)
        {
            _streams.Clear();
            _masterSeed = masterSeed;
        }

        private Xoshiro256StarStar GetOrCreateStream(Id stream)
        {
            if (_streams.TryGetValue(stream, out var generator))
            {
                return generator;
            }

            var initialState = SeedDerivation.DeriveInitialState(_masterSeed, stream);
            generator = new Xoshiro256StarStar(initialState);
            _streams[stream] = generator;
            return generator;
        }

        /// <summary>
        /// 在 [0, <paramref name="exclusiveBound"/>) 区间内产出无偏随机整数：用拒绝采样丢弃会
        /// 导致取模结果分布不均的"多余尾部"随机值，避免 <c>value % range</c> 直接取模的偏差。
        /// </summary>
        private static ulong NextBoundedUlong(Xoshiro256StarStar generator, ulong exclusiveBound)
        {
            // t = 2^64 mod exclusiveBound（用无符号回绕减法计算，等价于 (0 - exclusiveBound) mod 2^64，
            // 再对 exclusiveBound 取模）：小于 t 的抽样落在"不完整分片"里，会被拒绝重新抽取。
            var rejectionThreshold = unchecked(0UL - exclusiveBound) % exclusiveBound;

            ulong candidate;
            do
            {
                candidate = generator.Next();
            } while (candidate < rejectionThreshold);

            return candidate % exclusiveBound;
        }
    }
}
