using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// ADR-0051：把 <see cref="CreatureInteractionHost"/> 接入 sim_loop 的 tick 编排，挂在
    /// <see cref="TickPhase.TriggerEvaluation"/>（同
    /// <c>Core.Carriers.Gobj.InteractIntentTickHandler</c> 一贯挂载阶段）。消费
    /// <see cref="IWorldSim.CurrentIntents"/> 中 <see cref="Intent.Kind"/> 为 <c>"interact"</c> 的
    /// 意图——与 gobj 共用同一个意图 Kind（消费方反馈第 2 条口径"interact 是输入层的意图，不应该
    /// 绑定到某一种场景实体类型"），按 <c>Args</c> 形状分流：本处理器只认 <c>{creature_instance_id:
    /// Id}</c>，<c>Core.Carriers.Gobj.InteractIntentTickHandler</c> 只认 <c>{gobj_instance_id: Id}</c>；
    /// 两个键都不含的 <c>interact</c> 意图交由 gobj 侧处理器兜底记诊断（见该类型判断记录），本处理器
    /// 对此不重复警告。
    /// </summary>
    public sealed class CreatureInteractIntentTickHandler : ITickPhaseHandler
    {
        private readonly ICreatureInteractionHost _host;
        private readonly ICreatureDiagnostics _diagnostics;

        public CreatureInteractIntentTickHandler(ICreatureInteractionHost host, ICreatureDiagnostics? diagnostics = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _diagnostics = diagnostics ?? new InMemoryCreatureDiagnostics();
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            foreach (var intent in world.CurrentIntents)
            {
                if (intent.Kind != "interact")
                {
                    continue;
                }

                if (!intent.Args.TryGetValue("creature_instance_id", out var creatureIdVal) || !(creatureIdVal is JsonString creatureIdStr))
                {
                    // 不是发给本处理器的形状（多半是 gobj 的 gobj_instance_id），静默跳过——由
                    // Core.Carriers.Gobj.InteractIntentTickHandler 处理或记诊断，见类型判断记录。
                    continue;
                }

                var creatureInstanceId = new Id(creatureIdStr.Value);
                var result = _host.Interact(intent.ActorId, creatureInstanceId);
                if (!result.Success)
                {
                    _diagnostics.Warn(
                        $"interact 意图未成功（actorId=\"{intent.ActorId}\"，creatureInstanceId=\"{creatureInstanceId}\"）：{result.Outcome}");
                }
            }
        }
    }
}
