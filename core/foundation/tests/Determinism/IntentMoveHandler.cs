using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;

namespace Tests.Foundation.Determinism
{
    /// <summary>
    /// 阶段 4"移动与导航"（<see cref="TickPhase.MovementAndNavigation"/>）测试处理器：消费本
    /// tick <see cref="IWorldSim.CurrentIntents"/> 里 <c>Kind == "move"</c> 的意图，按
    /// <c>Args{dx,dy}</c> 平移对应实体，并 Enqueue 一条 <c>unit.moved</c> 事件（见
    /// <c>DeterminismTests</c> 用例设计：确定性回放集成测试自定义事件，字段见
    /// <see cref="DeterministicWorld.BuildCatalog"/> 里的登记）。不读取任何系统时间/随机源，
    /// 结果完全由 <see cref="IWorldSim.CurrentIntents"/> 的内容与提交顺序决定。
    /// </summary>
    internal sealed class IntentMoveHandler : ITickPhaseHandler
    {
        private readonly IEventBus _bus;
        private readonly Id _unitMovedKey;

        public IntentMoveHandler(IEventBus bus, Id unitMovedKey)
        {
            _bus = bus;
            _unitMovedKey = unitMovedKey;
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            var intents = world.CurrentIntents;

            for (var i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                if (intent.Kind != "move")
                {
                    continue;
                }

                var entity = world.GetEntity(intent.ActorId);
                if (entity == null)
                {
                    continue;
                }

                var dx = ReadNumber(intent.Args, "dx");
                var dy = ReadNumber(intent.Args, "dy");
                var newPosition = new Vec2(entity.Position.X + dx, entity.Position.Y + dy);
                entity.Position = newPosition;

                var fields = new Dictionary<string, object?>
                {
                    ["unitId"] = intent.ActorId.Value,
                    ["dx"] = dx,
                    ["dy"] = dy,
                    ["newX"] = newPosition.X,
                    ["newY"] = newPosition.Y,
                };
                _bus.Enqueue(new GenericEvent(_unitMovedKey, fields));
            }
        }

        private static double ReadNumber(JsonObject args, string key)
        {
            return args.TryGetValue(key, out var value) && value is JsonNumber number ? number.Value : 0.0;
        }
    }
}
