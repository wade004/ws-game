using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// 消费方反馈第 56 条追问（2026-09-18，对 1.41.0 处置结果的回应）：<c>quest_prerequisite</c> 图里
    /// "既无前置、也未被任何其它任务引用为前置"的孤立节点在 <see cref="QuestContentValidationRule"/>
    /// 判断记录里被认定为合法内容形态（一条独立支线任务本身就是合法的"无前置、未被引用"），不落地为
    /// 强制诊断；但编辑器一类内容工具仍然可能想要这类展示性提示。设计层拍板：提供一条**默认关闭**的
    /// 可选规则，由宿主经 <see cref="ContentValidationOptions.EnableGraphIsolationDiagnostics"/> 显式
    /// 开启（见 <c>Presentation.Assembly.ContentValidationAssembly.CreateRegistryCore</c> 接线点），
    /// 不像既有两条可选规则那样"提供依赖即启用"——本规则不需要任何外部依赖，天然适合一个纯布尔开关。
    /// <para>
    /// 图与算法：以 <c>quest.def</c> 全表记录为节点、<see cref="QuestReferenceExtractor.ExtractReferencedQuestIds"/>
    /// 从每条记录的 <c>prerequisite</c> 提取出的引用任务 id 为边（"任务 → 前置任务"），与
    /// <see cref="QuestContentValidationRule.ValidatePrerequisiteGraph"/> 构图口径一致，复用同一个提取
    /// 入口而不是自行重新遍历 Expr 语法树。交给 <see cref="ContentGraphAnalyzer.Analyze"/> 统一图分析
    /// （不传 <c>roots</c>，本规则只关心 <see cref="ContentGraphAnalysis.IsolatedNodeIds"/>，不做可达性
    /// 分析——图里允许多条独立支线，没有唯一入口）。语法本身不可解析（<see cref="ExprParseException"/>）
    /// 已由 <c>expr_parsable</c> 结构校验报告，遇到该情形本规则视为该记录无引用（同
    /// <see cref="QuestReferenceExtractor.ExtractReferencedQuestIds(string, IExprSchema?)"/> 既有语义），
    /// 不重复报告。
    /// </para>
    /// <para>
    /// 判断记录（"仅当图中任务总数 ≥2 时报"）：只有一条 <c>quest.def</c> 记录时，它必然无前置、也不
    /// 可能被别的任务引用——恒为孤立节点，报告没有信息量，纯属噪音，因此表内记录数不足 2 时本规则整体
    /// 不产出任何问题。
    /// </para>
    /// </summary>
    public sealed class QuestPrerequisiteIsolationRule : IValidationRule
    {
        /// <summary>见 <c>Presentation.Assembly.OptionalRuleDescriptor.CheckName</c> 判断记录：消费方
        /// 应引用本常量按检查名过滤诊断，不要复制字面量。</summary>
        public const string CheckName = "quest_prerequisite_node_isolated";

        private readonly IExprSchema _exprSchema;

        public QuestPrerequisiteIsolationRule(IExprSchema? exprSchema = null)
        {
            // 判断记录同 QuestContentValidationRule 构造函数：未提供时退回独立登记表，保证
            // new QuestPrerequisiteIsolationRule() 无参构造也能正确解析 quest.* 引用。
            _exprSchema = exprSchema ?? QuestExprSchemaEntries.BuildParsingSchema();
        }

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

        /// <summary>本规则唯一的检查项即为 Warning 级展示性提示（孤立节点在 quest_prerequisite 图里是
        /// 合法内容形态，见类型顶部判断记录），不应因为宿主把 <see cref="DataRegistryStrictness.WarningsBlock"/>
        /// 打开而被提升为阻断——同 <c>DialogContentValidationRule.story_tree_node_unreachable</c> 既有
        /// 口径。</summary>
        public bool NonEscalatable => true;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains(QuestSchemas.Def.Name))
            {
                yield break;
            }

            var records = view.GetAll(QuestSchemas.Def.Name).ToList();
            if (records.Count < 2)
            {
                yield break; // 见类型顶部判断记录。
            }

            var nodeIds = records.Select(r => r.Key).ToList();
            var edges = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

            foreach (var record in records)
            {
                if (!record.TryGetString("prerequisite", out var text) || string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var referenced = QuestReferenceExtractor.ExtractReferencedQuestIds(text, _exprSchema);
                if (referenced.Count == 0)
                {
                    continue;
                }

                edges[record.Key] = referenced.Select(id => id.ToString()).ToList();
            }

            var analysis = ContentGraphAnalyzer.Analyze(nodeIds, edges);
            foreach (var isolatedId in analysis.IsolatedNodeIds)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Warning, QuestSchemas.Def.Name, CheckName,
                    $"任务 \"{isolatedId}\" 既无前置任务，也未被任何其它任务的 prerequisite 引用——在" +
                        " quest_prerequisite 图中是孤立节点（可能是独立支线任务，也可能是遗漏的前置关系，" +
                        "本诊断只作提示，不代表内容缺陷）",
                    recordKey: isolatedId, field: null, group: null, note: null, ruleId: null,
                    affectedNodeIds: new[] { isolatedId });
            }
        }
    }
}
