using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <c>unit.target_changed</c> 的 <c>cause</c> 取值（ADR-0177）。字符串常量而非枚举：事件字段对表达式/反馈绑定可见，
    /// 新增取值不破坏既有消费方。
    /// </summary>
    public static class TargetChangeCause
    {
        /// <summary>调用方（玩家点选 / Tab 轮换 / 脚本）显式设置或清除。</summary>
        public const string Manual = "manual";

        /// <summary>受击自动选中：单位无有效目标时被敌对单位（经召唤者归属链归到的施放者）打中。</summary>
        public const string AutoHit = "auto_hit";

        /// <summary>当前目标死亡，自动清除。</summary>
        public const string TargetDied = "target_died";

        /// <summary>当前目标离开世界（销毁 / 换图清场），自动清除。</summary>
        public const string TargetGone = "target_gone";
    }

    /// <summary>
    /// 单位的"当前目标"持有者（ADR-0177）：目标选择式战斗里玩家（或任何单位）选中的敌人。
    /// 目标是操作状态，不是世界规则——但"无目标时被打自动选中攻击者"是目标选择式战斗的通用规则，由本宿主按数据执行
    /// （<c>creature.template.target_lock</c>），游戏只管显式设置/清除与读取。
    /// <para>
    /// 判断记录：①本宿主<b>只持有</b>目标，不替游戏联动普通攻击/目标框——游戏订阅 <c>unit.target_changed</c> 同步自动攻击、界面；
    /// ②不参与存档（目标是瞬时操作状态，读档后重新选）；③<see cref="GetTarget"/> 对失效目标（不存在 / 已死亡 / 不在同一地图）返回 null 但不改状态，
    /// 状态的清除由死亡/销毁事件驱动并发 <c>unit.target_changed</c>。
    /// </para>
    /// </summary>
    public interface ITargetLockHost
    {
        /// <summary>
        /// <paramref name="unitId"/> 当前有效的目标；没有或目标已失效（不存在、已死亡、与持有者不在同一地图）返回 null。
        /// </summary>
        Id? GetTarget(Id unitId);

        /// <summary>
        /// 显式设置（<paramref name="targetId"/> 非空）或清除（null）目标。目标须存在且存活，否则拒绝（返回 false，状态不变）。
        /// 目标没变（含"本就没有目标再清除"）返回 true 且不发事件；有变化发 <c>unit.target_changed</c>（cause = <see cref="TargetChangeCause.Manual"/>）。
        /// 本方法不检查阵营关系：能不能选友方是游戏的操作规则，不是宿主的。
        /// </summary>
        bool SetTarget(Id unitId, Id? targetId);

        /// <summary>
        /// 单位当前的操控模式标签（如 <c>"tab"</c>、<c>"action"</c>）；未设置为 null。
        /// 受击自动选中是否生效由数据（<c>target_lock.modes</c>）对照本标签决定。
        /// </summary>
        string? GetControlMode(Id unitId);

        /// <summary>设置操控模式标签（null = 未设置）。纯标签：只影响自动选中规则的判定，不触发任何事件。</summary>
        void SetControlMode(Id unitId, string? mode);
    }
}
