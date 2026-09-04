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

        /// <summary>供 <see cref="MovementTickHandler"/>（同程序集）在寻路失败时回调触发
        /// <see cref="OnMoveFailed"/>，不对外公开——外部调用方只应通过 <see cref="Request"/> 与订阅
        /// <see cref="OnMoveFailed"/> 与本类型交互。</summary>
        internal void RaiseMoveFailed(Id unitId, Vec2 from, Vec2 to) => OnMoveFailed?.Invoke(unitId, from, to);
    }
}
