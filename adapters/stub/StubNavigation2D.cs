// StubNavigation2D：INavigation2D 的最小可用桩实现——直线导航，不构建任何真实导航网格。
// 用途：测试依赖寻路/可行走判定/视线遮挡的上层逻辑，而不依赖真实导航网格生成算法。
// 与真实实现的差异：FindPath 只返回起点、终点两点组成的直线路径（找不到时才返回 null），
// 不做任何绕障路径规划；可行走判定默认全部可走，调用方可用契约方法 SetBlocking(mapId, rects)
// 登记按地图分组的阻挡矩形（Rect 的 AABB），IsWalkable/FindPath/Raycast 均据此判定；
// SetBlocking 语义为整批替换（不是追加）——判断记录：ADR-0016 决策 7 与 02 第 1.8 节都只说
// "登记一批"，未规定与既有登记的关系；契约没有单独的"移除某一个矩形"方法，若语义是追加，
// 调用方将永远无法在不 Clear 整图的前提下移除单个阻挡（例如重新打开一扇门），
// 因此按"调用方自行维护当前完整阻挡集合、每次整批替换"实现，Clear(mapId) 用于场景卸载重置。
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

        private readonly HashSet<Id> _builtMaps = new HashSet<Id>();
        private readonly Dictionary<Id, List<BlockingRect>> _blockingRects = new Dictionary<Id, List<BlockingRect>>();

        public void BuildNavMesh(Id mapId) => _builtMaps.Add(mapId);

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

        /// <summary>Slab 法求线段与 AABB 的最近入射交点。</summary>
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
        }

        /// <summary>契约方法：清空某地图的全部动态阻挡登记。</summary>
        public void Clear(Id mapId) => _blockingRects.Remove(mapId);
    }
}
