using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json）。<c>sim.turn_started</c>、
    /// <c>sim.turn_ended</c>、<c>sim.round_ended</c>、<c>sim.awaiting_input</c> 四个
    /// 属于离散模式，本项目暂不启用，本模块不发这四个事件，因此这里不登记对应常量。
    /// </summary>
    public static class SimEventKeys
    {
        public static readonly Id TickStarted = new Id("sim.tick_started");
        public static readonly Id TickFinished = new Id("sim.tick_finished");
        public static readonly Id EntityCreated = new Id("entity.created");
        public static readonly Id EntityDestroyed = new Id("entity.destroyed");
    }

    /// <summary>
    /// <c>WorldSim.tick</c> 开始处理本次 <see cref="SimStep"/> 时触发（见 01 模块表
    /// sim_loop 行、03 第 4.2 节）。字段：<see cref="TickIndex"/>、<see cref="Dt"/>。
    /// <para>
    /// 判断记录：<c>found.event_catalog.json</c> 里 <c>sim.tick_started</c> 一行的
    /// <c>fields</c> 只登记了 <c>tickIndex</c>（该行 description 明确标注"字段为建议值"，
    /// 且 03 原文本身未逐字段规定 tick_started 的载荷）。本类型额外携带 <see cref="Dt"/>——
    /// 连续步的经过秒数——因为下游（尤其是表现层插值、性能采样）订阅 tick 开始事件时
    /// 普遍需要知道本 tick 的步长，省得再反查 <see cref="ISimClockHost.StepSeconds"/>；
    /// 这不与 03 原文冲突（03 未对该事件的字段做出排他性规定），按任务书"若登记表字段与
    /// 03 原文不一致以 03 为准"处理为"03 未限定字段 ⇒ 允许在登记表建议值之外按需要补充"，
    /// 已在交付报告中标注，供设计层复核是否需要同步更新 <c>found.event_catalog.json</c>。
    /// </para>
    /// </summary>
    public sealed class SimTickStartedEvent : IEvent
    {
        public Id Key => SimEventKeys.TickStarted;

        /// <summary>本次 tick 的序号（从 0 起，<see cref="WorldSim"/> 自身维护）。</summary>
        public long TickIndex { get; }

        /// <summary>本次 tick 经过的秒数；离散步下恒为 0。</summary>
        public double Dt { get; }

        public SimTickStartedEvent(long tickIndex, double dt)
        {
            TickIndex = tickIndex;
            Dt = dt;
        }
    }

    /// <summary>
    /// <c>WorldSim.tick</c> 完成八步处理后触发（见 01 模块表 sim_loop 行、03 第 4.2 节）。
    /// 字段与 <c>found.event_catalog.json</c> 登记一致：<see cref="TickIndex"/>。
    /// </summary>
    public sealed class SimTickFinishedEvent : IEvent
    {
        public Id Key => SimEventKeys.TickFinished;

        public long TickIndex { get; }

        public SimTickFinishedEvent(long tickIndex)
        {
            TickIndex = tickIndex;
        }
    }

    /// <summary>
    /// <see cref="WorldSim"/> 把新逻辑对象加入集合时触发，供 ViewBinder 创建 View
    /// （见 03 第 5 节）。字段与 <c>found.event_catalog.json</c> 登记一致：
    /// <see cref="EntityId"/>、<see cref="Kind"/>、<see cref="DisplayId"/>。
    /// </summary>
    public sealed class EntityCreatedEvent : IEvent
    {
        public Id Key => SimEventKeys.EntityCreated;

        public Id EntityId { get; }

        public string Kind { get; }

        /// <summary>用于查询外形的逻辑 id：<c>TemplateId ?? EntityId</c>（见 03 第 5 节）。</summary>
        public Id DisplayId { get; }

        public EntityCreatedEvent(Id entityId, string kind, Id displayId)
        {
            EntityId = entityId;
            Kind = kind;
            DisplayId = displayId;
        }
    }

    /// <summary>
    /// <see cref="WorldSim"/> 生命周期清理阶段真正移除某实体前触发，供 ViewBinder 销毁
    /// View（见 03 第 5、4.2 节步骤 8）。字段与 <c>found.event_catalog.json</c> 登记一致：
    /// <see cref="EntityId"/>。
    /// </summary>
    public sealed class EntityDestroyedEvent : IEvent
    {
        public Id Key => SimEventKeys.EntityDestroyed;

        public Id EntityId { get; }

        public EntityDestroyedEvent(Id entityId)
        {
            EntityId = entityId;
        }
    }
}
