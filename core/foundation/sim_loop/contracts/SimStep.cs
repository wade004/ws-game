using System;
using Core.Foundation.Common;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 模拟步的"种类"：<see cref="Continuous"/> 携带经过的秒数，<see cref="Discrete"/>
    /// 携带当前行动者与阶段（见 03_运行时骨架.md 第 3、9 节）。ADR-0013 离散时间模型现已接线：
    /// <see cref="Core.Foundation.SimLoop.TurnScheduler"/> 是真实产出 <see cref="Discrete"/> 步的
    /// 调度器，<see cref="WorldSim.Tick"/> 对 Discrete 步的八步编排是其真实消费方，不再是"仅按
    /// 文档要求提供、暂无调用方"的占位形态。
    /// </summary>
    public enum SimStepKind
    {
        Continuous,
        Discrete
    }

    /// <summary>离散步的阶段（见 03 第 9 节 <c>Discrete{actorId, phase}</c>）。</summary>
    public enum StepPhase
    {
        TurnStart,
        Act,
        TurnEnd,
        RoundEnd
    }

    /// <summary>
    /// <c>WorldSim.tick</c> 的唯一输入类型（见 03 第 3、9 节、ADR-0013 决策 2）。
    /// <see cref="ActorId"/>、<see cref="Phase"/> 声明为可空：二者只在 <see cref="Kind"/> 为
    /// <see cref="SimStepKind.Discrete"/> 时有意义，连续步不携带这两个字段——用可空类型
    /// 显式表达"只在离散步下有值"，比用不可空类型 + 约定"连续步下忽略"更不容易被调用方
    /// 误用（判断记录见本模块 README）。
    /// </summary>
    public readonly struct SimStep : IEquatable<SimStep>
    {
        /// <summary>本步的种类：连续或离散。</summary>
        public SimStepKind Kind { get; }

        /// <summary>连续步经过的秒数；离散步下恒为 0，无意义。</summary>
        public double Dt { get; }

        /// <summary>离散步的当前行动者；连续步下为 null。</summary>
        public Id? ActorId { get; }

        /// <summary>离散步的阶段；连续步下为 null。</summary>
        public StepPhase? Phase { get; }

        private SimStep(SimStepKind kind, double dt, Id? actorId, StepPhase? phase)
        {
            Kind = kind;
            Dt = dt;
            ActorId = actorId;
            Phase = phase;
        }

        /// <summary>构造一个连续步，<paramref name="dt"/> 为本步经过的秒数（不能为负）。</summary>
        public static SimStep Continuous(double dt)
        {
            if (dt < 0)
            {
                throw new ArgumentException("dt 不能为负数", nameof(dt));
            }

            return new SimStep(SimStepKind.Continuous, dt, null, null);
        }

        /// <summary>构造一个离散步，携带当前行动者与阶段（见 03 第 3.2 节）。</summary>
        public static SimStep Discrete(Id actorId, StepPhase phase)
        {
            return new SimStep(SimStepKind.Discrete, 0.0, actorId, phase);
        }

        public bool Equals(SimStep other) =>
            Kind == other.Kind
            && Dt.Equals(other.Dt)
            && ActorId.Equals(other.ActorId)
            && Phase.Equals(other.Phase);

        public override bool Equals(object? obj) => obj is SimStep other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind;
                hash = (hash * 397) ^ Dt.GetHashCode();
                hash = (hash * 397) ^ ActorId.GetHashCode();
                hash = (hash * 397) ^ Phase.GetHashCode();
                return hash;
            }
        }

        public override string ToString() =>
            Kind == SimStepKind.Continuous
                ? $"Continuous(dt={Dt})"
                : $"Discrete(actorId={ActorId}, phase={Phase})";

        public static bool operator ==(SimStep left, SimStep right) => left.Equals(right);

        public static bool operator !=(SimStep left, SimStep right) => !left.Equals(right);
    }
}
