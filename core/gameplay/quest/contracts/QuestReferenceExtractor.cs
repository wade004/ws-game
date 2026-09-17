using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// 消费方反馈第 54 条（2026-09-18）：<c>quest.def.prerequisite</c> 文本里引用的其它任务 id 提取，
    /// 此前只有 <see cref="QuestContentValidationRule"/> 内部一个私有方法
    /// （<c>CollectQuestIdLiteralArgs</c>）能做这件事，消费方（编辑器"任务前置关系"面板一类内容工具）
    /// 拿不到等价能力，只能自己重新实现一份 Expr 遍历——本类型是新增的公开静态入口，<see
    /// cref="QuestContentValidationRule"/> 改为复用本类型的 <see cref="CollectQuestIdLiteralArgs"/>
    /// （内部方法，两者同属 <c>Core.Gameplay</c> 程序集），不再各自维护一份遍历逻辑。
    /// <para>
    /// 判断记录：<see cref="ExtractReferencedQuestIds"/> 返回类型是 <see cref="IReadOnlyList{T}"/>
    /// of <see cref="Id"/>，不直接暴露 <see cref="ExprNode"/>/<see cref="ExprLiteralNode"/> 等
    /// <c>Core.Foundation.Expr</c> 内部语法树类型——那些是表达式解析器的内部实现细节，暴露出去会把
    /// 消费方（编辑器）耦合到解析器内部结构，且未来解析器内部改型（如换一种 AST 节点表示）会直接
    /// 破坏消费方代码，不符合"契约面只暴露必要的最小信息"。
    /// </para>
    /// <para>
    /// 判断记录：语法解析失败（<see cref="ExprParseException"/>）时返回空列表，不向调用方抛出新的
    /// 未处理异常路径——与 <see cref="QuestContentValidationRule.ValidatePrerequisiteGraph"/> 内部
    /// "语法错误已由 <c>expr_parsable</c> 结构校验报告，这里跳过，不重复报告"的既有语义保持一致；
    /// 消费方若需要区分"语法错误"与"合法但无引用"，应自行先用 <see cref="ExprParser.Parse"/> +
    /// <c>expr_parsable</c> 校验结果判断，本方法不改变这一分工。
    /// </para>
    /// <para>
    /// 判断记录：同一目标任务 id 在表达式里被多次引用时（如
    /// <c>quest.is_completed("quest.a") &amp;&amp; quest.is_active("quest.a")</c>）只返回一次，
    /// 按"该 id 在表达式中第一次出现"的顺序排列——消费方（如"任务前置关系"面板）关心的是"这条
    /// prerequisite 依赖哪些任务"这一集合关系，不需要也不应该因为同一引用重复出现而重复展示；顺序
    /// 固定为源顺序而非任意集合顺序，保证同一份数据每次提取结果确定、可用于快照测试。
    /// </para>
    /// <para>
    /// 判断记录（整合验收，1.41.0）：<see cref="CollectQuestIdLiteralArgs"/> 原为手写的
    /// and/or/not/比较/引用节点完整 switch 递归，与并行落地的 <c>QuestPrerequisitePreview</c>
    /// （消费方反馈第 55 条）各自实现了一份等价的树遍历——两者均需要"找出子树内全部引用节点"，这正是
    /// 框架已公开的 <see cref="ExprReferenceCollector.Collect"/>（T-N5-2）提供的通用能力。整合时改为
    /// 基于 <see cref="ExprReferenceCollector.Collect"/> 实现（先拿到全部引用节点，再按
    /// <c>Group == quest</c> 过滤取 Id 字面量参数），不再手写遍历；`ExtractReferencedQuestIds`
    /// 新增一个接受已解析 <see cref="ExprNode"/> 的 <c>internal</c> 重载，供
    /// <c>QuestPrerequisitePreview</c> 直接复用（其已持有 <see cref="Core.Gameplay.Quest
    /// .QuestDefinition.Prerequisite"/> 解析好的语法树，不需要也不应该重新解析文本）——三处
    /// （<see cref="QuestContentValidationRule"/>、本类型公开入口、预演入口）最终共用同一份遍历与
    /// 过滤逻辑，行为逐字段不变（回归测试见 <c>QuestReferenceExtractorTests</c>/
    /// <c>E38_QuestPrerequisiteCycleTests</c>/<c>E55_QuestPrerequisitePreviewTests</c>）。
    /// </para>
    /// </summary>
    public static class QuestReferenceExtractor
    {
        /// <summary>提取 <paramref name="prerequisiteExprText"/> 中全部 <c>quest.*</c> 引用
        /// （<see cref="QuestExprSchemaEntries.RegisterInto"/> 登记的 <c>is_active</c>/
        /// <c>is_completed</c>/<c>is_available</c>/<c>is_objectives_complete</c>/
        /// <c>objective_progress</c> 五个 key）指向的任务 id，已去重、按首次出现顺序排列。
        /// <paramref name="prerequisiteExprText"/> 为 <c>null</c>/空串时返回空列表。
        /// <paramref name="schema"/> 缺省（<c>null</c>）时退回
        /// <see cref="QuestExprSchemaEntries.BuildParsingSchema"/> 构造的独立登记表，与
        /// <see cref="QuestContentValidationRule"/> 无参构造的退回策略一致。</summary>
        public static IReadOnlyList<Id> ExtractReferencedQuestIds(string prerequisiteExprText, IExprSchema? schema = null)
        {
            if (string.IsNullOrEmpty(prerequisiteExprText))
            {
                return Array.Empty<Id>();
            }

            var effectiveSchema = schema ?? QuestExprSchemaEntries.BuildParsingSchema();

            ExprNode root;
            try
            {
                root = ExprParser.Parse(prerequisiteExprText, effectiveSchema);
            }
            catch (ExprParseException)
            {
                // 见类型顶部判断记录：语法错误不向调用方抛出，返回空列表。
                return Array.Empty<Id>();
            }

            return ExtractReferencedQuestIds(root);
        }

        /// <summary>同上，但接受已解析的语法树（<paramref name="root"/> 为 <c>null</c> 时返回空
        /// 列表）——供已经持有解析好 <see cref="ExprNode"/> 的调用方（如 <c>QuestPrerequisitePreview</c>
        /// 直接读取 <c>QuestDefinition.Prerequisite</c>）复用，不必先转回文本再重新解析一遍。
        /// <c>internal</c>：只对 <c>Core.Gameplay</c> 程序集内部可见，公开入口是上面接受文本的重载
        /// （见类型顶部判断记录）。</summary>
        internal static IReadOnlyList<Id> ExtractReferencedQuestIds(ExprNode? root)
        {
            if (root == null)
            {
                return Array.Empty<Id>();
            }

            var literalArgs = new List<ExprLiteralNode>();
            CollectQuestIdLiteralArgs(root, literalArgs);

            if (literalArgs.Count == 0)
            {
                return Array.Empty<Id>();
            }

            var seen = new HashSet<Id>();
            var result = new List<Id>(literalArgs.Count);
            foreach (var literalArg in literalArgs)
            {
                var id = literalArg.Value.AsId;
                if (seen.Add(id))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        /// <summary>收集 <paramref name="node"/> 子树内全部 <c>quest.*</c> 引用（见
        /// <see cref="ExprGroups.Quest"/>）的 Id 字面量参数——不限定具体 key（现有五个已知 key 的首参数
        /// 全部是 Id，未来新增 <c>quest.*</c> 签名若仍以 Id 参数指向另一个任务，本方法不需要跟着改）。
        /// 基于框架已公开的 <see cref="ExprReferenceCollector.Collect"/> 实现（整合验收时勘误，见类型
        /// 顶部判断记录）——后者已经按深度优先收集了子树内全部引用节点（含嵌套在其它引用参数列表/
        /// and/or/not/比较子树里的引用），本方法只需按 <c>Group == quest</c> 过滤、取 Id 字面量参数，
        /// 不必再手写一遍树遍历。保持 <c>internal</c>（不返回内部 AST 类型给外部消费方，见类型顶部
        /// 判断记录），只对 <c>Core.Gameplay</c> 程序集内部可见——<see cref="QuestContentValidationRule"/>
        /// 与本类型另一 <c>internal</c> 重载均调用本方法。</summary>
        internal static void CollectQuestIdLiteralArgs(ExprNode node, List<ExprLiteralNode> results)
        {
            foreach (var reference in ExprReferenceCollector.Collect(node))
            {
                if (!string.Equals(reference.Group, ExprGroups.Quest, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var arg in reference.Args)
                {
                    if (arg is ExprLiteralNode literalArg && literalArg.Value.Kind == ExprValueKind.Id)
                    {
                        results.Add(literalArg);
                    }
                }
            }
        }
    }
}
