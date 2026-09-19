using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// 消费方反馈第 4 条 / ADR-0045：<see cref="QuestGiverIndicatorQuery.Evaluate"/> 的四态覆盖与
    /// 优先级裁决用例。全部用真实 <see cref="QuestHost"/>（经 <see cref="Harness"/>，同
    /// <c>QuestHostTests</c>/<c>E55_QuestPrerequisitePreviewTests</c> 惯例）驱动状态转移，不 mock
    /// <see cref="IQuestHost"/>——保证断言的是"聚合真实状态查询结果"而不是"聚合一个假状态"。
    /// </summary>
    public class QuestGiverIndicatorQueryTests
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

        private static QuestDefinition SimpleCollectQuest(Id id, Id itemTemplate, int count)
        {
            return new QuestDefinition(
                id,
                new[] { new QuestObjective(QuestObjectiveType.Collect, itemTemplate, count, consumeOnProgress: false) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
        }

        // ---------------------------------------------------------------
        // 参数校验
        // ---------------------------------------------------------------

        [Fact]
        public void Evaluate_NullQuestHost_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                QuestGiverIndicatorQuery.Evaluate(null!, Player, Array.Empty<Id>()));
        }

        [Fact]
        public void Evaluate_NullGiverQuestIds_Throws()
        {
            var h = new Harness(Array.Empty<QuestDefinition>());
            Assert.Throws<ArgumentNullException>(() =>
                QuestGiverIndicatorQuery.Evaluate(h.Host, Player, null!));
        }

        // ---------------------------------------------------------------
        // 四态各自覆盖
        // ---------------------------------------------------------------

        [Fact]
        public void Evaluate_EmptyGiverQuestIds_ReturnsNone()
        {
            var h = new Harness(Array.Empty<QuestDefinition>());
            var result = QuestGiverIndicatorQuery.Evaluate(h.Host, Player, Array.Empty<Id>());
            Assert.Equal(QuestGiverIndicatorState.None, result);
        }

        [Fact]
        public void Evaluate_PrerequisiteUnmet_ReturnsNone()
        {
            var aId = new Id("quest.sample_kill_wolves");
            var a = SimpleKillQuest(aId, new Id("creature.wolf"), 1);

            var bId = new Id("quest.sample_deliver_letter");
            var prereq = ExprParser.Parse($"quest.is_completed({aId})", Schema);
            var b = SimpleKillQuest(bId, new Id("creature.bear"), 1, prereq);

            var h = new Harness(new[] { a, b });

            // b 的前置（a 已完成）未满足 -> Unavailable，指示器聚合为 None（不受 a 本身可接的影响，
            // giverQuestIds 只传 b，模拟"这个给予者只给 b 这一条任务"）。
            var result = QuestGiverIndicatorQuery.Evaluate(h.Host, Player, new[] { bId });
            Assert.Equal(QuestGiverIndicatorState.None, result);
        }

        [Fact]
        public void Evaluate_SingleAvailableQuest_ReturnsAvailable()
        {
            var questId = new Id("quest.sample_kill_wolves");
            var quest = SimpleKillQuest(questId, new Id("creature.wolf"), 1);
            var h = new Harness(new[] { quest });

            var result = QuestGiverIndicatorQuery.Evaluate(h.Host, Player, new[] { questId });
            Assert.Equal(QuestGiverIndicatorState.Available, result);
        }

        [Fact]
        public void Evaluate_SingleActiveQuest_ReturnsInProgress()
        {
            var questId = new Id("quest.sample_kill_wolves");
            var quest = SimpleKillQuest(questId, new Id("creature.wolf"), 3);
            var h = new Harness(new[] { quest });
            Assert.True(h.Host.Accept(Player, questId));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));

            var result = QuestGiverIndicatorQuery.Evaluate(h.Host, Player, new[] { questId });
            Assert.Equal(QuestGiverIndicatorState.InProgress, result);
        }

        [Fact]
        public void Evaluate_SingleObjectivesCompleteQuest_ReturnsCompletable()
        {
            var questId = new Id("quest.sample_collect_flowers");
            var quest = SimpleCollectQuest(questId, new Id("item.flower"), 3);
            var h = new Harness(new[] { quest });
            Assert.True(h.Host.Accept(Player, questId));
            h.Inventory.AddItem(Player, new Id("item.flower"), 3);
            h.Bus.PublishImmediate(new ItemAddedEvent(
                Player, new Id("item.instance_flower"), new Id("item.flower"), 3));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));

            var result = QuestGiverIndicatorQuery.Evaluate(h.Host, Player, new[] { questId });
            Assert.Equal(QuestGiverIndicatorState.Completable, result);
        }

        [Fact]
        public void Evaluate_TurnedInNonRepeatable_ReturnsNone()
        {
            var questId = new Id("quest.sample_collect_flowers");
            var quest = SimpleCollectQuest(questId, new Id("item.flower"), 1);
            var h = new Harness(new[] { quest });
            Assert.True(h.Host.Accept(Player, questId));
            h.Inventory.AddItem(Player, new Id("item.flower"), 1);
            h.Bus.PublishImmediate(new ItemAddedEvent(
                Player, new Id("item.instance_flower"), new Id("item.flower"), 1));
            Assert.True(h.Host.TurnIn(Player, questId));
            Assert.Equal(QuestState.TurnedIn, h.Host.GetState(Player, questId));

            var result = QuestGiverIndicatorQuery.Evaluate(h.Host, Player, new[] { questId });
            Assert.Equal(QuestGiverIndicatorState.None, result);
        }

        // ---------------------------------------------------------------
        // 同一给予者同时具备多种状态时的优先级裁决
        // ---------------------------------------------------------------

        [Fact]
        public void Evaluate_AvailableAndInProgress_PrioritizesAvailable()
        {
            var availableId = new Id("quest.sample_kill_wolves");
            var activeId = new Id("quest.sample_kill_bears");
            var available = SimpleKillQuest(availableId, new Id("creature.wolf"), 1);
            var active = SimpleKillQuest(activeId, new Id("creature.bear"), 3);
            var h = new Harness(new[] { available, active });
            Assert.True(h.Host.Accept(Player, activeId));

            var result = QuestGiverIndicatorQuery.Evaluate(h.Host, Player, new[] { availableId, activeId });
            Assert.Equal(QuestGiverIndicatorState.Available, result);
        }

        [Fact]
        public void Evaluate_CompletableAndAvailableAndInProgress_PrioritizesCompletable()
        {
            var availableId = new Id("quest.sample_kill_wolves");
            var activeId = new Id("quest.sample_kill_bears");
            var completableId = new Id("quest.sample_collect_flowers");

            var available = SimpleKillQuest(availableId, new Id("creature.wolf"), 1);
            var active = SimpleKillQuest(activeId, new Id("creature.bear"), 3);
            var completable = SimpleCollectQuest(completableId, new Id("item.flower"), 1);

            var h = new Harness(new[] { available, active, completable });
            Assert.True(h.Host.Accept(Player, activeId));
            Assert.True(h.Host.Accept(Player, completableId));
            h.Inventory.AddItem(Player, new Id("item.flower"), 1);
            h.Bus.PublishImmediate(new ItemAddedEvent(
                Player, new Id("item.instance_flower"), new Id("item.flower"), 1));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, completableId));

            // 顺序打乱也不影响结果（不依赖遍历顺序，纯按状态集合裁决）。
            var result = QuestGiverIndicatorQuery.Evaluate(
                h.Host, Player, new[] { activeId, availableId, completableId });
            Assert.Equal(QuestGiverIndicatorState.Completable, result);

            var resultReordered = QuestGiverIndicatorQuery.Evaluate(
                h.Host, Player, new[] { completableId, activeId, availableId });
            Assert.Equal(QuestGiverIndicatorState.Completable, resultReordered);
        }

        [Fact]
        public void Evaluate_CompletableAndInProgress_PrioritizesCompletable()
        {
            var activeId = new Id("quest.sample_kill_bears");
            var completableId = new Id("quest.sample_collect_flowers");

            var active = SimpleKillQuest(activeId, new Id("creature.bear"), 3);
            var completable = SimpleCollectQuest(completableId, new Id("item.flower"), 1);

            var h = new Harness(new[] { active, completable });
            Assert.True(h.Host.Accept(Player, activeId));
            Assert.True(h.Host.Accept(Player, completableId));
            h.Inventory.AddItem(Player, new Id("item.flower"), 1);
            h.Bus.PublishImmediate(new ItemAddedEvent(
                Player, new Id("item.instance_flower"), new Id("item.flower"), 1));

            var result = QuestGiverIndicatorQuery.Evaluate(h.Host, Player, new[] { activeId, completableId });
            Assert.Equal(QuestGiverIndicatorState.Completable, result);
        }

        [Fact]
        public void Evaluate_DuplicateQuestIds_DoesNotChangeResult()
        {
            var questId = new Id("quest.sample_kill_wolves");
            var quest = SimpleKillQuest(questId, new Id("creature.wolf"), 1);
            var h = new Harness(new[] { quest });

            var result = QuestGiverIndicatorQuery.Evaluate(h.Host, Player, new[] { questId, questId, questId });
            Assert.Equal(QuestGiverIndicatorState.Available, result);
        }
    }
}
