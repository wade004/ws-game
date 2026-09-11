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
        {
            Origin = origin;
            Target = target;
            Speed = speed;
            Blocking = blocking;
            SampleStep = sampleStep;
        }
    }
}
