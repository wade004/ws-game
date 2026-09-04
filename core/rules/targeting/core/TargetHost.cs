using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Rules.Common;
// 见 TargetChainDef.cs 顶部同名判断记录：TargetChainDef.Shape 属性与 Shape 类型同名。
using Core.Foundation.EngineAdapter;
using EngineShape = Core.Foundation.EngineAdapter.Shape;
using EngineShapeKind = Core.Foundation.EngineAdapter.ShapeKind;

namespace Core.Rules.Targeting
{
    /// <summary>
    /// <see cref="TargetHost"/> 的构造期策略配置（见 01_分层与依赖.md L2 <c>targeting</c> 行
    /// "策略配置项：链的具体组合"、任务书拍板"EmitResolvedEvent 默认 false"）。
    /// </summary>
    public sealed class TargetingOptions
    {
        /// <summary>链未声明 <c>shape</c> 字段时，<c>nearest_in_shape</c>/<c>all_in_shape</c> 等
        /// 依赖形状的来源退化使用的圆形半径（见任务书"无 shape 用 circle radius=Options.DefaultRadius"）。</summary>
        public double DefaultRadius { get; set; } = 8;

        /// <summary>
        /// 是否在 <see cref="TargetHost.Resolve(Id, Id, Id?)"/> 完成后发布
        /// <see cref="TargetingResolvedEvent"/>（见 06 第 5 节"目标选择本身不发事件"与
        /// 01_分层与依赖.md L2 <c>targeting</c> 行"事件：targeting.resolved"的冲突，设计层裁定：
        /// 可选发布，默认 false）。为 true 时构造 <see cref="TargetHost"/> 必须提供
        /// <c>eventBus</c>，否则构造期抛 <see cref="ArgumentException"/>。
        /// </summary>
        public bool EmitResolvedEvent { get; set; }

        /// <summary>fallback 链最大追踪深度，超过视为疑似环并在 <see cref="TargetHost.Resolve(Id, Id, Id?)"/>
        /// 期间抛 <see cref="InvalidOperationException"/>（数据加载期的环检测见
        /// <see cref="ChainDefValidationRule"/>；本项是运行期的独立防线，见任务书"回退环……运行期
        /// 深度保护"）。</summary>
        public int MaxFallbackDepth { get; set; } = 8;
    }

    /// <summary>
    /// 目标选择模块对外契约 <see cref="ITargetHost"/> 的默认实现（见 06 第 5 节）。本类型只负责
    /// "来源收集 → 过滤 → 排序 → 截断 → 空则回退"这一固定管线的编排，具体来源实现一律经
    /// <see cref="TargetStrategyRegistry"/> 注入——本类型内不出现任何策略名字面量的 switch/if 分支
    /// （见落地方案 T2-9 行禁止事项，验收方式见 targeting/README.md）。
    /// </summary>
    public sealed class TargetHost : ITargetHost
    {
        private static readonly IExprSchema FilterSchema = new TargetFilterExprSchema();

        private readonly TargetStrategyRegistry _registry;
        private readonly IDataRegistryView _data;
        private readonly IUnitAccess _units;
        private readonly ISpatialQuery _spatial;
        private readonly IFactionMatrix _factions;
        private readonly IPowerHost _powers;
        private readonly IThreatTable? _threat;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly IEventBus? _eventBus;
        private readonly TargetingOptions _options;

        public TargetHost(
            TargetStrategyRegistry registry,
            IDataRegistryView data,
            IUnitAccess units,
            ISpatialQuery spatial,
            IFactionMatrix factions,
            IPowerHost powers,
            IExprHostFactory exprHostFactory,
            IThreatTable? threat = null,
            IEventBus? eventBus = null,
            TargetingOptions? options = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            _factions = factions ?? throw new ArgumentNullException(nameof(factions));
            _powers = powers ?? throw new ArgumentNullException(nameof(powers));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _threat = threat;
            _eventBus = eventBus;
            _options = options ?? new TargetingOptions();

            if (_options.EmitResolvedEvent && _eventBus == null)
            {
                throw new ArgumentException(
                    "TargetingOptions.EmitResolvedEvent 为 true 时必须提供 eventBus", nameof(eventBus));
            }
        }

