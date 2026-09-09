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
//
// 游戏侧 1.8.0 PlayMode 验收后新增判断记录（本轮契约精确化，见任务书"契约"一节）：
// 1) 阻挡版本：_blockingVersion 按 mapId 独立计数，SetBlocking/Clear/BuildNavMesh 三个会改变
//    该地图阻挡/网格状态的方法各自把计数 +1（首次改动即从 0 变 1）。GetBlockingVersion 目前是
//    本类型一个普通公开方法，尚未出现在 INavigation2D 接口上（核心侧 ADR 落地后会给接口补一个
//    返回 0 的默认成员）——C# 的隐式接口实现不要求方法体上标注 override/显式实现，只要签名一致，
//    接口一旦补上该默认成员，本方法即自动满足契约，不需要再改这个文件。
// 2) FindPath 端点精确契约：返回路径的首尾元素必须逐比特等于调用方传入的 from/to（不是网格
//    量化后的格子中心），零长度请求（|from-to|<=1e-6）返回单元素 [from]，起止点任一不可行走
//    返回 null。网格内部路径仍然是格子中心序列，首尾额外接一段"精确端点 -> 最近格子中心"的
//    连接段；接合段必须通过与 Raycast 完全同源的阻挡判定（见下）——判定不通过时尝试改接起止格
//    的相邻可行格，仍不通过则整体返回 null（不返回"看起来能走但实际穿墙"的假路径）。
// 3) 统一可通行规则：线段只有穿过阻挡矩形"内部"（严格意义上的开区间）才算受阻，仅与矩形边界或
//    角点相切（贴边/擦角）不算受阻——SegmentBlocked（Raycast 的判定核心）与 FindPath
//    返回路径的每一段共用同一份 SegmentBlocked 实现，不会出现"路径说能走，Raycast 说不能走"的
//    不一致。网格对角移动只有当两个正交邻居格都可行走时才允许（不许贴着两个都被封死的格子斜着
//    穿墙角），由 AStar 的八邻居展开逻辑保证。
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

        /// <summary>NAV-110-02 根治用兜底网格格子尺寸（见 <see cref="FindPathWithFineGrid"/>
        /// 判断记录）：比默认格子尺寸 <see cref="DefaultCellSize"/> 更细，只在主网格算出的路径未能
        /// 通过收尾防线（<see cref="SegmentBlocked"/> 复核）时才启用，不影响主网格的既有性能与行为。</summary>
        private const double ThinObstacleFallbackCellSize = DefaultCellSize / 2.0;

        /// <summary>判定"线段是否穿过矩形内部"时使用的容差：用来把"恰好落在边界/角点上"与"确实
        /// 越过边界进入了内部"区分开（见类型顶部判断记录 3）。取一个远小于 <see cref="DefaultCellSize"/>
        /// 的量级，避免正常网格判定被浮点误差污染，同时不会把真正贴边的情形误判为进入内部。</summary>
        private const double InteriorEpsilon = 1e-9;

        /// <summary>零长度请求判定阈值（见类型顶部判断记录 2：<c>|from-to|&lt;=1e-6</c>）。</summary>
        private const double ZeroLengthThreshold = 1e-6;

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
        private readonly Dictionary<Id, int> _blockingVersion = new Dictionary<Id, int>();

        public void BuildNavMesh(Id mapId)
        {
            _grids[mapId] = BuildGrid(mapId);
            BumpVersion(mapId);
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

        /// <summary>阻挡数据版本号（类型顶部判断记录 1）：从未变动过的地图返回 0，此后每次
        /// <see cref="SetBlocking"/>/<see cref="Clear"/>/<see cref="BuildNavMesh"/> 递增 1。供
        /// 移动系统在推进路径前比较版本、判断阻挡是否发生变化。</summary>
        public int GetBlockingVersion(Id mapId) =>
            _blockingVersion.TryGetValue(mapId, out var version) ? version : 0;

        private void BumpVersion(Id mapId)
        {
            _blockingVersion[mapId] = GetBlockingVersion(mapId) + 1;
        }

        public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to)
        {
            // 起止点任一不可行走：直接失败（类型顶部判断记录 2）。
            if (!IsWalkable(mapId, from) || !IsWalkable(mapId, to))
            {
                return null;
            }

            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            if (dx * dx + dy * dy <= ZeroLengthThreshold * ZeroLengthThreshold)
            {
                return new List<Vec2> { from };
            }

            if (!_grids.TryGetValue(mapId, out var grid))
            {
                grid = BuildGrid(mapId);
                _grids[mapId] = grid;
            }

            if (!TryWorldToCell(grid, from, out var startCell) || !TryWorldToCell(grid, to, out var goalCell))
            {
                // 起点或终点落在网格覆盖范围之外：退化为直线检查，与 Raycast 共用同一阻挡判定
                // （类型顶部判断记录 3），端点天然精确。
                return SegmentBlocked(mapId, from, to) ? null : new List<Vec2> { from, to };
            }

            // NAV-110-01 根治（architecture/落地计划/audit-ac3b622-20260909/presentation/
            // presentation-findings.md）：端点所在的采样格中心受阻，不等于端点本身（按 IsWalkable
            // 逐点判定，已在本方法开头确认可行走）不可行走——一个精确端点可能落在某格边缘，恰好使该
            // 格的中心采样点位于阻挡内部，即便端点自身与直线方向都完全通畅。此前直接把 startCell/
            // goalCell 交给 AStar，其入口检查（AStar 内 grid.Walkable[start]/[goal]）在格中心受阻时
            // 直接返回 null，BuildWorldPath 的邻格接合兜底完全没有机会执行。这里改为：格中心可行走时
            // 原样使用该格（不引入任何行为变化，覆盖既有全部路径场景）；格中心受阻时，参照
            // BuildWorldPath/FindAdjacentReconnect 同一套"向 8 邻居里找可行走、且与精确端点直连不受阻
            // （与 Raycast 同源的 SegmentBlocked 判定）的格子"逻辑，改用该邻格作为 A* 的实际入口/出口；
            // 找不到任何这样的邻格才判定该端点确实无法进入网格，返回 null（不是"看起来能走但实际穿墙"
            // 的假路径，也不是把一个真实可行的精确端点请求提前拒绝）。
            var effectiveStartCell = ResolveEntryCell(grid, mapId, startCell, from);
            var effectiveGoalCell = ResolveEntryCell(grid, mapId, goalCell, to);
            if (effectiveStartCell == null || effectiveGoalCell == null)
            {
                return null;
            }

            var cellPath = AStar(grid, effectiveStartCell.Value, effectiveGoalCell.Value);
            if (cellPath == null)
            {
                return null;
            }

            var worldPath = BuildWorldPath(mapId, grid, from, to, effectiveStartCell.Value, effectiveGoalCell.Value, cellPath);
            if (worldPath == null)
            {
                return null;
            }

            // 收尾防线（类型顶部判断记录 2/3）：绝不返回一条含有受阻分段的"假路径"——正常情况下
            // 不该走到这里（BuildWorldPath 已经逐段校验过接合段，网格内部相邻格中心之间的直线因
            // "不许切角"的八邻居展开规则天然不受阻）。
            //
            // NAV-110-02 根治：这里不再是纯粹的"极端浮点边界情形最后防线"——比主网格格子
            // （DefaultCellSize）更细的阻挡矩形（薄墙）可能使矩形两侧的格子中心都落在阻挡外部、
            // 因而都被判定为可行走，此时 grid.Walkable 对薄墙"视而不见"：AStar 会在这两个格子间
            // 直接建立一条正交/对角邻接边（网格意义上"合法"），但这条边对应的世界坐标线段确实穿过了
            // 薄墙内部，会在这里被 SegmentBlocked 命中。这种情形下真实世界仍可能存在合法绕路（薄墙
            // 本身没有把任何方向堵死，只是比采样间距更窄），直接返回 null 会把一个真实可行的绕路请求
            // 误判为无路可走——因此改为用更细、且用"格子内部与矩形内部相交才判定受阻"（而不是仅采样
            // 格子中心）的兜底网格重新寻路一次（见 FindPathWithFineGrid），兜底仍失败才真正返回 null。
            for (var i = 0; i < worldPath.Count - 1; i++)
            {
                if (SegmentBlocked(mapId, worldPath[i], worldPath[i + 1]))
                {
                    return FindPathWithFineGrid(mapId, from, to);
                }
            }

            return worldPath;
        }

        /// <summary>NAV-110-02 根治：主网格（<see cref="DefaultCellSize"/>、仅采样格子中心判定阻挡）
        /// 找到的路径未能通过收尾防线时的兜底——用 <see cref="ThinObstacleFallbackCellSize"/>
        /// （更细）且改用"格子内部与阻挡矩形内部相交即判定受阻"（<see cref="IsCellInteriorBlockedByRects"/>，
        /// 而不是仅采样格子中心）的独立网格重新执行一次完整寻路（入口接合、A*、世界路径拼装、收尾
        /// 复核，与主网格流程一致，逐字复用同一套 <see cref="ResolveEntryCell"/>/<see cref="BuildWorldPath"/>
        /// 逻辑，只是换了网格）。该网格更细、且判定方式改为"矩形是否与格子内部相交"而不是"矩形是否
        /// 包含格子中心"，因此不会再对比采样间距更窄的薄障碍视而不见；即便如此仍未能找到一条通过
        /// <see cref="SegmentBlocked"/> 复核的路径，才是真正的"无路可走"，返回 <c>null</c>
        /// （不额外递归兜底、不无限加细，避免极端场景下无限重试）。</summary>
        private List<Vec2>? FindPathWithFineGrid(Id mapId, Vec2 from, Vec2 to)
        {
            var grid = BuildGridWithCellSize(mapId, ThinObstacleFallbackCellSize, useAccurateInterior: true);

            if (!TryWorldToCell(grid, from, out var startCell) || !TryWorldToCell(grid, to, out var goalCell))
            {
                // 精细兜底网格的覆盖范围与主网格同源（同一份阻挡矩形 + 同一个 BoundsMargin），主网格
                // 已经能把 from/to 落在网格范围内，这里理应同样成立；退化到"找不到路径"而不是再做一次
                // 直线检查——直线检查已经在主网格路径里做过一次，仍未通过才会走到这里。
                return null;
            }

            var effectiveStartCell = ResolveEntryCell(grid, mapId, startCell, from);
            var effectiveGoalCell = ResolveEntryCell(grid, mapId, goalCell, to);
            if (effectiveStartCell == null || effectiveGoalCell == null)
            {
                return null;
            }

            var cellPath = AStar(grid, effectiveStartCell.Value, effectiveGoalCell.Value);
            if (cellPath == null)
            {
                return null;
            }

            var worldPath = BuildWorldPath(mapId, grid, from, to, effectiveStartCell.Value, effectiveGoalCell.Value, cellPath);
            if (worldPath == null)
            {
                return null;
            }

            for (var i = 0; i < worldPath.Count - 1; i++)
            {
                if (SegmentBlocked(mapId, worldPath[i], worldPath[i + 1]))
                {
                    return null;
                }
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
                if (TrySegmentRectInteriorEntry(from, to, rect.Min, rect.Max, out var hit))
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
            BumpVersion(mapId);
        }

        /// <summary>契约方法：清空某地图的全部动态阻挡登记（场景卸载时调用）。</summary>
        public void Clear(Id mapId)
        {
            _blockingRects.Remove(mapId);
            _grids.Remove(mapId);
            BumpVersion(mapId);
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

        private NavGrid BuildGrid(Id mapId) => BuildGridWithCellSize(mapId, DefaultCellSize, useAccurateInterior: false);

        /// <summary>NAV-110-02 根治新增：<see cref="BuildGrid"/> 的参数化版本，供
        /// <see cref="FindPathWithFineGrid"/> 复用同一套网格构建流程（边界计算、
        /// <see cref="MaxGridDimension"/> 自适应放大）而只替换格子尺寸与占用判定方式。
        /// <paramref name="useAccurateInterior"/> 为 <c>false</c> 时行为与改动前的 <c>BuildGrid</c>
        /// 完全一致（仅采样格子中心，见 <see cref="IsBlockedByRects"/>）；为 <c>true</c> 时改用
        /// "格子内部与阻挡矩形内部是否相交"判定（<see cref="IsCellInteriorBlockedByRects"/>），
        /// 不会再对比格子尺寸更窄的薄障碍视而不见。</summary>
        private NavGrid BuildGridWithCellSize(Id mapId, double baseCellSize, bool useAccurateInterior)
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

            var spanX = Math.Max(max.X - min.X, baseCellSize);
            var spanY = Math.Max(max.Y - min.Y, baseCellSize);
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
                    if (useAccurateInterior)
                    {
                        var cellMin = new Vec2(grid.Origin.X + gx * grid.CellSize, grid.Origin.Y + gy * grid.CellSize);
                        var cellMax = new Vec2(cellMin.X + grid.CellSize, cellMin.Y + grid.CellSize);
                        grid.Walkable[gx, gy] = !IsCellInteriorBlockedByRects(mapId, cellMin, cellMax);
                    }
                    else
                    {
                        var world = CellToWorld(grid, (gx, gy));
                        grid.Walkable[gx, gy] = !IsBlockedByRects(mapId, world);
                    }
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

        /// <summary>NAV-110-02 根治新增：格子（开区间内部 <c>(cellMin,cellMax)</c>）是否与任意一个
        /// 阻挡矩形的内部相交——标准 AABB 开区间重叠判定，与 <see cref="BlockingRect.Contains"/>
        /// （闭区间、单点包含）刻意不同：这里判定的是"矩形是否与格子有任何非零面积的交集"，只要薄墙
        /// 的宽度大于零、且与该格子的范围有重叠，不论薄墙是否窄于格子尺寸，都会被判定为占用——
        /// 因此不依赖格子尺寸必须小于阻挡宽度，只要求判定方式本身是"区间相交"而不是"点包含"。
        /// 仅贴边（区间恰好相切、无正面积重叠）不算相交，同 <see cref="TrySegmentRectInteriorEntry"/>
        /// 一贯的"仅贴边不算受阻"惯例。</summary>
        private bool IsCellInteriorBlockedByRects(Id mapId, Vec2 cellMin, Vec2 cellMax)
        {
            if (!_blockingRects.TryGetValue(mapId, out var rects)) return false;
            foreach (var rect in rects)
            {
                if (cellMin.X < rect.Max.X && cellMax.X > rect.Min.X &&
                    cellMin.Y < rect.Max.Y && cellMax.Y > rect.Min.Y)
                {
                    return true;
                }
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

        /// <summary>把 A* 求出的格子索引路径，转成首尾精确等于 <paramref name="from"/>/<paramref name="to"/>
        /// 的世界坐标路径（类型顶部判断记录 2）：中间沿用格子中心序列，首尾各接一段"精确端点 ->
        /// 最近格子中心"的连接段；接合段用与 <see cref="Raycast"/> 完全同源的 <see cref="SegmentBlocked"/>
        /// 校验，不通过时尝试改接起（止）点所在格的相邻可行格，仍不通过则返回 <c>null</c>。</summary>
        private List<Vec2>? BuildWorldPath(
            Id mapId, NavGrid grid, Vec2 from, Vec2 to,
            (int x, int y) startCell, (int x, int y) goalCell, List<(int x, int y)> cellPath)
        {
            var pts = new List<Vec2>(cellPath.Count + 2) { from };
            foreach (var cell in cellPath)
            {
                pts.Add(CellToWorld(grid, cell));
            }
            pts.Add(to);

            // 首端接合：pts[0]=from，pts[1]=起点格中心。
            if (!TryLink(mapId, pts[0], pts[1]))
            {
                if (pts.Count > 3 && TryLink(mapId, pts[0], pts[2]))
                {
                    // 起点格中心本身接不上，但跳过它直接接下一个路点可行：丢弃这一个多余路点。
                    pts.RemoveAt(1);
                }
                else
                {
                    var nextAnchor = pts.Count > 2 ? pts[2] : to;
                    var replacement = FindAdjacentReconnect(mapId, grid, startCell, from, nextAnchor);
                    if (replacement == null)
                    {
                        return null;
                    }

                    pts[1] = replacement.Value;
                    if (!TryLink(mapId, pts[0], pts[1])) return null;
                    if (pts.Count > 2 && !TryLink(mapId, pts[1], pts[2])) return null;
                }
            }

            // 尾端接合：pts[^2]=止点格中心（或替换后的相邻格中心），pts[^1]=to。
            var n = pts.Count;
            if (!TryLink(mapId, pts[n - 2], pts[n - 1]))
            {
                if (n > 3 && TryLink(mapId, pts[n - 3], pts[n - 1]))
                {
                    pts.RemoveAt(n - 2);
                }
                else
                {
                    n = pts.Count;
                    var prevAnchor = n > 2 ? pts[n - 3] : from;
                    var replacement = FindAdjacentReconnect(mapId, grid, goalCell, to, prevAnchor);
                    if (replacement == null)
                    {
                        return null;
                    }

                    pts[pts.Count - 2] = replacement.Value;
                    if (!TryLink(mapId, pts[pts.Count - 2], pts[pts.Count - 1])) return null;
                    if (pts.Count > 2 && !TryLink(mapId, pts[pts.Count - 3], pts[pts.Count - 2])) return null;
                }
            }

            return pts;
        }

        private bool TryLink(Id mapId, Vec2 a, Vec2 b) => !SegmentBlocked(mapId, a, b);

        /// <summary>NAV-110-01 根治：把一个精确端点 <paramref name="point"/> 量化得到的格子
        /// <paramref name="cell"/> 解析为供 A* 使用的实际入口/出口格。<paramref name="cell"/>
        /// 本身可行走时直接原样返回（不改变既有行为）；<paramref name="cell"/> 受阻时（端点格中心
        /// 恰好落在阻挡内部，但端点自身按 <see cref="IsWalkable"/> 逐点判定可行走——调用方已经确认过
        /// 这一点），在其 8 个邻居里找一个可行走、且与 <paramref name="point"/> 直连不受阻（与
        /// <see cref="Raycast"/> 完全同源的 <see cref="SegmentBlocked"/> 判定）的格子，按到
        /// <paramref name="point"/> 的距离取最近的一个；找不到任何候选（端点被完全围死）时返回
        /// <c>null</c>，由调用方判定该端点确实无法进入网格。</summary>
        private (int x, int y)? ResolveEntryCell(NavGrid grid, Id mapId, (int x, int y) cell, Vec2 point)
        {
            if (grid.Walkable[cell.x, cell.y])
            {
                return cell;
            }

            (int dx, int dy)[] dirs =
            {
                (1, 0), (-1, 0), (0, 1), (0, -1),
                (1, 1), (1, -1), (-1, 1), (-1, -1)
            };

            (int x, int y)? best = null;
            var bestDistSqr = double.MaxValue;

            foreach (var (dx, dy) in dirs)
            {
                var nx = cell.x + dx;
                var ny = cell.y + dy;
                if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height) continue;
                if (!grid.Walkable[nx, ny]) continue;

                var center = CellToWorld(grid, (nx, ny));
                if (SegmentBlocked(mapId, point, center)) continue;

                var distSqr = (center - point).SqrLength;
                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    best = (nx, ny);
                }
            }

            return best;
        }

        /// <summary>接合失败时的兜底：在 <paramref name="originCell"/> 的 8 个邻居里找一个可行走、
        /// 且同时能与 <paramref name="endpoint"/> 和 <paramref name="nextPoint"/> 都直连（不受阻）的
        /// 格子中心，按到 <paramref name="endpoint"/> 的距离取最近的一个；找不到返回 <c>null</c>
        /// （类型顶部判断记录 2"改接相邻可行格或返回 null"）。</summary>
        private Vec2? FindAdjacentReconnect(Id mapId, NavGrid grid, (int x, int y) originCell, Vec2 endpoint, Vec2 nextPoint)
        {
            (int dx, int dy)[] dirs =
            {
                (1, 0), (-1, 0), (0, 1), (0, -1),
                (1, 1), (1, -1), (-1, 1), (-1, -1)
            };

            Vec2? best = null;
            var bestDistSqr = double.MaxValue;

            foreach (var (dx, dy) in dirs)
            {
                var nx = originCell.x + dx;
                var ny = originCell.y + dy;
                if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height) continue;
                if (!grid.Walkable[nx, ny]) continue;

                var center = CellToWorld(grid, (nx, ny));
                if (!TryLink(mapId, endpoint, center)) continue;
                if (!TryLink(mapId, center, nextPoint)) continue;

                var distSqr = (center - endpoint).SqrLength;
                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    best = center;
                }
            }

            return best;
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

                    // 不许切角（类型顶部判断记录 3）：对角移动只有当两个正交邻居格也都可行走时才
                    // 允许——否则会出现"贴着两个被封死的格子斜穿墙角"的失真路径。
                    if (dx != 0 && dy != 0)
                    {
                        var orthoA = (x: currentCell.x + dx, y: currentCell.y);
                        var orthoB = (x: currentCell.x, y: currentCell.y + dy);
                        var orthoAWalkable = orthoA.x >= 0 && orthoA.x < grid.Width && orthoA.y >= 0 && orthoA.y < grid.Height && grid.Walkable[orthoA.x, orthoA.y];
                        var orthoBWalkable = orthoB.x >= 0 && orthoB.x < grid.Width && orthoB.y >= 0 && orthoB.y < grid.Height && grid.Walkable[orthoB.x, orthoB.y];
                        if (!orthoAWalkable || !orthoBWalkable) continue;
                    }

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

        /// <summary>Raycast 与 FindPath 共用的唯一阻挡判定入口（类型顶部判断记录 3）：线段是否穿过
        /// 任意一个已登记阻挡矩形的内部。</summary>
        private bool SegmentBlocked(Id mapId, Vec2 from, Vec2 to)
        {
            if (!_blockingRects.TryGetValue(mapId, out var rects)) return false;
            foreach (var rect in rects)
            {
                if (TrySegmentRectInteriorEntry(from, to, rect.Min, rect.Max, out _)) return true;
            }

            return false;
        }

        /// <summary>
        /// 线段 [from,to] 是否穿过闭矩形 [min,max] 的内部（严格意义上的开区间，即
        /// <c>min.X&lt;x&lt;max.X &amp;&amp; min.Y&lt;y&lt;max.Y</c>），仅贴边/擦角不算受阻
        /// （类型顶部判断记录 3）。做法：先用标准 slab 裁剪求出线段与闭矩形的重叠参数区间
        /// <c>[tMin,tMax]</c>；区间为空（无接触）或退化为单点（恰好相切于边界/角点）都不算受阻；
        /// 区间非退化时取中点做一次"是否严格在内部"的采样——线段方向不与矩形任何一条边共线时，
        /// 非退化重叠区间的内部必然有点落在矩形内部，中点采样足以判定；若线段恰好与某条边共线
        /// （贴着边界走一段），区间内所有点都在边界上，中点采样同样能正确得出"不算受阻"。
        /// </summary>
        private static bool TrySegmentRectInteriorEntry(Vec2 from, Vec2 to, Vec2 min, Vec2 max, out Vec2 hit)
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

            if (tMax - tMin <= InteriorEpsilon)
            {
                // 退化为单点接触（贴边/擦角）：不算受阻。
                hit = default;
                return false;
            }

            var tMid = (tMin + tMax) * 0.5;
            var midPoint = new Vec2(from.X + dx * tMid, from.Y + dy * tMid);
            var interior = midPoint.X > min.X + InteriorEpsilon && midPoint.X < max.X - InteriorEpsilon &&
                           midPoint.Y > min.Y + InteriorEpsilon && midPoint.Y < max.Y - InteriorEpsilon;
            if (!interior)
            {
                // 重叠区间非退化，但代表点仍落在边界上：线段与矩形某条边共线贴行，不算受阻。
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
