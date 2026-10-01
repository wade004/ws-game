using Core.Foundation.Common;

namespace Core.Foundation.InputMap
{
    /// <summary>缓冲记录的按住状态（手感设计/01 第 2.2 节 <c>holdState</c>）。</summary>
    public enum BufferHoldState
    {
        /// <summary>点按（或动作没有声明按住阈值）。</summary>
        Tap,

        /// <summary>已按下、尚未抬起，且动作声明了 <c>hold_threshold_ms</c>：不可被消费。</summary>
        HoldPending,

        /// <summary>已抬起且持续时长超过阈值（或蓄力达上限）；时长见 <see cref="BufferedIntent.HeldTicks"/>。</summary>
        HoldReleased,
    }

    /// <summary>记录离开缓冲的原因（手感设计/01 第 2.2/2.3 节 <c>input.buffer_dropped.reason</c>）。</summary>
    public enum BufferDropReason
    {
        /// <summary>槽满时被更高优先级的新意图替换。</summary>
        Replaced,

        /// <summary>槽满且新意图优先级不高于槽内最低者，新意图被丢弃。</summary>
        Full,

        /// <summary>过期（行动者动作时钟超过 <see cref="BufferedIntent.ExpiresAtActionTime"/>）。</summary>
        Expired,

        /// <summary>行动者死亡、场景切换、离散模式切换、读档、销毁时整体清空。</summary>
        Cleared,

        /// <summary>管线拒绝且原因不是"时间可解"（资源不足、条件不满足、无目标、死亡等），原因码见事件。</summary>
        Rejected,
    }

    /// <summary>
    /// 一条输入缓冲记录的只读快照（手感设计/01 第 2.2 节 <c>BufferedIntent</c>）。
    /// 过期以<b>行动者动作时钟</b>计（顿帧期间不流逝，手感设计/00 第 5 节）。只读观察经 <see cref="IInputBufferQuery"/>。
    /// </summary>
    public readonly struct BufferedIntent
    {
        /// <summary>输入动作 id（<c>found.input_action</c> 的 key）。</summary>
        public Id ActionId { get; }

        public ActionClass Class { get; }

        /// <summary>提交时的模拟 tick 序号。</summary>
        public long SubmittedTick { get; }

        /// <summary>过期时刻（行动者动作时钟，单位 tick）。</summary>
        public long ExpiresAtActionTime { get; }

        /// <summary>同 tick 多条待消费记录的排序与替换依据（越大越优先）。</summary>
        public int Priority { get; }

        /// <summary>按下瞬间的移动轴方向；无轴输入为 null。</summary>
        public Vec2? DirectionSnapshot { get; }

        public BufferHoldState HoldState { get; }

        /// <summary><see cref="BufferHoldState.HoldReleased"/> 时的按住时长（tick），其它状态为 0。</summary>
        public int HeldTicks { get; }

        /// <summary>是否已被消费（接受为意图）。</summary>
        public bool Consumed { get; }

        /// <summary>
        /// 接受时是否把行动者朝向对齐到 <see cref="DirectionSnapshot"/>（手感设计/01 第 2.1 节 <c>face_on_accept</c>，
        /// 由动作定义显式值或类别缺省得出）。无轴输入（<see cref="DirectionSnapshot"/> 为 null）时保持当前朝向。
        /// </summary>
        public bool FaceOnAccept { get; }

        /// <summary>九参数构造（S0 契约，原样保留）：<see cref="FaceOnAccept"/> 取类别缺省。</summary>
        public BufferedIntent(
            Id actionId,
            ActionClass actionClass,
            long submittedTick,
            long expiresAtActionTime,
            int priority,
            Vec2? directionSnapshot,
            BufferHoldState holdState,
            int heldTicks,
            bool consumed)
            : this(actionId, actionClass, submittedTick, expiresAtActionTime, priority, directionSnapshot, holdState, heldTicks,
                consumed, ActionClassDefaults.FaceOnAccept(actionClass))
        {
        }

        /// <summary>带 <see cref="FaceOnAccept"/> 的构造（纯加法重载）。</summary>
        public BufferedIntent(
            Id actionId,
            ActionClass actionClass,
            long submittedTick,
            long expiresAtActionTime,
            int priority,
            Vec2? directionSnapshot,
            BufferHoldState holdState,
            int heldTicks,
            bool consumed,
            bool faceOnAccept)
        {
            ActionId = actionId;
            Class = actionClass;
            SubmittedTick = submittedTick;
            ExpiresAtActionTime = expiresAtActionTime;
            Priority = priority;
            DirectionSnapshot = directionSnapshot;
            HoldState = holdState;
            HeldTicks = heldTicks;
            Consumed = consumed;
            FaceOnAccept = faceOnAccept;
        }
    }
}
