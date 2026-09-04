using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Rules.ExprHost;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <c>player</c> Expr 宿主引用分组下 <c>currency</c> 键的实现（见 <see
    /// cref="EconomyExprSchemaEntries"/>）。只实现这一个键——本模块不知道 <c>player</c> 分组下还有
    /// 哪些其它键（那是其它模块的职责，见 <see cref="EconomyExprSchemaEntries"/> 类型注释"链式包装"
    /// 判断记录），对不认识的 key 抛 <see cref="KeyNotFoundException"/>（供
    /// <see cref="ChainedExprGroupProvider"/> 识别"该 provider 不认识这个 key，继续尝试下一个"）。
    /// <para>
    /// 判断记录——"当前玩家单位"的解析：<c>IExprGroupProvider.Query</c> 本身不带 <c>selfId</c> 上下文
    /// （见该接口注释、<c>core/gameplay/world_state.WorldExprGroupProvider</c> 同款设计——<c>world</c>
    /// 分组不需要"谁"，但 <c>player</c> 分组语义上恒指"玩家角色"这个单例概念，不等同于表达式当前绑定
    /// 的 <c>self</c>），因此本类型经构造注入的 <c>Func&lt;Id&gt; playerUnitProvider</c> 取得"当前玩家
    /// 单位 id"，惯例同 <c>core/gameplay/loot.CreatureDeathLootListener</c> 的
    /// <c>Func&lt;double&gt; lootMultiplierProvider</c>——由组装层/单机存档系统提供真正的解析逻辑
    /// （单机游戏通常是全局唯一一个 <c>PlayerUnit</c>）。
    /// </para>
    /// </summary>
    public sealed class PlayerCurrencyExprGroupProvider : IExprGroupProvider
    {
        private readonly IEconomyHost _economy;
        private readonly Func<Id> _playerUnitProvider;

        public PlayerCurrencyExprGroupProvider(IEconomyHost economy, Func<Id> playerUnitProvider)
        {
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _playerUnitProvider = playerUnitProvider ?? throw new ArgumentNullException(nameof(playerUnitProvider));
        }

        public ExprValue Query(string key, IReadOnlyList<ExprValue> args)
        {
            if (key != "currency")
            {
                throw new KeyNotFoundException($"PlayerCurrencyExprGroupProvider 不认识 player.{key}");
            }

            if (args == null || args.Count < 1 || args[0].Kind != ExprValueKind.Id)
            {
                throw new ArgumentException(
                    "player.currency 需要恰好一个 Id 类型的 currencyId 参数（如 player.currency(econ.currency.sample_coin)）",
                    nameof(args));
            }

            var balance = _economy.GetBalance(_playerUnitProvider(), args[0].AsId);
            return ExprValue.OfInt(balance);
        }
    }
}
