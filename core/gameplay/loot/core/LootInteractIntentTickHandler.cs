using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// ADR-0062（消费方反馈第五批第 1 条）：把 <see cref="LootHost"/> 接入 sim_loop 的 tick 编排，挂在
    /// <see cref="TickPhase.TriggerEvaluation"/>——与 <c>Core.Carriers.Gobj.InteractIntentTickHandler</c>/
    /// <c>Core.Carriers.Creature.CreatureInteractIntentTickHandler</c> 共用同一个 <c>"interact"</c>
    /// Kind，按 <c>Args</c> 形状分流（消费方反馈第 2 条同一口径"interact 是输入层的意图，不应该绑定
    /// 到某一种场景实体类型"）：本处理器只认 <c>{loot_instance_id: Id}</c>，两个键都不含的
    /// <c>interact</c> 意图交由 gobj 侧处理器兜底记诊断（见该类型判断记录"ADR-0062"一节新增的第三个
    /// 排除分支），本处理器对此不重复警告。
    /// <para>
    /// 判断记录（挂载位置：<c>core/gameplay/assembly.GameplayAssembly</c> 而非
    /// <c>core/carriers/assembly.CarriersAssembly</c>）：gobj/creature 两条既有分流由
    /// <c>CarriersAssembly</c> 注册——<see cref="LootHost"/> 是 L4 玩法层模块（<c>core/gameplay/loot</c>），
    /// 构造发生在 <c>CarriersAssembly</c> 之后的 <c>GameplayAssembly</c> 内，本处理器只能在
    /// <see cref="LootHost"/> 构造完成之后才能注册（同该装配根 <c>LootExpiryTickHandler</c> 既有挂载
    /// 位置——两者都依赖 <see cref="LootHost"/>，同一阶段 <see cref="TickPhase.TriggerEvaluation"/>
    /// 相邻注册）。
    /// </para>
    /// </summary>
    public sealed class LootInteractIntentTickHandler : ITickPhaseHandler
    {
        private readonly LootHost _host;
        private readonly ILootDiagnostics _diagnostics;

        public LootInteractIntentTickHandler(LootHost host, ILootDiagnostics? diagnostics = null)
        {
            _host = host;
            _diagnostics = diagnostics ?? new InMemoryLootDiagnostics();
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            foreach (var intent in world.CurrentIntents)
            {
                if (intent.Kind != "interact")
                {
                    continue;
                }

                if (!intent.Args.TryGetValue("loot_instance_id", out var lootIdVal) || !(lootIdVal is JsonString lootIdStr))
                {
                    // 不是发给本处理器的形状（多半是 gobj 的 gobj_instance_id 或生物的
                    // creature_instance_id）：静默跳过，由对应处理器处理或记诊断（见类型判断记录）。
                    continue;
                }

                var lootInstanceId = new Id(lootIdStr.Value);
                var result = _host.Interact(intent.ActorId, lootInstanceId);
                if (!result.Success)
                {
                    _diagnostics.Warn(
                        $"interact 意图未成功（actorId=\"{intent.ActorId}\"，lootInstanceId=\"{lootInstanceId}\"）：{result.Reason}");
                }
            }
        }
    }
}
