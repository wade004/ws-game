using Core.Foundation.Common;

namespace Core.Gameplay.Common
{
    /// <summary>
    /// 契约缺口：架构未给货币系统（08 第 7 节 Economy，本次任务并行由另一 agent 建设
    /// <c>core/gameplay/economy</c>）与天赋点发放定义共享契约接口，<see cref="RewardDispatcher"/>
    /// 按任务书拍板改用具名委托绕过（惯例同 <c>core/carriers/item</c> 的
    /// <see cref="Core.Carriers.Item.SkillGranter"/>"契约缺口用模块内委托绕过并汇报"）。
    /// </summary>
    /// <param name="unitId">收到货币的单位。</param>
    /// <param name="currencyId">货币类型 id（<c>econ.currency.&lt;name&gt;</c>）。</param>
    /// <param name="amount">发放数量。</param>
    /// <param name="sourceId">发放来源（任务/遭遇/成就等 id），供日志/排查使用。</param>
    public delegate void CurrencyGranter(Id unitId, Id currencyId, long amount, Id sourceId);

    /// <summary>
    /// 契约缺口：架构未给天赋点数发放定义任何契约接口（08 第 2.1 节只提到
    /// <c>talent_points</c> 字段"交付时发放的天赋点数"，未指向具体模块），
    /// <see cref="RewardDispatcher"/> 同 <see cref="CurrencyGranter"/> 判断记录改用具名委托绕过。
    /// </summary>
    /// <param name="unitId">获得天赋点的单位。</param>
    /// <param name="amount">发放数量。</param>
    /// <param name="sourceId">发放来源，供日志/排查使用。</param>
    public delegate void TalentPointGranter(Id unitId, int amount, Id sourceId);
}
