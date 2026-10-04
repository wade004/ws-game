using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 运动模式（手感设计/02 第 3.1 节）。取值与 <c>feel.motion_mode_rules</c> 表的 <c>mode</c> 列一一对应
    /// （<see cref="MotionModeNames"/> 给出数据里的写法）。<see cref="Grounded"/> 是缺省模式，数值 0。
    /// </summary>
    public enum MotionMode
    {
        /// <summary>缺省：接受输入位移、允许转向。</summary>
        Grounded = 0,

        /// <summary>动作时间线进行中：输入位移按 <c>action_move_speed_ratio</c>，转向按 <c>action_turn_lock</c>。</summary>
        Action,

        /// <summary>击退、冲锋/跳跃/受控位移、抛飞：位移由受控任务给出。</summary>
        Forced,

        /// <summary>硬直。</summary>
        Staggered,

        /// <summary>定身（<c>movementLocked</c>/<c>NoMove</c> 控制）：不位移但可转向。</summary>
        Rooted,

        /// <summary>局部顿帧叠加态：叠在任一模式之上，位移为零，解冻后恢复原模式，速度保留。</summary>
        Frozen,

        /// <summary>死亡。</summary>
        Dead,
    }

    /// <summary>本 tick 胜出的位移来源（手感设计/02 第 2 节 <c>MovementState.motionSource</c>）。</summary>
    public enum MotionSource
    {
        /// <summary>没有任何来源产生位移（站立不动、被零位移模式压住）。</summary>
        None = 0,

        /// <summary>输入轴方向位移、路径跟随、追击或残余速度的减速滑行。</summary>
        Regular,

        /// <summary>动作时间线的位移段（代码驱动）。</summary>
        Action,

        /// <summary>击退、受控位移、抛飞。</summary>
        Forced,

        /// <summary>已删除（ADR-0147）：保留成员只为接口兼容，运动层不再产生该来源。</summary>
        [System.Obsolete("root_motion 来源已删除（ADR-0147）")]
        RootMotion,
    }

    /// <summary><see cref="MotionMode"/> 在数据里的写法（<c>feel.motion_mode_rules.mode</c>）。</summary>
    public static class MotionModeNames
    {
        public static string ToName(MotionMode mode)
        {
            switch (mode)
            {
                case MotionMode.Grounded: return "grounded";
                case MotionMode.Action: return "action";
                case MotionMode.Forced: return "forced";
                case MotionMode.Staggered: return "staggered";
                case MotionMode.Rooted: return "rooted";
                case MotionMode.Frozen: return "frozen";
                case MotionMode.Dead: return "dead";
                default: return mode.ToString();
            }
        }
    }

    /// <summary>
    /// 运动学状态（手感设计/02 第 2 节 <c>MovementState</c> 新增的四个字段，外加供步态派生的基础移速）。
    /// 不可变值类型，挂在 <see cref="MovementState.Motion"/> 上；全部是<b>瞬态</b>（不进存档，读档后速度为零、
    /// 模式为 <see cref="MotionMode.Grounded"/>）。
    /// <para>
    /// 只有装配了运动服务（<see cref="MotionServices"/>）的单位才会被 <c>MovementTickHandler</c> 写入；未装配时
    /// 恒为默认值（零速度、<see cref="MotionMode.Grounded"/>、<see cref="MotionSource.None"/>），既有行为不变。
    /// </para>
    /// </summary>
    public readonly struct MotionKinematics
    {
        /// <summary>当前速度（世界单位/秒）：加减速积分的状态；forced/action 来源胜出的 tick 取实际位移/dt。</summary>
        public Vec2 Velocity { get; }

        /// <summary>本 tick 期望方向（单位向量；输入轴或路径下一段；无期望时为零向量）。</summary>
        public Vec2 DesiredDirection { get; }

        /// <summary>当前运动模式（顿帧叠加态时为 <see cref="MotionMode.Frozen"/>）。</summary>
        public MotionMode Mode { get; }

        /// <summary>顿帧叠加态之下的底层模式（未顿帧时等于 <see cref="Mode"/>）；顿帧结束后恢复到它。</summary>
        public MotionMode BaseMode { get; }

        /// <summary>本 tick 胜出的位移来源。</summary>
        public MotionSource Source { get; }

        /// <summary>
        /// 该单位本 tick 的基础移速（世界单位/秒，来自移动速度属性）：表现层按 <c>|velocity| / BaseSpeed</c>
        /// 派生步态（手感设计/02 第 7 节）；未写入时为 0。
        /// </summary>
        public double BaseSpeed { get; }

        /// <summary>速率标量 <c>|Velocity|</c>。</summary>
        public double Speed => Velocity.Length;

        /// <summary>速度/基础移速比值（步态派生输入）；基础移速未知（0）时为 0。</summary>
        public double SpeedRatio => BaseSpeed > 0 ? Velocity.Length / BaseSpeed : 0.0;

        public MotionKinematics(
            Vec2 velocity, Vec2 desiredDirection, MotionMode mode, MotionMode baseMode, MotionSource source, double baseSpeed)
        {
            Velocity = velocity;
            DesiredDirection = desiredDirection;
            Mode = mode;
            BaseMode = baseMode;
            Source = source;
            BaseSpeed = baseSpeed;
        }

        /// <summary>零速度、缺省模式、无来源。</summary>
        public static MotionKinematics Rest => default;

        public MotionKinematics WithVelocity(Vec2 velocity) =>
            new MotionKinematics(velocity, DesiredDirection, Mode, BaseMode, Source, BaseSpeed);
    }
}
