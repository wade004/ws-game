using System;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 计时器句柄：内部只是一个递增序号，构造函数 <c>internal</c>——调用方只能通过
    /// <see cref="ISimTimers.Create"/> 取得实例，不能自行构造（见 03 第 8 节通用计时器原语）。
    /// </summary>
    public readonly struct TimerHandle : IEquatable<TimerHandle>
    {
        internal int Value { get; }

        internal TimerHandle(int value)
        {
            Value = value;
        }

        public bool Equals(TimerHandle other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is TimerHandle other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => $"TimerHandle({Value})";

        public static bool operator ==(TimerHandle left, TimerHandle right) => left.Equals(right);

        public static bool operator !=(TimerHandle left, TimerHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// 挂在模拟时间轴上的通用计时器原语（见 03_运行时骨架.md 第 8 节）。供冷却、光环
    /// 持续时间等一切"经过若干模拟时间后触发"的需求统一使用；由 <see cref="WorldSim"/>
    /// 在每个连续 tick 开头按 <c>step.Dt</c> 统一推进，调用方只创建/查询，不自己维护累加。
    /// 计时器不发事件，到期状态由调用方轮询（<see cref="IsExpired"/>），保留到期状态直到
    /// <see cref="Cancel"/>。时长以数据集声明的时间单位计（秒或回合），本模块不关心
    /// 当前处于哪种时间模型，只机械地按传入的 dt 推进。
    /// </summary>
    public interface ISimTimers
    {
        /// <summary>创建一个新计时器，<paramref name="durationUnits"/> 为初始剩余时长（不能为负）。</summary>
        TimerHandle Create(double durationUnits);

        /// <summary>查询剩余时长；已到期时可能为 0 或负值（表示"过期了多少"）。
        /// 句柄不存在或已被 <see cref="Cancel"/> 时抛 <see cref="ArgumentException"/>。</summary>
        double Remaining(TimerHandle handle);

        /// <summary>是否已到期（剩余时长 &lt;= 0，见实现的浮点容差说明）。
        /// 句柄不存在或已被 <see cref="Cancel"/> 时抛 <see cref="ArgumentException"/>。</summary>
        bool IsExpired(TimerHandle handle);

        /// <summary>取消计时器：之后 <see cref="IsAlive"/> 返回 false，
        /// 再次查询 <see cref="Remaining"/>/<see cref="IsExpired"/> 会抛异常。</summary>
        void Cancel(TimerHandle handle);

        /// <summary>句柄是否仍然存活（已创建且未被 <see cref="Cancel"/>），
        /// 到期但未取消的计时器仍然"存活"。不存在的句柄返回 false，不抛异常。</summary>
        bool IsAlive(TimerHandle handle);
    }
}
