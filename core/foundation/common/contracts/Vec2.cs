using System;

namespace Core.Foundation.Common
{
    /// <summary>
    /// 全架构统一的二维平面坐标类型（见 00_架构总则.md 第 4.1 节）。逻辑层的世界坐标、
    /// 移动、范围判定均使用平面二维坐标（架构定位声明：逻辑均为二维平面坐标）。
    /// 数值统一用 double（对应记法约定里的 Number 类型），不与任何引擎的向量类型做转换——
    /// 该转换职责属于各引擎适配层实现内部，不属于本原语类型。
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public double X { get; }
        public double Y { get; }

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0, 0);

        public double SqrLength => X * X + Y * Y;

        public double Length => Math.Sqrt(SqrLength);

        public double Dot(Vec2 other) => X * other.X + Y * other.Y;

        public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);

        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);

        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);

        public static Vec2 operator *(Vec2 a, double scalar) => new Vec2(a.X * scalar, a.Y * scalar);

        public static Vec2 operator *(double scalar, Vec2 a) => a * scalar;

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public override string ToString() => $"({X}, {Y})";

        public static bool operator ==(Vec2 left, Vec2 right) => left.Equals(right);

        public static bool operator !=(Vec2 left, Vec2 right) => !left.Equals(right);
    }
}
