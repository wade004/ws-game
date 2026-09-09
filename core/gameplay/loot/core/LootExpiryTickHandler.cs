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
                // 文档勘误（architecture/落地计划/audit-ac3b622-20260909，DATA-DOC-02，文档更新）：
                // ADR-0013 离散时间模型已接线（见 core/gameplay/assembly.GameplayAssembly、
                // Core.Foundation.SimLoop.TurnScheduler），本处旧注释"离散时间模型本项目暂不启用"
                // 是过期文案，不是当前的真实局部语义——过期销毁/跟随移动一类逻辑按设计只在连续步
                // 推进（07 第 4 节未要求离散步内也推进），本处理器收到 Discrete 步时按已有约定跳过
                // 本次清理调用，惯例同 core/carriers/summon.SummonTickHandler 的同一处理。用于判断
                // "是否过期"的绝对模拟时钟（_simTimeProvider，见 Core.Rules.Assembly.RulesAssembly.
                // TrackSimTime 订阅 sim.tick_started 累加）不受这一步跳过影响，在离散步下仍照常
                // 累加——只是本 handler 这一 tick 不去调用 PurgeExpired，不能把这一处局部收窄表述成
                // "离散时间模型整体不启用"。
                return;
            }

            _lootHost.PurgeExpired(_simTimeProvider());
        }
    }
}
