using System;
using Core.Foundation.Common;

namespace Core.Carriers.Gobj
{
    /// <summary>对 <paramref name="dialogRef"/>（<c>dialog.gossip_menu</c> 动作项 id，见 07 第 3.3
    /// 节）打开对话的 L4 回调（见 <see cref="GobjOptions.DialogOpener"/>）。</summary>
    public delegate void DialogOpenerDelegate(Id unitId, Id dialogRef);

    /// <summary>把 <paramref name="teleportTargetRef"/> 解析成具体地图 + 坐标的 L4 回调（见
    /// <see cref="GobjOptions.TeleportResolver"/>）；无法解析返回 null。</summary>
    public delegate (Id MapId, Vec2 Position)? TeleportResolverDelegate(Id teleportTargetRef);

    /// <summary>请求为 <paramref name="unitId"/> 存档的 L4 回调（见 <see cref="GobjOptions.SaveRequester"/>）。</summary>
    public delegate void SaveRequesterDelegate(Id unitId);

    /// <summary>
    /// CR140-01 根治（architecture/落地计划/audit-c86bfa9-20260908）：<c>chest</c> 一次性开箱时，掉落
    /// 表抽出的多个物品堆叠交付进背包，其中某一堆放不下该怎么对待整次开箱——与
    /// <c>Core.Gameplay.Loot.LootPickupPolicy</c> 是同一层面的两难，本枚举独立定义在 L3（不依赖 L4
    /// 的 <c>core/gameplay/loot</c>），语义与命名对齐 <c>LootPickupPolicy</c>，供 <see
    /// cref="GameObjectHost"/> 复用 <c>LootHost.PickUp</c> 同款 batch/Partial 判断记录。
    /// </summary>
    public enum GobjLootDeliveryPolicy
    {
        /// <summary>只要有任意一个物品堆叠放不下（部分或全部），整次开箱不生效：已经放入背包的堆叠
        /// 按事务整体撤销（<see cref="Core.Carriers.Common.IBatchableInventoryHost"/> 可用时）或逐项
        /// 补偿移除，<c>open_state</c> 不标记为已开，允许下次交互重新完整 roll 一遍并重试。</summary>
        Reject,

        /// <summary>能拿多少拿多少：放不下的部分留在箱子自身的待补发记录里（不落地为地面掉落物——L3
        /// 不引入 <c>core/gameplay/loot</c> 的 <c>DroppedLootEntity</c> 概念），<c>open_state</c>
        /// 标记为已开（避免下次交互重新 roll 导致已交付部分之上又叠加一份全新掉落），下次交互只补发
        /// 剩余部分，不重新抽取。</summary>
        Partial,
    }

    /// <summary>把 <paramref name="questActionRef"/> 分发给任务系统的 L4 回调（见
    /// <see cref="GobjOptions.QuestActionDispatcher"/>）。</summary>
    public delegate void QuestActionDispatcherDelegate(Id unitId, Id questActionRef);

    /// <summary>
    /// <see cref="GameObjectHost"/> 的口味配置项 + L4 回调注入点（见 01_分层与依赖.md 第 8 节"跨层
    /// 调用三种合法方式"之三"策略注入/依赖倒置"：<c>dialog.gossip_menu</c>/跨地图传送/存档/任务系统
    /// 均是 L4 玩法层职责，本模块——L3 载体层——不得直接引用它们，改用委托回调由游戏组装根注入；
    /// 未注入时对应交互按 <see cref="Core.Carriers.Common.InteractOutcome.NoAction"/> 处理并记一条
    /// 诊断，见 <see cref="GameObjectHost"/> 顶部判断记录）。
    /// </summary>
    public sealed class GobjOptions
    {
        /// <summary><see cref="GameObjectHost.Interact"/> 允许的最大交互距离（见 07 第 3.6 节
        /// 契约"交互的统一入口"，具体距离阈值属任务书拍板的策略配置项），默认 2。</summary>
        public double InteractRange { get; set; } = 2;

        /// <summary><see cref="Core.Carriers.Common.IWorldFlags.Set"/> 的 <c>writerId</c>（见 05 第
        /// 8.2 节"每次写入必须带 writerId"），默认 <c>"gobj.host"</c>。</summary>
        public Id WriterId { get; set; } = new Id("gobj.host");

