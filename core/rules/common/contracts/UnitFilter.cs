using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>阵营关系过滤维度（见 06 第 7 节 <c>SkillHost.findUnits(shape, filter)</c> 的
    /// <c>filter</c> 入参、05 第 6.1 节 <c>ISpatialQuery</c> 的 <c>filter</c> 说明"阵营、单位类型等，
    /// 由调用方按需构造"）。<see cref="Any"/> 不做阵营过滤；<see cref="Hostile"/>/<see cref="Friendly"/>/
    /// <see cref="Neutral"/> 复用 L1 <c>Core.Numbers.Faction.Reaction</c> 的三值语义，相对施法者判定；
    /// <see cref="Self"/>/<see cref="NotSelf"/> 是与阵营无关的补充维度（供"仅目标自己"/"排除自己"这类
    /// 常见目标形状复用同一过滤结构，而不必另开一个字段）。</summary>
    public enum RelationFilter
    {
        Any,
        Hostile,
        Friendly,
        Neutral,
        Self,
        NotSelf,
    }

    /// <summary>
    /// 目标过滤条件（见 06 第 7 节 <c>SkillHost.findUnits</c>、05 第 6.1 节 <c>ISpatialQuery</c> 的
    /// <c>filter</c> 说明）。供技能目标解析、AI 感知、目标选择链的候选过滤复用同一个结构。
    /// </summary>
    public readonly struct UnitFilter
    {
        private readonly bool? _aliveOnly;
        private readonly IReadOnlyList<Id>? _requiredTags;
        private readonly IReadOnlyList<Id>? _excludedTags;

        public RelationFilter Relation { get; }

        /// <summary>是否只保留存活单位；默认 true（06 各处目标选择默认排除尸体，见第 5 节
        /// <c>TargetChainDef.filters</c>"存活...等标志位"作为常见过滤项）。</summary>
        public bool AliveOnly => _aliveOnly ?? true;

        /// <summary>需要排除的单位 id（通常是施法者自身，用于"选取范围内除自己以外的单位"一类场景，
        /// 与 <see cref="RelationFilter.NotSelf"/> 语义重叠时任取其一即可，二者不冲突）。</summary>
        public Id? Exclude { get; }

        public IReadOnlyList<Id> RequiredTags => _requiredTags ?? Array.Empty<Id>();

        public IReadOnlyList<Id> ExcludedTags => _excludedTags ?? Array.Empty<Id>();

        public UnitFilter(
            RelationFilter relation = RelationFilter.Any,
            bool aliveOnly = true,
            Id? exclude = null,
            IReadOnlyList<Id>? requiredTags = null,
            IReadOnlyList<Id>? excludedTags = null)
        {
            Relation = relation;
            _aliveOnly = aliveOnly;
            Exclude = exclude;
            _requiredTags = requiredTags ?? Array.Empty<Id>();
            _excludedTags = excludedTags ?? Array.Empty<Id>();
        }

        /// <summary>未做任何限定的默认过滤条件：<see cref="Relation"/> = <see cref="RelationFilter.Any"/>、
        /// <see cref="AliveOnly"/> = true、无排除/标签限定。等价于 <c>default(UnitFilter)</c>。</summary>
        public static readonly UnitFilter Default = default;
    }
}
