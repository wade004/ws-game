using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 消费方反馈第 56 条（2026-09-18）验收：<see cref="ContentGraphAnalyzer"/> 统一提供的不可达/孤立/
    /// 入度出度/环四类只读图分析结果。
    /// </summary>
    public sealed class ContentGraphAnalyzerTests
    {
        private static IReadOnlyDictionary<string, IReadOnlyList<string>> Edges(
            params (string from, string[] to)[] entries)
        {
            var dict = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var (from, to) in entries)
            {
                dict[from] = to;
            }
            return dict;
        }

        [Fact]
        public void Reachability_UnreachableNodeReported_ReachableNodeNotReported()
        {
            var nodeIds = new[] { "a", "b", "c" };
            var edges = Edges(("a", new[] { "c" }));

            var analysis = ContentGraphAnalyzer.Analyze(nodeIds, edges, roots: new[] { "a" });

            Assert.Equal(new[] { "a", "c" }, analysis.ReachableNodeIds);
            Assert.Equal(new[] { "b" }, analysis.UnreachableNodeIds);
        }

        [Fact]
        public void NoRoots_ReachabilityResultsAreEmpty()
        {
            var nodeIds = new[] { "a", "b" };
            var edges = Edges(("a", new[] { "b" }));

            var analysis = ContentGraphAnalyzer.Analyze(nodeIds, edges);

            Assert.Empty(analysis.ReachableNodeIds);
            Assert.Empty(analysis.UnreachableNodeIds);
        }

        [Fact]
        public void IsolatedNodes_ZeroInAndOutDegree()
        {
            var nodeIds = new[] { "a", "b", "c" };
            var edges = Edges(("a", new[] { "b" }));
            // c 既不指向任何节点，也不被任何节点指向——孤立。

            var analysis = ContentGraphAnalyzer.Analyze(nodeIds, edges);

            Assert.Equal(new[] { "c" }, analysis.IsolatedNodeIds);
        }

        [Fact]
        public void InAndOutDegree_CountedCorrectly()
        {
            var nodeIds = new[] { "a", "b", "c" };
            var edges = Edges(("a", new[] { "b", "c" }), ("b", new[] { "c" }));

            var analysis = ContentGraphAnalyzer.Analyze(nodeIds, edges);

            Assert.Equal(0, analysis.InDegree["a"]);
            Assert.Equal(2, analysis.OutDegree["a"]);
            Assert.Equal(1, analysis.InDegree["b"]);
            Assert.Equal(1, analysis.OutDegree["b"]);
            Assert.Equal(2, analysis.InDegree["c"]);
            Assert.Equal(0, analysis.OutDegree["c"]);
        }

        [Fact]
        public void DanglingEdgeTarget_IgnoredForDegreeAndReachability()
        {
            var nodeIds = new[] { "a" };
            var edges = Edges(("a", new[] { "missing" }));

            var analysis = ContentGraphAnalyzer.Analyze(nodeIds, edges, roots: new[] { "a" });

            Assert.Equal(0, analysis.OutDegree["a"]);
            Assert.Equal(new[] { "a" }, analysis.ReachableNodeIds);
            Assert.Empty(analysis.Cycles);
        }

        [Fact]
        public void Cycle_TwoNodeLoop_Detected()
        {
            var nodeIds = new[] { "a", "b" };
            var edges = Edges(("a", new[] { "b" }), ("b", new[] { "a" }));

            var analysis = ContentGraphAnalyzer.Analyze(nodeIds, edges);

            var cycle = Assert.Single(analysis.Cycles);
            Assert.Equal(new[] { "a", "b", "a" }, cycle);
        }

        [Fact]
        public void SelfLoop_CountsAsCycle()
        {
            var nodeIds = new[] { "a" };
            var edges = Edges(("a", new[] { "a" }));

            var analysis = ContentGraphAnalyzer.Analyze(nodeIds, edges);

            var cycle = Assert.Single(analysis.Cycles);
            Assert.Equal(new[] { "a", "a" }, cycle);
        }

        [Fact]
        public void AcyclicDiamond_NoCyclesReported()
        {
            var nodeIds = new[] { "a", "b", "c", "d" };
            var edges = Edges(("a", new[] { "b", "c" }), ("b", new[] { "d" }), ("c", new[] { "d" }));

            var analysis = ContentGraphAnalyzer.Analyze(nodeIds, edges, roots: new[] { "a" });

            Assert.Empty(analysis.Cycles);
            Assert.Equal(new[] { "a", "b", "c", "d" }, analysis.ReachableNodeIds);
            Assert.Empty(analysis.UnreachableNodeIds);
            Assert.Empty(analysis.IsolatedNodeIds);
        }

        [Fact]
        public void RootNotInGraph_IgnoredNoThrow()
        {
            var nodeIds = new[] { "a" };
            var edges = Edges();

            var analysis = ContentGraphAnalyzer.Analyze(nodeIds, edges, roots: new[] { "missing_root" });

            Assert.Empty(analysis.ReachableNodeIds);
            Assert.Equal(new[] { "a" }, analysis.UnreachableNodeIds);
        }
    }
}
