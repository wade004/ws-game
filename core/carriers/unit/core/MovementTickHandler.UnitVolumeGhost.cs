using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 穿过式动作位移（"幽灵"，档案 <c>dodge_through_units</c> + <c>pass_through_motion_kinds</c>）的落点修正（M4-W2，ADR-0128 追加决定）。
    /// <para>
    /// 幽灵在位移窗口内仍可穿过别的单位（阶段 A 不拦、阶段 B 不撞停、不参与重叠分离）；但窗口的最后一个 tick（<see cref="MotionTick.PassThroughEnds"/>）
    /// 它不得终止在别的单位体积里：终点按"就近可行位置"修正——候选是各个被压着的体积圆（半径之和 + 一个到达容差）上离终点最近的点（径向投影），
    /// 以及两个体积圆的交点（夹在两个单位之间时），取不落进任何单位体积（含另一个幽灵的终点）、且可走的、离原终点最近的那个候选。
    /// 别的单位（非幽灵）的成对裁决在窗口内每个 tick 都把幽灵的最终位置当作静止的体积（<see cref="ResolveMovedPairs"/>），落点修正后对新落点重算，
    /// 两者迭代到不再变化（<see cref="GhostRounds"/> 轮封顶；封顶仍重叠的由随后的重叠分离兜底，幽灵最后一个 tick 参与分离）。
    /// </para>
    /// </summary>
    public sealed partial class MovementTickHandler
    {
        private const int GhostNeighborCap = 32;

        /// <summary>
        /// 幽灵 <paramref name="gi"/>（快照下标）的终点 <c>pos[gi]</c> 落进别的体积时，求就近可行落点 <paramref name="result"/>；终点本来就在体积外、
        /// 或周围没有任何可行位置（被地形与体积围死）时返回 false（<paramref name="result"/> 为原终点）。<paramref name="pos"/> 是各单位当前的最终位置。
        /// </summary>
        private bool TryRelocateGhost(int gi, Vec2[] pos, out Vec2 result)
        {
            const double overlapTolerance = 1e-9;
            var g = _volumes[gi];
            var p = pos[gi];
            result = p;
            var eps = _options.ArrivalEpsilon;
            var neighborRadius = 6.0 * (g.Radius + _maxVolumeRadius);

            var centers = new List<Vec2>();
            var sums = new List<double>();
            var order = new List<(double Dist, int Index)>();
            for (var j = 0; j < _volumes.Count; j++)
            {
                var o = _volumes[j];
                if (j == gi || !o.Active)
                {
                    continue;
                }

                if (o.Map.HasValue && g.Map.HasValue && !o.Map.Value.Equals(g.Map.Value))
                {
                    continue;
                }

                var d = (p - pos[j]).Length;
                if (d <= neighborRadius)
                {
                    order.Add((d, j));
                }
            }

            order.Sort((x, y) =>
            {
                var c = x.Dist.CompareTo(y.Dist);
                return c != 0 ? c : x.Index.CompareTo(y.Index);
            });
            var count = Math.Min(order.Count, GhostNeighborCap);
            var overlapped = false;
            for (var k = 0; k < count; k++)
            {
                var o = _volumes[order[k].Index];
                var sum = g.Radius + o.Radius;
                centers.Add(pos[order[k].Index]);
                sums.Add(sum);
                if (order[k].Dist < sum - overlapTolerance)
                {
                    overlapped = true;
                }
            }

            if (!overlapped)
            {
                return false;
            }

            var candidates = new List<Vec2>();
            for (var k = 0; k < count; k++)
            {
                var offset = p - centers[k];
                var d = offset.Length;
                if (d >= sums[k] + eps)
                {
                    continue; // 这个圆没压着终点，投影没有意义。
                }

                Vec2 dir;
                if (d > 1e-12)
                {
                    dir = new Vec2(offset.X / d, offset.Y / d);
                }
                else
                {
                    // 与对方中心完全重合：沿"本 tick 位移的反方向"退出（幽灵是从那边来的），没有位移时取 +x（确定）。
                    var back = g.Start - p;
                    var bl = back.Length;
                    dir = bl > 1e-12 ? new Vec2(back.X / bl, back.Y / bl) : new Vec2(1.0, 0.0);
                }

                candidates.Add(centers[k] + dir * (sums[k] + eps));
            }

            for (var k = 0; k < count; k++)
            {
                for (var l = k + 1; l < count; l++)
                {
                    AddCircleIntersections(centers[k], sums[k] + eps, centers[l], sums[l] + eps, candidates);
                }
            }

            var best = p;
            var bestDistance = double.MaxValue;
            var found = false;
            for (var c = 0; c < candidates.Count; c++)
            {
                var q = candidates[c];
                var ok = true;
                for (var k = 0; k < count && ok; k++)
                {
                    ok = (q - centers[k]).Length >= sums[k] + 0.5 * eps;
                }

                if (!ok)
                {
                    continue;
                }

                if (_navigation != null && g.Unit != null && !_navigation.IsWalkable(g.Unit.MapId, q))
                {
                    continue;
                }

                var distance = (q - p).Length;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = q;
                    found = true;
                }
            }

            if (!found)
            {
                return false;
            }

            result = best;
            return true;
        }

        /// <summary>两个圆（圆心 c0/c1，半径 r0/r1）的交点（0、1 或 2 个，相切取一个）加入 <paramref name="into"/>。</summary>
        private static void AddCircleIntersections(Vec2 c0, double r0, Vec2 c1, double r1, List<Vec2> into)
        {
            var d = c1 - c0;
            var dist = d.Length;
            if (dist < 1e-12 || dist > r0 + r1 || dist < Math.Abs(r0 - r1))
            {
                return;
            }

            var along = (r0 * r0 - r1 * r1 + dist * dist) / (2.0 * dist);
            var h2 = r0 * r0 - along * along;
            var h = h2 > 0.0 ? Math.Sqrt(h2) : 0.0;
            var ux = d.X / dist;
            var uy = d.Y / dist;
            var mid = new Vec2(c0.X + ux * along, c0.Y + uy * along);
            into.Add(new Vec2(mid.X - uy * h, mid.Y + ux * h));
            if (h > 0.0)
            {
                into.Add(new Vec2(mid.X + uy * h, mid.Y - ux * h));
            }
        }
    }
}
