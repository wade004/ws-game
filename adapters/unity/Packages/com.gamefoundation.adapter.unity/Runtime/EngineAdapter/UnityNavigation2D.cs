#nullable enable
// UnityNavigation2D：INavigation2D 的 Unity 引擎实现——网格 A*，不引入第三方寻路包
// （任务书拍板；记录判断：Unity 内置的 NavMesh 系统面向三维导航网格，与本框架"逻辑层永远是
// 二维平面坐标"的定位不完全贴合，直接手写二维网格 A* 更贴合 02 第 1.8 节的接口语义，
// 也避免引入 com.unity.ai.navigation 包与烘焙流程）。
//
// 阻挡数据来源（ADR-0016 决策 7 已给 INavigation2D 补上 SetBlocking(mapId, rects)/Clear(mapId)
// 两个契约方法，取代此前"契约本身没有定义如何灌入阻挡数据"的缺口）：SetBlocking 是"整批替换"
// 语义（一次调用替换该地图当前登记的全部动态阻挡，见 adapters/stub/StubNavigation2D 同款判断
// 记录）；本类型额外保留一个非契约便捷方法 RegisterBlockingFromTilemap，从一个 Unity Tilemap
// 的实心格子批量算出矩形集合后同样经 SetBlocking 整批替换（不是增量追加——此前有一个逐格追加的
// RegisterBlockingRect 方法，已随 ADR-0016 落地删除，其增量语义现由调用方自行收集矩形列表后
// 一次性调用 SetBlocking 承担）。
//
// 网格判断记录：BuildNavMesh 时按已登记矩形的包围盒 + 边距生成网格，格子尺寸在
// DefaultCellSize（0.25 世界单位）与"包围盒必须能装进 MaxGridDimension×MaxGridDimension
// 个格子"之间自适应放大，避免地图过大时网格无限膨胀；FindPath 用标准 8 方向 A*（曼哈顿+对角
// 混合启发式），找不到路径返回 null（而非空列表，同契约语义）。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityNavigation2D : INavigation2D
    {
        private const double DefaultCellSize = 0.25;
        private const int MaxGridDimension = 192;
        private const double BoundsMargin = 2.0;

        private readonly struct BlockingRect
        {
            public readonly Vec2 Min;
            public readonly Vec2 Max;

            public BlockingRect(Vec2 min, Vec2 max)
            {
                Min = min;
                Max = max;
            }

            public bool Contains(Vec2 point) =>
                point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
        }

        private sealed class NavGrid
        {
            public Vec2 Origin;
            public double CellSize;
            public int Width;
            public int Height;
            public bool[,] Walkable = null!;
        }

        private readonly Dictionary<Id, List<BlockingRect>> _blockingRects = new Dictionary<Id, List<BlockingRect>>();
        private readonly Dictionary<Id, NavGrid> _grids = new Dictionary<Id, NavGrid>();

        public void BuildNavMesh(Id mapId)
        {
            _grids[mapId] = BuildGrid(mapId);
        }

        public bool IsWalkable(Id mapId, Vec2 point)
        {
            if (!_blockingRects.TryGetValue(mapId, out var rects))
            {
                return true;
            }

            foreach (var rect in rects)
            {
                if (rect.Contains(point))
                {
                    return false;
                }
            }

            return true;
        }

        public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to)
        {
            if (!_grids.TryGetValue(mapId, out var grid))
            {
                grid = BuildGrid(mapId);
                _grids[mapId] = grid;
            }

            if (!TryWorldToCell(grid, from, out var startCell) || !TryWorldToCell(grid, to, out var goalCell))
            {
                // 起点或终点落在网格覆盖范围之外：退化为直线检查（与 stub 的最小实现精神一致）。
                return SegmentBlocked(mapId, from, to) ? null : new List<Vec2> { from, to };
            }

            var path = AStar(grid, startCell, goalCell);
            if (path == null) return null;

            var worldPath = new List<Vec2>(path.Count);
            foreach (var cell in path)
            {
                worldPath.Add(CellToWorld(grid, cell));
            }

            return worldPath;
        }

        public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to)
        {
            if (!_blockingRects.TryGetValue(mapId, out var rects))
            {
                return null;
            }

            Vec2? closestHit = null;
            var closestDistanceSqr = double.MaxValue;

            foreach (var rect in rects)
            {
                if (TrySegmentAabbEntry(from, to, rect.Min, rect.Max, out var hit))
                {
                    var distanceSqr = (hit - from).SqrLength;
                    if (distanceSqr < closestDistanceSqr)
                    {
                        closestDistanceSqr = distanceSqr;
                        closestHit = hit;
                    }
                }
            }

            return closestHit;
        }

        /// <summary>契约方法（INavigation2D / ADR-0016 决策 7）：以传入的整批矩形替换该地图当前
        /// 登记的动态阻挡（不是追加，见类型顶部判断记录）。</summary>
        public void SetBlocking(Id mapId, IReadOnlyList<Core.Foundation.Common.Rect> rects)
        {
            var list = new List<BlockingRect>(rects.Count);
            foreach (var rect in rects)
            {
                list.Add(new BlockingRect(rect.Min, rect.Max));
            }

            _blockingRects[mapId] = list;
            _grids.Remove(mapId); // 阻挡数据变化后网格需要重建，下次 FindPath/BuildNavMesh 时重算。
        }

        /// <summary>契约方法：清空某地图的全部动态阻挡登记（场景卸载时调用）。</summary>
        public void Clear(Id mapId)
        {
            _blockingRects.Remove(mapId);
            _grids.Remove(mapId);
        }

        /// <summary>非契约便捷方法：从一个 Tilemap 的实心格子（TileBase 非空）生成阻挡矩形，
        /// 每个实心格子一个独立 AABB，整批经 <see cref="SetBlocking"/> 一次性替换该地图当前登记
        /// （不是增量追加——同一张 Tilemap 的两次调用不会产生重复登记，供使用 Unity Tilemap
        /// 编辑地图的游戏在地图加载时调用一次）。</summary>
        public void RegisterBlockingFromTilemap(Id mapId, Tilemap tilemap)
        {
            if (tilemap == null) throw new ArgumentNullException(nameof(tilemap));

            var rects = new List<Core.Foundation.Common.Rect>();
            var bounds = tilemap.cellBounds;
            for (var x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (var y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var cellPos = new Vector3Int(x, y, 0);
                    if (tilemap.HasTile(cellPos))
                    {
                        var worldMin = tilemap.CellToWorld(cellPos);
                        var cellSize = tilemap.cellSize;
                        rects.Add(new Core.Foundation.Common.Rect(
                            new Vec2(worldMin.x, worldMin.y),
                            new Vec2(worldMin.x + cellSize.x, worldMin.y + cellSize.y)));
                    }
                }
            }

            SetBlocking(mapId, rects);
        }

        private NavGrid BuildGrid(Id mapId)
        {
            Vec2 min = new Vec2(-8, -8);
            Vec2 max = new Vec2(8, 8);

            if (_blockingRects.TryGetValue(mapId, out var rects) && rects.Count > 0)
            {
                min = rects[0].Min;
                max = rects[0].Max;
                foreach (var rect in rects)
                {
                    min = new Vec2(Math.Min(min.X, rect.Min.X), Math.Min(min.Y, rect.Min.Y));
                    max = new Vec2(Math.Max(max.X, rect.Max.X), Math.Max(max.Y, rect.Max.Y));
                }
            }

            min = new Vec2(min.X - BoundsMargin, min.Y - BoundsMargin);
            max = new Vec2(max.X + BoundsMargin, max.Y + BoundsMargin);

            var spanX = Math.Max(max.X - min.X, DefaultCellSize);
            var spanY = Math.Max(max.Y - min.Y, DefaultCellSize);
            var cellSize = DefaultCellSize;

            var width = (int)Math.Ceiling(spanX / cellSize);
            var height = (int)Math.Ceiling(spanY / cellSize);
            if (width > MaxGridDimension || height > MaxGridDimension)
            {
                var scale = Math.Max((double)width / MaxGridDimension, (double)height / MaxGridDimension);
                cellSize *= scale;
                width = (int)Math.Ceiling(spanX / cellSize);
                height = (int)Math.Ceiling(spanY / cellSize);
            }

            var grid = new NavGrid
            {
                Origin = min,
                CellSize = cellSize,
                Width = Math.Max(width, 1),
                Height = Math.Max(height, 1),
                Walkable = new bool[Math.Max(width, 1), Math.Max(height, 1)]
            };

            for (var gx = 0; gx < grid.Width; gx++)
            {
                for (var gy = 0; gy < grid.Height; gy++)
                {
                    var world = CellToWorld(grid, (gx, gy));
                    grid.Walkable[gx, gy] = !IsBlockedByRects(mapId, world);
                }
            }

            return grid;
        }

        private bool IsBlockedByRects(Id mapId, Vec2 point)
        {
            if (!_blockingRects.TryGetValue(mapId, out var rects)) return false;
            foreach (var rect in rects)
            {
                if (rect.Contains(point)) return true;
            }

            return false;
        }

        private static bool TryWorldToCell(NavGrid grid, Vec2 world, out (int x, int y) cell)
        {
            var gx = (int)Math.Floor((world.X - grid.Origin.X) / grid.CellSize);
            var gy = (int)Math.Floor((world.Y - grid.Origin.Y) / grid.CellSize);
            cell = (gx, gy);
            return gx >= 0 && gx < grid.Width && gy >= 0 && gy < grid.Height;
        }

        private static Vec2 CellToWorld(NavGrid grid, (int x, int y) cell)
        {
            return new Vec2(
                grid.Origin.X + (cell.x + 0.5) * grid.CellSize,
                grid.Origin.Y + (cell.y + 0.5) * grid.CellSize);
        }

        private static List<(int x, int y)>? AStar(NavGrid grid, (int x, int y) start, (int x, int y) goal)
        {
            if (!grid.Walkable[start.x, start.y] || !grid.Walkable[goal.x, goal.y])
            {
                return null;
            }

            if (start == goal)
            {
                return new List<(int, int)> { start };
            }

            var open = new SortedSet<(double f, int seq, (int x, int y) cell)>();
            var seqCounter = 0;
            var gScore = new Dictionary<(int, int), double> { [start] = 0 };
            var cameFrom = new Dictionary<(int, int), (int, int)>();
            var closed = new HashSet<(int, int)>();

            open.Add((Heuristic(start, goal), seqCounter++, start));

            (int dx, int dy, double cost)[] neighbors =
            {
                (1, 0, 1.0), (-1, 0, 1.0), (0, 1, 1.0), (0, -1, 1.0),
                (1, 1, 1.41421356), (1, -1, 1.41421356), (-1, 1, 1.41421356), (-1, -1, 1.41421356)
            };

            while (open.Count > 0)
            {
                var current = open.Min;
                open.Remove(current);
                var currentCell = current.cell;

                if (currentCell == goal)
                {
                    return ReconstructPath(cameFrom, currentCell);
                }

                if (!closed.Add(currentCell)) continue;

                foreach (var (dx, dy, cost) in neighbors)
                {
                    var next = (x: currentCell.x + dx, y: currentCell.y + dy);
                    if (next.x < 0 || next.x >= grid.Width || next.y < 0 || next.y >= grid.Height) continue;
                    if (!grid.Walkable[next.x, next.y]) continue;
                    if (closed.Contains(next)) continue;

                    var tentativeG = gScore[currentCell] + cost;
                    if (!gScore.TryGetValue(next, out var existingG) || tentativeG < existingG)
                    {
                        gScore[next] = tentativeG;
                        cameFrom[next] = currentCell;
                        var f = tentativeG + Heuristic(next, goal);
                        open.Add((f, seqCounter++, next));
                    }
                }
            }

            return null;
        }

        private static double Heuristic((int x, int y) a, (int x, int y) b)
        {
            var dx = Math.Abs(a.x - b.x);
            var dy = Math.Abs(a.y - b.y);
            return Math.Max(dx, dy) + (1.41421356 - 1.0) * Math.Min(dx, dy);
        }

        private static List<(int x, int y)> ReconstructPath(Dictionary<(int, int), (int, int)> cameFrom, (int x, int y) current)
        {
            var path = new List<(int, int)> { current };
            while (cameFrom.TryGetValue(current, out var prev))
            {
                current = prev;
                path.Add(current);
            }

            path.Reverse();
            return path;
        }

        private bool SegmentBlocked(Id mapId, Vec2 from, Vec2 to)
        {
            if (!_blockingRects.TryGetValue(mapId, out var rects)) return false;
            foreach (var rect in rects)
            {
                if (TrySegmentAabbEntry(from, to, rect.Min, rect.Max, out _)) return true;
            }

            return false;
        }

        private static bool TrySegmentAabbEntry(Vec2 from, Vec2 to, Vec2 min, Vec2 max, out Vec2 hit)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;

            double tMin = 0.0;
            double tMax = 1.0;

            if (!ClipAxis(from.X, dx, min.X, max.X, ref tMin, ref tMax) ||
                !ClipAxis(from.Y, dy, min.Y, max.Y, ref tMin, ref tMax))
            {
                hit = default;
                return false;
            }

            hit = new Vec2(from.X + dx * tMin, from.Y + dy * tMin);
            return true;
        }

        private static bool ClipAxis(double origin, double delta, double boundMin, double boundMax, ref double tMin, ref double tMax)
        {
            if (Math.Abs(delta) < double.Epsilon)
            {
                return origin >= boundMin && origin <= boundMax;
            }

            var t1 = (boundMin - origin) / delta;
            var t2 = (boundMax - origin) / delta;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            return tMin <= tMax;
        }
    }
}
