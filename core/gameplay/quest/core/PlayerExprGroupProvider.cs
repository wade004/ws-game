using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Numbers.Progression;
using Core.Rules.ExprHost;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <c>player</c> Expr 宿主引用分组的实现（见 04 第 6.2 节示例 <c>player.level &gt;= 10</c>、
    /// <c>player.has_item(item.town_key)</c>）。构造期注入方式与 <see cref="QuestExprGroupProvider"/>
    /// 同一判断记录——单机单人游戏，<c>player</c> 分组专指当前玩家单位，构造期绑定
    /// <see cref="Func{Id}"/> 而非要求调用点传参。
    /// </summary>
    public sealed class PlayerExprGroupProvider : IExprGroupProvider
    {
        private readonly IInventoryHost _inventory;
        private readonly IProgressionHost _progression;
        private readonly Func<Id> _playerUnitProvider;
        private readonly IExprDiagnostics? _diagnostics;

        public PlayerExprGroupProvider(
            IInventoryHost inventory,
            IProgressionHost progression,
            Func<Id> playerUnitProvider,
            IExprDiagnostics? diagnostics = null)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
            _playerUnitProvider = playerUnitProvider ?? throw new ArgumentNullException(nameof(playerUnitProvider));
            _diagnostics = diagnostics;
        }

        public ExprValue Query(string key, IReadOnlyList<ExprValue> args)
        {
            var player = _playerUnitProvider();

            switch (key)
            {
                case "level":
                    return ExprValue.OfInt(_progression.GetLevel(player));

                case "has_item":
                    return ExprValue.OfBool(_inventory.CountOf(player, RequireItemIdArg(key, args)) > 0);

                case "item_count":
                    return ExprValue.OfInt(_inventory.CountOf(player, RequireItemIdArg(key, args)));

                default:
                    _diagnostics?.Warn($"未知的 player.{key} 引用，按默认值 Bool(false) 处理");
                    return ExprValue.OfBool(false);
            }
        }

        private static Id RequireItemIdArg(string key, IReadOnlyList<ExprValue> args)
        {
            if (args == null || args.Count < 1 || args[0].Kind != ExprValueKind.Id)
            {
                throw new ArgumentException($"player.{key} 需要恰好一个 Id 类型的 itemId 参数", nameof(args));
            }
            return args[0].AsId;
        }
    }
}
