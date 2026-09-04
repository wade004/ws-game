using System;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// "某点是否落在某 <see cref="Shape"/> 内"的纯几何判定（见 05_对象模型与世界.md 第 3.5 节四种
    /// 形状定义）。<see cref="AreaTriggerHost"/> 用它判断单位当前位置与已登记触发范围的进入/离开
    /// 关系，不经 <see cref="Core.Foundation.EngineAdapter.ISpatialQuery"/>（判断记录：
    /// <c>ISpatialQuery</c> 是"给一个形状查一批已登记对象"的空间索引查询，本模块只需要"给一个点
    /// 判断是否在一个已知形状内"这一更简单的关系，且区域触发的数量级远小于战斗目标查询，直接算
    /// 更简单、不需要额外维护一份"全部单位"的空间索引登记）。角度（<see cref="Shape.Angle"/>/
    /// <see cref="Shape.Direction"/>/<see cref="Shape.Rotation"/>）统一按弧度处理（见
    /// <see cref="AreaTriggerShapeJson"/> 判断记录）。
    /// </summary>
    public static class AreaTriggerShapeGeometry
    {
        public static bool Contains(Shape shape, Vec2 point)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Circle:
                    return Vec2.Distance(point, shape.Origin) <= shape.Radius;

                case ShapeKind.Rect:
                    return ContainsRect(shape, point);

                case ShapeKind.Cone:
                    return ContainsCone(shape, point);

                case ShapeKind.Line:
                    return ContainsLine(shape, point);

                default:
                    return false;
            }
        }

        /// <summary>把 <paramref name="point"/> 变换到以 <paramref name="origin"/> 为原点、绕
        /// <paramref name="rotation"/> 旋转后的局部坐标系（供 rect/line 的"矩形带"判定复用）。</summary>
        private static (double X, double Y) ToLocal(Vec2 point, Vec2 origin, double rotation)
        {
            var dx = point.X - origin.X;
            var dy = point.Y - origin.Y;
            var cos = Math.Cos(-rotation);
            var sin = Math.Sin(-rotation);
            return (dx * cos - dy * sin, dx * sin + dy * cos);
        }

        private static bool ContainsRect(Shape shape, Vec2 point)
        {
            var local = ToLocal(point, shape.Origin, shape.Rotation);
            return Math.Abs(local.X) <= shape.HalfExtents.X && Math.Abs(local.Y) <= shape.HalfExtents.Y;
        }

        /// <summary>line 以 <see cref="Shape.Origin"/> 为起点、沿 <see cref="Shape.Direction"/> 延伸
        /// <see cref="Shape.Length"/>、宽 <see cref="Shape.Width"/>（见 05 第 3.5 节 line 定义"以
        /// origin 为起点"，与 rect 以中心为原点不同：局部 X 落在 [0, Length] 区间，不是
        /// [-Length/2, Length/2]）。</summary>
        private static bool ContainsLine(Shape shape, Vec2 point)
        {
            var local = ToLocal(point, shape.Origin, shape.Direction);
            return local.X >= 0 && local.X <= shape.Length && Math.Abs(local.Y) <= shape.Width / 2.0;
        }

        private static bool ContainsCone(Shape shape, Vec2 point)
        {
            var dx = point.X - shape.Origin.X;
            var dy = point.Y - shape.Origin.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > shape.Radius)
            {
                return false;
            }

            if (distance < 1e-9)
            {
                // 顶点本身：与朝向无关，视为落在扇形内。
                return true;
            }

            var pointAngle = Math.Atan2(dy, dx);
            var diff = NormalizeAngle(pointAngle - shape.Direction);
            return Math.Abs(diff) <= shape.Angle / 2.0;
        }

        private static double NormalizeAngle(double angle)
        {
            while (angle > Math.PI)
            {
                angle -= 2 * Math.PI;
            }

            while (angle < -Math.PI)
            {
                angle += 2 * Math.PI;
            }

            return angle;
        }
    }
}
