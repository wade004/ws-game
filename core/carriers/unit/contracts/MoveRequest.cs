using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 一次移动请求（见 05 第 6.2 节 <c>MoveRequest { unitId, target: Vec2 | directionVector, mode }</c>）。
    /// <see cref="Target"/>/<see cref="Direction"/> 二选一（判别方式：非 null 的那个生效），由
    /// <see cref="MovementHost.Request"/> 转译为一条 <c>Kind == "move"</c> 的
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

        public MoveRequest(Id unitId, Vec2? target, Vec2? direction, MoveMode mode)
        {
            UnitId = unitId;
            Target = target;
            Direction = direction;
            Mode = mode;
        }

        /// <summary>构造一个"移动到目标点"的请求。</summary>
        public static MoveRequest ToTarget(Id unitId, Vec2 target, MoveMode mode = MoveMode.Run) =>
            new MoveRequest(unitId, target, null, mode);

        /// <summary>构造一个"沿方向移动"的请求。</summary>
        public static MoveRequest InDirection(Id unitId, Vec2 direction, MoveMode mode = MoveMode.Walk) =>
            new MoveRequest(unitId, null, direction, mode);
    }
}
