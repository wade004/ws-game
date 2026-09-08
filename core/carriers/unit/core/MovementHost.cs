using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Unit
{
    /// <summary>供 <see cref="MovementHost.OnMoveFailed"/> 使用的具名委托（见 00 架构总则"携带参数的
    /// 回调按各自接口语义定义具名委托，不复用裸 Action/Func"惯例，同
    /// <c>Core.Foundation.SimLoop.EntityPredicate</c>）。</summary>
    public delegate void MoveFailedHandler(Id unitId, Vec2 from, Vec2 to);

    /// <summary>寻路失败的具体原因（游戏侧通用能力需求，05 第 6 节勘误）：<c>NoPath</c> 是本任务之前
    /// 唯一的失败原因（<see cref="Core.Foundation.EngineAdapter.INavigation2D.FindPath"/> 直接返回
    /// null）；<c>BlockingChanged</c> 是新增场景——单位持有路径期间地图动态阻挡发生变化，按
    /// <see cref="MovementOptions.BlockingChangePolicy"/> 重算/重验时又失败。</summary>
    public enum MoveFailReason
    {
        NoPath,
        BlockingChanged,
    }

    /// <summary>供 <see cref="MovementHost.OnMoveFailedDetailed"/> 使用的具名委托，携带
    /// <see cref="MoveFailReason"/>——与 <see cref="MoveFailedHandler"/>/<see cref="OnMoveFailed"/>
    /// 同时触发，签名不同的独立事件（不是取代关系，见 <see cref="MovementHost.OnMoveFailed"/>
    /// 判断记录"保持原签名"）。</summary>
    public delegate void MoveFailedDetailedHandler(Id unitId, Vec2 from, Vec2 to, MoveFailReason reason);

    /// <summary><see cref="MovementHost.OnMoveStopped"/> 触发原因（游戏侧通用能力需求，05 第 6 节
    /// 勘误）。</summary>
    public enum MoveStopReason
    {
        /// <summary>调用方经 <see cref="MovementHost.Stop"/> 显式请求停止。</summary>
        Requested,

        /// <summary>寻路失败且 <see cref="MovementOptions.PathFailurePolicy"/> 为 <c>Stop</c>。</summary>
        PathFailed,

        /// <summary>地图动态阻挡发生变化且 <see cref="MovementOptions.BlockingChangePolicy"/> 为
        /// <c>Stop</c>，或重算/重验失败后 <see cref="MovementOptions.PathFailurePolicy"/> 为
        /// <c>Stop</c>。</summary>
        BlockingChanged,

        /// <summary>一次新的 <see cref="MovementHost.Request"/>（目标类）整体替换了仍在进行中的旧
        /// 路径（仅当旧路径确实存在时触发，见 <see cref="MovementHost.Request"/> 判断记录）。</summary>
        Replaced,
    }

    /// <summary>供 <see cref="MovementHost.OnMoveStopped"/> 使用的具名委托：<paramref name="position"/>
    /// 是触发时该单位所在的位置（本次调用不产生任何位移，位置即调用前的当前位置）。</summary>
    public delegate void MoveStoppedHandler(Id unitId, Vec2 position, MoveStopReason reason);

    /// <summary>
    /// 移动请求的提交入口（见 05 第 6 节 <c>MovementHost.Request(MoveRequest)</c>）。本类型只负责把
    /// <see cref="MoveRequest"/> 转译成一条 <c>Kind == "move"</c> 的 <see cref="Intent"/> 并提交给
    /// <see cref="IWorldSim"/>；真正推进 <see cref="MovementState"/> 的是挂在
    /// <see cref="TickPhase.MovementAndNavigation"/> 的 <see cref="MovementTickHandler"/>——二者配合
    /// 使用：调用方持有同一个 <see cref="MovementHost"/> 实例向外暴露 <see cref="Request"/>，同时把
    /// 该实例传给 <see cref="MovementTickHandler"/> 的构造函数，供其在寻路失败时回调
    /// <see cref="OnMoveFailed"/>（found.event_catalog 未登记 <c>unit.move_failed</c> 事件，任务书
    /// 拍板"不新增事件，改为委托回调"，见本模块 README）。
    /// </summary>
    public sealed class MovementHost
    {
        private readonly IWorldSim _world;

        public MovementHost(IWorldSim world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>寻路失败时触发（见类型注释判断记录）。</summary>
        public event MoveFailedHandler? OnMoveFailed;

        /// <summary>寻路失败时触发，携带具体原因（游戏侧通用能力需求，05 第 6 节勘误）：与
        /// <see cref="OnMoveFailed"/> 在同一失败点同时触发（不是互斥的替代事件），已订阅
        /// <see cref="OnMoveFailed"/> 的既有调用方不受影响，需要区分原因的新调用方改订阅本事件。</summary>
        public event MoveFailedDetailedHandler? OnMoveFailedDetailed;

        /// <summary>某单位的路径跟随/待处理移动意图被取消时触发（游戏侧通用能力需求，05 第 6 节
        /// 勘误）：仅在确有路径或待处理意图被取消时触发一次（见 <see cref="Stop"/> 判断记录"幂等"）。
        /// </summary>
        public event MoveStoppedHandler? OnMoveStopped;

        /// <summary>把 <paramref name="request"/> 转译为一条 <c>move</c> 意图并提交（见
        /// <see cref="IWorldSim.SubmitIntent"/>，进入"下一 tick 待收集"队列——<see cref="Request"/>
        /// 可在 tick 内外任意时刻调用，与 <see cref="Intent"/> 本身的确定性约定一致）。
        /// <see cref="MoveRequest.Target"/>/<see cref="MoveRequest.Direction"/> 必须恰好提供一个。</summary>
        public void Request(MoveRequest request)
        {
            JsonObject args;

            if (request.Target.HasValue)
            {
                args = new JsonObjectBuilder()
                    .Add("x", new JsonNumber(request.Target.Value.X))
                    .Add("y", new JsonNumber(request.Target.Value.Y))
                    .Add("mode", new JsonString(request.Mode.ToString()))
                    .Build();
            }
            else if (request.Direction.HasValue)
            {
                args = new JsonObjectBuilder()
                    .Add("dx", new JsonNumber(request.Direction.Value.X))
                    .Add("dy", new JsonNumber(request.Direction.Value.Y))
                    .Add("mode", new JsonString(request.Mode.ToString()))
                    .Build();
            }
            else
            {
                throw new ArgumentException(
                    "MoveRequest 必须指定 Target 或 Direction 之一", nameof(request));
            }

            _world.SubmitIntent(new Intent(request.UnitId, "move", args));
        }

        /// <summary>
        /// 游戏侧通用能力需求（05 第 6 节勘误）：请求停止 <paramref name="unitId"/> 当前的路径跟随/
        /// 待处理的移动意图。与 <see cref="Request"/> 同样只是"提交一条意图"——经
        /// <c>Kind == "move_stop"</c> 的 <see cref="Intent"/> 提交给 <see cref="IWorldSim"/>，在下一次
        /// <see cref="Core.Foundation.SimLoop.TickPhase.MovementAndNavigation"/> 阶段才真正生效，本方法
        /// 本身不立即改变任何 <see cref="MovementState"/>（惯例同 <see cref="Request"/> 判断记录）。
        /// <para>
        /// 生效时的作用范围（由 <c>MovementTickHandler</c> 落地，见该类型判断记录）：清除该单位的
        /// <see cref="MovementState.CurrentPath"/>、把状态收回 <see cref="MoveMode.Idle"/>（
        /// <see cref="MovementState.MovementLocked"/> 不变），并丢弃同一 tick 内在它之前提交的该单位
        /// <c>move</c> 意图（之后提交的 <c>move</c> 意图照常生效，见"移动与导航"阶段的处理顺序）；
        /// 不改变位置、不产生位移。幂等：生效那一刻若该单位既无活动路径、也没有被丢弃的待处理
        /// <c>move</c> 意图，静默不触发 <see cref="OnMoveStopped"/>。
        /// </para>
        /// </summary>
        public void Stop(Id unitId)
        {
            _world.SubmitIntent(new Intent(unitId, "move_stop"));
        }

        /// <summary>供 <see cref="MovementTickHandler"/>（同程序集）在寻路失败时回调触发
        /// <see cref="OnMoveFailed"/>，不对外公开——外部调用方只应通过 <see cref="Request"/> 与订阅
        /// <see cref="OnMoveFailed"/> 与本类型交互。</summary>
        internal void RaiseMoveFailed(Id unitId, Vec2 from, Vec2 to) => OnMoveFailed?.Invoke(unitId, from, to);

        /// <summary>供 <see cref="MovementTickHandler"/>（同程序集）回调触发
        /// <see cref="OnMoveFailedDetailed"/>，不对外公开，惯例同 <see cref="RaiseMoveFailed"/>。
        /// </summary>
        internal void RaiseMoveFailedDetailed(Id unitId, Vec2 from, Vec2 to, MoveFailReason reason) =>
            OnMoveFailedDetailed?.Invoke(unitId, from, to, reason);

        /// <summary>供 <see cref="MovementTickHandler"/>（同程序集）回调触发
        /// <see cref="OnMoveStopped"/>，不对外公开，惯例同 <see cref="RaiseMoveFailed"/>。</summary>
        internal void RaiseMoveStopped(Id unitId, Vec2 position, MoveStopReason reason) =>
            OnMoveStopped?.Invoke(unitId, position, reason);
    }
}
