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
    }
}
