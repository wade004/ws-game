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
    /// <summary><see cref="ISpatialQuery"/> 的最小桩实现：对测试手工登记的对象做线性扫描的范围/射线查询。</summary>
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

        private Func<Vec2, Vec2, bool>? _lineOfSightBlocker;

        /// <summary>
        /// 桩实现默认视线永不受阻（没有注入遮挡判定时恒为 <c>true</c>，既有行为逐位不变）；注入了遮挡判定（<see cref="SetLineOfSightBlocker"/>、
        /// <see cref="UseNavigationLineOfSight"/>）后，两点连线被遮挡即无视线。
        /// </summary>
        public bool HasLineOfSight(Vec2 from, Vec2 to) => _lineOfSightBlocker == null || !_lineOfSightBlocker(from, to);

        /// <summary>
        /// 非契约方法（同 <c>UnitySpatialQuery.SetLineOfSightBlocker</c>）：注入"两点间是否有遮挡"的判定函数；传 <c>null</c> 恢复"视线永不受阻"。
        /// </summary>
        public void SetLineOfSightBlocker(Func<Vec2, Vec2, bool>? blocker) => _lineOfSightBlocker = blocker;

        /// <summary>
        /// 手感落地 M4-W3：视线遮挡取自导航的阻挡判定——<c>navigation.Raycast(mapId, from, to)</c> 返回非空即被遮挡
        /// （<see cref="INavigation2D.Raycast"/> 契约本就写明"视线是否受阻即 Raycast 返回值是否非空"）。导航在每次查询时读取当前阻挡集合，
        /// 所以静态地形阻挡与运行期登记的动态阻挡（<c>SetBlocking</c>/<c>AddBlocking</c>/<c>RemoveBlocking</c>）都立刻生效。
        /// </summary>
        public void UseNavigationLineOfSight(INavigation2D navigation, Id mapId)
        {
            if (navigation == null) throw new ArgumentNullException(nameof(navigation));
            _lineOfSightBlocker = (from, to) => navigation.Raycast(mapId, from, to).HasValue;
        }

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

        /// <summary>
        /// ADR-0125 第三批（StubSpatialQuery.QueryShape(Line) 口径）：线带是<b>矩形带</b>（端面平直，05 §3.5 与
        /// <see cref="ShapeGeometry.Contains"/> 同口径），不是"点到线段距离 &lt;= width/2 + r"的胶囊——后者在端面外
        /// 侧有圆头，零半径实体落在端面外 &lt;= width/2 处会被误命中，带半径实体在端面外 gap &gt; r 处也会被误命中。
        /// 判定：实体中心落在带内（直接复用 <see cref="ShapeGeometry.Contains"/>）即命中；带半径实体另按
        /// "圆与矩形带相交"判定（局部坐标下圆心到带的最近点距离 &lt;= r，闭区间，同本类 <see cref="IsInRotatedRect"/>
        /// 对 Rect 的口径）。
        /// </summary>
        private static bool IsInLineShape(Shape shape, Entry entry)
        {
            if (ShapeGeometry.Contains(shape, entry.Position))
            {
                return true;
            }

            if (entry.Radius <= 0)
            {
                return false;
            }

            var relative = entry.Position - shape.Origin;
            var cos = Math.Cos(-shape.Direction);
            var sin = Math.Sin(-shape.Direction);
            var localX = relative.X * cos - relative.Y * sin;
            var localY = relative.X * sin + relative.Y * cos;

            var halfWidth = shape.Width / 2.0;
            var closestX = Math.Max(0.0, Math.Min(localX, shape.Length));
            var closestY = Math.Max(-halfWidth, Math.Min(localY, halfWidth));

            var dx = localX - closestX;
            var dy = localY - closestY;
            return Math.Sqrt(dx * dx + dy * dy) <= entry.Radius;
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
