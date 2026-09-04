using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 每个 tick 的八个固定阶段，顺序即 03_运行时骨架.md 第 4.2 节规定的顺序，
    /// 两种模式（连续/离散）共用同一套顺序，不可调整（见第 4.2 节表格）。
    /// </summary>
    public enum TickPhase
    {
        /// <summary>1. 输入意图收集。</summary>
        IntentCollection,

        /// <summary>2. AI 决策。</summary>
        AiDecision,

        /// <summary>3. 技能管线。</summary>
        SkillPipeline,

        /// <summary>4. 移动与导航。</summary>
        MovementAndNavigation,

        /// <summary>5. 战斗结算。</summary>
        CombatResolution,

        /// <summary>6. 触发评估。</summary>
        TriggerEvaluation,

        /// <summary>7. 事件派发——由 <see cref="WorldSim"/> 自己执行，不可外部注册处理器。</summary>
        EventDispatch,

        /// <summary>8. 生命周期清理——由 <see cref="WorldSim"/> 自己执行，不可外部注册处理器。</summary>
        LifecycleCleanup
    }

    /// <summary>
    /// 挂在某个 <see cref="TickPhase"/> 上的处理器（见 03 第 4.2 节）。同一阶段可注册多个
    /// 处理器，按 <see cref="IWorldSim.RegisterPhaseHandler"/> 调用顺序依次执行。
    /// </summary>
    public interface ITickPhaseHandler
    {
        void Execute(SimStep step, IWorldSim world);
    }

    /// <summary>
    /// 世界模拟契约（见 03 第 4、9 节）。承载 03 第 4.1 节列出的全部逻辑对象集合
    /// （本模块只提供公共基类 <see cref="Entity"/> 与集合管理机制，具体子类
    /// Units/GameObjects/Projectiles/AreaTriggers/DroppedLoot 属于更上层模块）。
    /// </summary>
    public interface IWorldSim
    {
        /// <summary>处理一个模拟步：按 03 第 4.2 节八步固定顺序执行。</summary>
        void Tick(SimStep step);

        /// <summary>按 id 查询实体，不存在返回 null。</summary>
        Entity? GetEntity(Id id);

        /// <summary>按条件查询实体，结果按 <c>EntityId</c> 序数排序。</summary>
        IReadOnlyList<Entity> QueryEntities(EntityFilter filter);

        /// <summary>标记某实体待销毁：真正的移除与 <c>entity.destroyed</c> 事件发生在
        /// 本次或下一次 <see cref="Tick"/> 的生命周期清理阶段（阶段 8）。</summary>
        void MarkForDestruction(Id id);

        /// <summary>把一个新实体加入集合并发出 <c>entity.created</c> 事件（见 03 第 5 节）。
        /// tick 内外均可调用；重复 id 抛 <see cref="System.InvalidOperationException"/>。</summary>
        void AddEntity(Entity entity);

        /// <summary>按类型生成一个确定性的运行期实体 id：<c>"&lt;kind&gt;.inst_&lt;序号&gt;"</c>，
        /// 序号从 1 起按 <paramref name="kind"/> 分别递增。</summary>
        Id AllocateEntityId(string kind);

        /// <summary>为某个可注册阶段追加一个处理器；<see cref="TickPhase.EventDispatch"/>、
        /// <see cref="TickPhase.LifecycleCleanup"/> 两个阶段由 <see cref="WorldSim"/> 自己执行，
        /// 传入这两个阶段抛 <see cref="System.ArgumentException"/>。</summary>
        void RegisterPhaseHandler(TickPhase phase, ITickPhaseHandler handler);

        /// <summary>本世界实例的通用计时器（见 03 第 8 节）。</summary>
        ISimTimers Timers { get; }

        /// <summary>当前存活（未销毁）实体总数。</summary>
        int EntityCount { get; }
    }
}
