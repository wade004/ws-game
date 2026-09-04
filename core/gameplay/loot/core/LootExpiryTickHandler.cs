using System;
using Core.Foundation.SimLoop;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// 地面掉落物过期销毁（见 05 第 1.6 节 <c>expireAt</c>、<see cref="LootOptions.DefaultLifetime"/>）。
    /// <para>
    /// 判断记录（挂载阶段）：任务书要求"不可注册 <c>TickPhase.LifecycleCleanup</c>（<see
    /// cref="IWorldSim"/> 自己执行）→ 用 <c>TriggerEvaluation</c>"（见 <see
    /// cref="IWorldSim.RegisterPhaseHandler"/> 注释"<c>EventDispatch</c>/<c>LifecycleCleanup</c>
    /// 两个阶段由 WorldSim 自己执行"）：<see cref="TickPhase.TriggerEvaluation"/>（阶段 6）在
    /// <see cref="TickPhase.EventDispatch"/>（阶段 7，真正把 <see cref="IWorldSim.MarkForDestruction"/>
    /// 排入的销毁请求落地到阶段 8 生命周期清理）之前，语义上贴合"触发条件评估"（掉落物过期即一种
    /// 随时间推移触发的条件），且早于事件派发保证 <see cref="LootExpiryTickHandler"/> 本 tick 标记的
    /// 销毁能在同一 tick 内完成实际清理，不用等到下一 tick。
    /// </para>
    /// </summary>
    public sealed class LootExpiryTickHandler : ITickPhaseHandler
    {
        private readonly LootHost _lootHost;
        private readonly Func<double> _simTimeProvider;

        public LootExpiryTickHandler(LootHost lootHost, Func<double> simTimeProvider)
        {
            _lootHost = lootHost ?? throw new ArgumentNullException(nameof(lootHost));
            _simTimeProvider = simTimeProvider ?? throw new ArgumentNullException(nameof(simTimeProvider));
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (step.Kind != SimStepKind.Continuous)
            {
                // 离散时间模型本项目暂不启用（ADR-0013），同 core/carriers/summon.SummonTickHandler
                // 的既有惯例：收到 Discrete 步直接跳过，不推进过期判定。
                return;
            }

            _lootHost.PurgeExpired(_simTimeProvider());
        }
    }
}
