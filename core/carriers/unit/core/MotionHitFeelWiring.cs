using System;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Combat;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <see cref="IStaggerStateQuery"/>（运动层读）到 <see cref="IHitReactionQuery"/>（受击裁决写）的适配：
    /// 受击裁决在 rules 层，运动层在 carriers 层，不能反向引用，故由本适配接上。硬直（含倒地）即 <c>staggered</c> 模式。
    /// </summary>
    public sealed class HitReactionStaggerQuery : IStaggerStateQuery
    {
        private readonly IHitReactionQuery _reactions;

        public HitReactionStaggerQuery(IHitReactionQuery reactions)
        {
            _reactions = reactions ?? throw new ArgumentNullException(nameof(reactions));
        }

        public bool IsStaggered(Id unitId) => _reactions.IsStaggered(unitId);
    }

    /// <summary>
    /// 运动层与局部顿帧/受击裁决的接线（手感设计/02 第 3.1 节 <c>frozen</c>/<c>staggered</c>、第 6 节击退）：
    /// 把动作时钟接成 <c>frozen</c> 叠加态的来源、把受击裁决的硬直接成 <c>staggered</c> 模式的来源、把 <see cref="MovementHost"/>
    /// 设为受击裁决的击退口。<see cref="MotionServices.Feel"/> 等其它服务由调用方自行设置（缺它运动层本身不开启）。
    /// </summary>
    public static class MotionHitFeelWiring
    {
        public static void Connect(MovementHost movement, IActorActionClockQuery clock, HitFeelHost host)
        {
            if (movement == null) throw new ArgumentNullException(nameof(movement));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            if (host == null) throw new ArgumentNullException(nameof(host));
            var motion = movement.Motion ?? (movement.Motion = new MotionServices());
            motion.ActionClock = clock;
            motion.Stagger = new HitReactionStaggerQuery(host);
            host.Knockback = movement;
        }
    }
}
