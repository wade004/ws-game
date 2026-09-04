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

        /// <summary>
        /// 立即移除全部实体，对每个被移除的实体 <c>Enqueue</c> 一条 <c>entity.destroyed</c>
        /// 事件（供场景路由切换场景时清空旧场景集合，见 03 第 6 节步骤 4"卸载旧场景的
        /// WorldSim 集合与全部 View"）；同时清空全部通用计时器（<see cref="Timers"/>）与待销毁
        /// 列表。与 <see cref="Tick"/> 阶段 8 同样只 <c>Enqueue</c> 不立即派发——调用方需要
        /// 自行经事件总线的 <c>DispatchPending</c>（或下一次 <see cref="Tick"/>）才会把这些
        /// <c>entity.destroyed</c> 事件送达订阅者。调用后 <see cref="EntityCount"/> 归零，
        /// 全部既有 <see cref="TimerHandle"/> 立即失效（<see cref="ISimTimers.IsAlive"/> 返回
        /// false）。
        /// </summary>
        void ClearAll();

        /// <summary>本世界实例的通用计时器（见 03 第 8 节）。</summary>
        ISimTimers Timers { get; }

        /// <summary>当前存活（未销毁）实体总数。</summary>
        int EntityCount { get; }

        /// <summary>
        /// 提交一条意图（见 <see cref="Intent"/>、03 第 4.2 节步骤 1"输入意图收集"）：
        /// tick 外或某次 <see cref="Tick"/> 阶段 1 之前调用均可，进入"本 tick 待收集"的队列；
        /// 确定性：多次调用按调用顺序进入 <see cref="CurrentIntents"/>（呼应 03 第 3.2 节第 6 条
        /// "回放 = 意图序列"——同一份意图序列、同样的提交顺序，产生同样的结果）。
        /// </summary>
        void SubmitIntent(Intent intent);

        /// <summary>
        /// 本 tick 的意图列表：在 <see cref="Tick"/> 阶段 1（<see cref="TickPhase.IntentCollection"/>）
        /// 开头由待提交队列整体搬入（此后同一 tick 内多次读取值不变），阶段 8
        /// （<see cref="TickPhase.LifecycleCleanup"/>）结束时清空。tick 之外（尚未调用过
        /// <see cref="Tick"/>，或上一次 <see cref="Tick"/> 已完成）读取为空列表。
        /// </summary>
        IReadOnlyList<Intent> CurrentIntents { get; }

        /// <summary>
        /// 追加一条意图进入本 tick 的 <see cref="CurrentIntents"/>（集成任务补齐的契约缺口，见
        /// 03_运行时骨架.md 第 4.2 节步骤 2"AI 决策……为非玩家单位生成本 tick 的意图，追加进意图
        /// 列表"——原契约只有 <see cref="SubmitIntent"/>，产生的意图要到下一 tick 才进入
        /// <see cref="CurrentIntents"/>，与文档"追加进意图列表"的字面描述存在一 tick 落差，见
        /// <c>core/rules/ai/core/AiTickHandler.cs</c> 与 ai 模块 README"契约缺口"一节）。与
        /// <see cref="SubmitIntent"/>（tick 内外均可调用，进入"下一 tick 待收集"队列）互补：本方法
        /// 只在某次 <see cref="Tick"/> 执行期间（阶段 1～7 之间，<see cref="CurrentIntents"/> 已经
        /// 固定但尚未在阶段 8 末尾清空）可调用，让本 tick 内产生的意图（如 AI 决策）立即参与本
        /// tick 剩余阶段，不必等到下一 tick 才生效；tick 之外调用抛
        /// <see cref="System.InvalidOperationException"/>。追加顺序即调用顺序（确定性同
        /// <see cref="SubmitIntent"/>）。
        /// </summary>
        void AppendCurrentIntent(Intent intent);
    }
}