        public IReadOnlyList<Id> Resolve(Id chainId, Id casterId) => Resolve(chainId, casterId, null);

        public IReadOnlyList<Id> Resolve(Id chainId, Id casterId, Id? currentTarget)
        {
            var result = ResolveChain(chainId, casterId, currentTarget, depth: 0);

            if (_options.EmitResolvedEvent)
            {
                _eventBus!.Enqueue(new TargetingResolvedEvent(casterId, chainId, result));
            }

            return result;
        }

        private IReadOnlyList<Id> ResolveChain(Id chainId, Id casterId, Id? currentTarget, int depth)
        {
            if (depth > _options.MaxFallbackDepth)
            {
                throw new InvalidOperationException(
                    $"目标链回退深度超过上限 {_options.MaxFallbackDepth}（疑似 fallback 环，起点链 \"{chainId}\"）");
            }

            var chain = LoadChain(chainId);
            var strategy = _registry.Get(chain.Source);

            var origin = _units.GetPosition(casterId);
            var facing = _units.GetFacing(casterId);
            var template = chain.Shape ?? EngineShape.Circle(Vec2.Zero, _options.DefaultRadius);
            var shape = RebaseShape(template, origin, facing);

            var ctx = new TargetContext(casterId, currentTarget, shape, origin, _units, _spatial, _factions, _powers, _threat);

            IReadOnlyList<Id> candidates = strategy.Collect(ctx) ?? Array.Empty<Id>();
            candidates = ApplyFilters(chain, casterId, candidates);
            candidates = ApplySort(chain, casterId, origin, candidates);
            candidates = ApplyMaxTargets(chain, candidates);

            if (candidates.Count == 0 && chain.Fallback.HasValue)
            {
                return ResolveChain(chain.Fallback.Value, casterId, currentTarget, depth + 1);
            }

            return candidates;
        }

        private TargetChainDef LoadChain(Id chainId)
        {
            var record = _data.Get(TargetSchemas.ChainDef.Name, chainId)
                ?? throw new ArgumentException($"未知的目标链：\"{chainId}\"", nameof(chainId));
            return new TargetChainDef(record);
        }

        private IReadOnlyList<Id> ApplyFilters(TargetChainDef chain, Id casterId, IReadOnlyList<Id> candidates)
        {
            if (chain.Filters.Count == 0 || candidates.Count == 0)
            {
                return candidates;
            }

            var casterFaction = _units.GetFaction(casterId);
            var result = new List<Id>(candidates.Count);

            foreach (var candidateId in candidates)
            {
                var passes = true;
                foreach (var filterText in chain.Filters)
                {
                    if (!PassesFilter(filterText, casterId, casterFaction, candidateId))
                    {
                        passes = false;
                        break;
                    }
                }

                if (passes)
                {
                    result.Add(candidateId);
                }
            }

            return result;
        }

        // filters 语义判断记录：这里对数组内全部条件取 AND（逐条过滤，任一不通过即剔除该候选）——
        // 与 Core.Rules.Common.SkillFilter.Matches 的"三维度取 OR"不是同一场景：SkillFilter 的三个
        // 维度是"影响范围的并集式声明"（06 未给出组合方式，common 模块按此拍板），而这里的
        // filters 是调用方在同一条链里显式列出的一串独立筛选条件（06 第 5 节示例"存活、阵营关系、
        // 免疫标志等"），语义上是"同时满足"，AND 更符合"目标必须同时是存活的敌人"这类常见表达。
        private bool PassesFilter(string filterText, Id casterId, Id casterFaction, Id candidateId)
        {
            if (filterText == "alive")
            {
                return _units.IsAlive(candidateId);
            }

            if (filterText.StartsWith("tag:", StringComparison.Ordinal))
            {
                var tagText = filterText.Substring("tag:".Length);
                if (!Id.TryParse(tagText, out var tagId))
                {
                    throw new ArgumentException($"非法的 tag 过滤简写：\"{filterText}\"");
                }

                return _units.GetTags(candidateId).Contains(tagId);
            }

            if (filterText.StartsWith("relation:", StringComparison.Ordinal))
            {
                var relationText = filterText.Substring("relation:".Length);
                if (relationText == "not_self")
                {
                    return candidateId != casterId;
                }

                var candidateFaction = _units.GetFaction(candidateId);
                var reaction = _factions.GetReaction(casterFaction, candidateFaction);
                switch (relationText)
                {
                    case "hostile": return reaction == Reaction.Hostile;
                    case "friendly": return reaction == Reaction.Friendly;
                    case "neutral": return reaction == Reaction.Neutral;
                    default:
                        throw new ArgumentException($"非法的 relation 过滤简写：\"{filterText}\"");
                }
            }

            // 其余按 Expr 文本处理（见 06 第 5 节 filters: List<FilterExpr>、任务书"Expr 过滤经
            // IExprHostFactory.CreateFor(caster, candidate, null) 求值 EvaluateBool"）。
            var node = ExprParser.Parse(filterText, FilterSchema);
            var host = _exprHostFactory.CreateFor(casterId, candidateId, null);
            var diagnostics = new ExprDiagnosticsRecorder();
            return ExprEvaluator.EvaluateBool(node, host, diagnostics);
        }

