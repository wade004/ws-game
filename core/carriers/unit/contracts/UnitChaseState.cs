using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// ADR-0097《以单位为目标的追击移动请求》：挂在 <see cref="MovementState.Chase"/> 上的追击态——
    /// 与 <see cref="MovementState.CurrentPath"/>（路径跟随）、<see cref="MovementState.Displacement"/>
    /// （受控位移）平级、互斥的第三种由 <c>MovementTickHandler</c> 驱动的移动任务，由
    /// <c>Intent.Kind == "move_to_unit"</c> 发起（见 <see cref="MoveRequest.ToUnit"/>）。不可变值类型，
    /// 惯例同 <see cref="ControlledDisplacementState"/>：每次状态变化由 <c>MovementTickHandler</c>
    /// 构造一份新实例整体替换。
    /// </summary>
    public readonly struct UnitChaseState
    {
        /// <summary>正在追击的目标单位 id。</summary>
        public Id TargetUnitId { get; }

        /// <summary>与目标保持的最小距离（见 <see cref="MoveRequest.StopRange"/>），恒 &gt; 0（
        /// <see cref="MoveRequest.ToUnit"/> 已校验）。</summary>
        public double StopRange { get; }

        /// <summary>需要靠近时使用的移动模式（见 <see cref="MoveRequest.ToUnit"/> 的 <c>mode</c>
        /// 参数）——单位与目标距离进入停止区间时，<see cref="Core.Carriers.Unit.MovementState.Mode"/>
        /// 收回 <see cref="MoveMode.Idle"/>，恢复靠近时改回本值。</summary>
        public MoveMode Mode { get; }

        /// <summary>上一次重新规划路径时，目标单位所在的位置快照——供
        /// <see cref="MovementOptions.FollowRepathDistance"/> 判断"目标是否已经移动得足够远，
        /// 值得重新规划一次路径"使用（见 <c>MovementTickHandler.AdvanceChase</c> 判断记录）。</summary>
        public Vec2 LastPlannedTargetPosition { get; }

        public UnitChaseState(Id targetUnitId, double stopRange, MoveMode mode, Vec2 lastPlannedTargetPosition)
        {
            TargetUnitId = targetUnitId;
            StopRange = stopRange;
            Mode = mode;
            LastPlannedTargetPosition = lastPlannedTargetPosition;
        }

        /// <summary>返回一份仅 <see cref="LastPlannedTargetPosition"/> 不同的新实例，供
        /// <c>MovementTickHandler.AdvanceChase</c> 每次成功重新规划路径后更新快照（惯例同
        /// <see cref="MovementState.WithLocked"/>）。</summary>
        public UnitChaseState WithLastPlannedTargetPosition(Vec2 position) =>
            new UnitChaseState(TargetUnitId, StopRange, Mode, position);
    }
}
