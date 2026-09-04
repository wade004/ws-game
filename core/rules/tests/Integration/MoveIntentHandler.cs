using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// 挂在 <see cref="TickPhase.MovementAndNavigation"/> 的最小移动系统替身：消费本 tick
    /// <see cref="IWorldSim.CurrentIntents"/> 里 <c>Kind == "move"</c> 的意图（<c>AiHost</c>
    /// 产出的位移意图，见 <c>core/rules/ai/core/AiHost.cs</c> <c>BuildMoveIntent</c>，
    /// <c>Args: {dx, dy}</c>），按位移平移 <c>TestUnit.Position</c> 并同步更新
    /// <see cref="StubSpatialQuery"/> 里的登记位置——<c>core/rules/assembly</c> 的 <c>RulesAssembly</c>
    /// 明确不负责这一阶段（见其 README"tick 阶段挂载表"："把 move 意图真正应用成位置变化"是 L3
    /// 载体层/移动系统的职责），本类型只是集成测试自己补的最小替身，不代表任何正式实现。
    /// </summary>
    internal sealed class MoveIntentHandler : ITickPhaseHandler
    {
        private readonly StubSpatialQuery _spatial;
        private readonly double _spatialRadius;

        public MoveIntentHandler(StubSpatialQuery spatial, double spatialRadius = 0.1)
        {
            _spatial = spatial;
            _spatialRadius = spatialRadius;
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
                entity.Position = new Vec2(entity.Position.X + dx, entity.Position.Y + dy);

                _spatial.Register(intent.ActorId, entity.Position, _spatialRadius);
            }
        }

        private static double ReadNumber(JsonObject args, string key) =>
            args.TryGetValue(key, out var value) && value is JsonNumber number ? number.Value : 0.0;
    }
}
