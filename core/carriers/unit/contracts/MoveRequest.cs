using System;
using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 一次移动请求（见 05 第 6.2 节 <c>MoveRequest { unitId, target: Vec2 | directionVector, mode }</c>）。
    /// <see cref="Target"/>/<see cref="Direction"/>/<see cref="TargetUnitId"/> 三选一（判别方式：非
    /// null 的那个生效，见 <see cref="MovementHost.Request"/> 判断记录），由
    /// <see cref="MovementHost.Request"/> 转译为一条 <c>Kind == "move"</c>（前两种）或
    /// <c>Kind == "move_to_unit"</c>（<see cref="TargetUnitId"/>，ADR-0097）的
    /// <see cref="Core.Foundation.SimLoop.Intent"/> 提交给 <see cref="Core.Foundation.SimLoop.IWorldSim"/>，
    /// 真正的位移推进由 <c>MovementTickHandler</c> 在 <c>TickPhase.MovementAndNavigation</c> 阶段消费该
    /// 意图完成（本类型本身不持有任何行为）。
    /// </summary>
    public readonly struct MoveRequest
    {
        public Id UnitId { get; }

        /// <summary>目标点：提供时按 <see cref="Core.Foundation.EngineAdapter.INavigation2D.FindPath"/>
        /// （或直线兜底）推进，直至到达（见 05 第 6.2 节"寻路失败处理"）。</summary>
        public Vec2? Target { get; }

        /// <summary>方向向量：提供时按该方向直接位移，不做寻路，每 tick 需要调用方重新提交（典型场景：
        /// 玩家持续按住移动键期间逐 tick 提交）。</summary>
        public Vec2? Direction { get; }

        public MoveMode Mode { get; }

        /// <summary>
        /// ADR-0097《以单位为目标的追击移动请求》：非 null 时表示"追击该单位"——每 tick 按目标单位
        /// 当前位置持续跟随，与 <see cref="StopRange"/> 配对使用，与 <see cref="Target"/>/
        /// <see cref="Direction"/> 互斥（见 <see cref="ToUnit"/>）。
        /// </summary>
        public Id? TargetUnitId { get; }

        /// <summary>
        /// ADR-0097：与 <see cref="TargetUnitId"/> 配对使用，追击时与目标保持的最小距离——单位与目标
        /// 距离 ≤ 本值时停止靠近（见 <c>MovementTickHandler</c> 追击判断记录）。<see cref="ToUnit"/>
        /// 已校验必须 &gt; 0。<see cref="TargetUnitId"/> 为 null 时本字段无意义。
        /// </summary>
        public double? StopRange { get; }

        public MoveRequest(Id unitId, Vec2? target, Vec2? direction, MoveMode mode)
            : this(unitId, target, direction, mode, null, null)
        {
        }

        /// <summary>ADR-0097 新增内部构造函数（不改动上面既有 4 参数构造函数的物理签名，惯例同
        /// <see cref="MovementState"/> 判断记录"ABI 安全：新增构造函数重载而非新增参数"）：额外携带
        /// <paramref name="targetUnitId"/>/<paramref name="stopRange"/>，只供 <see cref="ToUnit"/>
        /// 内部使用，不对外公开（外部一律经三个静态工厂构造，不直接拼装这两个新字段）。</summary>
        private MoveRequest(Id unitId, Vec2? target, Vec2? direction, MoveMode mode, Id? targetUnitId, double? stopRange)
        {
            UnitId = unitId;
            Target = target;
            Direction = direction;
            Mode = mode;
            TargetUnitId = targetUnitId;
            StopRange = stopRange;
        }

        /// <summary>构造一个"移动到目标点"的请求。</summary>
        public static MoveRequest ToTarget(Id unitId, Vec2 target, MoveMode mode = MoveMode.Run) =>
            new MoveRequest(unitId, target, null, mode, null, null);

        /// <summary>构造一个"沿方向移动"的请求。</summary>
        public static MoveRequest InDirection(Id unitId, Vec2 direction, MoveMode mode = MoveMode.Walk) =>
            new MoveRequest(unitId, null, direction, mode, null, null);

        /// <summary>
        /// ADR-0097《以单位为目标的追击移动请求》：构造一个"追击某单位"的请求——每 tick 按
        /// <paramref name="targetUnitId"/> 当前位置持续跟随，直至与其距离 ≤
        /// <paramref name="stopRange"/>（见 <c>MovementTickHandler</c> 追击判断记录"每 tick 语义"）。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="stopRange"/> 不是正数。
        /// </exception>
        public static MoveRequest ToUnit(Id unitId, Id targetUnitId, double stopRange, MoveMode mode = MoveMode.Run)
        {
            if (stopRange <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stopRange), stopRange, "ToUnit 的 stopRange 必须 > 0");
            }

            return new MoveRequest(unitId, null, null, mode, targetUnitId, stopRange);
        }
    }
}
