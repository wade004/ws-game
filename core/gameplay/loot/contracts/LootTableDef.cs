using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Gameplay.Loot
{
    /// <summary>组内抽取模式（见 08 第 1.1 节 <c>LootGroup.rollMode</c>）。</summary>
    public enum LootRollMode
    {
        /// <summary>组内每一条独立按自身概率判定是否掉落，互不影响。</summary>
        ChanceEach,

        /// <summary>组内按权重抽取一条（或 <see cref="LootGroup.PickCount"/> 条不放回）。</summary>
        WeightedPickOne,
    }

    /// <summary>一条掉落候选（见 08 第 1.1 节 <c>LootEntry</c>）。</summary>
    public sealed class LootEntry
    {
        /// <summary>引用目标：<c>item.*</c>（直接掉落该物品模板）或 <c>loot.*</c>（嵌套引用另一张
        /// 掉落表，见 <see cref="LootHost"/> 判断记录"count 对嵌套表的语义"）。</summary>
        public Id Ref { get; }

        /// <summary><c>chance_each</c> 组下是 [0,1] 概率；<c>weighted_pick_one</c> 组下是相对权重
        /// （不要求归一化）。</summary>
        public double WeightOrChance { get; }

        /// <summary>可选前置条件（如"任务相关掉落只在任务激活时可能出现"），已按
        /// <c>core/rules/expr_host.RulesExprSchema.Base</c>（默认）解析为 <see cref="ExprNode"/>；为
        /// null 表示恒真。</summary>
        public ExprNode? Condition { get; }

        public int CountMin { get; }

        public int CountMax { get; }

        /// <summary>T-N2-8 新增（ADR-0032 决策 7；08 第 1.1 节修订段"装备类条目新增
        /// <c>qualityWeights: Optional&lt;Map&lt;Id, Number&gt;&gt;</c>（品质权重），缺省取模板品质"）：
        /// 品质 id → 权重，按 <see cref="LootTableParser"/> 解析出的 JSON 键顺序保留（<see
        /// cref="IReadOnlyList{T}"/> 而非 <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>——
        /// 字典枚举顺序不是语言契约保证的稳定顺序，加权抽取的"累加到阈值命中"算法是顺序敏感的，必须
        /// 与数据声明顺序一致才能保证同种子重跑逐字段一致，见 <see cref="LootHost"/> 判断记录）。
        /// <c>null</c>/空表示未配置，掉落时不掷品质骰、直接取模板自身品质（不消耗随机数，保证未配置
        /// 该字段的旧数据行为完全不变）。</summary>
        public IReadOnlyList<KeyValuePair<Id, double>>? QualityWeights { get; }

        public LootEntry(Id @ref, double weightOrChance, ExprNode? condition, int countMin, int countMax)
            : this(@ref, weightOrChance, condition, countMin, countMax, null)
        {
        }

        /// <summary>T-N2-8 新增重载（ABI 硬性规则"只允许新增"，不改既有 5 参构造函数签名）：额外接受
        /// <see cref="QualityWeights"/>。</summary>
        public LootEntry(
            Id @ref, double weightOrChance, ExprNode? condition, int countMin, int countMax,
            IReadOnlyList<KeyValuePair<Id, double>>? qualityWeights)
        {
            Ref = @ref;
            WeightOrChance = weightOrChance;
            Condition = condition;
            CountMin = countMin;
            CountMax = countMax;
            QualityWeights = qualityWeights;
        }
    }

    /// <summary>一个抽取分组（见 08 第 1.1 节 <c>LootGroup</c>）。</summary>
    public sealed class LootGroup
    {
        public LootRollMode RollMode { get; }

        public IReadOnlyList<LootEntry> Entries { get; }

        /// <summary><c>weighted_pick_one</c> 时可选多次抽取（不放回）；<c>chance_each</c> 下忽略本字段。
        /// 未提供时按 1 处理。</summary>
        public int? PickCount { get; }

        public LootGroup(LootRollMode rollMode, IReadOnlyList<LootEntry> entries, int? pickCount)
        {
            RollMode = rollMode;
            Entries = entries;
            PickCount = pickCount;
        }
    }

    /// <summary>一张 <c>loot.table</c> 记录的强类型视图（见 08 第 1.1 节字段表）。</summary>
    public sealed class LootTableDef
    {
        public Id Id { get; }

        public IReadOnlyList<LootGroup> Groups { get; }

        /// <summary>保底计数：本表整体至少掉落的条目数（见 08 第 1.1 节 <c>guaranteed_min</c>）；
        /// null 表示不启用保底。</summary>
        public int? GuaranteedMin { get; }

        public LootTableDef(Id id, IReadOnlyList<LootGroup> groups, int? guaranteedMin)
        {
            Id = id;
            Groups = groups;
            GuaranteedMin = guaranteedMin;
        }
    }
}
