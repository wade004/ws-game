using System;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// <see cref="Shape"/> 的"给定单点，判断是否落在形状内"几何判定（05_对象模型与世界.md 第 3.5
    /// 节定义了 <see cref="Shape"/> 联合类型与 <see cref="ISpatialQuery.QueryShape"/>——按形状查询
    /// 场上已登记对象集合，但没有一个面向任意坐标点的独立方法，见
    /// <c>Core.Gameplay.Encounter.EncounterShapeMath</c> 同名判断记录）。本类型是同一几何需求在
    /// L-1 的通用化版本：<c>EncounterShapeMath</c> 是 L4 内部私有实现，只覆盖该模块自己的一个用法，
    /// 不能被 L2 的 <c>core/rules/targeting</c>/<c>core/rules/skill</c> 引用（依赖方向不允许 L2 依赖
    /// L4）；格子吸附（ADR-0013 决策 6、04 第 3.1 节 <c>grid_snap</c>）要求"范围形状按格子中心采样"
    /// ——判定候选对象格子中心点是否落在形状内，需要一份两个 L2 模块都能引用的同类判定，因此在 L-1
    /// 新增一份公开版本，与 <c>EncounterShapeMath</c> 各自独立维护（同一段 ~40 行的几何算法出现两处
    /// 是刻意的权衡：把 L4 私有实现提升为公开契约会改变它的既有可见性/兼容性约束，超出本任务"落地
    /// 格子吸附"的范围，不顺带做这次重构）。角度统一按弧度制处理（同 <see cref="Shape"/> 类型注释
    /// "角度单位由调用方与实现方约定"）。
    /// </summary>
    public static class ShapeGeometry
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
