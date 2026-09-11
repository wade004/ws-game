using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// ADR-0026《技能位移的连续模式》：<c>move</c> 效果原语 <c>motion: continuous</c> 时，被阻挡后的
    /// 处理策略——<see cref="Stop"/> 停在阻挡前最后可通行采样点，<see cref="Revert"/> 回到起点（见
    /// <see cref="IControlledDisplacementSink.BeginControlledDisplacement"/> 判断记录）。
    /// </summary>
    public enum DisplacementBlockingPolicy
    {
        Stop,
        Revert,
    }

    /// <summary>
    /// ADR-0026：一次"受控位移"请求——<c>core/rules/skill.EffectDispatcher.ApplyMove</c>
    /// （<c>motion: continuous</c> 分支）按既有三种 <c>move</c> 子类型（<c>charge</c>/<c>leap</c>/
    /// <c>knockback</c>）算出的目标点组装本结构，交给 <see cref="IControlledDisplacementSink"/>；
    /// 真正逐 tick 推进的是 L3 <c>core/carriers/unit.MovementHost</c>/<c>MovementTickHandler</c>
    /// （依赖倒置，惯例同 <see cref="IProjectileSpawner"/> 顶部判断记录）。本结构是纯数据 DTO，不
    /// 引用任何 L3 类型（<c>core/rules</c> 是 L2，见 01 第 3 节依赖矩阵）。
    /// </summary>
    public readonly struct ControlledDisplacementRequest
    {
        /// <summary>被位移的单位（<c>charge</c>/<c>leap</c> 是施法者，<c>knockback</c> 是目标，
        /// 判定发生在 <c>EffectDispatcher.ApplyMove</c>，本结构只携带结果）。</summary>
        public Id UnitId { get; }

        /// <summary>位移起点（<see cref="DisplacementBlockingPolicy.Revert"/> 被阻挡时回到这一点）。
        /// </summary>
        public Vec2 Origin { get; }

        /// <summary>位移终点——与瞬移模式对同一输入算出的落点完全一致（ADR-0026 兼容性 4：无阻挡时
        /// 连续/瞬移终点相同）。</summary>
        public Vec2 Target { get; }

        /// <summary>每秒位移距离（<c>speed</c> 声明时直接使用；<c>duration</c> 声明时由调用方按
        /// <c>|Target-Origin|/duration</c> 换算后传入，本结构不区分来源）。必须 &gt; 0。</summary>
        public double Speed { get; }

        public DisplacementBlockingPolicy Blocking { get; }

        /// <summary>路径采样步长——每次推进最多前进这么远再做一次阻挡裁决（见
        /// <see cref="IControlledDisplacementSink.BeginControlledDisplacement"/> 判断记录"路径采样"）。
        /// <c>&lt;= 0</c> 表示"未声明，使用实现方的默认值"（<c>MovementOptions.DefaultDisplacementSampleStep</c>，
        /// ADR-0026 决策 1"默认取导航网格尺寸或固定值"——L2 不持有导航网格尺寸信息，固定值分支由 L3
        /// 兜底）。</summary>
        public double SampleStep { get; }

        public ControlledDisplacementRequest(
            Id unitId, Vec2 origin, Vec2 target, double speed, DisplacementBlockingPolicy blocking, double sampleStep)
        {
            UnitId = unitId;
            Origin = origin;
            Target = target;
            Speed = speed;
            Blocking = blocking;
            SampleStep = sampleStep;
        }
    }
}