        private IReadOnlyList<Id> ApplySort(TargetChainDef chain, Id casterId, Vec2 origin, IReadOnlyList<Id> candidates)
        {
            if (chain.SortBy == null || candidates.Count <= 1)
            {
                return candidates;
            }

            var spec = chain.SortBy.Value;
            var keyed = candidates
                .Select(id => (Id: id, Key: SortKeyValue(spec.Key, casterId, origin, id)))
                .ToList();

            var ordered = spec.Direction == SortDirection.Asc
                ? keyed.OrderBy(x => x.Key)
                : keyed.OrderByDescending(x => x.Key);

            // 同值一律按 Id 升序决胜（见任务书"平局按 Id 序（确定性）"）。
            return ordered.ThenBy(x => x.Id).Select(x => x.Id).ToList();
        }

        private double SortKeyValue(TargetSortKey key, Id casterId, Vec2 origin, Id candidateId)
        {
            switch (key)
            {
                case TargetSortKey.Distance:
                    return Vec2.Distance(origin, _units.GetPosition(candidateId));

                case TargetSortKey.HpPct:
                {
                    if (!_powers.HasPower(candidateId, WellKnownPowers.Health))
                    {
                        return 0.0;
                    }

                    var max = _powers.GetPowerMax(candidateId, WellKnownPowers.Health);
                    return max > 0 ? _powers.GetPower(candidateId, WellKnownPowers.Health) / max : 0.0;
                }

                case TargetSortKey.Threat:
                    // caster 自己的仇恨表中该候选贡献的仇恨值（见任务书"threat（caster 的仇恨表中该
                    // 候选的值）"，与 IThreatTable.GetThreat(unitId, sourceId) 的方向一致）。
                    return _threat?.GetThreat(casterId, candidateId) ?? 0.0;

                case TargetSortKey.Level:
                    return _units.GetLevel(candidateId);

                default:
                    throw new ArgumentOutOfRangeException(nameof(key), key, "未知排序键");
            }
        }

        private static IReadOnlyList<Id> ApplyMaxTargets(TargetChainDef chain, IReadOnlyList<Id> candidates)
        {
            if (chain.MaxTargets <= 0)
            {
                return candidates;
            }

            return candidates.Count <= chain.MaxTargets ? candidates : candidates.Take(chain.MaxTargets).ToList();
        }

        /// <summary>把"形状模板"（Origin=Zero、Direction/Rotation=0，见 <see cref="TargetChainDef.Shape"/>
        /// 判断记录）用施法者当前坐标/朝向重新锚定成一个可直接查询的 <see cref="EngineShape"/>。</summary>
        private static EngineShape RebaseShape(EngineShape template, Vec2 origin, double facing)
        {
            switch (template.Kind)
            {
                case EngineShapeKind.Circle:
                    return EngineShape.Circle(origin, template.Radius);
                case EngineShapeKind.Cone:
                    return EngineShape.Cone(origin, facing, template.Angle, template.Radius);
                case EngineShapeKind.Line:
                    return EngineShape.Line(origin, facing, template.Length, template.Width);
                case EngineShapeKind.Rect:
                    return EngineShape.Rect(origin, template.HalfExtents, facing);
                default:
                    throw new ArgumentOutOfRangeException(nameof(template), template.Kind, "未知 Shape 种类");
            }
        }
    }
}
