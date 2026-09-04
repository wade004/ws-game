using System.Collections.Generic;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;

namespace Tests.Gameplay.WorldState
{
    /// <summary>
    /// 本模块测试的最小公共设施：构造一个已登记 <c>world.flag_changed</c> 的 <see cref="IEventBus"/>
    /// （惯例同 <c>core/carriers/unit/tests</c> 各模块自带的 Fake/TestSupport——只覆盖测试实际用到的
    /// 行为，不代表任何正式实现的完整语义）。
    /// </summary>
    internal static class TestSupport
    {
        public static IEventBus NewEventBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(
                    Core.Gameplay.WorldState.WorldStateEventKeys.FlagChanged,
                    domain: "world",
                    fields: new[] { "flagKey", "oldValue", "newValue", "writerId" },
                    description: "WorldState.set 写入标志后触发"),
            });
            return new EventBus(catalog);
        }
    }

    /// <summary>
    /// 只处理 <c>world</c> 分组、其余分组一律返回默认值的最小 <see cref="IExprHost"/>
    /// （见任务书"用 RulesExprHostFactory 注入 extraGroups 或直接构造宿主"——本测试选用后者：
    /// <c>core/rules/expr_host.RulesExprHostFactory</c> 需要注入八九个 Fake 依赖才能构造，
    /// 而本模块测试只关心 <c>world.get</c>/<c>world.has</c>/<c>world.get_int</c> 三个键经真实
    /// <see cref="ExprParser"/>/<see cref="ExprEvaluator"/> 求值，直接实现 <see cref="IExprHost"/>
    /// 把 <c>world</c> 分组委托给 <see cref="Core.Gameplay.WorldState.WorldExprGroupProvider"/>
    /// 更小、更聚焦，不代表 <c>RulesExprHostFactory</c> 正式实现的完整语义）。
    /// </summary>
    internal sealed class WorldOnlyExprHost : IExprHost
    {
        private readonly Core.Gameplay.WorldState.WorldExprGroupProvider _worldGroup;

        public WorldOnlyExprHost(Core.Gameplay.WorldState.WorldExprGroupProvider worldGroup)
        {
            _worldGroup = worldGroup;
        }

        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
        {
            return group == ExprGroups.World ? _worldGroup.Query(key, args) : ExprValue.OfBool(false);
        }
    }
}
