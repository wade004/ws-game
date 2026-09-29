using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// ADR-0110：导航网格的布局（原点、格宽、格数）。二维网格导航实现（真实网格实现与测试桩）在
    /// "最近可走点"搜索里必须按**同一份**格子几何量化距离、枚举候选格心，否则两个实现对同一张阻挡
    /// 图会给出不同的结果——本类型是这份几何的唯一定义点：<see cref="Compute{T}"/> 由一张地图登记的
    /// 阻挡矩形并集包围盒推出网格（无阻挡时用 <c>(-8,-8)..(8,8)</c> 缺省包围盒），外扩
    /// <see cref="BoundsMargin"/>，格宽取 <c>baseCellSize</c>，包围盒装不进
    /// <see cref="MaxGridDimension"/>×<see cref="MaxGridDimension"/> 格时按比例放大格宽。
    /// 真实网格实现构建自己的 A* 网格时同样经本方法（不另抄一份公式）。
    /// </summary>
    public readonly struct NavGridLayout
    {
        /// <summary>缺省格宽（世界单位）。</summary>
        public const double DefaultCellSize = 0.25;

        /// <summary>单轴格数上限，包围盒超出时按比例放大格宽。</summary>
        public const int MaxGridDimension = 192;

        /// <summary>阻挡包围盒外扩的边距（世界单位）。</summary>
        public const double BoundsMargin = 2.0;

        public Vec2 Origin { get; }

        public double CellSize { get; }

        public int Width { get; }

        public int Height { get; }

        public NavGridLayout(Vec2 origin, double cellSize, int width, int height)
        {
            Origin = origin;
            CellSize = cellSize;
            Width = width;
            Height = height;
        }

        /// <summary>格 <paramref name="ix"/>,<paramref name="iy"/> 的中心点。</summary>
        public Vec2 CellCenter(int ix, int iy) =>
            new Vec2(Origin.X + (ix + 0.5) * CellSize, Origin.Y + (iy + 0.5) * CellSize);

        /// <summary>
        /// 由阻挡矩形并集推出网格布局（见类型注释）。<paramref name="blockers"/> 可为空。
        /// </summary>
        public static NavGridLayout Compute<T>(
            IReadOnlyList<T>? blockers, Func<T, Vec2> min, Func<T, Vec2> max, double baseCellSize = DefaultCellSize)
        {
            var lo = new Vec2(-8, -8);
            var hi = new Vec2(8, 8);

            if (blockers != null && blockers.Count > 0)
            {
                lo = min(blockers[0]);
                hi = max(blockers[0]);
                for (var i = 0; i < blockers.Count; i++)
                {
                    var bMin = min(blockers[i]);
                    var bMax = max(blockers[i]);
                    lo = new Vec2(Math.Min(lo.X, bMin.X), Math.Min(lo.Y, bMin.Y));
                    hi = new Vec2(Math.Max(hi.X, bMax.X), Math.Max(hi.Y, bMax.Y));
                }
            }

            lo = new Vec2(lo.X - BoundsMargin, lo.Y - BoundsMargin);
            hi = new Vec2(hi.X + BoundsMargin, hi.Y + BoundsMargin);

            var spanX = Math.Max(hi.X - lo.X, baseCellSize);
            var spanY = Math.Max(hi.Y - lo.Y, baseCellSize);
            var cellSize = baseCellSize;

            var width = (int)Math.Ceiling(spanX / cellSize);
            var height = (int)Math.Ceiling(spanY / cellSize);
            if (width > MaxGridDimension || height > MaxGridDimension)
            {
                var scale = Math.Max((double)width / MaxGridDimension, (double)height / MaxGridDimension);
                cellSize *= scale;
                width = (int)Math.Ceiling(spanX / cellSize);
                height = (int)Math.Ceiling(spanY / cellSize);
            }

            return new NavGridLayout(lo, cellSize, Math.Max(width, 1), Math.Max(height, 1));
        }

        /// <summary>矩形阻挡列表的便捷重载（<see cref="Rect.Min"/>/<see cref="Rect.Max"/>）。</summary>
        public static NavGridLayout Compute(IReadOnlyList<Rect>? blockers, double baseCellSize = DefaultCellSize) =>
            Compute(blockers, r => r.Min, r => r.Max, baseCellSize);
    }

    /// <summary>
    /// ADR-0110《导航契约新增"最近可走点"》：<see cref="INavigation2D.FindNearestWalkableCandidates"/>
    /// 的排序规则与搜索实现的**唯一一份**——默认接口实现、测试桩、真实网格实现都只是把各自的
    /// "格子是否可走"谓词交给本类型，排序规则不在任何别处重复。
    /// <para>
    /// 排序规则（写死）：<b>主键</b> = 候选到 <c>point</c> 的距离按格宽量化（<c>floor(d / 格宽)</c>，
    /// 同一量化档视为并列）；<b>次键</b> = 候选到 <c>preferNear</c> 的距离（小者优先，按 1e-9 量化
    /// 以吸收浮点噪声）；仍并列按坐标字典序（先 X 后 Y，小者优先）。<c>point</c> 本身可走时它排在
    /// 第一位（主键 -1）。候选集合限定在以 <c>point</c> 为圆心、<c>maxRadius</c> 为半径的圆内。
    /// </para>
    /// </summary>
    public static class NearestWalkableSearch
    {
        /// <summary>默认接口实现的同心环采样步长（世界单位），取与 <see cref="NavGridLayout.DefaultCellSize"/>
        /// 相同的值，使主键量化档与缺省网格一致。</summary>
        public const double SampledStep = NavGridLayout.DefaultCellSize;

        /// <summary>默认接口实现每一环至少采样的方向数（环半径 r 上实际方向数
        /// <c>max(MinSampledDirections, ceil(2πr / SampledStep))</c>，方向按 <c>2πj/n</c> 从 +X 轴起逆时针）。</summary>
        public const int MinSampledDirections = 8;

        /// <summary>默认接口实现最多采样的环数（<c>maxRadius</c> 过大或为无穷时的上限，即最远
        /// <c>MaxSampledRings × SampledStep</c> = 256 世界单位）。</summary>
        public const int MaxSampledRings = 1024;

        /// <summary><see cref="INavigation2D.TryFindNearestReachable"/> 默认实现最多试探的候选个数：候选按
        /// 排序规则从近到远逐个 <see cref="INavigation2D.FindPath"/>，试到第 64 个仍不可达就放弃（近似，
        /// 见该成员注释）。</summary>
        internal const int ReachableProbeLimit = 64;

        private const double QuantizeEpsilon = 1e-9;
        private const double PreferQuantum = 1e-9;

        private readonly struct Candidate
        {
            public readonly long Primary;
            public readonly long Secondary;
            public readonly Vec2 Point;

            public Candidate(long primary, long secondary, Vec2 point)
            {
                Primary = primary;
                Secondary = secondary;
                Point = point;
            }
        }

        /// <summary>排序规则的唯一实现（见类型注释）。</summary>
        private static int Compare(Candidate a, Candidate b)
        {
            var c = a.Primary.CompareTo(b.Primary);
            if (c != 0) return c;
            c = a.Secondary.CompareTo(b.Secondary);
            if (c != 0) return c;
            c = a.Point.X.CompareTo(b.Point.X);
            if (c != 0) return c;
            return a.Point.Y.CompareTo(b.Point.Y);
        }

        private static Candidate MakeCandidate(Vec2 point, Vec2 candidate, Vec2 preferNear, double cellSize)
        {
            var primary = (long)Math.Floor(Vec2.Distance(point, candidate) / cellSize + QuantizeEpsilon);
            var secondary = (long)Math.Round(Vec2.Distance(candidate, preferNear) / PreferQuantum);
            return new Candidate(primary, secondary, candidate);
        }

        /// <summary>把浮点格坐标区间 [lo, hi] 夹取为 [0, size-1] 的整数区间；与网格完全不相交时返回 false
        /// （先在浮点域夹取再转 int，避免极端坐标转换溢出）。</summary>
        private static bool TryClampRange(double lo, double hi, int size, out int first, out int last)
        {
            var a = Math.Floor(lo);
            var b = Math.Floor(hi);
            if (double.IsNaN(a) || double.IsNaN(b) || b < 0 || a > size - 1)
            {
                first = 0;
                last = -1;
                return false;
            }

            first = (int)Math.Max(0, a);
            last = (int)Math.Min(size - 1, b);
            return true;
        }

        private static bool IsFinite(Vec2 v) =>
            !double.IsNaN(v.X) && !double.IsInfinity(v.X) && !double.IsNaN(v.Y) && !double.IsInfinity(v.Y);

        private static void Flush(List<Candidate> pool, int maxCount, List<Vec2> results)
        {
            if (pool.Count > 1)
            {
                pool.Sort(Compare);
            }

            for (var i = 0; i < pool.Count && results.Count < maxCount; i++)
            {
                results.Add(pool[i].Point);
            }
        }

        /// <summary>
        /// 按网格精确搜索：候选 = <paramref name="layout"/> 内各格的中心点（<paramref name="cellQualifies"/>
        /// 为真者），距离 <paramref name="point"/> 不超过 <paramref name="maxRadius"/>。"qualifies"由调用方定义：
        /// 可走（<see cref="INavigation2D.FindNearestWalkableCandidates"/>），或可走且与某点连通
        /// （<see cref="INavigation2D.TryFindNearestReachable"/>）——排序规则与候选限定无关，只有这一份。
        /// <paramref name="pointItselfQualifies"/> 为真时 <paramref name="point"/> 本身排第一（原样返回，
        /// 不量化到格心）。<paramref name="maxCount"/> 为 1 时先算候选的排序键、只在它还能超过当前最优时才调用
        /// <paramref name="cellQualifies"/>（谓词可以很贵，例如一次寻路，调用次数因此只与"刷新最优"的次数
        /// 相关，而不是候选格总数）；<paramref name="maxCount"/> 大于 1 时对范围内全部格调用。把至多
        /// <paramref name="maxCount"/> 个候选按排序规则依次追加到 <paramref name="results"/>（先清空），
        /// 返回追加个数。
        /// </summary>
        public static int CollectOnGrid(
            NavGridLayout layout, Func<int, int, bool> cellQualifies, Vec2 point, bool pointItselfQualifies,
            double maxRadius, Vec2 preferNear, int maxCount, List<Vec2> results)
        {
            results.Clear();
            if (maxCount <= 0 || !IsFinite(point) || !IsFinite(preferNear))
            {
                return 0;
            }

            if (double.IsNaN(maxRadius) || maxRadius < 0)
            {
                maxRadius = 0;
            }

            var pool = new List<Candidate>();
            if (pointItselfQualifies)
            {
                pool.Add(new Candidate(-1, 0, point));
            }

            if (!(pointItselfQualifies && maxCount == 1))
            {
                var cell = layout.CellSize;
                if (!TryClampRange((point.X - maxRadius - layout.Origin.X) / cell, (point.X + maxRadius - layout.Origin.X) / cell, layout.Width, out var loX, out var hiX) ||
                    !TryClampRange((point.Y - maxRadius - layout.Origin.Y) / cell, (point.Y + maxRadius - layout.Origin.Y) / cell, layout.Height, out var loY, out var hiY))
                {
                    loX = 0;
                    hiX = -1;
                    loY = 0;
                    hiY = -1;
                }

                Candidate? best = null;
                for (var ix = loX; ix <= hiX; ix++)
                {
                    for (var iy = loY; iy <= hiY; iy++)
                    {
                        var center = layout.CellCenter(ix, iy);
                        if (Vec2.Distance(point, center) > maxRadius)
                        {
                            continue;
                        }

                        var candidate = MakeCandidate(point, center, preferNear, cell);
                        if (maxCount == 1)
                        {
                            if (best.HasValue && Compare(candidate, best.Value) >= 0)
                            {
                                continue; // 超不过当前最优：不必调用（可能很贵的）谓词。
                            }

                            if (cellQualifies(ix, iy))
                            {
                                best = candidate;
                            }
                        }
                        else if (cellQualifies(ix, iy))
                        {
                            pool.Add(candidate);
                        }
                    }
                }

                if (best.HasValue)
                {
                    pool.Add(best.Value);
                }
            }

            Flush(pool, maxCount, results);
            return results.Count;
        }

        /// <summary>
        /// 只依赖 <paramref name="isWalkable"/> 的近似搜索（<see cref="INavigation2D"/> 默认接口实现用，
        /// 不知道网格的第三方实现的兜底）：以 <see cref="SampledStep"/> 为步长，对
        /// <paramref name="point"/> 做同心环采样（环 k 半径 <c>k × SampledStep</c>，方向数见
        /// <see cref="MinSampledDirections"/>），环 k 的采样点主键恒为 k，因此"主键 = 距离量化档"的
        /// 排序规则在环粒度上精确成立；环内并列由同一套次键/字典序裁决。采样不是穷举，环上两个采样点之
        /// 间的可走点可能被漏掉——这是没有网格信息时的近似，真实网格实现应覆盖为
        /// <see cref="CollectOnGrid"/>。
        /// </summary>
        public static int CollectSampled(
            Func<Vec2, bool> isWalkable, Vec2 point, double maxRadius, Vec2 preferNear, int maxCount,
            List<Vec2> results)
        {
            results.Clear();
            if (maxCount <= 0 || !IsFinite(point) || !IsFinite(preferNear))
            {
                return 0;
            }

            if (double.IsNaN(maxRadius) || maxRadius < 0)
            {
                maxRadius = 0;
            }

            var pool = new List<Candidate>();
            if (isWalkable(point))
            {
                pool.Add(new Candidate(-1, 0, point));
            }

            var rings = (int)Math.Min(MaxSampledRings, Math.Floor(maxRadius / SampledStep + QuantizeEpsilon));
            for (var k = 1; k <= rings && pool.Count < maxCount; k++)
            {
                var radius = k * SampledStep;
                var directions = Math.Max(MinSampledDirections, (int)Math.Ceiling(2.0 * Math.PI * radius / SampledStep));
                for (var j = 0; j < directions; j++)
                {
                    var angle = 2.0 * Math.PI * j / directions;
                    var sample = new Vec2(point.X + radius * Math.Cos(angle), point.Y + radius * Math.Sin(angle));
                    if (isWalkable(sample))
                    {
                        pool.Add(MakeCandidate(point, sample, preferNear, SampledStep));
                    }
                }
            }

            Flush(pool, maxCount, results);
            return results.Count;
        }
    }
}
