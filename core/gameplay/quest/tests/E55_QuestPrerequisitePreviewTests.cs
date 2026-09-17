using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// 消费方反馈第 55 条：<see cref="QuestPrerequisitePreview"/> 只读无状态预演的对账与边界用例。
    /// <para>
    /// 对账证据（判断记录）：用与 <see cref="QuestHostTests.Prerequisite_UsingQuestIsCompleted_GatesAvailability"/>
    /// 同款"接取 → 完成 → 交付"真实 <see cref="QuestHost"/> 流程，核对 <see cref="QuestPrerequisitePreview.Preview"/>
    /// 给出的"可接任务/被哪些前置阻塞"判定与真实 <see cref="IQuestHost.GetState"/> 结果一致——本类型只覆盖
    /// <c>quest.*</c> 引用类前置这一个维度，见类型判断记录"语义边界"，reconciliation 用例特意只用纯
    /// <c>quest.is_completed(...)</c> 前置（不掺杂世界标志等其它条件），确保两者可比。
    /// </para>
    /// </summary>
    public class E55_QuestPrerequisitePreviewTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly IExprSchema Schema = QuestExprSchemaEntries.BuildParsingSchema();

        private static QuestDefinition SimpleKillQuest(Id id, Id creatureTemplate, int count = 1, ExprNode? prerequisite = null)
        {
            return new QuestDefinition(
                id,
                new[] { new QuestObjective(QuestObjectiveType.Kill, creatureTemplate, count) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None,
                prerequisite: prerequisite);
        }

        private static void CompleteViaRealHost(Harness h, Id questId, Id creatureTemplate)
        {
            Assert.True(h.Host.Accept(Player, questId));
            var unitId = new Id("unit." + questId.Value.Replace('.', '_') + "_target");
            h.Units.SetTemplate(unitId, creatureTemplate);
            h.Bus.PublishImmediate(new Core.Rules.Common.UnitDiedEvent(unitId, Player));
            Assert.True(h.Host.TurnIn(Player, questId));
        }

        // ---------------------------------------------------------------
        // 对账：可接任务 / 被哪些前置阻塞
        // ---------------------------------------------------------------

        [Fact]
        public void Preview_Availability_MatchesRealHostGetState_SinglePrerequisite()
        {
            var aId = new Id("quest.sample_kill_wolves");
            var a = SimpleKillQuest(aId, new Id("creature.wolf"), 1);

            var bId = new Id("quest.sample_deliver_letter");
            var prereq = ExprParser.Parse($"quest.is_completed({aId})", Schema);
            var b = new QuestDefinition(
                bId,
                new[] { new QuestObjective(QuestObjectiveType.Talk, new Id("dialog.sample_menu"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None,
                prerequisite: prereq);

            var definitions = new[] { a, b };

            var previewBefore = QuestPrerequisitePreview.Preview(bId, definitions, completedQuestIds: Array.Empty<Id>());
            Assert.Equal(new[] { aId }, previewBefore.DirectPrerequisiteQuestIds);
            Assert.False(previewBefore.IsAvailableByReferencedQuests);
            Assert.Equal(new[] { aId }, previewBefore.BlockingQuestIds);

            var h = new Harness(definitions);
            Assert.Equal(QuestState.Unavailable, h.Host.GetState(Player, bId));

            CompleteViaRealHost(h, aId, new Id("creature.wolf"));
            Assert.Equal(QuestState.Available, h.Host.GetState(Player, bId));

            var previewAfter = QuestPrerequisitePreview.Preview(bId, definitions, completedQuestIds: new[] { aId });
            Assert.True(previewAfter.IsAvailableByReferencedQuests);
            Assert.Empty(previewAfter.BlockingQuestIds!);
        }

        [Fact]
        public void Preview_ClosureAndTopologicalOrder_MatchesRealHostThreeQuestChain()
        {
            var aId = new Id("quest.chain_a");
            var bId = new Id("quest.chain_b");
            var cId = new Id("quest.chain_c");

            var a = SimpleKillQuest(aId, new Id("creature.wolf"), 1);
            var b = SimpleKillQuest(bId, new Id("creature.bear"), 1, ExprParser.Parse($"quest.is_completed({aId})", Schema));
            var c = SimpleKillQuest(cId, new Id("creature.boar"), 1, ExprParser.Parse($"quest.is_completed({bId})", Schema));
            var definitions = new[] { a, b, c };

            var preview = QuestPrerequisitePreview.Preview(cId, definitions);
            Assert.Equal(new[] { bId }, preview.DirectPrerequisiteQuestIds);
            Assert.Equal(new[] { bId, aId }, preview.TransitiveClosureQuestIds);
            Assert.False(preview.HasCycle);
            Assert.Equal(new[] { aId, bId, cId }, preview.TopologicalOrder);
            Assert.False(preview.ClosureTruncated);
            Assert.Empty(preview.UnknownReferencedQuestIds);

            var h = new Harness(definitions);
            Assert.Equal(QuestState.Unavailable, h.Host.GetState(Player, bId));
            Assert.Equal(QuestState.Unavailable, h.Host.GetState(Player, cId));

            CompleteViaRealHost(h, aId, new Id("creature.wolf"));
            Assert.Equal(QuestState.Available, h.Host.GetState(Player, bId));
            Assert.Equal(QuestState.Unavailable, h.Host.GetState(Player, cId));

            CompleteViaRealHost(h, bId, new Id("creature.bear"));
            Assert.Equal(QuestState.Available, h.Host.GetState(Player, cId));

            var previewAfterB = QuestPrerequisitePreview.Preview(cId, definitions, completedQuestIds: new[] { aId, bId });
            Assert.True(previewAfterB.IsAvailableByReferencedQuests);
        }

        // ---------------------------------------------------------------
        // 拓扑序有效性（菱形依赖，不硬编码具体顺序，只断言"前置排在依赖者之前"这一通用性质）
        // ---------------------------------------------------------------

        [Fact]
        public void Preview_DiamondDependency_TopologicalOrderRespectsAllEdges()
        {
            var aId = new Id("quest.diamond_a");
            var bId = new Id("quest.diamond_b");
            var cId = new Id("quest.diamond_c");
            var dId = new Id("quest.diamond_d");

            var a = SimpleKillQuest(aId, new Id("creature.wolf"), 1);
            var b = SimpleKillQuest(bId, new Id("creature.bear"), 1, ExprParser.Parse($"quest.is_completed({aId})", Schema));
            var c = SimpleKillQuest(cId, new Id("creature.boar"), 1, ExprParser.Parse($"quest.is_completed({aId})", Schema));
            var d = SimpleKillQuest(dId, new Id("creature.rat"), 1,
                ExprParser.Parse($"quest.is_completed({bId}) and quest.is_completed({cId})", Schema));
            var definitions = new[] { a, b, c, d };

            var preview = QuestPrerequisitePreview.Preview(dId, definitions);

            Assert.False(preview.HasCycle);
            var order = preview.TopologicalOrder;
            Assert.Equal(4, order.Count);
            AssertBefore(order, aId, bId);
            AssertBefore(order, aId, cId);
            AssertBefore(order, bId, dId);
            AssertBefore(order, cId, dId);

            Assert.Equal(new[] { bId, cId }, preview.DirectPrerequisiteQuestIds);
            Assert.Equal(3, preview.TransitiveClosureQuestIds.Count);
            Assert.Contains(aId, preview.TransitiveClosureQuestIds);
            Assert.Contains(bId, preview.TransitiveClosureQuestIds);
            Assert.Contains(cId, preview.TransitiveClosureQuestIds);
        }

        private static void AssertBefore(IReadOnlyList<Id> order, Id before, Id after)
        {
            var iBefore = order.ToList().IndexOf(before);
            var iAfter = order.ToList().IndexOf(after);
            Assert.True(iBefore >= 0 && iAfter >= 0, "两个 id 都应出现在拓扑序里");
            Assert.True(iBefore < iAfter, $"{before} 应排在 {after} 之前，实际下标 {iBefore} vs {iAfter}");
        }

        // ---------------------------------------------------------------
        // 无状态 / 确定性
        // ---------------------------------------------------------------

        [Fact]
        public void Preview_RepeatedAndConcurrentCalls_ProduceEqualResults()
        {
            var (definitions, dId) = DiamondDefinitions();

            var baseline = QuestPrerequisitePreview.Preview(dId, definitions, completedQuestIds: new[] { new Id("quest.diamond_a") });

            var repeated = QuestPrerequisitePreview.Preview(dId, definitions, completedQuestIds: new[] { new Id("quest.diamond_a") });
            AssertSameShape(baseline, repeated);

            var results = new QuestPrerequisitePreviewResult[16];
            Parallel.For(0, results.Length, i =>
            {
                results[i] = QuestPrerequisitePreview.Preview(dId, definitions, completedQuestIds: new[] { new Id("quest.diamond_a") });
            });
            foreach (var r in results)
            {
                AssertSameShape(baseline, r);
            }
        }

        private static (QuestDefinition[] Definitions, Id DId) DiamondDefinitions()
        {
            var aId = new Id("quest.diamond_a");
            var bId = new Id("quest.diamond_b");
            var cId = new Id("quest.diamond_c");
            var dId = new Id("quest.diamond_d");
            var a = SimpleKillQuest(aId, new Id("creature.wolf"), 1);
            var b = SimpleKillQuest(bId, new Id("creature.bear"), 1, ExprParser.Parse($"quest.is_completed({aId})", Schema));
            var c = SimpleKillQuest(cId, new Id("creature.boar"), 1, ExprParser.Parse($"quest.is_completed({aId})", Schema));
            var d = SimpleKillQuest(dId, new Id("creature.rat"), 1,
                ExprParser.Parse($"quest.is_completed({bId}) and quest.is_completed({cId})", Schema));
            return (new[] { a, b, c, d }, dId);
        }

        private static void AssertSameShape(QuestPrerequisitePreviewResult a, QuestPrerequisitePreviewResult b)
        {
            Assert.Equal(a.QuestId.Value, b.QuestId.Value);
            Assert.Equal(a.DirectPrerequisiteQuestIds.Select(x => x.Value), b.DirectPrerequisiteQuestIds.Select(x => x.Value));
            Assert.Equal(a.TransitiveClosureQuestIds.Select(x => x.Value), b.TransitiveClosureQuestIds.Select(x => x.Value));
            Assert.Equal(a.UnknownReferencedQuestIds.Select(x => x.Value), b.UnknownReferencedQuestIds.Select(x => x.Value));
            Assert.Equal(a.HasCycle, b.HasCycle);
            Assert.Equal(a.TopologicalOrder.Select(x => x.Value), b.TopologicalOrder.Select(x => x.Value));
            Assert.Equal(a.IsAvailableByReferencedQuests, b.IsAvailableByReferencedQuests);
            Assert.Equal(a.BlockingQuestIds?.Select(x => x.Value), b.BlockingQuestIds?.Select(x => x.Value));
        }

        // ---------------------------------------------------------------
        // 边界用例
        // ---------------------------------------------------------------

        [Fact]
        public void Preview_NoPrerequisite_EmptyClosureAndSingleNodeTopoOrder()
        {
            var aId = new Id("quest.lone");
            var a = SimpleKillQuest(aId, new Id("creature.wolf"), 1);

            var preview = QuestPrerequisitePreview.Preview(aId, new[] { a });

            Assert.Empty(preview.DirectPrerequisiteQuestIds);
            Assert.Empty(preview.TransitiveClosureQuestIds);
            Assert.False(preview.HasCycle);
            Assert.Equal(new[] { aId }, preview.TopologicalOrder);
            Assert.Empty(preview.UnknownReferencedQuestIds);
        }

        [Fact]
        public void Preview_UnknownReferencedQuest_MarksUnknownAndBlocks()
        {
            var aId = new Id("quest.exists");
            var missingId = new Id("quest.does_not_exist");
            var a = SimpleKillQuest(aId, new Id("creature.wolf"), 1, ExprParser.Parse($"quest.is_completed({missingId})", Schema));

            var preview = QuestPrerequisitePreview.Preview(aId, new[] { a }, completedQuestIds: Array.Empty<Id>());

            Assert.Equal(new[] { missingId }, preview.DirectPrerequisiteQuestIds);
            Assert.Equal(new[] { missingId }, preview.UnknownReferencedQuestIds);
            Assert.Empty(preview.TransitiveClosureQuestIds); // 未知引用不参与图扩展
            Assert.False(preview.HasCycle);
            Assert.False(preview.IsAvailableByReferencedQuests);
            Assert.Equal(new[] { missingId }, preview.BlockingQuestIds);
        }

        [Fact]
        public void Preview_MutualCycle_HasCycleTrueAndEmptyTopologicalOrder()
        {
            var aId = new Id("quest.cycle_a");
            var bId = new Id("quest.cycle_b");
            var a = SimpleKillQuest(aId, new Id("creature.wolf"), 1, ExprParser.Parse($"quest.is_completed({bId})", Schema));
            var b = SimpleKillQuest(bId, new Id("creature.bear"), 1, ExprParser.Parse($"quest.is_completed({aId})", Schema));

            var preview = QuestPrerequisitePreview.Preview(aId, new[] { a, b });

            Assert.True(preview.HasCycle);
            Assert.Empty(preview.TopologicalOrder);
            Assert.NotEmpty(preview.CyclePath);
            Assert.Equal(preview.CyclePath.First(), preview.CyclePath.Last());
        }

        [Fact]
        public void Preview_SelfReference_IsTreatedAsCycle()
        {
            var aId = new Id("quest.self_ref");
            var a = SimpleKillQuest(aId, new Id("creature.wolf"), 1, ExprParser.Parse($"quest.is_completed({aId})", Schema));

            var preview = QuestPrerequisitePreview.Preview(aId, new[] { a });

            Assert.True(preview.HasCycle);
            Assert.Empty(preview.TopologicalOrder);
        }

        [Fact]
        public void Preview_MaxNodesCap_TruncatesLongChain()
        {
            // 五条依次相连的任务链（e 依赖 d 依赖 c 依赖 b 依赖 a），maxNodes 设为 2 应截断。
            var ids = Enumerable.Range(0, 5).Select(i => new Id($"quest.chain5_{i}")).ToArray();
            var defs = new List<QuestDefinition>();
            for (var i = 0; i < ids.Length; i++)
            {
                ExprNode? prereq = i > 0 ? ExprParser.Parse($"quest.is_completed({ids[i - 1]})", Schema) : null;
                defs.Add(SimpleKillQuest(ids[i], new Id("creature.wolf"), 1, prereq));
            }

            var preview = QuestPrerequisitePreview.Preview(ids[4], defs, maxNodes: 2);

            Assert.True(preview.ClosureTruncated);
            Assert.True(preview.TransitiveClosureQuestIds.Count <= 2);
        }

        [Fact]
        public void Preview_QuestIdNotInDefinitions_ThrowsArgumentException()
        {
            var a = SimpleKillQuest(new Id("quest.a"), new Id("creature.wolf"), 1);
            Assert.Throws<ArgumentException>(() =>
                QuestPrerequisitePreview.Preview(new Id("quest.not_registered"), new[] { a }));
        }

        [Fact]
        public void Preview_NullDefinitions_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                QuestPrerequisitePreview.Preview(new Id("quest.a"), null!));
        }

        [Fact]
        public void Preview_NonPositiveMaxNodes_ThrowsArgumentOutOfRangeException()
        {
            var a = SimpleKillQuest(new Id("quest.a"), new Id("creature.wolf"), 1);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                QuestPrerequisitePreview.Preview(new Id("quest.a"), new[] { a }, maxNodes: 0));
        }

        [Fact]
        public void Preview_WithoutCompletedQuestIds_AvailabilityFieldsAreNull()
        {
            var a = SimpleKillQuest(new Id("quest.a"), new Id("creature.wolf"), 1);

            var preview = QuestPrerequisitePreview.Preview(new Id("quest.a"), new[] { a });

            Assert.Null(preview.IsAvailableByReferencedQuests);
            Assert.Null(preview.BlockingQuestIds);
        }
    }
}
