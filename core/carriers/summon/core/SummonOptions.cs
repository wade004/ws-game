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
        /// <para>判断记录（收边任务补齐）：<c>ICombatHost.GetThreatTable</c> 已落地（此前不存在任何
        /// 仇恨表读写契约的注入点），<see cref="SummonTickHandler"/> 现经该契约实现最小语义——
        /// 每 tick 把 owner 与召唤物各自的仇恨表条目按来源合并（同源取较大值）后回写到双方，效果上
        /// 等价于"共享一张仇恨表"，不修改 <c>core/rules/combat</c> 的存储结构（详见
        /// <see cref="SummonTickHandler"/> 的 <c>ShareThreatWithOwner</c> 判断记录）。默认 <c>false</c>
        /// 时不调用 <c>GetThreatTable</c>，零额外开销，行为与补齐之前一致。</para>
        /// </summary>
        public bool ShareThreat { get; set; } = false;

        /// <summary>玩家是否可直接操控召唤物（见 07 第 4 节召唤与宠物策略配置项列举的口味之一）。
        /// <para>判断记录（收边任务补齐）：经 <see cref="SummonHost.IsControllableByOwner"/> 接入两处——
        /// ①供游戏层/输入系统查询"当前召唤物是否允许玩家直接输入"，再决定要不要把玩家操作转成
        /// <c>world.SubmitIntent(new Intent(summonId, ...))</c>（<c>WorldSim.SubmitIntent</c> 本身
        /// 对 <c>Intent.ActorId</c> 是谁没有限制，这条路由本就通，只是此前没有"是否应该允许"的可
        /// 查询判断）；②<c>GameplayAssembly</c> 的 <c>IsPlayerActor</c> 判定——为 <c>true</c> 且
        /// <c>unitId</c> 确实是玩家拥有的召唤物时，该召唤物在离散模式下与玩家本人同等对待：轮到它
        /// 时等待外部提交意图，不会被 AI 自动接管（"离散模式下召唤物是否独立行动者"的判断记录：
        /// 按 <see cref="PlayerCanControl"/> 决定，开启则是独立的玩家行动者，未开启仍是普通 AI
        /// 行动者）。默认 <c>false</c> 时 <c>IsControllableByOwner</c> 恒返回 false，行为与补齐之前
        /// 完全一致。</para>
        /// </summary>
        public bool PlayerCanControl { get; set; } = false;

        /// <summary>新召唤物是否继承 owner 的阵营（见 07 第 4 节"召唤物与宠物复用 CreatureUnit"、
        /// 06 第 3.2 节 <c>summon</c> 原语"归属"），默认 true——绝大多数召唤物/宠物应该与 owner
        /// 同阵营，才能被 AI 的敌我判定正确对待。</summary>
        public bool InheritOwnerFaction { get; set; } = true;
    }
}