        /// <summary>
        /// <see cref="GameObjectHost.TriggerTrap"/> 释放 <c>trap</c> 效果时使用的施法者 id（见 06 第
        /// 7 节 <c>ISkillHost.CastSkill</c> 要求施法者是单位，<c>GameObject</c> 本身不是
        /// <c>Unit</c>，无法直接作为施法者）。判断记录：本字段可空，缺省（null）时
        /// <see cref="GameObjectHost.TriggerTrap"/> 改用触发该陷阱的单位自身作为施法者——多数陷阱
        /// 效果（伤害/控制）以"谁踩中就打谁"为默认语义，这一近似覆盖绝大多数场景；若某个陷阱需要
        /// 一个与触发者无关的固定施法者（如"陷阱对所有踩中者都按陷阱设置者的属性结算伤害"），
        /// 由具体游戏在装配期显式设置本字段。
        /// </summary>
        public Id? TrapCasterId { get; set; }

        /// <summary>见 <see cref="DialogOpenerDelegate"/>；未注入时 <c>on_use: dialog</c> 分发失败
        /// （记诊断，返回 <see cref="Core.Carriers.Common.InteractOutcome.NoAction"/>，<c>Success=false</c>）。</summary>
        public DialogOpenerDelegate? DialogOpener { get; set; }

        /// <summary>见 <see cref="TeleportResolverDelegate"/>；未注入或解析失败时 <c>teleporter</c>
        /// 交互不产生位移，只记诊断。</summary>
        public TeleportResolverDelegate? TeleportResolver { get; set; }

        /// <summary>见 <see cref="SaveRequesterDelegate"/>；未注入时 <c>save_point</c> 交互只记诊断。</summary>
        public SaveRequesterDelegate? SaveRequester { get; set; }

        /// <summary>见 <see cref="QuestActionDispatcherDelegate"/>；未注入时 <c>quest_object</c>
        /// 交互只记诊断。</summary>
        public QuestActionDispatcherDelegate? QuestActionDispatcher { get; set; }

        /// <summary>
        /// 模拟时间源，供 <c>gather_node</c> 的 <c>respawn_after_use</c> 到期判定使用（见 07 第 3.1
        /// 节 <c>gather_node</c> 行 <c>respawn_after_use</c>）。判断记录：任务书"respawn_after_use
        /// 到期后可再次采集——Update(dt) 或读取时判断，二选一说明"，本模块选择"读取时判断"（不新增
        /// <c>ITickPhaseHandler</c>）——采集只在 <see cref="GameObjectHost.Interact"/> 被调用时才有
        /// 意义判定，不需要每 tick 主动检查是否已到刷新时间，"读取时判断"与"Update(dt) 主动判断"
        /// 在效果上等价（下次交互时才会体现刷新结果），但省去一个额外的 tick 阶段注册与状态机维护。
        /// 未注入时默认为 <c>() =&gt; 0</c>（等价于"时间恒定不前进"，配合 <c>respawn_after_use &gt; 0</c>
        /// 时表现为"永不刷新"，与"未提供时间源就不应假装时间在流逝"的保守语义一致）。
        /// </summary>
        public Func<double> SimTime { get; set; } = () => 0;

        /// <summary>CR140-01 根治：<c>chest</c> 一次性开箱时背包放不下抽出物品的整体处理策略，见
        /// <see cref="GobjLootDeliveryPolicy"/>，默认 <see cref="GobjLootDeliveryPolicy.Partial"/>
        /// （与 <c>LootOptions.FullPolicy</c> 默认值同一取舍：不会因为背包只差一格就让整批已经能
        /// 装下的部分也作废）。</summary>
        public GobjLootDeliveryPolicy ChestLootPolicy { get; set; } = GobjLootDeliveryPolicy.Partial;

        /// <summary>
        /// CR150-04 根治（architecture/落地计划/audit-3224ca1-20260908，P2）：<c>gather_node</c>
        /// 采集时背包放不下抽出物品的整体处理策略，语义与 <see cref="ChestLootPolicy"/> 完全对称
        /// （同用 <see cref="GobjLootDeliveryPolicy"/>，默认同为 <see
        /// cref="GobjLootDeliveryPolicy.Partial"/>）。判断记录——不复用 <see cref="ChestLootPolicy"/>
        /// 同一个字段：两种 <c>GobjKind</c> 的交付策略是各自独立的口味配置，具体游戏可能希望箱子按
        /// Partial（能拿多少拿多少）、采集物按 Reject（要么整批拿到要么一件都不拿，逼玩家先腾出
        /// 足够空间）分别配置，合用一个字段会让这种组合无法表达；新增字段是纯加法，不影响
        /// <see cref="ChestLootPolicy"/> 的既有默认值与既有调用方。
        /// </summary>
        public GobjLootDeliveryPolicy GatherNodeLootPolicy { get; set; } = GobjLootDeliveryPolicy.Partial;
    }
}
