using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// <see cref="LootEntry.Condition"/> 的求值方式（见 <see cref="LootTableAnalyzer"/> 类型注释、
    /// 消费方内容编辑器第 35 条"LootAnalysisContext 提供……条件视为真/假/按给定求值器三种模式"）。
    /// </summary>
    public enum LootConditionEvaluationMode
    {
        /// <summary>忽略 <see cref="ExprHost"/>，全部条件视为恒真——适合"不考虑任务/世界状态、只看
        /// 掉落表结构本身能产出什么"的编辑器预览场景。</summary>
        AssumeTrue,

        /// <summary>忽略 <see cref="ExprHost"/>，全部条件视为恒假（等价于该条目永不可能通过条件）——
        /// 适合"条件相关内容尚未配齐求值上下文、宁可保守估计下限"的场景。</summary>
        AssumeFalse,

        /// <summary>按 <see cref="ExprHost"/>（+ 可选 <see cref="Diagnostics"/>）真正求值——语义与
        /// <see cref="LootHost.Roll"/> 内部对 <see cref="LootEntry.Condition"/> 的求值完全一致（同一份
        /// <see cref="LootRollCore.ConditionPasses"/>），要求调用方提供一个已经按目标场景装配好的
        /// <see cref="IExprHost"/>（<c>self</c>=击杀者或来源单位、<c>target</c>=来源单位，同
        /// <see cref="LootHost.Roll"/> 里 <c>_exprHostFactory.CreateFor(...)</c> 的约定）。</summary>
        Evaluate,
    }

    /// <summary>
    /// <see cref="LootTableAnalyzer.ExpectedProbabilities"/> 的求值上下文——消费方内容编辑器第 35 条
    /// 要求的"条件求值所需的上下文……与保底状态"。所有字段均有合理默认值，最小可用上下文是
    /// <c>new LootAnalysisContext()</c>（全部条件视为恒真、无伪随机状态、不解析嵌套 <c>loot.*</c>
    /// 引用——见 <see cref="Tables"/>）。
    /// </summary>
    public sealed class LootAnalysisContext
    {
        /// <summary>见 <see cref="LootConditionEvaluationMode"/>，默认 <see
        /// cref="LootConditionEvaluationMode.AssumeTrue"/>。</summary>
        public LootConditionEvaluationMode ConditionMode { get; set; } = LootConditionEvaluationMode.AssumeTrue;

        /// <summary><see cref="ConditionMode"/> = <see cref="LootConditionEvaluationMode.Evaluate"/> 时
        /// 必填，其余模式忽略。</summary>
        public IExprHost? ExprHost { get; set; }

        /// <summary><see cref="ConditionMode"/> = <see cref="LootConditionEvaluationMode.Evaluate"/> 时
        /// 使用；未提供时按 <see cref="LootHost"/> 惯例退回一个只落地、不上抛的
        /// <see cref="ExprDiagnosticsRecorder"/>。</summary>
        public IExprDiagnostics? Diagnostics { get; set; }

        /// <summary>对应 <see cref="RollContext.Multiplier"/>（难度倍率），默认 1.0。</summary>
        public double Multiplier { get; set; } = 1.0;

        /// <summary>对应 <see cref="RollContext.ContextId"/>（本次结算 id），仅用于拼出
        /// <see cref="LootPseudoRandomKey"/> 以便在 <see cref="PseudoRandomMissStreaks"/> 里查找当前
        /// 连续未中次数；默认 null——留空等价于"不关心具体结算 id、伪随机按 0 连续未中计算"（见
        /// <see cref="PseudoRandomMissStreaks"/> 类型注释）。</summary>
        public Id? ContextId { get; set; }

        /// <summary>对应 <see cref="LootOptions.PseudoRandom"/>，默认 false（关闭）。开启且
        /// <see cref="PseudoRandomMissStreaks"/> 未显式提供某个 <see cref="LootPseudoRandomKey"/> 时，
        /// 该条目的连续未中次数按 0 计（"从零开始"，见类型注释）。</summary>
        public bool PseudoRandom { get; set; }

        /// <summary>对应 <see cref="LootOptions.PseudoRandomStep"/>，默认 0.1（与 <see
        /// cref="LootOptions"/> 默认值保持一致）。</summary>
        public double PseudoRandomStep { get; set; } = 0.1;

        /// <summary>伪随机"连续未中次数"的当前运行期状态快照（<see cref="LootHost"/> 判断记录 3 的
        /// <c>_missStreaks</c>——该字典本身不对外公开，调用方需要自行从别处获知/估计当前计数），供
        /// 调用方按已知的实际状态求解"当前这一刻再抽一次"的期望概率；<see cref="ContextId"/> 为 null，
        /// 或本字段为 null，或某个 <see cref="LootPseudoRandomKey"/> 不在字典里，均视为 0（"从零开始"，
        /// 等价于一个刚创建、从未抽取过的 <see cref="LootHost"/>）。<see cref="ConditionMode"/> 与本
        /// 字段是两回事——<see cref="PseudoRandom"/> = false 时本字段整体被忽略。</summary>
        public IReadOnlyDictionary<LootPseudoRandomKey, int>? PseudoRandomMissStreaks { get; set; }

        /// <summary>嵌套 <c>loot.*</c> 引用的解析源：key 为 <see cref="LootTableDef.Id"/>。未提供
        /// （默认空字典）或某个被引用的 <c>loot.*</c> id 不在字典里时，按 <see cref="LootHost"/> 判断
        /// 记录 2/<see cref="ResolveEntryAtDepth"/> 同款语义静默跳过该分支（不贡献概率/期望数量），
        /// 不抛异常——调用方（编辑器）通常应传入"当前已加载的全部 <c>loot.table</c>"（如
        /// <c>IDataRegistryView.GetAll(LootSchemas.Table.Name)</c> 解析后的结果），只分析单表结构时
        /// 可以留空。</summary>
        public IReadOnlyDictionary<Id, LootTableDef> Tables { get; set; } = EmptyTables;

        /// <summary>对应 <see cref="LootOptions.MaxNestedDepth"/>，默认 8——嵌套展开达到该深度时静默
        /// 停止（同 <see cref="LootHost"/> 判断记录，运行期兜底防御，正常内容应已被
        /// <see cref="LootContentValidationRule"/> 的成环检测拦截）。</summary>
        public int MaxNestedDepth { get; set; } = 8;

        /// <summary><c>weighted_pick_one</c>（含 <c>guaranteed_min</c> 补抽阶段的候选池）精确计算"不
        /// 放回多抽"逐条目命中概率的候选池条目数上限——不超过该数值时用 <see
        /// cref="LootTableAnalyzer"/> 内部的位掩码动态规划精确求解（枚举全部可能的抽取顺序状态），
        /// 超过时退化为近似估计并把涉及的产出标注 <see cref="LootExpectedOutcome.IsApproximate"/>
        /// （见 <see cref="LootTableAnalyzer"/> 类型注释"精确/近似边界"一节）。默认 12——该规模下状态数
        /// 上限 2^12=4096，单次分析调用耗时可忽略；调大会显著增加计算量（状态数按候选池条目数指数
        /// 增长），调用方（编辑器）按自己愿意承受的延迟自行调整。</summary>
        public int ExactWithoutReplacementMaxEntries { get; set; } = 12;

        private static readonly IReadOnlyDictionary<Id, LootTableDef> EmptyTables = new Dictionary<Id, LootTableDef>();
    }

    /// <summary>伪随机连续未中计数的 key（见 <see cref="LootHost"/> 判断记录 3 原始 key 格式
    /// <c>$"{ContextId}|{TableId}|{groupIndex}|{entryIndex}"</c>；本结构体是同一份 key 的强类型版本，
    /// 供 <see cref="LootAnalysisContext.PseudoRandomMissStreaks"/> 使用，避免调用方手拼字符串出错）。
    /// </summary>
    public readonly struct LootPseudoRandomKey : IEquatable<LootPseudoRandomKey>
    {
        /// <summary>对应 <see cref="RollContext.ContextId"/>（本次结算 id）。</summary>
        public Id ContextId { get; }

        /// <summary>发生伪随机判定的掉落表 id（含嵌套展开后实际在计数的那一层表，即
        /// <see cref="LootTableDef.Id"/>，不是最外层调用 <see cref="LootHost.Roll"/> 传入的
        /// <c>tableId</c>）。</summary>
        public Id TableId { get; }

        public int GroupIndex { get; }

        public int EntryIndex { get; }

        public LootPseudoRandomKey(Id contextId, Id tableId, int groupIndex, int entryIndex)
        {
            ContextId = contextId;
            TableId = tableId;
            GroupIndex = groupIndex;
            EntryIndex = entryIndex;
        }

        public bool Equals(LootPseudoRandomKey other) =>
            ContextId.Equals(other.ContextId) && TableId.Equals(other.TableId) &&
            GroupIndex == other.GroupIndex && EntryIndex == other.EntryIndex;

        public override bool Equals(object? obj) => obj is LootPseudoRandomKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ContextId, TableId, GroupIndex, EntryIndex);
    }

    /// <summary>
    /// <see cref="LootTableAnalyzer.ExpectedProbabilities"/> 单条产出的期望值分析结果（消费方内容
    /// 编辑器第 35 条"每个可掉落条目（含嵌套展开后的叶子，按 item/currency id 聚合）"）。同一
    /// <see cref="LeafRef"/> 可能经多条不同路径产出（不同分组的直接条目、不同嵌套表分支、
    /// <c>guaranteed_min</c> 补抽），本类型是按 <see cref="LeafRef"/> 聚合之后的结果——<see
    /// cref="SourcePaths"/> 保留全部贡献路径的可读描述，供编辑器展示"这个概率是怎么来的"。
    /// </summary>
    public sealed class LootExpectedOutcome
    {
        /// <summary>叶子产出的模板引用（见 <see cref="LootEntry.Ref"/> 类型注释——本模块的 <c>ref</c>
        /// 只有 <c>item.*</c>/<c>loot.*</c> 两种域，叶子聚合后只剩 <c>item.*</c>；本模块没有独立的
        /// "货币"引用域，货币类掉落在本架构下就是一个 <c>item.template</c>，与其它物品按同一路径
        /// 聚合，不需要特殊处理，见 <see cref="LootTableAnalyzer"/> 类型注释"抽取语义清单"一节）。
        /// </summary>
        public Id LeafRef { get; }

        /// <summary>一次 <see cref="LootHost.Roll"/> 调用里该叶子至少出现一次（合并堆叠前，"出现"指
        /// <see cref="LootHost"/> 判断记录里贡献了至少一条 <c>(TemplateId, Count)</c>，不要求
        /// <c>Count &gt; 0</c>——本模块内容里 <c>count_range.min &gt;= 1</c>，恒成立)的概率，
        /// [0,1] 区间。</summary>
        public double DropProbability { get; }

        /// <summary>一次 <see cref="LootHost.Roll"/> 调用里该叶子的期望产出数量（对多条不同来源路径
        /// 求和后的结果，已经把"是否命中该路径"与"命中后数量在 <c>[min,max]</c> 内的期望值"都计入）。
        /// </summary>
        public double ExpectedCount { get; }

        /// <summary>贡献了本条聚合结果的全部路径的可读描述，如 <c>"groups[0].entries[1]"</c>（本表
        /// 直接条目）、<c>"groups[0].entries[0] -&gt; loot.sample_inner.groups[0].entries[0]"</c>
        /// （经嵌套表展开）、<c>"groups[0].entries[0](guaranteed_min top-up)"</c>（保底补抽贡献，见
        /// <see cref="LootTableAnalyzer"/> 类型注释"保底"一节）。</summary>
        public IReadOnlyList<string> SourcePaths { get; }

        /// <summary>true 表示 <see cref="DropProbability"/>/<see cref="ExpectedCount"/> 至少有一条
        /// 贡献路径经过了近似计算：不放回多抽候选池超过 <see
        /// cref="LootAnalysisContext.ExactWithoutReplacementMaxEntries"/>，或该叶子经 <c>loot.*</c>
        /// 嵌套引用在 <c>guaranteed_min</c> 补抽阶段被命中——<c>guaranteed_min</c> 补抽对
        /// <c>chance_each</c>/<c>weighted_pick_one</c> 直接条目本身是精确值，不因涉及保底而自动标注
        /// 近似，见 <see cref="LootTableAnalyzer"/> 类型注释"精确/近似边界"一节。false 表示全部贡献
        /// 路径都是解析式精确值。</summary>
        public bool IsApproximate { get; }

        public LootExpectedOutcome(Id leafRef, double dropProbability, double expectedCount, IReadOnlyList<string> sourcePaths, bool isApproximate)
        {
            LeafRef = leafRef;
            DropProbability = dropProbability;
            ExpectedCount = expectedCount;
            SourcePaths = sourcePaths;
            IsApproximate = isApproximate;
        }
    }
}
