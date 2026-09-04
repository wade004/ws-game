using System;

namespace Presentation.Common
{
    /// <summary>
    /// 表现层承载的朝向：量化方向索引 + 档位数（见 09_表现层.md 第 2 节 View 绑定协议
    /// <c>syncPose(pos, facing: Direction, height)</c>、第 3.2 节"仅 sprite 型外形在表现层将其
    /// 量化为固定档位的方向枚举……model 型外形直接使用连续朝向驱动骨骼与挂点，不做方向分桶"）。
    /// <para>
    /// 判断记录：09 原文只要求"量化方向索引 + 档位数"，但同一份 <see cref="Direction"/> 值需要
    /// 同时服务 sprite 型（只关心 <see cref="Index"/>/<see cref="DirectionCount"/>）与 model 型
    /// （只关心连续角度）两种消费方——为避免定义两套朝向类型，本结构体额外携带
    /// <see cref="RawRadians"/>（量化前的原始弧度），sprite 型消费方忽略它，model 型消费方忽略
    /// <see cref="Index"/>/<see cref="DirectionCount"/>，互不冲突。量化算法本身复用
    /// <c>Core.Carriers.Unit.DirectionQuantizer</c>（05 第 3.2 节），本类型不重复实现。
    /// </para>
    /// </summary>
    public readonly struct Direction : IEquatable<Direction>
    {
        /// <summary>量化前的原始朝向弧度（供 model 型外形直接使用，见类型注释）。</summary>
        public double RawRadians { get; }

        /// <summary>量化后的方向桶索引，范围 [0, DirectionCount)（供 sprite 型外形使用）。</summary>
        public int Index { get; }

        /// <summary>方向档位数（4/8/16，见 05 第 3.2 节），随 DisplayInfo 的 sprite 集而定。</summary>
        public int DirectionCount { get; }

        public Direction(double rawRadians, int index, int directionCount)
        {
            RawRadians = rawRadians;
            Index = index;
            DirectionCount = directionCount;
        }

        /// <summary>用给定档位数对原始弧度做量化（见 <c>Core.Carriers.Unit.DirectionQuantizer</c>）。</summary>
        public static Direction FromQuantized(double rawRadians, int directionCount)
        {
            var index = Core.Carriers.Unit.DirectionQuantizer.Quantize(rawRadians, directionCount);
            return new Direction(rawRadians, index, directionCount);
        }

        /// <summary>不做量化，只携带原始弧度（供 model 型外形使用；<see cref="Index"/>/
        /// <see cref="DirectionCount"/> 恒为 0，消费方不应读取）。</summary>
        public static Direction Continuous(double rawRadians) => new Direction(rawRadians, 0, 0);

        public bool Equals(Direction other) =>
            RawRadians.Equals(other.RawRadians) && Index == other.Index && DirectionCount == other.DirectionCount;

        public override bool Equals(object? obj) => obj is Direction other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = RawRadians.GetHashCode();
                hash = (hash * 397) ^ Index;
                hash = (hash * 397) ^ DirectionCount;
                return hash;
            }
        }

        public override string ToString() => $"Direction(raw={RawRadians}, index={Index}/{DirectionCount})";

        public static bool operator ==(Direction left, Direction right) => left.Equals(right);

        public static bool operator !=(Direction left, Direction right) => !left.Equals(right);
    }
}
