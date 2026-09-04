using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;

namespace Tests.Foundation.Determinism
{
    /// <summary>
    /// 阶段 2"AI 决策"（<see cref="TickPhase.AiDecision"/>）测试处理器：对 <c>Kind == "npc"</c>
    /// 的实体每 tick 用分流随机源 <c>rng.Next(stream)</c> 决定是否随机走一步，走则用
    /// <c>rng.NextInt(stream, 0, 3)</c> 取方向，并 Enqueue 一条 <c>ai.state_changed</c> 事件
    /// （事件登记表已有该 key，见 `found.event_catalog`）。全部随机数经
    /// <see cref="IRngHost"/> 分流取得，不使用系统级伪随机数生成器，保证同种子同结果。
    /// </summary>
    internal sealed class WanderHandler : ITickPhaseHandler
    {
        private readonly IRngHost _rng;
        private readonly IEventBus _bus;
        private readonly Id _stream;

        public WanderHandler(IRngHost rng, IEventBus bus, Id stream)
        {
            _rng = rng;
            _bus = bus;
            _stream = stream;
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            // QueryEntities 结果按 EntityId 序数排序（见 WorldSim.QueryEntities），逐个消费
            // 同一条随机流：遍历顺序确定 ⇒ 随机数消耗顺序确定 ⇒ 结果可复现。
            var npcs = world.QueryEntities(new EntityFilter(kind: "npc"));

            for (var i = 0; i < npcs.Count; i++)
            {
                var npc = npcs[i];
                var roll = _rng.Next(_stream);
                if (roll >= 0.5)
                {
                    continue;
                }

                var direction = _rng.NextInt(_stream, 0, 3);
                var delta = direction switch
                {
                    0 => new Vec2(0, 1),
                    1 => new Vec2(0, -1),
                    2 => new Vec2(-1, 0),
                    _ => new Vec2(1, 0),
                };

                npc.Position = npc.Position + delta;

                var fields = new Dictionary<string, object?>
                {
                    ["unitId"] = npc.EntityId.Value,
                    ["oldState"] = "idle",
                    ["newState"] = $"wander_{direction}",
                };
                _bus.Enqueue(new GenericEvent(EventKeys.AiStateChanged, fields));
            }
        }
    }
}
