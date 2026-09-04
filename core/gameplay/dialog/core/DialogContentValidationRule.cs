using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Gameplay.Quest;

namespace Core.Gameplay.Dialog
{
    /// <summary>
    /// <c>dialog.gossip_menu</c>/<c>dialog.story_tree</c> 专属内容校验规则（见
    /// <see cref="IValidationRule"/>、任务书"校验：动作 kind 十种合法；next_node_id 存在；树无环
    /// （DFS）；起始节点 = nodes[0]（README）"）。<c>kind</c> 合法性、<c>next_node_id</c> 是否存在
    /// 于同一棵树内（<see cref="StoryTreeDefinition"/> 构造期已经用一个 <c>Dictionary</c> 建过索引，
    /// 但"引用到树外不存在的节点"这一具体检查在解析阶段不会失败——<see cref="StoryBranchDef.NextNodeId"/>
    /// 只是一个 <see cref="Core.Foundation.Common.Id"/>，解析期不校验它是否命中某个已知节点）由本规则
    /// 在解析成功后二次核对；树是否成环用 <see cref="StoryTreeDefinition.HasCycle"/>（DFS）；
    /// "起始节点 = nodes[0]"是 <see cref="StoryTreeDefinition.FirstNode"/> 的既定语义，不需要额外
    /// 校验（数组第一个元素恒是起始节点，不存在"取错"的可能）。
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方需要显式
    /// <c>registry.RegisterValidationRule(new DialogContentValidationRule())</c>（惯例同
    /// <c>QuestContentValidationRule</c>）。
    /// </para>
    /// </summary>
    public sealed class DialogContentValidationRule : IValidationRule
    {
        private const string Check = "dialog_content";

        private readonly IExprSchema _exprSchema;

        public DialogContentValidationRule(IExprSchema? exprSchema = null)
        {
            _exprSchema = exprSchema ?? QuestExprSchemaEntries.BuildParsingSchema();
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(DialogSchemas.GossipMenu.Name))
            {
                // 判断记录：C# 不允许在 catch 子句体内 yield return（CS1631），先落到局部变量。
                string? errorMessage = null;
                try
                {
                    GossipMenuDefinition.FromRecord(record, _exprSchema);
                }
                catch (System.Exception ex)
                {
                    errorMessage = ex.Message;
                }

                if (errorMessage != null)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, DialogSchemas.GossipMenu.Name, Check,
                        $"dialog.gossip_menu 解析失败：{errorMessage}", recordKey: record.Key);
                }
            }

            foreach (var record in view.GetAll(DialogSchemas.StoryTree.Name))
            {
                StoryTreeDefinition? tree = null;
                string? treeErrorMessage = null;
                try
                {
                    tree = StoryTreeDefinition.FromRecord(record, _exprSchema);
                }
                catch (System.Exception ex)
                {
                    treeErrorMessage = ex.Message;
                }

                if (treeErrorMessage != null)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, DialogSchemas.StoryTree.Name, Check,
                        $"dialog.story_tree 解析失败：{treeErrorMessage}", recordKey: record.Key);
                    continue;
                }

                foreach (var node in tree!.Nodes)
                {
                    foreach (var branch in node.Branches)
                    {
                        if (branch.NextNodeId.HasValue && !tree.TryGetNode(branch.NextNodeId.Value, out _))
                        {
                            yield return new ValidationIssue(
                                ValidationSeverity.Error, DialogSchemas.StoryTree.Name, Check,
                                $"节点 \"{node.Id}\" 的分支 next_node_id \"{branch.NextNodeId.Value}\" 在树内不存在",
                                recordKey: record.Key, field: "nodes");
                        }
                    }
                }

                if (tree.HasCycle(out var cyclePath))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, DialogSchemas.StoryTree.Name, Check,
                        $"剧情树成环：{string.Join(" -> ", cyclePath)}", recordKey: record.Key);
                }
            }
        }
    }
}
