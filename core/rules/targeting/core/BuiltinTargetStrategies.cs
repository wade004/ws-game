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

        /// <summary>
        /// 加固任务（05 §3.6 碰撞层落地）：不带 <c>RequiredTags</c> 的空间查询点排查——见
        /// <c>Core.Carriers.Assembly.CarriersAssembly.DefaultSpatialSyncKinds</c> 判断记录，区域触发
        /// 实体现在会打 <see cref="CollisionLayers.TriggerOnly"/> 标签登记进空间索引。本类
        /// <see cref="QueryFilter.None"/> 不做任何标签过滤，若不排除该标签，<c>QueryShape</c> 结果会
        /// 混入触发体 id；<see cref="NearestInShapeStrategy"/> 紧接着对每个候选调用
        /// <c>ctx.Units.GetPosition(id)</c>（<c>WorldUnitAccess.Require</c> 内部 <c>is Unit</c> 强转
        /// 失败即抛 <see cref="InvalidOperationException"/>），命中即在这里直接崩溃——不是理论风险，
        /// 是真实的必现异常（同 <c>CarriersAssembly.DefaultSpatialSyncKinds</c>"默认不含 gobj"判断
        /// 记录描述的同一类问题，但那里是"理论上可能被误捞"，这里是"一定会抛异常"）。
        /// </summary>
        private static readonly QueryFilter ExcludeTriggerOnly =
            new QueryFilter(excludedTags: new[] { CollisionLayers.TriggerOnly });

        /// <summary>
        /// ADR-0013 决策 6、04 第 3.1 节 <c>grid_snap</c> 落地：<paramref name="ctx"/>.
        /// <see cref="TargetContext.GridSnapCellSize"/> 非 <c>null</c> 时（当前处于离散步且声明了
        /// 格子吸附，见该属性判断记录），改用
        /// <see cref="Core.Foundation.EngineAdapter.GridSnapShapeQuery.QueryShapeAtCellCenters"/>——
        /// 候选按其所属格子中心点是否落在 <paramref name="shape"/> 内判定；否则回退到未吸附的
        /// <see cref="ISpatialQuery.QueryShape"/>，与格子吸附落地之前逐字节一致。
        /// <see cref="NearestInShapeStrategy"/>/<see cref="AllInShapeStrategy"/> 共用本方法。
        /// </summary>
        private static IReadOnlyList<Id> QueryShape(TargetContext ctx, Shape shape, QueryFilter filter)
        {
            if (!ctx.GridSnapCellSize.HasValue)
            {
                return WithoutStale(ctx, AddBodyRadiusHits(ctx, shape, filter, ctx.Spatial.QueryShape(shape, filter)));
            }

            // 已不在世界里的单位（空间索引的过期条目）位置取不到：给 NaN，格子中心判定自然不命中，随后 WithoutStale 再兜底。
            return WithoutStale(ctx, GridSnapShapeQuery.QueryShapeAtCellCenters(
                ctx.Spatial, shape, filter,
                id => ctx.Units.Exists(id) ? ctx.Units.GetPosition(id) : new Vec2(double.NaN, double.NaN),
                ctx.GridSnapPolicy!, ctx.GridSnapCellSize.Value));
        }

        /// <summary>
        /// 判断记录（空间索引过期条目，样板游戏 C 消费方反馈）：空间索引靠订阅"实体已销毁"事件注销条目，而事件在世界销毁实体之后才分发；同一固定步里实体被销毁、
        /// 事件尚未分发的窗口内，空间查询仍会返回该 id，后面对它取位置/阵营的调用（<c>WorldUnitAccess.Require</c>）直接抛异常——
        /// 玩家的动作技能在这个窗口内起手做目标辅助就必现。这里在所有空间查询的出口丢掉已不是世界里单位的 id（<see cref="IUnitAccess.Exists"/>），
        /// 与 AI 宿主对候选的处理一致；窗口过后条目由事件正常注销，行为不变。
        /// </summary>
        private static IReadOnlyList<Id> WithoutStale(TargetContext ctx, IReadOnlyList<Id> ids)
        {
            List<Id>? kept = null;
            for (var i = 0; i < ids.Count; i++)
            {
                var alive = ctx.Units.Exists(ids[i]);
                if (!alive && kept == null)
                {
                    kept = new List<Id>(ids.Count);
                    for (var j = 0; j < i; j++)
                    {
                        kept.Add(ids[j]);
                    }
                }

                if (alive && kept != null)
                {
                    kept.Add(ids[i]);
                }
            }

            return kept ?? ids;
        }

        /// <summary>
        /// 目标命中半径（<see cref="TargetContext.TargetRadius"/>，M4 清扫）：在"目标中心落在形状内"的原始结果之后，追加"形状到目标中心的最近距离不超过该目标半径"
        /// 的单位——先用外扩 <see cref="TargetContext.MaxTargetRadius"/> 的形状取一批宁多勿少的候选，再按各自半径精确重判，追加项按 Id 序排在原始结果之后（确定性）。
        /// 没有配置半径来源时原样返回（逐位不变）；格子吸附路径不参与（格子中心采样本来就以格为单位）。
        /// </summary>
        private static IReadOnlyList<Id> AddBodyRadiusHits(TargetContext ctx, Shape shape, QueryFilter filter, IReadOnlyList<Id> raw)
        {
            var radiusOf = ctx.TargetRadius;
            if (radiusOf == null || ctx.MaxTargetRadius <= 0.0)
            {
                return raw;
            }

            var seen = new HashSet<Id>(raw);
            var extra = new List<Id>();
            foreach (var id in ctx.Spatial.QueryShape(shape.Expand(ctx.MaxTargetRadius), filter))
            {
                if (seen.Contains(id) || id.Equals(ctx.CasterId))
                {
                    continue;
                }

                if (!ctx.Units.Exists(id))
                {
                    continue;
                }

                var radius = radiusOf(id);
                if (radius <= 0.0)
                {
                    continue;
                }

                var position = ctx.Units.GetPosition(id);
                if ((ShapeGeometry.ClosestPoint(shape, position) - position).Length <= radius + 1e-12)
                {
                    extra.Add(id);
                }
            }

            if (extra.Count == 0)
            {
                return raw;
            }

            extra.Sort();
            var merged = new List<Id>(raw.Count + extra.Count);
            merged.AddRange(raw);
            merged.AddRange(extra);
            return merged;
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
                // targeting/README.md 判断记录）。ExcludeTriggerOnly 见本文件顶部判断记录。
                var shape = ctx.Shape!.Value;
                var raw = QueryShape(ctx, shape, ExcludeTriggerOnly);
                return raw
                    .Select(id => (Id: id, Dist: ctx.DistanceTo(id)))
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
                // ExcludeTriggerOnly 见本文件顶部判断记录：即便本策略自身不在 Collect 内调用
                // GetPosition，返回的候选集合会直接流出到调用方（技能释放目标列表等），下游同样可能
                // 把触发体 id 当作单位 id 处理。
                var shape = ctx.Shape!.Value;
                return QueryShape(ctx, shape, ExcludeTriggerOnly)
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
