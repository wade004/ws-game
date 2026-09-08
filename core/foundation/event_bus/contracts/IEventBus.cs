using System;
using Core.Foundation.Common;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// 事件总线契约（见 01_分层与依赖.md L0 模块表 event_bus 行"契约接口名：EventBus"、
    /// 03_运行时骨架.md 第 4.2 节 tick 第 7 步"事件派发"、落地方案与分阶段计划.md 第 4.2 节
    /// "同步派发 + tick 末批处理"）。事件总线承载全架构一切跨层/跨模块的事件通知（见 01 第 8 节
    /// 跨层调用三种合法方式之二），自身不发任何业务事件，也不认识任何具体业务事件类型，只认
    /// <see cref="IEvent.Key"/>。
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        /// 按 key 订阅：处理函数会收到任意实现 <see cref="IEvent"/> 且 <see cref="IEvent.Key"/>
        /// 等于 <paramref name="key"/> 的事件实例。同一 key 下多个订阅者按 Subscribe 调用顺序
        /// 被依次派发调用。
        /// </summary>
        SubscriptionHandle Subscribe(Id key, EventHandler handler);

        /// <summary>
        /// 按 key 订阅，并要求事件实例是具体类型 <typeparamref name="T"/>。若某次派发的事件
        /// Key 匹配但运行时类型不是 <typeparamref name="T"/>，按策略跳过该订阅者的这次调用并
        /// 记一条警告，不影响其它订阅者与派发流程（默认策略，见 EventBusOptions）。
        /// </summary>
        SubscriptionHandle Subscribe<T>(Id key, EventHandler<T> handler) where T : IEvent;

        /// <summary>把事件加入待处理队列，不立即派发（tick 内产生的事件走这里，
        /// tick 末尾统一由 <see cref="DispatchPending"/> 批量派发）。</summary>
        void Enqueue(IEvent evt);

        /// <summary>
        /// 按入队顺序派发全部待处理事件（供 tick 第 7 步"事件派发"调用）。派发过程中
        /// 订阅者通过 <see cref="Enqueue"/> 新产生的事件，会在同一次 <see cref="DispatchPending"/>
        /// 调用内继续被派发，直到队列清空；超过 <see cref="EventBusOptions.MaxDispatchPasses"/>
        /// 时停止并记一条诊断错误。返回本次调用实际派发的事件总数。
        /// </summary>
        int DispatchPending();

        /// <summary>
        /// 同步立即派发单个事件，不经过队列（供非 tick 上下文使用，例如应用状态机、
        /// 场景路由发出的事件）。
        /// </summary>
        void PublishImmediate(IEvent evt);

        /// <summary>
        /// CORE-170-03 根治（architecture/落地计划/audit-8160178-20260908，P2）：进入一个"读档不是
        /// 业务事件"抑制作用域（见 10_存档与持久化.md 存档契约勘误、<c>core/foundation/save_system/
        /// core/SaveSystem.cs</c> <c>Load</c> 判断记录）。本仓库多处 <see cref="Core.Foundation.
        /// SaveSystem.IPersistable.Load"/> 实现（<c>WorldState</c>/<c>DifficultyHost</c>/
        /// <c>AchievementHost</c> 等）已经明确"读档不是一次业务事件，不重发业务事件"这一惯例，但
        /// <c>EquipmentPersistable.Load</c> 为了复用真实装备逻辑重新聚合属性/技能/光环（见该类型
        /// 判断记录"读档后重新执行装备联动"），会调用真正的 <c>EquipmentHost.Equip</c>/<c>Unequip</c>，
        /// 这两者本身就会正常派发 <c>ItemEquipped</c>/<c>ItemUnequipped</c>/<c>StatChanged</c> 等
        /// 领域事件——<c>SaveSystem.Load</c> 回滚失败读档时重放这些调用，会让计数类消费者（如
        /// <c>AchievementHost</c> 的 <c>custom_event</c> 观察条件）把"读档/回滚期间的重放"误当成
        /// 真实的一次玩家操作再计一次数（真实探针复现：<c>achievement_before_event_dispatch=1</c>
        /// → <c>after_event_dispatch=2</c>，进度被回滚重放的事件错误推高并触发解锁）。
        /// <para>
        /// 作用域内经 <see cref="Enqueue"/>/<see cref="PublishImmediate"/> 提交的事件被直接丢弃
        /// （不进入待处理队列、不派发给任何订阅者），不是"标记后仍照常派发"——读档/回滚期间不产生
        /// 任何外部可观察的事件，与 <c>WorldState.Load</c> 等既有"读档不是业务事件"实现完全对齐，
        /// 消费者不需要各自识别"这是不是一次重放"，因为重放事件从一开始就不会到达它们。支持嵌套
        /// 调用（<c>SaveSystem.Load</c> 内部可能间接触发另一次 <c>Load</c>，惯例同其它可重入
        /// 场景）：只有全部嵌套作用域都释放（<see cref="IDisposable.Dispose"/>）后才真正恢复正常
        /// 派发。<c>SaveSystem.Load</c> 用 <c>using</c> 包裹整段"逐段 Load + 失败回滚"逻辑，成功
        /// 读档结束后在作用域<b>外</b>正常派发 <c>SaveLoadedEvent</c>/<c>SaveMigratedEvent</c>——
        /// "本次读档完成了"这个通知本身不是重放，理应正常送达。
        /// </para>
        /// <para>
        /// 默认实现（C#8 默认接口方法）返回一个空操作的 <see cref="IDisposable"/>、不做任何抑制：
        /// 本接口目前只有 <c>core/foundation/event_bus.EventBus</c> 一个生产实现，其它调用方
        /// （测试假实现，若存在）不因新增本成员而编译失败，只是丧失"读档期间抑制事件"这一项能力
        /// （事件仍会照常派发，退化为本次改动之前的行为），不是必须支持的抽象成员。
        /// </para>
        /// </summary>
        IDisposable SuppressDispatch() => NoopSuppressScope.Instance;

        private sealed class NoopSuppressScope : IDisposable
        {
            public static readonly NoopSuppressScope Instance = new NoopSuppressScope();
            public void Dispose()
            {
            }
        }
    }
}
