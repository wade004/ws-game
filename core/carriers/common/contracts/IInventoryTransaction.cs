using System;

namespace Core.Carriers.Common
{
    /// <summary>
    /// R01 根治（architecture/落地计划/audit-5e779c6-20260907）：批量物品发放事务，由 <see
    /// cref="IBatchableInventoryHost.BeginBatch"/> 开启。判断记录——问题根因：<c>RewardDispatcher.
    /// GrantItems</c> 整批发放中某一项失败时，此前对已发放项按"实际落地量"调用 <c>RemoveItem</c> 补一
    /// 条 <c>item.removed</c> 事件来抵消，但更早那条 <c>item.added</c> 事件已经入队（<see
    /// cref="Core.Foundation.EventBus.IEventBus.Enqueue"/> 只入队、不立即派发），二者作为两条独立事件
    /// 排在同一批 <c>DispatchPending</c> 里先后派发：下游订阅者（如 <c>QuestHost.HandleItemAdded</c>
    /// 的 <c>consumeOnProgress</c>）会把先到的 <c>item.added</c> 当作真实发生的事件立即消费、推进进度，
    /// 后到的 <c>item.removed</c> 抵消不了已经产生的副作用——最终表现为"整批回滚后背包数量确实回到
    /// 发放前，但任务系统仍然误判为发放成功并额外扣走了一份已有物品、推进了一次进度"。
    /// <para>
    /// 事务把"这批发放最终是否成立"提前到事件真正离开宿主之前判定：事务期间对背包做的增删操作，其
    /// 对应的 <c>item.added</c>/<c>item.removed</c> 通知事件不会立即发往事件总线，而是缓存在事务内部；
    /// <see cref="Commit"/> 把缓存事件按发生顺序原样补发；<see cref="Dispose"/>（未调用 <see
    /// cref="Commit"/> 就释放，即回滚）则把事务期间对背包状态的全部变更整体撤销、缓存事件整批丢弃——
    /// 调用方与事件总线的订阅者完全观察不到这次失败的发放发生过，不会再被拆成两条独立事件分别响应。
    /// </para>
    /// </summary>
    public interface IInventoryTransaction : IDisposable
    {
        /// <summary>提交事务：把事务期间缓存的事件按顺序发往事件总线。只应调用一次；重复调用，或在
        /// <see cref="Dispose"/> 之后调用，均为 no-op（不重复发送、不抛异常）。</summary>
        void Commit();
    }

    /// <summary>
    /// R01 根治：可选能力接口——<see cref="IInventoryHost"/> 的实现如果支持真正的批量事务（见 <see
    /// cref="IInventoryTransaction"/>），额外实现本接口。调用方（如 <c>RewardDispatcher</c>）用
    /// <c>is</c> 判断是否可用；不支持本接口的 <see cref="IInventoryHost"/> 实现（多数测试用的最小
    /// Fake）保持历史行为不变——不强制所有实现都要跟进，只有真正需要"整批失败时事件也要一并撤销"这一
    /// 保证的调用方才需要依赖本接口，其余调用方按原有 <see cref="IInventoryHost.RemoveItem"/> 逐项
    /// 回滚的方式不受影响。
    /// </summary>
    public interface IBatchableInventoryHost
    {
        /// <summary>开启一次批量事务，见 <see cref="IInventoryTransaction"/>。
        /// <para>
        /// 第五轮外部审核相邻缺口根治（architecture/落地计划/audit-5e779c6-20260907）：支持隐式
        /// 加入外层事务——若调用时已有一个事务在同一宿主实例上开启（例如 <c>QuestHost.TurnIn</c>
        /// 已经 <see cref="BeginBatch"/> 且尚未 Commit/Dispose，其内部又调用了
        /// <c>RewardDispatcher.Grant</c>，后者同样会尝试 <see cref="BeginBatch"/>），返回的
        /// <see cref="IInventoryTransaction"/> 是一个透传句柄：其 <c>Commit</c>/<c>Dispose</c>
        /// 都是 no-op，不改变宿主任何状态——真正的提交（缓存事件按序补发）/回滚（<see cref="_bags"/>
        /// 与缓存事件一并撤销）权限，始终且只归最先在这一轮嵌套里调用 <see cref="BeginBatch"/> 的
        /// 那次持有的最外层事务实例。判断记录：这是让"发起方"（QuestHost 这类需要把'移除若干物品 +
        /// 调用可能内部也会发放物品的奖励发放器'包成一次原子操作的上层调用方）与"被调用方内部同样
        /// 独立开启事务的既有代码"（RewardDispatcher.GrantItems，见其判断记录）无需互相感知即可
        /// 安全组合的最小改法——不要求调用方查询"是否已在事务中"，也不需要额外的"透传已有事务"参数，
        /// 嵌套调用天然与不嵌套调用返回同一个契约类型，行为按是否已有外层事务自动收敛。
        /// </para>
        /// </summary>
        IInventoryTransaction BeginBatch();
    }
}
