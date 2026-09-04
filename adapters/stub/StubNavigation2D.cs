// StubNavigation2D：INavigation2D 的最小可用桩实现——直线导航，不构建任何真实导航网格。
// 用途：测试依赖寻路/可行走判定/视线遮挡的上层逻辑，而不依赖真实导航网格生成算法。
// 与真实实现的差异：FindPath 只返回起点、终点两点组成的直线路径（找不到时才返回 null），
// 不做任何绕障路径规划；可行走判定默认全部可走，测试可用 AddBlockingRect 登记按地图分组的
// 阻挡矩形（Vec2 min, Vec2 max 的 AABB），IsWalkable/FindPath/Raycast 均据此判定。
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

        /// <summary>测试用：为某张地图登记一个矩形阻挡区域。</summary>
        public void AddBlockingRect(Id mapId, Vec2 min, Vec2 max)
        {
            if (!_blockingRects.TryGetValue(mapId, out var rects))
            {
                rects = new List<BlockingRect>();
                _blockingRects[mapId] = rects;
            }

            rects.Add(new BlockingRect(min, max));
        }

        /// <summary>测试用：清空某张地图的全部阻挡区域。</summary>
        public void ClearBlockingRects(Id mapId) => _blockingRects.Remove(mapId);
    }
}
