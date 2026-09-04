using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Numbers.Faction;
using Core.Rules.Common;

namespace Core.Rules.Targeting
{
    /// <summary>
    /// 六个内置目标来源策略（见 06_规则层_属性技能战斗AI.md 第 5 节
    /// <c>TargetChainDef.source</c> 枚举示例、任务书"内置：current_target|nearest_in_shape|
    /// self|party_lowest_hp_pct|threat_top|all_in_shape"）。内置策略与游戏层自定义策略经同一个
    /// <see cref="TargetStrategyRegistry.Register"/> 入口登记——本类型只是"预置一批调用方"，
    /// <see cref="Core.Rules.Targeting.TargetHost"/> 本身不对任何策略名做 switch/if 硬编码分支
    /// （见落地方案 T2-9 行禁止事项）。
    /// <para>
    /// 判断记录：内置策略一律不在 <see cref="ITargetSourceStrategy.Collect"/> 内部自行做
    /// 存活/阵营过滤（<see cref="PartyLowestHpPctStrategy"/> 的"友方"关系判定是其名字本身的语义，
    /// 不算过滤——不筛"友方"就不是"选队友里最低血量"这条策略了；但它不会额外排除死亡单位）——
    /// 06 第 5 节把"存活、阵营关系、免疫标志等"明确列为 <c>filters</c> 字段的职责，见
    /// targeting/README.md"设计要点"。
    /// </para>
    /// </summary>
    public static class BuiltinTargetStrategies
    {
        public const string CurrentTarget = "current_target";
        public const string NearestInShape = "nearest_in_shape";
        public const string Self = "self";
        public const string PartyLowestHpPct = "party_lowest_hp_pct";
        public const string ThreatTop = "threat_top";
        public const string AllInShape = "all_in_shape";

        /// <summary>把六个内置策略登记到 <paramref name="registry"/>；集成方还可以在此调用之后
        /// 继续 <see cref="TargetStrategyRegistry.Register"/> 游戏层自定义策略。</summary>
        public static void RegisterAll(TargetStrategyRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(new CurrentTargetStrategy());
            registry.Register(new SelfStrategy());
            registry.Register(new NearestInShapeStrategy());
            registry.Register(new AllInShapeStrategy());
            registry.Register(new PartyLowestHpPctStrategy());
            registry.Register(new ThreatTopStrategy());
        }

        private sealed class CurrentTargetStrategy : ITargetSourceStrategy
        {
            public string Name => BuiltinTargetStrategies.CurrentTarget;

            public IReadOnlyList<Id> Collect(TargetContext ctx)
            {
                if (ctx.CurrentTarget.HasValue
                    && ctx.Units.Exists(ctx.CurrentTarget.Value)
                    && ctx.Units.IsAlive(ctx.CurrentTarget.Value))
                {
                    return new[] { ctx.CurrentTarget.Value };
                }

                return Array.Empty<Id>();
            }
        }

        private sealed class SelfStrategy : ITargetSourceStrategy
        {
            public string Name => BuiltinTargetStrategies.Self;

            public IReadOnlyList<Id> Collect(TargetContext ctx) => new[] { ctx.CasterId };
        }

        private sealed class NearestInShapeStrategy : ITargetSourceStrategy
        {
            public string Name => BuiltinTargetStrategies.NearestInShape;

            public IReadOnlyList<Id> Collect(TargetContext ctx)
            {
                // 判断记录：本策略不在这里就把结果掐到 1 个——按距离升序（同距离按 Id 升序决胜，
                // 保证确定性）返回完整候选集合，交给 TargetHost 的通用过滤/排序/max_targets 管线
                // 处理；数据未显式声明 sort_by 时 max_targets 默认 1，效果等价于"取最近一个"，同时
                // 允许链再叠加 relation:hostile 一类过滤而不必对本策略做特殊处理（见
                // targeting/README.md 判断记录）。
                var shape = ctx.Shape!.Value;
                var raw = ctx.Spatial.QueryShape(shape, QueryFilter.None);
                return raw
                    .Select(id => (Id: id, Dist: Vec2.Distance(ctx.Origin, ctx.Units.GetPosition(id))))
                    .OrderBy(x => x.Dist)
                    .ThenBy(x => x.Id)
                    .Select(x => x.Id)
                    .ToList();
            }
        }

        private sealed class AllInShapeStrategy : ITargetSourceStrategy
        {
            public string Name => BuiltinTargetStrategies.AllInShape;

            public IReadOnlyList<Id> Collect(TargetContext ctx)
            {
                var shape = ctx.Shape!.Value;
                return ctx.Spatial.QueryShape(shape, QueryFilter.None)
                    .OrderBy(id => id)
                    .ToList();
            }
        }

        private sealed class PartyLowestHpPctStrategy : ITargetSourceStrategy
        {
            public string Name => BuiltinTargetStrategies.PartyLowestHpPct;

            public IReadOnlyList<Id> Collect(TargetContext ctx)
            {
                var casterFaction = ctx.Units.GetFaction(ctx.CasterId);
                var candidates = new List<(Id Id, double HpPct)>();

                foreach (var unitId in ctx.Units.AllUnits)
                {
                    var faction = ctx.Units.GetFaction(unitId);
                    if (ctx.Factions.GetReaction(casterFaction, faction) != Reaction.Friendly)
                    {
                        continue;
                    }

                    if (!ctx.Powers.HasPower(unitId, WellKnownPowers.Health))
                    {
                        continue;
                    }

                    var max = ctx.Powers.GetPowerMax(unitId, WellKnownPowers.Health);
                    var pct = max > 0 ? ctx.Powers.GetPower(unitId, WellKnownPowers.Health) / max : 0.0;
                    candidates.Add((unitId, pct));
                }

                return candidates
                    .OrderBy(c => c.HpPct)
                    .ThenBy(c => c.Id)
                    .Select(c => c.Id)
                    .ToList();
            }
        }

        private sealed class ThreatTopStrategy : ITargetSourceStrategy
        {
            public string Name => BuiltinTargetStrategies.ThreatTop;

            public IReadOnlyList<Id> Collect(TargetContext ctx)
            {
                if (ctx.Threat == null)
                {
                    return Array.Empty<Id>();
                }

                var top = ctx.Threat.GetTopThreat(ctx.CasterId);
                return top.HasValue ? new[] { top.Value } : Array.Empty<Id>();
            }
        }
    }
}
