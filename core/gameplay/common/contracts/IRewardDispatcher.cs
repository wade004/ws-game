using Core.Foundation.Common;

namespace Core.Gameplay.Common
{
    /// <summary>
    /// 奖励分发契约：把一个 <see cref="RewardBundle"/> 结算到具体单位身上（见 08 第 9 节汇总表
    /// Quest/Encounter/Achievement 行"结算 rewards"这一共同步骤）。由 <c>quest</c>/<c>encounter</c>/
    /// <c>achievement</c> 等模块在各自的完成/达成时机调用，避免每个模块各自重写一遍"按字段逐项发放"
    /// 的样板逻辑。
    /// </summary>
    public interface IRewardDispatcher
    {
        /// <summary>
        /// 把 <paramref name="bundle"/> 的全部奖励项发放给 <paramref name="unitId"/>，返回是否全部
        /// 发放成功。<paramref name="sourceId"/> 是奖励来源（任务/遭遇/成就的 id），供
        /// <c>IProgressionHost.AddXp</c> 的 <c>sourceId</c>、<c>IWorldState.Set</c> 的
        /// <c>writerId</c>、货币/天赋点委托的 <c>sourceId</c> 参数使用（用于日志/排查，见各自契约
        /// "每次写入必须带来源标识"的惯例）。某一类奖励对应的依赖未注入时，跳过该类奖励并记一条诊断
        /// （见 <see cref="RewardDispatcher"/> 判断记录），不影响其余类别的发放、不抛异常。
        /// <para>
        /// 原子性（N02 根治）：物品奖励最先发放；若因背包已满（<c>InventoryFullPolicy.Reject</c>）
        /// 无法完整发放某一项物品，已发放的物品项会被回滚（从背包移除），本方法返回
        /// <see langword="false"/>，且不再发放 <c>xp</c>/货币/技能/世界标志/天赋点等其余类别——调用方
        /// （如 <c>QuestHost.TurnIn</c>）应在返回 false 时视为"整批奖励未生效"，不得提交任何依赖本次
        /// 奖励已发放的状态转移。物品奖励以外的类别本身没有"容量不足"这类失败模式（数值型奖励只做
        /// 增减/夹取，不会失败），因此物品先行发放、失败即整体中止，已经覆盖"要么全部生效、要么全部
        /// 不生效"的原子性要求，不需要对其余类别做额外补偿式回滚。
        /// </para>
        /// </summary>
        bool Grant(Id unitId, RewardBundle bundle, Id sourceId);
    }
}
