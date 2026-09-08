// StubNavigation2D：INavigation2D 的最小可用桩实现——直线导航，不构建任何真实导航网格。
// 用途：测试依赖寻路/可行走判定/视线遮挡的上层逻辑，而不依赖真实导航网格生成算法。
// 与真实实现的差异：FindPath 只返回起点、终点两点组成的直线路径（找不到时才返回 null），
// 不做任何绕障路径规划；可行走判定默认全部可走，调用方可用契约方法 SetBlocking(mapId, rects)
// 登记按地图分组的阻挡矩形（Rect 的 AABB），IsWalkable/FindPath/Raycast 均据此判定；
// SetBlocking 语义为整批替换（不是追加）——判断记录：ADR-0016 决策 7 与 02 第 1.8 节都只说
// "登记一批"，未规定与既有登记的关系；契约没有单独的"移除某一个矩形"方法，若语义是追加，
// 调用方将永远无法在不 Clear 整图的前提下移除单个阻挡（例如重新打开一扇门），
// 因此按"调用方自行维护当前完整阻挡集合、每次整批替换"实现，Clear(mapId) 用于场景卸载重置。
// 游戏侧通用能力需求（02 第 1.8 节勘误，见各成员判断记录）：GetBlockingVersion 按 mapId 独立计数，
// BuildNavMesh/SetBlocking/Clear 均递增；FindPath 补齐端点契约（不可行走端点/零长度目标）；
// Raycast/FindPath 共用的线段-矩形相交判定改为"内部相交才受阻，边界/角点相切不算受阻"（本桩仍然
// 不做网格寻路/绕障，"网格实现对角邻居不切角"一条不适用于本桩）。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubNavigation2D : INavigation2D
    {
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

        /// <summary>零长度目标的判定阈值，与 02 第 1.8 节勘误"端点契约"、
        /// <c>Core.Carriers.Unit.MovementTickHandler.ZeroLengthEpsilon</c> 取同一常量——两侧各自独立
        /// 定义（不跨程序集共享一个常量，避免 adapters/stub 反向依赖 core/carriers），保证数值上
        /// 一致即可。</summary>
        private const double ZeroLengthEpsilon = 1e-6;

        private readonly HashSet<Id> _builtMaps = new HashSet<Id>();
        private readonly Dictionary<Id, List<BlockingRect>> _blockingRects = new Dictionary<Id, List<BlockingRect>>();

        // 游戏侧通用能力需求（02 第 1.8 节勘误 GetBlockingVersion）：按 mapId 独立计数的动态阻挡
        // 版本号，BuildNavMesh/SetBlocking/Clear 均递增（见三处调用点、GetBlockingVersion 判断记录）。
        private readonly Dictionary<Id, int> _blockingVersions = new Dictionary<Id, int>();

        public void BuildNavMesh(Id mapId)
        {
            _builtMaps.Add(mapId);
            BumpBlockingVersion(mapId);
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

        /// <summary>
        /// 端点契约（见 02 第 1.8 节勘误）：<paramref name="from"/>/<paramref name="to"/> 任一不可
        /// 行走先返回 null（优先级高于零长度判断——"起终点重合但那一点本身不可行走"仍然是不可行走，
        /// 不构成一次合法的"原地不动"路径）；否则 <c>|from-to| &lt;= 1e-6</c>（零长度目标）返回单
        /// 元素路径 <c>[from]</c>；否则线段受阻返回 null，均不受阻返回 <c>[from, to]</c>（本桩不做
        /// 网格寻路/绕障，见类型顶部注释）。
        /// </summary>
        public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to)
        {
            if (!IsWalkable(mapId, from) || !IsWalkable(mapId, to))
            {
                return null;
            }

            if ((to - from).Length <= ZeroLengthEpsilon)
            {
                return new List<Vec2> { from };
            }

            if (SegmentBlocked(mapId, from, to))
            {
                return null;
            }

            return new List<Vec2> { from, to };
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

        private bool SegmentBlocked(Id mapId, Vec2 from, Vec2 to)
        {
            if (!_blockingRects.TryGetValue(mapId, out var rects))
            {
                return false;
            }

            foreach (var rect in rects)
            {
                if (TrySegmentAabbEntry(from, to, rect.Min, rect.Max, out _))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Slab 法求线段与 AABB **内部**的最近入射交点（见 02 第 1.8 节勘误"统一可通行
        /// 规则"——线段与阻挡区域内部相交才算受阻，仅与边界/角点相切不算受阻，判断记录见
        /// <see cref="ClipAxis"/>）。</summary>
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

        /// <summary>
        /// 判断记录（游戏侧通用能力需求，02 第 1.8 节勘误"统一可通行规则"）：本方法与此前版本唯一的
        /// 差异是把两处比较从闭区间改为开区间——常量轴分支 <c>origin &gt;= boundMin &amp;&amp; origin
        /// &lt;= boundMax</c> 改为 <c>origin &gt; boundMin &amp;&amp; origin &lt; boundMax</c>，最终
        /// 判定 <c>tMin &lt;= tMax</c> 改为 <c>tMin &lt; tMax</c>。原因：线段沿矩形某条边"贴着走"
        /// （例如水平线段恰好落在阻挡矩形的上边界 <c>y == max.Y</c> 上）在闭区间语义下会被判定为
        /// "受阻"，但该线段全程没有进入矩形内部，只是与边界重合——这不符合"内部相交才受阻，边界/
        /// 角点相切不算受阻"的统一规则。改为开区间后：常量轴上 origin 恰好等于边界值时该轴视为
        /// "未进入"（两侧分支都不满足严格不等式），最终 <c>t</c> 参数区间退化为单点（<c>tMin ==
        /// tMax</c>）时也视为"只是切过一个点，未真正进入内部"。两处改动都只影响"贴边/切角"这一类
        /// 边界情形，线段真正穿过矩形内部的情形（存在正长度的参数子区间使得两轴同时严格落在开区间
        /// 内）判定结果不变，见本模块 README 判断记录与配套测试
        /// （<c>core/foundation/engine_adapter/tests/StubNavigation2DTests.cs</c>）。
        /// </summary>
        private static bool ClipAxis(double origin, double delta, double boundMin, double boundMax, ref double tMin, ref double tMax)
        {
            if (Math.Abs(delta) < double.Epsilon)
            {
                return origin > boundMin && origin < boundMax;
            }

            var t1 = (boundMin - origin) / delta;
            var t2 = (boundMax - origin) / delta;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            return tMin < tMax;
        }

        /// <summary>契约方法（见 INavigation2D 第 1.8 节 / ADR-0016 决策 7）：以传入的整批矩形
        /// 替换该地图当前登记的动态阻挡（不是追加，见类型顶部判断记录）。</summary>
        public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects)
        {
            var list = new List<BlockingRect>(rects.Count);
            foreach (var rect in rects)
            {
                list.Add(new BlockingRect(rect.Min, rect.Max));
            }

            _blockingRects[mapId] = list;
            BumpBlockingVersion(mapId);
        }

        /// <summary>契约方法：清空某地图的全部动态阻挡登记。</summary>
        public void Clear(Id mapId)
        {
            _blockingRects.Remove(mapId);
            BumpBlockingVersion(mapId);
        }

        /// <summary>契约方法（游戏侧通用能力需求，见 INavigation2D 第 1.8 节勘误）：本桩按 mapId
        /// 独立计数，从未调用过 BuildNavMesh/SetBlocking/Clear 的地图返回 0——与
        /// <see cref="INavigation2D.GetBlockingVersion"/> 默认实现"不支持版本追踪"共享同一个"0"
        /// 取值，consumer 侧据此跳过重验；由于第一次真正的变更调用会把版本从 0 递增到 1，这个共享
        /// 不会掩盖任何一次真实的阻挡变化（见该接口成员判断记录）。</summary>
        public int GetBlockingVersion(Id mapId) => _blockingVersions.TryGetValue(mapId, out var v) ? v : 0;

        private void BumpBlockingVersion(Id mapId) =>
            _blockingVersions[mapId] = (_blockingVersions.TryGetValue(mapId, out var v) ? v : 0) + 1;
    }
}
