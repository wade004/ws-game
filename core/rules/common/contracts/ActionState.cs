using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>动作时间线的相位（手感设计/01 第 3.7 节 <c>action.phase_changed.phase</c>）。</summary>
    public enum ActionPhase
    {
        /// <summary>蓄力相（仅 hold 类动作，在前摇之前）。</summary>
        Charge,

        /// <summary>前摇。</summary>
        Startup,

        /// <summary>判定相。</summary>
        Active,

        /// <summary>后摇。</summary>
        Recovery,
    }

    /// <summary>动作未自然结束即终止的原因（手感设计/01 第 3.7 节 <c>action.cancelled.reason</c>）。</summary>
    public enum ActionCancelReason
    {
        /// <summary>取消进入：缓冲记录在取消窗口内被接受，开始新动作。</summary>
        CancelInto,

        /// <summary>受击硬直打断。</summary>
        Stagger,

        /// <summary>行动者死亡。</summary>
        Death,

        /// <summary>清空（场景切换、离散模式切换、销毁等）。</summary>
        Cleared,
    }

    /// <summary>受击裁决结果（手感设计/03 第 4 节，<c>combat.hit_confirmed.reaction</c>）。</summary>
    public enum HitReaction
    {
        /// <summary>无反应（回避类结局、霸体）。</summary>
        None,

        /// <summary>只播受击动画，不打断（韧性不低于攻击强度）。</summary>
        Flinch,

        /// <summary>轻硬直（light 冲击）。</summary>
        StaggerLight,

        /// <summary>硬直（medium 冲击）。</summary>
        Stagger,

        /// <summary>硬直 + 击退（heavy 冲击）。</summary>
        Knockback,

        /// <summary>硬直 + 击退 + 倒地（massive 冲击）。</summary>
        Knockdown,

        /// <summary>死亡（死亡优先于一切）。</summary>
        Death,
    }

    /// <summary>行动者进行中动作的只读快照（手感设计/01 第 3.7 节 <c>IActionStateQuery.current</c>）。</summary>
    public readonly struct ActionState
    {
        public Id SkillId { get; }

        public Id CastInstanceId { get; }

        public ActionPhase Phase { get; }

        /// <summary>已经过的动作时钟 tick 数（顿帧期间不增长）。</summary>
        public int ElapsedTicks { get; }

        /// <summary>连招序号（首段为 0）。</summary>
        public int ComboIndex { get; }

        public ActionState(Id skillId, Id castInstanceId, ActionPhase phase, int elapsedTicks, int comboIndex)
        {
            SkillId = skillId;
            CastInstanceId = castInstanceId;
            Phase = phase;
            ElapsedTicks = elapsedTicks;
            ComboIndex = comboIndex;
        }
    }
}
