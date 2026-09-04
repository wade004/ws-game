namespace Core.Carriers.Summon
{
    /// <summary>
    /// 召唤与宠物的策略配置项（见 07_载体层_物品生物物件.md 第 4 节"具体跟随距离、是否参战、
    /// 是否共享仇恨表是策略配置项"）。惯例同 <c>core/carriers/unit</c> 的 <c>MovementOptions</c>：
    /// 非架构固定语义的取值放构造期可配置项。
    /// </summary>
    public sealed class SummonOptions
    {
        /// <summary>超出此距离才触发跟随移动（见 07 第 4 节"owner 移动超出跟随距离时优先转入向
        /// owner 靠拢的移动"），默认 3。</summary>
        public double FollowDistance { get; set; } = 3;

        /// <summary>跟随移动的目标点与 owner 之间保留的距离（避免召唤物移动到与 owner 完全重合
        /// 的位置，见 <c>SummonTickHandler</c> 判断记录），默认 1.5。</summary>
        public double FollowStopDistance { get; set; } = 1.5;

        /// <summary>召唤物是否参与战斗（见 07 第 4 节"是否参战...是策略配置项"）；为 false 时召唤物
        /// 恒被视为"不在战斗中"参与跟随判定（见 <c>SummonTickHandler</c>"距离 &gt; FollowDistance 且
        /// 召唤物不在战斗中（或 JoinCombat=false）"），默认 true。</summary>
        public bool JoinCombat { get; set; } = true;

        /// <summary>是否联动 owner 的进出战斗状态（见 07 第 4 节"随主人进出战斗...是否联动、联动
        /// 方向为策略配置项"）：true 时 owner 进战而召唤物尚未进战时，调用
        /// <see cref="Core.Rules.Common.ICombatHost.NotifyCombatEvent"/> 让召唤物同步进战；脱战由
        /// 战斗系统自身的脱战计时器处理，不在本模块单独实现反向联动，默认 true。</summary>
        public bool SyncCombatState { get; set; } = true;

        /// <summary>是否共享仇恨表（见 07 第 4 节"是否共享仇恨表是策略配置项"）。
        /// <para>判断记录：本任务未获得任何仇恨表读写契约的注入点（<c>SummonHost</c>/
        /// <c>SummonTickHandler</c> 构造参数不含 <c>IThreatTable</c>），本选项目前只是数据位，
        /// 不驱动任何运行期行为——真正接入共享仇恨表需要 <c>core/rules/combat</c> 侧配合暴露
        /// "把某单位的仇恨表指向另一单位"一类契约，超出本次 T3-4 范围，见 README"契约缺口"。</para>
        /// </summary>
        public bool ShareThreat { get; set; } = false;

        /// <summary>玩家是否可直接操控召唤物（见 07 第 4 节召唤与宠物策略配置项列举的口味之一）。
        /// <para>判断记录：本任务范围不包含任何"玩家输入路由"相关契约（不属于 07 第 4 节"跟随/
        /// 联动机制"本身），本选项同 <see cref="ShareThreat"/> 只是数据位，供游戏层/输入系统未来
        /// 读取，不在 <c>SummonTickHandler</c> 内驱动任何行为。</para>
        /// </summary>
        public bool PlayerCanControl { get; set; } = false;

        /// <summary>新召唤物是否继承 owner 的阵营（见 07 第 4 节"召唤物与宠物复用 CreatureUnit"、
        /// 06 第 3.2 节 <c>summon</c> 原语"归属"），默认 true——绝大多数召唤物/宠物应该与 owner
        /// 同阵营，才能被 AI 的敌我判定正确对待。</summary>
        public bool InheritOwnerFaction { get; set; } = true;
    }
}
