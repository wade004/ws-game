namespace Core.Foundation.Rng
{
    /// <summary>
    /// xoshiro256** 伪随机数生成器（David Blackman &amp; Sebastiano Vigna 公开发布的算法，
    /// 原始实现见 https://prng.di.unimi.it/xoshiro256starstar.c ，已进入公有领域）。
    /// 256 位内部状态（4 个 ulong），周期 2^256-1，全架构确定性模拟场景下用作单条随机流的底层
    /// 生成器；不依赖任何系统随机源、时间或线程，纯函数式状态推进，同一初始状态永远产出同一序列。
    /// </summary>
    internal sealed class Xoshiro256StarStar
    {
        private ulong _s0;
        private ulong _s1;
        private ulong _s2;
        private ulong _s3;

        public Xoshiro256StarStar(RngStreamState state)
        {
            SetState(state);
        }

        /// <summary>读出当前内部状态，供 <see cref="RngHost.GetStreamState"/> 使用。</summary>
        public RngStreamState GetState() => new RngStreamState(_s0, _s1, _s2, _s3);

        /// <summary>整体替换内部状态，供 <see cref="RngHost.SetStreamState"/> 使用。</summary>
        public void SetState(RngStreamState state)
        {
            _s0 = state.S0;
            _s1 = state.S1;
            _s2 = state.S2;
            _s3 = state.S3;
        }

        /// <summary>推进一步内部状态并产出下一个 64 位随机数。</summary>
        public ulong Next()
        {
            // 算法固定为四步：取当前 s1 做"**"变换得到本次输出 → 用 s1 派生临时量 t →
            // 按固定异或/移位组合更新 s0..s3 → 对 s3 做旋转。顺序与位移量均为算法定义，不可更改。
            var result = RotateLeft(_s1 * 5, 7) * 9;

            var t = _s1 << 17;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;

            _s2 ^= t;

            _s3 = RotateLeft(_s3, 45);

            return result;
        }

        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));
    }
}
