using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Gameplay.Dialog;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Dialog
{
    /// <summary>
    /// 消费方反馈第 55 条：<see cref="DialogStoryPreview"/> 只读无状态预演的对账与边界用例。
    /// <para>
    /// 对账证据（判断记录）：不用最小夹具"看起来像"就下结论，而是用真实 <see cref="DialogHost"/>
    /// （经 <see cref="Harness"/> 装配）在同一份 <see cref="StoryTreeDefinition"/> 上真的走一遍
    /// （<see cref="DialogHost.StartStory"/>/<see cref="DialogHost.GetStoryView"/>/
    /// <see cref="DialogHost.AdvanceStory"/>），核对结果与 <see cref="DialogStoryPreview.Preview"/>
    /// 给出的起点/可选分支逐项一致。<c>evaluateCondition</c> 回调用与 <see cref="DialogHost"/> 内部
    /// 同一份 <see cref="QuestExprSchemaEntries.BuildParsingSchema"/> schema 把
    /// <see cref="DialogStoryPreviewBranch.ConditionText"/> 重新解析回 <see cref="ExprNode"/> 再求值——
    /// 这正是"调用方通过回调自己求值"的预期用法，见 <see cref="DialogStoryPreview"/> 类型判断记录。
    /// </para>
    /// </summary>
    public class E55_DialogStoryPreviewTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly IExprSchema Schema = QuestExprSchemaEntries.BuildParsingSchema();

        private static Func<string, bool?> ConditionEvaluatorFor(
            Func<string, IReadOnlyList<ExprValue>, ExprValue>? playerGroup)
        {
            var host = new TestExprHostFactory(playerGroup).CreateFor(Player, null, null);
            var diagnostics = new ExprDiagnosticsRecorder();
            return text => ExprEvaluator.EvaluateBool(ExprParser.Parse(text, Schema), host, diagnostics);
        }

        // ---------------------------------------------------------------
        // 对账：起点 + 可选分支
        // ---------------------------------------------------------------

        [Fact]
        public void Preview_StartNodeId_MatchesRealHostStartStory()
        {
            var treeId = new Id("dialog.sample_tree");
            var node2 = new Id("dialog.node_2");
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.node_1"), new Id("l10n.node1"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.continue"), null, node2),
                }, null),
                new StoryNodeDefinition(node2, new Id("l10n.node2"), null, Array.Empty<StoryBranchDef>(), null),
            });

            var preview = DialogStoryPreview.Preview(tree);

            var h = new Harness(Array.Empty<GossipMenuDefinition>(), new[] { tree });
            h.Host.StartStory(Player, treeId);
            var realStartNodeId = h.Host.GetStoryView(Player)!.NodeId;

            Assert.Equal(realStartNodeId, preview.StartNodeId);
            Assert.Equal(tree.FirstNode.Id, preview.StartNodeId);
        }

        [Fact]
        public void Preview_VisibleBranchesUnderCallback_MatchesRealHostGetStoryView()
        {
            var treeId = new Id("dialog.gated_tree");
            var node2 = new Id("dialog.node_2");
            var condition = ExprParser.Parse("player.level >= 5", Schema);
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.node_1"), new Id("l10n.node1"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.branch_always"), null, node2),
                    new StoryBranchDef(new Id("l10n.branch_gated"), condition, null),
                }, null),
                new StoryNodeDefinition(node2, new Id("l10n.node2"), null, Array.Empty<StoryBranchDef>(), null),
            });

            Func<string, IReadOnlyList<ExprValue>, ExprValue> playerGroupLow =
                (key, args) => key == "level" ? ExprValue.OfInt(1) : ExprValue.OfBool(false);
            Func<string, IReadOnlyList<ExprValue>, ExprValue> playerGroupHigh =
                (key, args) => key == "level" ? ExprValue.OfInt(10) : ExprValue.OfBool(false);

            foreach (var playerGroup in new[] { playerGroupLow, playerGroupHigh })
            {
                var preview = DialogStoryPreview.Preview(tree, ConditionEvaluatorFor(playerGroup));

                var h = new Harness(Array.Empty<GossipMenuDefinition>(), new[] { tree }, playerGroup);
                h.Host.StartStory(Player, treeId);
                var realVisible = h.Host.GetStoryView(Player)!.VisibleBranches
                    .Select(b => b.TextKey)
                    .OrderBy(id => id.Value, StringComparer.Ordinal)
                    .ToList();

                var previewVisibleFromStart = preview.Nodes
                    .Single(n => n.NodeId == tree.FirstNode.Id)
                    .Branches
                    .Where(b => b.ConditionText == null || ConditionEvaluatorFor(playerGroup)(b.ConditionText) == true)
                    .Select(b => b.TextKey)
                    .OrderBy(id => id.Value, StringComparer.Ordinal)
                    .ToList();

                Assert.Equal(realVisible, previewVisibleFromStart);
            }
        }

        [Fact]
        public void Preview_ReachableNodeIds_MatchesRealHostWalkAcrossBothBranches()
        {
            // 一个二叉分支剧情：level>=5 走 node_high，否则走 node_low，两条分支都指向真实存在的节点。
            var treeId = new Id("dialog.branching_tree");
            var high = new Id("dialog.node_high");
            var low = new Id("dialog.node_low");
            var condition = ExprParser.Parse("player.level >= 5", Schema);
            var notCondition = ExprParser.Parse("player.level < 5", Schema);
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.node_1"), new Id("l10n.node1"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.to_high"), condition, high),
                    new StoryBranchDef(new Id("l10n.to_low"), notCondition, low),
                }, null),
                new StoryNodeDefinition(high, new Id("l10n.high"), null, Array.Empty<StoryBranchDef>(), null),
                new StoryNodeDefinition(low, new Id("l10n.low"), null, Array.Empty<StoryBranchDef>(), null),
            });

            foreach (var level in new[] { 1, 10 })
            {
                Func<string, IReadOnlyList<ExprValue>, ExprValue> playerGroup =
                    (key, args) => key == "level" ? ExprValue.OfInt(level) : ExprValue.OfBool(false);

                var preview = DialogStoryPreview.Preview(tree, ConditionEvaluatorFor(playerGroup));

                var h = new Harness(Array.Empty<GossipMenuDefinition>(), new[] { tree }, playerGroup);
                h.Host.StartStory(Player, treeId);
                var visible = h.Host.GetStoryView(Player)!.VisibleBranches;
                Assert.Single(visible); // level>=5 与 level<5 互斥，恒好有一条分支可见
                h.Host.AdvanceStory(Player, visible[0].Index);
                var realStoryView = h.Host.GetStoryView(Player);
                var expectedNode = level >= 5 ? high : low;
                Assert.NotNull(realStoryView); // 会话仍在（目标节点没有分支，但会话本身没有结束）
                Assert.Equal(expectedNode, realStoryView!.NodeId);
                Assert.Empty(realStoryView.VisibleBranches);
                Assert.False(preview.PathsTruncated);
                Assert.Contains(expectedNode, preview.ReachableNodeIds!);
                Assert.DoesNotContain(level >= 5 ? low : high, preview.ReachableNodeIds!);
            }
        }

        // ---------------------------------------------------------------
        // 无状态 / 确定性
        // ---------------------------------------------------------------

        [Fact]
        public void Preview_RepeatedCalls_ProduceEqualResults()
        {
            var tree = SimpleBranchingTree(out _, out _);
            Func<string, bool?> alwaysTrue = _ => true;

            var first = DialogStoryPreview.Preview(tree, alwaysTrue);
            var second = DialogStoryPreview.Preview(tree, alwaysTrue);

            AssertSameShape(first, second);
        }

        [Fact]
        public void Preview_ConcurrentCalls_ProduceEqualResultsAndDoNotThrow()
        {
            var tree = SimpleBranchingTree(out _, out _);
            Func<string, bool?> alwaysTrue = _ => true;

            var baseline = DialogStoryPreview.Preview(tree, alwaysTrue);

            var results = new DialogStoryPreviewResult[16];
            Parallel.For(0, results.Length, i =>
            {
                results[i] = DialogStoryPreview.Preview(tree, alwaysTrue);
            });

            foreach (var r in results)
            {
                AssertSameShape(baseline, r);
            }
        }

        private static void AssertSameShape(DialogStoryPreviewResult a, DialogStoryPreviewResult b)
        {
            Assert.Equal(a.TreeId.Value, b.TreeId.Value);
            Assert.Equal(a.StartNodeId.Value, b.StartNodeId.Value);
            Assert.Equal(a.TerminalNodeIds.Select(x => x.Value), b.TerminalNodeIds.Select(x => x.Value));
            Assert.Equal(a.ReachableNodeIds!.Select(x => x.Value), b.ReachableNodeIds!.Select(x => x.Value));
            Assert.Equal(a.Paths!.Count, b.Paths!.Count);
            for (var i = 0; i < a.Paths.Count; i++)
            {
                Assert.Equal(a.Paths[i].Select(x => x.Value), b.Paths[i].Select(x => x.Value));
            }
            Assert.Equal(a.PathsTruncated, b.PathsTruncated);
        }

        private static StoryTreeDefinition SimpleBranchingTree(out Id branchA, out Id branchB)
        {
            var treeId = new Id("dialog.stateless_tree");
            branchA = new Id("dialog.branch_a");
            branchB = new Id("dialog.branch_b");
            return new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.root"), new Id("l10n.root"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.a"), null, branchA),
                    new StoryBranchDef(new Id("l10n.b"), null, branchB),
                }, null),
                new StoryNodeDefinition(branchA, new Id("l10n.a_node"), null, Array.Empty<StoryBranchDef>(), null),
                new StoryNodeDefinition(branchB, new Id("l10n.b_node"), null, Array.Empty<StoryBranchDef>(), null),
            });
        }

        // ---------------------------------------------------------------
        // 边界用例
        // ---------------------------------------------------------------

        [Fact]
        public void Preview_SingleNodeNoBranches_IsTerminalAndHasSinglePathOfLengthOne()
        {
            var treeId = new Id("dialog.trivial_tree");
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.only"), new Id("l10n.only"), null, Array.Empty<StoryBranchDef>(), null),
            });

            var preview = DialogStoryPreview.Preview(tree, _ => true);

            Assert.Single(preview.Nodes);
            Assert.Equal(tree.FirstNode.Id, Assert.Single(preview.TerminalNodeIds));
            Assert.False(preview.PathsTruncated);
            var path = Assert.Single(preview.Paths!);
            Assert.Equal(new[] { tree.FirstNode.Id }, path);
            Assert.Equal(new[] { tree.FirstNode.Id }, preview.ReachableNodeIds!);
        }

        [Fact]
        public void Preview_Cycle_TerminatesSafelyAndMarksTruncated()
        {
            var treeId = new Id("dialog.cyclic_tree");
            var nodeA = new Id("dialog.a");
            var nodeB = new Id("dialog.b");
            // A -> B -> A：构造期不受 HasCycle 校验拦截（校验是内容管线职责，本类型自身必须对任意
            // 输入防御），预演入口必须安全终止，不能死循环/栈溢出。
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(nodeA, new Id("l10n.a"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.to_b"), null, nodeB),
                }, null),
                new StoryNodeDefinition(nodeB, new Id("l10n.b"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.to_a"), null, nodeA),
                }, null),
            });

            var preview = DialogStoryPreview.Preview(tree, _ => true, maxPaths: 50, maxDepth: 50);

            Assert.True(preview.PathsTruncated);
            Assert.Equal(new[] { nodeA, nodeB }, preview.ReachableNodeIds);
            Assert.NotEmpty(preview.Paths!);
        }

        [Fact]
        public void Preview_DanglingNextNodeId_DoesNotThrowAndMarksTruncated()
        {
            var treeId = new Id("dialog.dangling_tree");
            var missing = new Id("dialog.does_not_exist");
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.a"), new Id("l10n.a"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.to_missing"), null, missing),
                }, null),
            });

            var preview = DialogStoryPreview.Preview(tree, _ => true);

            Assert.True(preview.PathsTruncated);
            Assert.DoesNotContain(missing, preview.ReachableNodeIds!);
        }

        [Fact]
        public void Preview_MaxPathsCap_TruncatesAndRespectsLimit()
        {
            // 根节点四条互不冲突的可见终止分支——不设上限会枚举 4 条路径，maxPaths=2 应截断为 2 条。
            var treeId = new Id("dialog.wide_tree");
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.root"), new Id("l10n.root"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.o1"), null, null),
                    new StoryBranchDef(new Id("l10n.o2"), null, null),
                    new StoryBranchDef(new Id("l10n.o3"), null, null),
                    new StoryBranchDef(new Id("l10n.o4"), null, null),
                }, null),
            });

            var preview = DialogStoryPreview.Preview(tree, _ => true, maxPaths: 2);

            Assert.True(preview.PathsTruncated);
            Assert.Equal(2, preview.Paths!.Count);
        }

        [Fact]
        public void Preview_MaxDepthCap_TruncatesLongLinearChain()
        {
            var treeId = new Id("dialog.long_chain");
            var ids = Enumerable.Range(0, 10).Select(i => new Id($"dialog.n{i}")).ToArray();
            var nodes = new List<StoryNodeDefinition>();
            for (var i = 0; i < ids.Length; i++)
            {
                var next = i + 1 < ids.Length ? ids[i + 1] : (Id?)null;
                var branches = next.HasValue
                    ? new[] { new StoryBranchDef(new Id("l10n.next"), null, next) }
                    : Array.Empty<StoryBranchDef>();
                nodes.Add(new StoryNodeDefinition(ids[i], new Id("l10n.node"), null, branches, null));
            }
            var tree = new StoryTreeDefinition(treeId, nodes);

            var preview = DialogStoryPreview.Preview(tree, _ => true, maxDepth: 3);

            Assert.True(preview.PathsTruncated);
            Assert.All(preview.Paths!, p => Assert.True(p.Count <= 3));
        }

        [Fact]
        public void Preview_NullEvaluateCondition_OmitsReachabilityFields()
        {
            var tree = SimpleBranchingTree(out _, out _);

            var preview = DialogStoryPreview.Preview(tree);

            Assert.Null(preview.ReachableNodeIds);
            Assert.Null(preview.Paths);
            Assert.False(preview.PathsTruncated);
            Assert.NotEmpty(preview.Nodes);
        }

        [Fact]
        public void Preview_NullTree_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => DialogStoryPreview.Preview(null!));
        }

        [Fact]
        public void Preview_NonPositiveCaps_ThrowArgumentOutOfRangeException()
        {
            var tree = SimpleBranchingTree(out _, out _);
            Assert.Throws<ArgumentOutOfRangeException>(() => DialogStoryPreview.Preview(tree, maxPaths: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => DialogStoryPreview.Preview(tree, maxDepth: 0));
        }
    }
}
