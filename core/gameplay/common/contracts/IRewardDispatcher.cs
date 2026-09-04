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
        /// 把 <paramref name="bundle"/> 的全部奖励项发放给 <paramref name="unitId"/>。
        /// <paramref name="sourceId"/> 是奖励来源（任务/遭遇/成就的 id），供 <c>IProgressionHost.AddXp</c>
        /// 的 <c>sourceId</c>、<c>IWorldState.Set</c> 的 <c>writerId</c>、货币/天赋点委托的
        /// <c>sourceId</c> 参数使用（用于日志/排查，见各自契约"每次写入必须带来源标识"的惯例）。
        /// 某一类奖励对应的依赖未注入时，跳过该类奖励并记一条诊断（见 <see cref="RewardDispatcher"/>
        /// 判断记录），不影响其余类别的发放、不抛异常。
        /// </summary>
        void Grant(Id unitId, RewardBundle bundle, Id sourceId);
    }
}
