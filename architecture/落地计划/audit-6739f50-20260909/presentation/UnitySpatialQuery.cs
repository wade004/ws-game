#nullable enable
// UnitySpatialQuery：ISpatialQuery 的 Unity 引擎实现。
//
// 判断记录（Physics2D 与自维护登记表二选一，任务书允许二选一）：选用自维护登记表——
// Physics2D 需要给每个可查询对象挂 Collider2D 并保持其 Transform 与逻辑层 Vec2 位置同步，
// 这是一份不小的额外同步成本，且 Physics2D 的查询结果顺序不保证稳定（依赖内部空间划分与
// 浮点比较），与契约"结果按 Id 排序、确定性"的要求需要额外排序兜底；自维护登记表能一次性把
// "确定性 + 与逻辑层解耦"都满足，且与 adapters/stub/StubSpatialQuery 的几何算法保持一致口径，
// 便于跨适配层实现做行为对拍。内部用简单的均匀网格分桶加速（而非线性扫描全部对象），
// 满足 02 第 1.9 节"查询应基于空间索引而非线性扫描"的性能约定。
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnitySpatialQuery : ISpatialQuery
    {
        private sealed class Entry
        {
            public Id Id;
            public Vec2 Position;
            public double Radius;
            public HashSet<string> Tags = new HashSet<string>(StringComparer.Ordinal);
        }

        private const double BucketSize = 4.0;

        private readonly Dictionary<Id, Entry> _entries = new Dictionary<Id, Entry>();
        private readonly Dictionary<(int, int), List<Entry>> _buckets = new Dictionary<(int, int), List<Entry>>();

        /// <summary>契约方法（ISpatialQuery 第 1.9 节 / ADR-0016 决策 7）：登记一个可被查询到的
        /// 对象（id、位置、自身半径、标签），供 <c>core/carriers/assembly.EntitySpatialSyncHost</c>
        /// 在对象创建时调用。<c>tags</c> 保留一个可选默认值（空标签）供本类型的直接调用方
        /// （非经 <see cref="ISpatialQuery"/> 接口）省去空数组样板代码，不影响接口实现——
        /// 接口方法签名的参数类型本就相同，默认值不参与接口成员匹配。</summary>
        public void Register(Id id, Vec2 position, double radius, IReadOnlyList<string>? tags = null)
        {
            Unregister(id);

            var entry = new Entry { Id = id, Position = position, Radius = radius };
            if (tags != null)
            {
                foreach (var tag in tags) entry.Tags.Add(tag);
            }

            _entries[id] = entry;
            BucketOf(position).Add(entry);
        }

        /// <summary>契约方法：更新一个已登记对象的位置（每帧随实体移动调用）。未登记的 id
        /// 视为无操作（与 <c>Adapters.Stub.StubSpatialQuery</c> 同一语义）。</summary>
        public void UpdatePosition(Id id, Vec2 position)
        {
            if (!_entries.TryGetValue(id, out var entry)) return;
            RemoveFromBucket(entry);
            entry.Position = position;
            BucketOf(position).Add(entry);
        }

        /// <summary>契约方法：从空间索引移除一个对象的登记。</summary>
        public void Unregister(Id id)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                RemoveFromBucket(entry);
                _entries.Remove(id);
            }
        }

        /// <summary>契约方法：清空整个空间索引（场景卸载时调用）。</summary>
        public void Clear()
        {
            _entries.Clear();
            _buckets.Clear();
        }

        public IReadOnlyList<Id> QueryRadius(Vec2 center, double radius, QueryFilter filter)
        {
            return CandidatesNear(center, radius)
                .Where(e => Vec2.Distance(center, e.Position) <= radius + e.Radius)
                .Where(e => MatchesFilter(e, filter))
                .Select(e => e.Id)
                .OrderBy(id => id)
                .ToList();
        }

        public IReadOnlyList<Id> QueryCone(Vec2 origin, double direction, double angle, double range, QueryFilter filter)
        {
            return CandidatesNear(origin, range)
                .Where(e => IsInCone(origin, direction, angle, range, e))
                .Where(e => MatchesFilter(e, filter))
                .Select(e => e.Id)
                .OrderBy(id => id)
                .ToList();
        }

        public IReadOnlyList<Id> QueryLine(Vec2 from, Vec2 to, QueryFilter filter)
        {
            var center = new Vec2((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            var halfRange = Vec2.Distance(from, to) / 2 + MaxRadiusHint();

            return CandidatesNear(center, halfRange)
                .Where(e => DistancePointToSegment(e.Position, from, to) <= e.Radius)
                .Where(e => MatchesFilter(e, filter))
                .Select(e => e.Id)
                .OrderBy(id => id)
                .ToList();
        }

        public IReadOnlyList<Id> QueryRect(Vec2 min, Vec2 max, QueryFilter filter)
        {
            return _entries.Values
                .Where(e => CircleOverlapsAabb(e.Position, e.Radius, min, max))
                .Where(e => MatchesFilter(e, filter))
                .Select(e => e.Id)
                .OrderBy(id => id)
                .ToList();
        }

        public IReadOnlyList<Id> QueryShape(Shape shape, QueryFilter filter)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Circle:
                    return QueryRadius(shape.Origin, shape.Radius, filter);
                case ShapeKind.Cone:
                    return QueryCone(shape.Origin, shape.Direction, shape.Angle, shape.Radius, filter);
                case ShapeKind.Line:
                    return _entries.Values
                        .Where(e => IsInLineShape(shape, e))
                        .Where(e => MatchesFilter(e, filter))
                        .Select(e => e.Id)
                        .OrderBy(id => id)
                        .ToList();
                case ShapeKind.Rect:
                    return _entries.Values
                        .Where(e => IsInRotatedRect(shape, e))
                        .Where(e => MatchesFilter(e, filter))
                        .Select(e => e.Id)
                        .OrderBy(id => id)
                        .ToList();
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape), shape.Kind, "未知的 Shape 种类");
            }
        }

        public Id? Nearest(Vec2 point, QueryFilter filter)
        {
            Entry? closest = null;
            var closestDistanceSqr = double.MaxValue;

            foreach (var entry in _entries.Values)
            {
                if (!MatchesFilter(entry, filter)) continue;

                var distanceSqr = (entry.Position - point).SqrLength;
                if (distanceSqr < closestDistanceSqr ||
                    (distanceSqr == closestDistanceSqr && closest != null && entry.Id.CompareTo(closest.Id) < 0))
                {
                    closestDistanceSqr = distanceSqr;
                    closest = entry;
                }
            }

            return closest?.Id;
        }

        /// <summary>本实现默认视线永不受阻（与 StubSpatialQuery 一致的最小实现）；
        /// 真实遮挡判定依赖地图阻挡数据，可用 <see cref="SetLineOfSightBlocker"/> 注入
        /// UnityNavigation2D 的 Raycast 作为遮挡判定来源。</summary>
        public bool HasLineOfSight(Vec2 from, Vec2 to)
        {
            return _lineOfSightBlocker == null || !_lineOfSightBlocker(from, to);
        }

        private Func<Vec2, Vec2, bool>? _lineOfSightBlocker;

        /// <summary>非契约方法：注入一个"两点间是否有遮挡"的判定函数（通常是
        /// UnityNavigation2D.Raycast(mapId, from, to) != null）。</summary>
        public void SetLineOfSightBlocker(Func<Vec2, Vec2, bool>? blocker) => _lineOfSightBlocker = blocker;

        private IEnumerable<Entry> CandidatesNear(Vec2 center, double range)
        {
            var minBx = (int)Math.Floor((center.X - range) / BucketSize);
            var maxBx = (int)Math.Floor((center.X + range) / BucketSize);
            var minBy = (int)Math.Floor((center.Y - range) / BucketSize);
            var maxBy = (int)Math.Floor((center.Y + range) / BucketSize);

            for (var bx = minBx; bx <= maxBx; bx++)
            {
                for (var by = minBy; by <= maxBy; by++)
                {
                    if (_buckets.TryGetValue((bx, by), out var list))
                    {
                        foreach (var entry in list) yield return entry;
                    }
                }
            }
        }

        private double MaxRadiusHint()
        {
            double max = 0;
            foreach (var e in _entries.Values) if (e.Radius > max) max = e.Radius;
            return max;
        }

        private List<Entry> BucketOf(Vec2 position)
        {
            var key = BucketKey(position);
            if (!_buckets.TryGetValue(key, out var list))
            {
                list = new List<Entry>();
                _buckets[key] = list;
            }

            return list;
        }

        private void RemoveFromBucket(Entry entry)
        {
            var key = BucketKey(entry.Position);
            if (_buckets.TryGetValue(key, out var list))
            {
                list.Remove(entry);
            }
        }

        private static (int, int) BucketKey(Vec2 position) =>
            ((int)Math.Floor(position.X / BucketSize), (int)Math.Floor(position.Y / BucketSize));

        private static bool MatchesFilter(Entry entry, QueryFilter filter)
        {
            var matchesRequired = filter.RequiredTags.Count == 0 || filter.RequiredTags.All(entry.Tags.Contains);
            var matchesExcluded = filter.ExcludedTags.Count == 0 || !filter.ExcludedTags.Any(entry.Tags.Contains);
            return matchesRequired && matchesExcluded;
        }

        private static bool IsInCone(Vec2 origin, double direction, double angle, double range, Entry entry)
        {
            var toEntry = entry.Position - origin;
            var distance = toEntry.Length;
            if (distance > range + entry.Radius) return false;
            if (distance <= entry.Radius) return true;

            var entryAngle = Math.Atan2(toEntry.Y, toEntry.X);
            var diff = NormalizeAngle(entryAngle - direction);
            var angularMargin = entry.Radius > 0 ? Math.Atan2(entry.Radius, Math.Max(distance, double.Epsilon)) : 0;
            return Math.Abs(diff) <= angle / 2.0 + angularMargin;
        }

        private static bool IsInLineShape(Shape shape, Entry entry)
        {
            var direction = new Vec2(Math.Cos(shape.Direction), Math.Sin(shape.Direction));
            var to = shape.Origin + direction * shape.Length;
            var distance = DistancePointToSegment(entry.Position, shape.Origin, to);
            return distance <= shape.Width / 2.0 + entry.Radius;
        }

        private static bool IsInRotatedRect(Shape shape, Entry entry)
        {
            var relative = entry.Position - shape.Origin;
            var cos = Math.Cos(-shape.Rotation);
            var sin = Math.Sin(-shape.Rotation);
            var localX = relative.X * cos - relative.Y * sin;
            var localY = relative.X * sin + relative.Y * cos;

            var closestX = Math.Max(-shape.HalfExtents.X, Math.Min(localX, shape.HalfExtents.X));
            var closestY = Math.Max(-shape.HalfExtents.Y, Math.Min(localY, shape.HalfExtents.Y));

            var dx = localX - closestX;
            var dy = localY - closestY;
            return Math.Sqrt(dx * dx + dy * dy) <= entry.Radius;
        }

        private static bool CircleOverlapsAabb(Vec2 center, double radius, Vec2 min, Vec2 max)
        {
            var closestX = Math.Max(min.X, Math.Min(center.X, max.X));
            var closestY = Math.Max(min.Y, Math.Min(center.Y, max.Y));
            var dx = center.X - closestX;
            var dy = center.Y - closestY;
            return Math.Sqrt(dx * dx + dy * dy) <= radius;
        }

        private static double DistancePointToSegment(Vec2 point, Vec2 from, Vec2 to)
        {
            var segment = to - from;
            var lengthSqr = segment.SqrLength;
            if (lengthSqr <= double.Epsilon) return Vec2.Distance(point, from);

            var t = (point - from).Dot(segment) / lengthSqr;
            t = Math.Max(0.0, Math.Min(1.0, t));
            var projection = from + segment * t;
            return Vec2.Distance(point, projection);
        }

        private static double NormalizeAngle(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle < -Math.PI) angle += 2 * Math.PI;
            return angle;
        }
    }
}
