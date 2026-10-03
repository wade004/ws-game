using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 地形感知的网格寻路（ADR-0130 追加决定"寻路感知台阶"）：在导航网格（<see cref="NavGridLayout"/>）上做八邻接 A*，
    /// 额外把"地形台阶挡住的边"（<see cref="ITerrainStepConstraint"/>，有向）当作不可通行，规划出绕开台阶、陡坡的路径。
    /// 测试桩、核心层的开阔场地导航与引擎适配层的网格导航在<b>声明了地形约束</b>时共用这一份实现；没有声明地形约束时它们仍走各自原有的
    /// <c>FindPath</c>（缺省逐位不变）。
    /// <para>
    /// 调用方给出"点可走"（<paramref name="isWalkable"/>）与"线段不穿阻挡内部"（<paramref name="segmentClear"/>）两个谓词，
    /// 它们<b>就是导航自己的</b> <c>IsWalkable</c>/<c>Raycast</c> 判定，因此返回路径的每一段既满足 <c>Raycast</c> 通畅，也满足地形约束——
    /// 同一条路径移动时不会被台阶截断。<paramref name="pullClear"/> 是视线剪枝（string pulling）用的判定，调用方可以传更严格的版本
    /// （例如"贴边擦角也算接触"，引擎适配层用它禁止拉直后的路径从两块对角相接阻挡的角点挤过去）；传同一个谓词即可。
    /// </para>
    /// <para>
    /// 确定性：邻居展开顺序固定，开放表按 <c>(f, 节点序号)</c> 排序，同一输入必得同一路径。
    /// 网格覆盖阻挡矩形并集、两个端点与 <see cref="NavGridLayout.BoundsMargin"/> 的边距；端点必在网格内。
    /// </para>
    /// <para>
    /// 判断记录（端点接合）：起点（终点）先试"与端点直连不受阻（阻挡与地形都通畅）"的所在格与其 8 个邻格中心作为入口（出口），
    /// 它们是 A* 的多源起点（多汇终点），初始代价是端点到格心的直线距离；因此端点所在格心被阻挡盖住、或被台阶隔在另一侧时仍能找到路。
    /// 没有任何入口（出口）可接合就判定无路。两端点之间直线已通畅时直接返回 <c>[from, to]</c>（寻路契约的视线剪枝要求）。
    /// </para>
    /// </summary>
    public static class TerrainStepPathPlanner
    {
        private static readonly int[] Dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
        private static readonly int[] Dy = { -1, -1, -1, 0, 0, 1, 1, 1 };

        /// <summary>
        /// 规划一条避开地形台阶的路径；找不到返回 <c>null</c>。调用方保证两端点本身可走、且不是零长度请求（导航契约的端点处理在调用方）。
        /// </summary>
        /// <param name="blockers">导航当前登记的阻挡矩形（决定网格范围）；可为 <c>null</c>。</param>
        /// <param name="smooth">是否做视线剪枝（缺省真）。</param>
        public static List<Vec2>? FindPath(
            IReadOnlyList<Rect>? blockers, Id mapId, Vec2 from, Vec2 to,
            Func<Vec2, bool> isWalkable, Func<Vec2, Vec2, bool> segmentClear, Func<Vec2, Vec2, bool> pullClear,
            ITerrainStepConstraint terrain, bool smooth = true)
        {
            if (isWalkable == null) throw new ArgumentNullException(nameof(isWalkable));
            if (segmentClear == null) throw new ArgumentNullException(nameof(segmentClear));
            if (pullClear == null) throw new ArgumentNullException(nameof(pullClear));
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));

            bool EdgeOk(Vec2 a, Vec2 b) => segmentClear(a, b) && !terrain.FirstStepBlock(mapId, a, b).HasValue;
            bool PullOk(Vec2 a, Vec2 b) => pullClear(a, b) && !terrain.FirstStepBlock(mapId, a, b).HasValue;

            if (EdgeOk(from, to))
            {
                return new List<Vec2> { from, to };
            }

            var layout = LayoutFor(blockers, from, to);
            var width = layout.Width;
            var height = layout.Height;
            var cellCount = width * height;
            var goal = cellCount; // 虚拟终点节点

            // 0 = 未算，1 = 可走，2 = 不可走。
            var walkState = new byte[cellCount];
            bool CellWalkable(int ix, int iy)
            {
                if (ix < 0 || iy < 0 || ix >= width || iy >= height) return false;
                var idx = iy * width + ix;
                if (walkState[idx] == 0)
                {
                    walkState[idx] = isWalkable(layout.CellCenter(ix, iy)) ? (byte)1 : (byte)2;
                }

                return walkState[idx] == 1;
            }

            var gScore = new double[cellCount + 1];
            var parent = new int[cellCount + 1];
            var closed = new bool[cellCount + 1];
            for (var i = 0; i <= cellCount; i++)
            {
                gScore[i] = double.PositiveInfinity;
                parent[i] = -1;
            }

            var heap = new MinHeap();

            // 入口：端点所在格及其 8 邻格里，中心可走且端点能直连的。
            var startX = ClampCell((from.X - layout.Origin.X) / layout.CellSize, width);
            var startY = ClampCell((from.Y - layout.Origin.Y) / layout.CellSize, height);
            for (var oy = -1; oy <= 1; oy++)
            {
                for (var ox = -1; ox <= 1; ox++)
                {
                    var ix = startX + ox;
                    var iy = startY + oy;
                    if (!CellWalkable(ix, iy)) continue;
                    var center = layout.CellCenter(ix, iy);
                    if (!EdgeOk(from, center)) continue;
                    var idx = iy * width + ix;
                    var g = Vec2.Distance(from, center);
                    if (g < gScore[idx])
                    {
                        gScore[idx] = g;
                        heap.Push(g + Vec2.Distance(center, to), idx);
                    }
                }
            }

            // 出口：终点所在格及其 8 邻格里，中心可走且能直连终点的。
            var exitCost = new Dictionary<int, double>();
            var goalX = ClampCell((to.X - layout.Origin.X) / layout.CellSize, width);
            var goalY = ClampCell((to.Y - layout.Origin.Y) / layout.CellSize, height);
            for (var oy = -1; oy <= 1; oy++)
            {
                for (var ox = -1; ox <= 1; ox++)
                {
                    var ix = goalX + ox;
                    var iy = goalY + oy;
                    if (!CellWalkable(ix, iy)) continue;
                    var center = layout.CellCenter(ix, iy);
                    if (!EdgeOk(center, to)) continue;
                    exitCost[iy * width + ix] = Vec2.Distance(center, to);
                }
            }

            if (heap.Count == 0 || exitCost.Count == 0)
            {
                return null;
            }

            while (heap.Count > 0)
            {
                var node = heap.Pop();
                if (closed[node]) continue;
                closed[node] = true;
                if (node == goal) break;

                var cx = node % width;
                var cy = node / width;
                var centerA = layout.CellCenter(cx, cy);

                if (exitCost.TryGetValue(node, out var finalLeg))
                {
                    var g = gScore[node] + finalLeg;
                    if (g < gScore[goal])
                    {
                        gScore[goal] = g;
                        parent[goal] = node;
                        heap.Push(g, goal);
                    }
                }

                for (var d = 0; d < 8; d++)
                {
                    var nx = cx + Dx[d];
                    var ny = cy + Dy[d];
                    if (!CellWalkable(nx, ny)) continue;
                    if (Dx[d] != 0 && Dy[d] != 0 && !(CellWalkable(cx + Dx[d], cy) && CellWalkable(cx, cy + Dy[d])))
                    {
                        continue; // 不许切角：对角移动要求两个正交邻格都可走。
                    }

                    var next = ny * width + nx;
                    if (closed[next]) continue;
                    var centerB = layout.CellCenter(nx, ny);
                    var step = Vec2.Distance(centerA, centerB);
                    var g2 = gScore[node] + step;
                    if (g2 >= gScore[next]) continue;
                    if (!EdgeOk(centerA, centerB)) continue;
                    gScore[next] = g2;
                    parent[next] = node;
                    heap.Push(g2 + Vec2.Distance(centerB, to), next);
                }
            }

            if (parent[goal] < 0)
            {
                return null;
            }

            var cells = new List<int>();
            for (var n = parent[goal]; n >= 0; n = parent[n])
            {
                cells.Add(n);
            }

            cells.Reverse();
            var points = new List<Vec2>(cells.Count + 2) { from };
            foreach (var cell in cells)
            {
                points.Add(layout.CellCenter(cell % width, cell / width));
            }

            points.Add(to);

            if (!smooth || points.Count <= 2)
            {
                return points;
            }

            var result = new List<Vec2> { points[0] };
            var i0 = 0;
            while (i0 < points.Count - 1)
            {
                var j = points.Count - 1;
                while (j > i0 + 1 && !PullOk(points[i0], points[j]))
                {
                    j--;
                }

                result.Add(points[j]);
                i0 = j;
            }

            return result;
        }

        /// <summary>
        /// 地形感知的"与 <paramref name="from"/> 连通的最近可走点"：候选枚举与排序同 <see cref="NearestWalkableSearch.CollectOnGrid"/>
        /// （格子由 <paramref name="layout"/> 给出，<paramref name="isWalkable"/> 过滤格心），<paramref name="pathExists"/> 是调用方的
        /// "从 <paramref name="from"/> 到该点是否存在地形感知路径"判定（即 <c>FindPath(..., terrain) != null</c>），只在候选能刷新当前最优时调用。
        /// </summary>
        public static bool TryFindNearestReachable(
            NavGridLayout layout, Vec2 from, Vec2 point, double maxRadius,
            Func<Vec2, bool> isWalkable, Func<Vec2, bool> pathExists, out Vec2 reachable)
        {
            if (isWalkable == null) throw new ArgumentNullException(nameof(isWalkable));
            if (pathExists == null) throw new ArgumentNullException(nameof(pathExists));
            var results = new List<Vec2>(1);
            NearestWalkableSearch.CollectOnGrid(
                layout,
                (ix, iy) =>
                {
                    var center = layout.CellCenter(ix, iy);
                    return isWalkable(center) && pathExists(center);
                },
                point,
                isWalkable(point) && pathExists(point),
                maxRadius,
                from,
                1,
                results);
            if (results.Count > 0)
            {
                reachable = results[0];
                return true;
            }

            reachable = default;
            return false;
        }

        /// <summary>
        /// 规划用的网格布局：阻挡矩形并集再并上"两端点包围盒外扩绕行余量"（<see cref="DetourMargin"/>），按
        /// <see cref="NavGridLayout.Compute(IReadOnlyList{Rect}, double)"/> 的同一公式。判断记录（绕行搜索窗口）：地形台阶的范围规划器并不知道
        /// （只有"两点间能不能走"的查询），所以只在端点包围盒外扩一圈里找绕路；台阶大到绕行路线超出这个窗口时判定无路——
        /// 窗口随端点距离放大，足以覆盖场景里的台地、峭壁绕行；确需更远的绕行由数据把目标点放在可达一侧，或拆成途经点。
        /// </summary>
        public static NavGridLayout LayoutFor(IReadOnlyList<Rect>? blockers, Vec2 from, Vec2 to)
        {
            var all = new List<Rect>((blockers?.Count ?? 0) + 1);
            if (blockers != null)
            {
                all.AddRange(blockers);
            }

            var margin = DetourMargin(from, to);
            all.Add(new Rect(
                new Vec2(Math.Min(from.X, to.X) - margin, Math.Min(from.Y, to.Y) - margin),
                new Vec2(Math.Max(from.X, to.X) + margin, Math.Max(from.Y, to.Y) + margin)));
            return NavGridLayout.Compute(all);
        }

        /// <summary>绕行余量：端点距离的一半，至少 4 个世界单位（再加 <see cref="NavGridLayout.BoundsMargin"/>）。</summary>
        public static double DetourMargin(Vec2 from, Vec2 to) => Math.Max(4.0, 0.5 * Vec2.Distance(from, to));

        /// <summary>
        /// 最近可达点搜索用的网格布局：阻挡矩形并集，加上 <paramref name="from"/> 与 <paramref name="point"/> 周围 <paramref name="maxRadius"/> 半径的包围盒
        /// （候选圆盘必须落在网格内），再外扩绕行余量。
        /// </summary>
        public static NavGridLayout ReachableLayoutFor(IReadOnlyList<Rect>? blockers, Vec2 from, Vec2 point, double maxRadius)
        {
            var all = new List<Rect>((blockers?.Count ?? 0) + 1);
            if (blockers != null)
            {
                all.AddRange(blockers);
            }

            var r = double.IsNaN(maxRadius) || maxRadius < 0.0 ? 0.0 : maxRadius;
            var margin = DetourMargin(from, point);
            all.Add(new Rect(
                new Vec2(Math.Min(from.X, point.X - r) - margin, Math.Min(from.Y, point.Y - r) - margin),
                new Vec2(Math.Max(from.X, point.X + r) + margin, Math.Max(from.Y, point.Y + r) + margin)));
            return NavGridLayout.Compute(all);
        }

        private static int ClampCell(double value, int size)
        {
            var i = (int)Math.Floor(value);
            return i < 0 ? 0 : (i > size - 1 ? size - 1 : i);
        }

        /// <summary>按 (键, 节点序号) 排序的最小堆（确定性：键相同按序号小者先出）。</summary>
        private sealed class MinHeap
        {
            private readonly List<KeyValuePair<double, int>> _items = new List<KeyValuePair<double, int>>();

            public int Count => _items.Count;

            public void Push(double key, int node)
            {
                _items.Add(new KeyValuePair<double, int>(key, node));
                var i = _items.Count - 1;
                while (i > 0)
                {
                    var p = (i - 1) / 2;
                    if (Less(_items[i], _items[p]))
                    {
                        (_items[i], _items[p]) = (_items[p], _items[i]);
                        i = p;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public int Pop()
            {
                var top = _items[0].Value;
                var last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                var i = 0;
                var n = _items.Count;
                while (true)
                {
                    var l = 2 * i + 1;
                    var r = l + 1;
                    var m = i;
                    if (l < n && Less(_items[l], _items[m])) m = l;
                    if (r < n && Less(_items[r], _items[m])) m = r;
                    if (m == i) break;
                    (_items[i], _items[m]) = (_items[m], _items[i]);
                    i = m;
                }

                return top;
            }

            private static bool Less(KeyValuePair<double, int> a, KeyValuePair<double, int> b) =>
                a.Key < b.Key || (a.Key == b.Key && a.Value < b.Value);
        }
    }
}
