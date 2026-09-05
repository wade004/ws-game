// StubSpatialQuery：ISpatialQuery 的最小可用桩实现——对手工登记的对象列表做暴力遍历查询，
// 不构建任何真实空间索引。
// 用途：测试依赖范围查询/最近对象/视线遮挡的上层逻辑，而不依赖真实空间索引结构。
// 与真实实现的差异：查询对象需要先用测试方法 Register(id, position, radius, tags) 登记
// （ISpatialQuery 契约本身不包含注册方法——登记机制是引擎适配层实现的内部细节，见
// 02_引擎适配层.md 第 1.9 节只描述查询语义，未规定对象如何进入索引）；每个登记对象带一个
// 自身半径，查询时按"圆与查询范围是否重叠"判定（而非只判定对象中心点是否落在范围内），
// 这样小范围查询也能查到跨越边界的大对象；结果一律按 Id 序数排序，保证跨平台可复现。
// 角度参数（QueryCone 的 direction/angle、Shape.Cone/Line/Rect 的方向与旋转）统一按弧度处理，
// 因为文档未注明单位，由调用方与实现方自行约定一致（见 ISpatialQuery.cs 顶部判断记录）。
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubSpatialQuery : ISpatialQuery
    {
        private sealed class Entry
        {
            public Id Id;
            public Vec2 Position;
            public double Radius;
            public HashSet<string> Tags = new HashSet<string>(StringComparer.Ordinal);
        }

        private readonly Dictionary<Id, Entry> _entries = new Dictionary<Id, Entry>();

        /// <summary>便捷重载：不带标签登记（大量既有测试调用点只关心位置/半径，见判断记录）。</summary>
        public void Register(Id id, Vec2 position, double radius) => Register(id, position, radius, Array.Empty<string>());

        /// <summary>登记一个对象进空间索引（契约方法，见 ISpatialQuery 第 1.9 节 / ADR-0016 决策 7）。</summary>
        public void Register(Id id, Vec2 position, double radius, IReadOnlyList<string> tags)
        {
            var entry = new Entry { Id = id, Position = position, Radius = radius };
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    entry.Tags.Add(tag);
                }
            }

            _entries[id] = entry;
        }

        /// <summary>同步一个已登记对象的位置（契约方法）。未登记的 id 视为无操作，
        /// 与真实实现"位置变化时调用"的语义保持宽松一致，不因调用时机差异而抛异常。</summary>
        public void UpdatePosition(Id id, Vec2 position)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                entry.Position = position;
            }
        }

        /// <summary>从空间索引移除一个对象的登记（契约方法）。</summary>
        public void Unregister(Id id) => _entries.Remove(id);

        /// <summary>清空整个空间索引（契约方法）。</summary>
        public void Clear() => _entries.Clear();

        public IReadOnlyList<Id> QueryRadius(Vec2 center, double radius, QueryFilter filter)
        {
            return Filtered(filter)
                .Where(e => Vec2.Distance(center, e.Position) <= radius + e.Radius)
                .Select(e => e.Id)
                .OrderBy(id => id)
                .ToList();
        }

        public IReadOnlyList<Id> QueryCone(Vec2 origin, double direction, double angle, double range, QueryFilter filter)
        {
            return Filtered(filter)
                .Where(e => IsInCone(origin, direction, angle, range, e))
                .Select(e => e.Id)
                .OrderBy(id => id)
                .ToList();
        }

        public IReadOnlyList<Id> QueryLine(Vec2 from, Vec2 to, QueryFilter filter)
        {
            return Filtered(filter)
                .Where(e => DistancePointToSegment(e.Position, from, to) <= e.Radius)
                .Select(e => e.Id)
                .OrderBy(id => id)
                .ToList();
        }

        public IReadOnlyList<Id> QueryRect(Vec2 min, Vec2 max, QueryFilter filter)
        {
            return Filtered(filter)
                .Where(e => CircleOverlapsAabb(e.Position, e.Radius, min, max))
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
                    return Filtered(filter)
                        .Where(e => IsInLineShape(shape, e))
                        .Select(e => e.Id)
                        .OrderBy(id => id)
                        .ToList();
                case ShapeKind.Rect:
                    return Filtered(filter)
                        .Where(e => IsInRotatedRect(shape, e))
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

            foreach (var entry in Filtered(filter))
            {
                var distanceSqr = (entry.Position - point).SqrLength;
                if (distanceSqr < closestDistanceSqr ||
                    (distanceSqr == closestDistanceSqr && closest != null && entry.Id.CompareTo(closest.Id) < 0))
                {
                    closestDistanceSqr = distanceSqr;
                    closest = entry;
                }
            }

            return closest == null ? (Id?)null : closest.Id;
        }

        /// <summary>桩实现默认视线永不受阻；如需模拟遮挡，可在测试子类中覆盖或另行扩展。</summary>
        public bool HasLineOfSight(Vec2 from, Vec2 to) => true;

        private IEnumerable<Entry> Filtered(QueryFilter filter)
        {
            foreach (var entry in _entries.Values)
            {
                var matchesRequired = filter.RequiredTags.Count == 0 || filter.RequiredTags.All(entry.Tags.Contains);
                var matchesExcluded = filter.ExcludedTags.Count == 0 || !filter.ExcludedTags.Any(entry.Tags.Contains);
                if (matchesRequired && matchesExcluded)
                {
                    yield return entry;
                }
            }
        }

        private static bool IsInCone(Vec2 origin, double direction, double angle, double range, Entry entry)
        {
            var toEntry = entry.Position - origin;
            var distance = toEntry.Length;
            if (distance > range + entry.Radius)
            {
                return false;
            }

            if (distance <= entry.Radius)
            {
                return true; // 对象覆盖了锥形顶点
            }

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
            if (lengthSqr <= double.Epsilon)
            {
                return Vec2.Distance(point, from);
            }

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
