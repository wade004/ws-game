using Core.Foundation.Common;

namespace Core.Gameplay.Common
{
    /// <summary>
    /// 契约缺口：架构未给货币系统（08 第 7 节 Economy，本次任务并行由另一 agent 建设
    /// <c>core/gameplay/economy</c>）与天赋点发放定义共享契约接口，<see cref="RewardDispatcher"/>
    /// 按任务书拍板改用具名委托绕过（惯例同 <c>core/carriers/item</c> 的
    /// <see cref="Core.Carriers.Item.SkillGranter"/>"契约缺口用模块内委托绕过并汇报"）。
    /// <para>
    /// T-N4-8 判断记录（分阶段落地计划 M5"任务奖励货币入账收口到 IEconomyHost，替代
    /// CurrencyGranter 委托缺口"）：写下上一段判断记录时（早于 N4 阶段），<c>core/gameplay/economy</c>
    /// 尚不存在，"架构未给货币系统定义共享契约接口"这句话是真的——本模块因此不能反过来依赖一个
    /// 还没建好的模块，只能靠具名委托绕过。T-N4-6/T-N4-7 把 <c>core/gameplay/economy</c> 与其
    /// <see cref="Core.Gameplay.Economy.IEconomyHost"/> 建起来之后，这个"缺共享契约接口"的前提已经
    /// 不成立——<c>IEconomyHost</c> 本身就是那个共享契约，且与本模块同属 L4、同一个 Core.Gameplay
    /// 程序集（同 <c>core/gameplay/loot</c> 反向依赖 <c>economy</c> 的先例，T-N4-7 README 判断记录），
    /// 不需要新增 <c>ProjectReference</c> 即可直接引用。本次未删除/未改写 <see cref="CurrencyGranter"/>
    /// 委托类型本身（ABI 硬性规则"只允许新增"，且既有 <see cref="RewardDispatcher"/> 构造参数与
    /// <c>core/gameplay/assembly.GameplayAssembly</c> 现有 <c>currencyGranter</c> 闭包——效果同样是
    /// 转发到 <c>EconomyHost.Add</c>，见该文件"7) RewardDispatcher"步骤——都依赖这个委托类型继续存在），
    /// 而是新增 <see cref="CurrencyGranters.ViaEconomyHost"/> 静态工厂，把"货币奖励经
    /// <c>IEconomyHost.Add</c> 入账"固化为一个标准、可复用的构造方式，供后续新增的组装点直接使用，
    /// 不必像现有 <c>GameplayAssembly</c> 那样各自手写等价的转发闭包。<c>GameplayAssembly</c> 现有
    /// 闭包未切换到本工厂（该文件按任务书要求本次"只加行"，且其现有写法已经是同一效果——直接调用
    /// <c>EconomyHost.Add</c>，替换成本工厂纯属风格统一，不产生行为差异，留给后续任务收敛，不在
    /// T-N4-8 范围内）。
    /// </para>
    /// </summary>
    /// <param name="unitId">收到货币的单位。</param>
    /// <param name="currencyId">货币类型 id（<c>econ.currency.&lt;name&gt;</c>）。</param>
    /// <param name="amount">发放数量。</param>
    /// <param name="sourceId">发放来源（任务/遭遇/成就等 id），供日志/排查使用。</param>
    public delegate void CurrencyGranter(Id unitId, Id currencyId, long amount, Id sourceId);

    /// <summary>
    /// T-N4-8：<see cref="CurrencyGranter"/> 的标准构造方式——把货币奖励经 <see
    /// cref="Core.Gameplay.Economy.IEconomyHost.Add"/> 入账，<paramref name="sourceId"/>（奖励来源，
    /// 如 <c>quest.def</c>/<c>encounter.def</c> 的 id）原样透传给 <c>Add</c> 的 <c>sourceId</c> 参数
    /// （供事件追溯来源，见 <c>EconomyHost.Add</c> 判断记录 7"谁扣的款/谁发的钱"）。
    /// </summary>
    public static class CurrencyGranters
    {
        /// <summary>构造一个把货币奖励经 <paramref name="economyHost"/>.<c>Add</c> 入账的
        /// <see cref="CurrencyGranter"/>。</summary>
        public static CurrencyGranter ViaEconomyHost(Core.Gameplay.Economy.IEconomyHost economyHost)
        {
            if (economyHost == null) throw new System.ArgumentNullException(nameof(economyHost));
            return (unitId, currencyId, amount, sourceId) => economyHost.Add(unitId, currencyId, amount, sourceId);
        }
    }

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
