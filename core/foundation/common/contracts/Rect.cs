using System;

namespace Core.Foundation.Common
{
    /// <summary>
    /// 轴对齐矩形（记法约定 Rect = {min: Vec2, max: Vec2}，见 02_引擎适配层.md
    /// ADR-0016 决策 7：INavigation2D.SetBlocking 用它表达运行时动态阻挡区域）。
    /// 与 ISpatialQuery.QueryRect 的 min/max 参数对齐同一约定，不做旋转，
    /// 需要旋转矩形请使用 ISpatialQuery 的 Shape.Rect。
    /// </summary>
    public readonly struct Rect : IEquatable<Rect>
    {
        public Vec2 Min { get; }
        public Vec2 Max { get; }

        public Rect(Vec2 min, Vec2 max)
        {
            Min = min;
            Max = max;
        }

        public bool Equals(Rect other) => Min.Equals(other.Min) && Max.Equals(other.Max);

        public override bool Equals(object? obj) => obj is Rect other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Min.GetHashCode() * 397) ^ Max.GetHashCode();
            }
        }

        public override string ToString() => $"[{Min} .. {Max}]";

        public static bool operator ==(Rect left, Rect right) => left.Equals(right);

        public static bool operator !=(Rect left, Rect right) => !left.Equals(right);
    }
}
