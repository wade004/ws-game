using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Rules.ExprHost;

namespace Core.Gameplay.WorldState
{
    /// <summary>
    /// <c>world</c> Expr 宿主引用分组的实现（见 04 第 6.2 节"<c>world.get(world.bridge.repaired)</c>"、
    /// <c>core/rules/expr_host</c> 的 <see cref="IExprGroupProvider"/>"world/quest/player 三个分组的
    /// 查询整体委托给调用方按分组名注入的 IExprGroupProvider……由 L4（或游戏组装根）实现"）。
    /// 把三个键（<c>get</c>/<c>has</c>/<c>get_int</c>）接到构造注入的 <see cref="IWorldState"/> 上，
    /// 供 <c>RulesExprHostFactory</c> 的 <c>extraGroups["world"]</c> 使用。
    /// </summary>
    public sealed class WorldExprGroupProvider : IExprGroupProvider
    {
        private readonly IWorldState _worldState;
        private readonly IExprDiagnostics? _diagnostics;

        public WorldExprGroupProvider(IWorldState worldState, IExprDiagnostics? diagnostics = null)
        {
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            _diagnostics = diagnostics;
        }

        public ExprValue Query(string key, IReadOnlyList<ExprValue> args)
        {
            switch (key)
            {
                case "get":
                    // world.get(flagKey)：按标志实际类型返回；缺失按 IWorldState.Get 的约定
                    // 退化为 ExprValue.OfBool(false)（04 第 6.3 节分组默认值语义）。
                    return _worldState.Get(RequireFlagKeyArg(key, args));

                case "has":
                    return ExprValue.OfBool(_worldState.Has(RequireFlagKeyArg(key, args)));

                case "get_int":
                    return QueryGetInt(RequireFlagKeyArg(key, args));

                default:
                    _diagnostics?.Warn($"未知的 world.{key} 引用，按默认值 Bool(false) 处理");
                    return ExprValue.OfBool(false);
            }
        }

        private ExprValue QueryGetInt(Id flagKey)
        {
            if (!_worldState.Has(flagKey))
            {
                // world.get_int(flagKey)：无敌人/无目标一类"缺失返回默认值"惯例的 world 分组版本
                // （任务书拍板"缺失 0"），不是 04 §6.3 的 Bool 默认值分支——get_int 精确登记的
                // ReturnKind 是 Int，因此缺失时也应返回 Int(0) 而不是 Bool(false)。
                return ExprValue.OfInt(0);
            }

            var value = _worldState.Get(flagKey);
            switch (value.Kind)
            {
                case ExprValueKind.Int:
                    return value;
                case ExprValueKind.Number:
                    // 判断记录：任务书未规定"标志实际是 Number 时 get_int 该怎么办"，按 Expr
                    // Int/Number 可互相比较的惯例（见 ExprValue.IsNumeric）截断为 Int，不视为错误。
                    return ExprValue.OfInt((long)value.AsNumber);
                default:
                    _diagnostics?.Warn(
                        $"world.get_int(\"{flagKey}\")：标志实际类型是 {value.Kind}，不是 Int/Number，按默认值 Int(0) 处理");
                    return ExprValue.OfInt(0);
            }
        }

        private Id RequireFlagKeyArg(string key, IReadOnlyList<ExprValue> args)
        {
            if (args == null || args.Count < 1 || args[0].Kind != ExprValueKind.Id)
            {
                throw new ArgumentException(
                    $"world.{key} 需要恰好一个 Id 类型的 flagKey 参数（如 world.{key}(world.bridge.repaired)）",
                    nameof(args));
            }
            return args[0].AsId;
        }
    }
}
