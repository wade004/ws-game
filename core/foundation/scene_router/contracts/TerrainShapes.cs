using System;
using Core.Foundation.Common;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// <c>world.map.terrain</c> 里一块地形区域的几何与高度（ADR-0130 追加决定"地形形状"）：回答"这个点在不在区域内、区域内的地面有多高"，
    /// 以及区域的天花板绝对高度。<see cref="MapTerrainHeights"/> 按声明顺序、<b>后声明的盖住先声明的</b>组合它们。
    /// 既有的矩形平台/斜面（<see cref="TerrainRegion"/>）、凸多边形平面/斜面（<see cref="TerrainPolygon"/>）与规则网格高度场
    /// （<see cref="TerrainHeightField"/>）三种形状同表声明、同一组合规则。
    /// </summary>
    public interface ITerrainShape
    {
        /// <summary>该点是否在区域内（含边界）。</summary>
        bool Contains(Vec2 p);

        /// <summary>区域内一点的地面高度（只在 <see cref="Contains"/> 为真时有意义）。</summary>
        double GroundAt(Vec2 p);

        /// <summary>区域的天花板绝对高度；<see cref="double.PositiveInfinity"/> = 没有。</summary>
        double Ceiling { get; }
    }

    /// <summary>
    /// 凸多边形区域：地面是一张平面 <c>ground + slope.x·(x − origin.x) + slope.y·(y − origin.y)</c>（<see cref="Origin"/> 缺省取第一个顶点；
    /// 平面 = 平台，斜面 = 坡道）。顶点按任意绕向给出，内部统一成逆时针；必须是严格凸的、面积不为零（共线的多余顶点允许）。
    /// 区域含边界。判断记录：只支持凸多边形——"点在多边形内"是 O(顶点数) 的叉积判定，无需三角剖分，确定性成立；
    /// 凹的区域由多个凸多边形（后声明盖住先声明）拼出。
    /// </summary>
    public sealed class TerrainPolygon : ITerrainShape
    {
        private const double ContainEpsilon = 1e-12;

        private readonly Vec2[] _vertices;

        public Vec2 Origin { get; }

        public double Ground { get; }

        public double SlopeX { get; }

        public double SlopeY { get; }

        public double Ceiling { get; }

        /// <summary>逆时针排列的顶点（副本）。</summary>
        public Vec2[] Vertices => (Vec2[])_vertices.Clone();

        public TerrainPolygon(
            Vec2[] vertices, double ground = 0.0, double slopeX = 0.0, double slopeY = 0.0,
            double ceiling = double.PositiveInfinity, Vec2? origin = null)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (vertices.Length < 3)
            {
                throw new ArgumentException("terrain 多边形至少需要 3 个顶点", nameof(vertices));
            }

            foreach (var v in vertices)
            {
                if (!IsFinite(v.X) || !IsFinite(v.Y))
                {
                    throw new ArgumentException("terrain 多边形的顶点必须是有限数", nameof(vertices));
                }
            }

            // 有向面积（鞋带公式）：决定绕向并排除退化多边形。
            var area2 = 0.0;
            for (var i = 0; i < vertices.Length; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Length];
                area2 += a.X * b.Y - b.X * a.Y;
            }

            if (Math.Abs(area2) < 1e-12)
            {
                throw new ArgumentException("terrain 多边形的面积为零", nameof(vertices));
            }

            var ordered = (Vec2[])vertices.Clone();
            if (area2 < 0.0)
            {
                Array.Reverse(ordered);
            }

            // 严格凸：逆时针下每个顶点处的转向叉积必须不为负（共线允许，向右拐即凹）。
            var n = ordered.Length;
            for (var i = 0; i < n; i++)
            {
                var a = ordered[i];
                var b = ordered[(i + 1) % n];
                var c = ordered[(i + 2) % n];
                var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (cross < -1e-12)
                {
                    throw new ArgumentException("terrain 多边形必须是凸的（凹区域请拆成多个凸多边形，后声明的盖住先声明的）", nameof(vertices));
                }
            }

            _vertices = ordered;
            Origin = origin ?? vertices[0];
            Ground = ground;
            SlopeX = slopeX;
            SlopeY = slopeY;
            Ceiling = ceiling;
        }

        public bool Contains(Vec2 p)
        {
            var n = _vertices.Length;
            for (var i = 0; i < n; i++)
            {
                var a = _vertices[i];
                var b = _vertices[(i + 1) % n];
                var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                if (cross < -ContainEpsilon)
                {
                    return false;
                }
            }

            return true;
        }

        public double GroundAt(Vec2 p) => Ground + SlopeX * (p.X - Origin.X) + SlopeY * (p.Y - Origin.Y);

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }

    /// <summary>
    /// 规则网格高度场：结点位于 <c>Min + (i·Cell, j·Cell)</c>（<c>i</c> 沿 x、<c>j</c> 沿 y），每个结点一个地面高度；格子内按<b>双线性插值</b>
    /// 取高（结点处恰为结点高度，相邻格共享结点因此处处连续）。区域是结点包围的矩形 <c>[Min, Min + ((Columns−1)·Cell, (Rows−1)·Cell)]</c>，含边界；
    /// 天花板与其它形状一样是绝对高度（整块区域一个值）。判断记录：高度场只回答"各处多高"，不推断台阶——悬崖要用相邻结点的高度差
    /// 表达（插值在一个格宽内连续过渡，是陡坡；要垂直的边缘请用矩形/多边形平台盖在上面）。
    /// </summary>
    public sealed class TerrainHeightField : ITerrainShape
    {
        private readonly double[] _heights; // 行优先：j * Columns + i

        public Vec2 Min { get; }

        public double Cell { get; }

        public int Columns { get; }

        public int Rows { get; }

        public double Ceiling { get; }

        public Vec2 Max => new Vec2(Min.X + (Columns - 1) * Cell, Min.Y + (Rows - 1) * Cell);

        /// <param name="heights">结点高度，<c>heights[j][i]</c> 是第 <c>j</c> 行（y 向）第 <c>i</c> 列（x 向）；每行等长，至少 2×2。</param>
        public TerrainHeightField(Vec2 min, double cell, double[][] heights, double ceiling = double.PositiveInfinity)
        {
            if (heights == null) throw new ArgumentNullException(nameof(heights));
            if (!(cell > 0.0) || double.IsInfinity(cell))
            {
                throw new ArgumentException("高度场的 cell 必须为正的有限数", nameof(cell));
            }

            if (heights.Length < 2)
            {
                throw new ArgumentException("高度场至少需要 2 行结点", nameof(heights));
            }

            var columns = heights[0] == null ? 0 : heights[0].Length;
            if (columns < 2)
            {
                throw new ArgumentException("高度场至少需要 2 列结点", nameof(heights));
            }

            var flat = new double[heights.Length * columns];
            for (var j = 0; j < heights.Length; j++)
            {
                if (heights[j] == null || heights[j].Length != columns)
                {
                    throw new ArgumentException("高度场每一行的结点数必须相同", nameof(heights));
                }

                for (var i = 0; i < columns; i++)
                {
                    var h = heights[j][i];
                    if (double.IsNaN(h) || double.IsInfinity(h))
                    {
                        throw new ArgumentException("高度场的结点高度必须是有限数", nameof(heights));
                    }

                    flat[j * columns + i] = h;
                }
            }

            Min = min;
            Cell = cell;
            Columns = columns;
            Rows = heights.Length;
            Ceiling = ceiling;
            _heights = flat;
        }

        /// <summary>结点 <paramref name="i"/>（列）、<paramref name="j"/>（行）的高度。</summary>
        public double NodeHeight(int i, int j) => _heights[j * Columns + i];

        public bool Contains(Vec2 p)
        {
            var max = Max;
            return p.X >= Min.X && p.X <= max.X && p.Y >= Min.Y && p.Y <= max.Y;
        }

        public double GroundAt(Vec2 p)
        {
            var fx = (p.X - Min.X) / Cell;
            var fy = (p.Y - Min.Y) / Cell;
            var i = (int)Math.Floor(fx);
            var j = (int)Math.Floor(fy);
            if (i < 0) i = 0;
            if (j < 0) j = 0;
            if (i > Columns - 2) i = Columns - 2;
            if (j > Rows - 2) j = Rows - 2;
            var tx = fx - i;
            var ty = fy - j;
            var h00 = _heights[j * Columns + i];
            var h10 = _heights[j * Columns + i + 1];
            var h01 = _heights[(j + 1) * Columns + i];
            var h11 = _heights[(j + 1) * Columns + i + 1];
            var lower = h00 + (h10 - h00) * tx;
            var upper = h01 + (h11 - h01) * tx;
            return lower + (upper - lower) * ty;
        }
    }
}
