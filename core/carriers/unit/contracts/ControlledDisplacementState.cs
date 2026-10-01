using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// ADR-0026《技能位移的连续模式》：挂在 <see cref="MovementState.Displacement"/> 上的"受控位移"
    /// 进行中快照——由 <see cref="MovementHost.BeginControlledDisplacement"/> 提交的
    /// <see cref="ControlledDisplacementRequest"/> 落地而来（<see cref="Origin"/>/<see cref="Target"/>/
    /// <see cref="Speed"/>/<see cref="Blocking"/> 直接照抄该请求；<see cref="SampleStep"/> 已经过
    /// <c>MovementOptions.DefaultDisplacementSampleStep</c> 兜底，恒为正数，不再是"未声明"的哨兵
    /// 值），供 <c>MovementTickHandler</c> 跨多个 tick 记住"这个单位正在从哪飞到哪、飞多快、被挡住
    /// 怎么办"。不可变值类型，惯例同 <see cref="MovementState"/> 本身"整体替换而非部分字段更新"。
    /// </summary>
    public readonly struct ControlledDisplacementState
    {
        public Vec2 Origin { get; }

        public Vec2 Target { get; }

        public double Speed { get; }

        public DisplacementBlockingPolicy Blocking { get; }

        /// <summary>恒为正数（见类型注释——构造本结构的调用方须先完成默认值兜底）。</summary>
        public double SampleStep { get; }

        public ControlledDisplacementState(
            Vec2 origin, Vec2 target, double speed, DisplacementBlockingPolicy blocking, double sampleStep)
            : this(origin, target, speed, blocking, sampleStep, null, 0.0, 0.0)
        {
        }

        /// <summary>
        /// 手感设计/02 第 6 节（ADR-0116 决策 7）新增重载（既有 5 参数构造的物理签名不变）：带进度曲线的位移——
        /// 击退用 <c>ease_out</c>，在 <paramref name="durationSeconds"/> 内按曲线走完整段距离（此时 <see cref="Speed"/>
        /// 是平均速度，仅作记录）。<paramref name="curve"/> 为 null 即既有的匀速位移。
        /// </summary>
        public ControlledDisplacementState(
            Vec2 origin, Vec2 target, double speed, DisplacementBlockingPolicy blocking, double sampleStep,
            string? curve, double durationSeconds, double elapsedSeconds)
        {
            Origin = origin;
            Target = target;
            Speed = speed;
            Blocking = blocking;
            SampleStep = sampleStep;
            Curve = curve;
            DurationSeconds = durationSeconds;
            ElapsedSeconds = elapsedSeconds;
        }

        /// <summary>进度曲线引用（<c>ease_out</c> 等）；null 表示既有的匀速位移。</summary>
        public string? Curve { get; }

        /// <summary>带曲线位移的总时长（秒）；匀速位移为 0。</summary>
        public double DurationSeconds { get; }

        /// <summary>带曲线位移已经过的时间（秒）；匀速位移为 0。</summary>
        public double ElapsedSeconds { get; }

        /// <summary>返回仅已过时间不同的新实例。</summary>
        public ControlledDisplacementState WithElapsed(double elapsedSeconds) =>
            new ControlledDisplacementState(Origin, Target, Speed, Blocking, SampleStep, Curve, DurationSeconds, elapsedSeconds);
    }
}
