namespace Core.Gameplay.Common
{
    /// <summary>
    /// <see cref="RewardDispatcher"/> 的诊断出口（惯例同
    /// <c>core/gameplay/world_state</c> 的 <c>IWorldStateDiagnostics</c>）：某一类奖励对应的
    /// 依赖未注入时记一条警告并跳过该类奖励，不抛异常、不影响其余类别的发放。
    /// </summary>
    public interface IRewardDiagnostics
    {
        void Warn(string message);
    }
}
