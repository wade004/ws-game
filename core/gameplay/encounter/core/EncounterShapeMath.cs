using System;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <c>arena_rules.bounds_shape</c>（<see cref="Shape"/>）的"点是否在形状内"判定（供
    /// <see cref="EncounterHost"/> 判断玩家是否离开场地边界，见 08 第 4.3 节
    /// <c>arena_rules.reset_if_leave</c>）。
    /// <para>
    /// 契约缺口判断记录：05 第 3.5 节定义了 <see cref="Shape"/> 联合类型与
    /// <see cref="ISpatialQuery.QueryShape"/>（按形状查询场上对象），但没有一个"给定单点，判断是否
    /// 落在形状内"的独立方法（<c>ISpatialQuery.QueryShape</c> 面向"查询场上已登记对象集合"，不是
    /// 面向任意一个坐标点，且要为了判断一个点而调用一次全场景查询也不合适）。本类型是本模块自行
    /// 补上的最小实现，只覆盖 <see cref="EncounterHost"/> 实际用到的"玩家当前坐标是否仍在
    /// bounds_shape 内"这一个用法，不追求作为 <c>Shape</c> 类型本身的通用几何库；角度统一按弧度制
    /// 处理（同 <see cref="Shape"/> 类型注释"角度单位由调用方与实现方约定"）。
    /// </para>
    /// </summary>
    internal static class EncounterShapeMath
    {
        public static bool Contains(Shape shape, Vec2 point)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Circle:
                    return Vec2.Distance(shape.Origin, point) <= shape.Radius;

                case ShapeKind.Rect:
                {
                    var (localX, localY) = ToLocal(shape.Origin, point, shape.Rotation);
                    return Math.Abs(localX) <= shape.HalfExtents.X && Math.Abs(localY) <= shape.HalfExtents.Y;
                }

                case ShapeKind.Cone:
                {
                    var toPoint = point - shape.Origin;
                    var distance = toPoint.Length;
                    if (distance > shape.Radius)
                    {
                        return false;
                    }
                    if (distance < 1e-9)
                    {
                        return true;
                    }
                    var angleToPoint = Math.Atan2(toPoint.Y, toPoint.X);
                    var delta = NormalizeAngle(angleToPoint - shape.Direction);
                    return Math.Abs(delta) <= shape.Angle / 2.0;
                }

                case ShapeKind.Line:
                {
                    var (alongAxis, acrossAxis) = ToLocal(shape.Origin, point, shape.Direction);
                    return alongAxis >= 0 && alongAxis <= shape.Length && Math.Abs(acrossAxis) <= shape.Width / 2.0;
                }

                default:
                    return false;
            }
        }

        private static (double, double) ToLocal(Vec2 origin, Vec2 point, double axisRotation)
        {
            var dx = point.X - origin.X;
            var dy = point.Y - origin.Y;
            var cos = Math.Cos(-axisRotation);
            var sin = Math.Sin(-axisRotation);
            return (dx * cos - dy * sin, dx * sin + dy * cos);
        }

        private static double NormalizeAngle(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle < -Math.PI) angle += 2 * Math.PI;
            return angle;
        }
    }
}
