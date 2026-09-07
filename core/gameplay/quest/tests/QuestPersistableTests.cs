using System.Linq;
using Core.Foundation.Common;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// GP-01 复现与回归测试（architecture/落地计划/audit-b3b91ee-20260907/code-review.md、
    /// validation-repros.txt R1）：<see cref="QuestPersistable.Load"/> 此前只对快照里出现的
    /// questId 调用 <c>RestoreProgress</c>，运行期已有、快照未覆盖到的任务原样残留——空档读档
    /// 无法回滚到保存点，任务无法被"读档删除"，完成计数/每日记录也无法被清空。
    /// <para>
    /// 复用 <see cref="Harness"/>（定义于 QuestHostTests.cs，同程序集/命名空间下 internal 可见）
    /// 搭建最小可用的 <see cref="QuestHost"/> 装配。
    /// </para>
    /// </summary>
    public class QuestPersistableTests
    {
        private static readonly Id Player = TestSupport.Player;

        private static QuestDefinition SimpleEscortQuest(Id id, QuestRepeatable repeatable = QuestRepeatable.None)
        {
            return new QuestDefinition(
                id,
                new[] { new QuestObjective(QuestObjectiveType.Escort, new Id("creature.villager"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, repeatable);
        }

        /// <summary>R1 的确切形状：先在"什么任务都没接"的时刻存一份空档，随后接取任务，
        /// 再用这份空档 Load——必须把任务状态整个回滚掉（不再是 Active）。</summary>
        [Fact]
        public void Load_EmptySnapshot_RollsBackSubsequentlyAcceptedQuest()
        {
            var questId = new Id("quest.sample_escort");
            var quest = SimpleEscortQuest(questId);
            var h = new Harness(new[] { quest });
            var persistable = new QuestPersistable(h.Host, () => Player);

            var emptySnapshot = persistable.Save();
            Assert.True(h.Host.Accept(Player, questId));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));

            persistable.Load(emptySnapshot);

            Assert.Equal(QuestState.Available, h.Host.GetState(Player, questId));
            Assert.Empty(h.Host.GetLog(Player));
        }

        /// <summary>快照里没有出现的 questId（因为存档时还没接）读档后必须从运行期状态里消失，
        /// 不能保留读档前刚接的那条记录。</summary>
        [Fact]
        public void Load_SnapshotWithoutQuest_RemovesQuestAcceptedAfterSave()
        {
            var questA = new Id("quest.sample_a");
            var questB = new Id("quest.sample_b");
            var h = new Harness(new[] { SimpleEscortQuest(questA), SimpleEscortQuest(questB) });
            var persistable = new QuestPersistable(h.Host, () => Player);

            Assert.True(h.Host.Accept(Player, questA));
            var snapshotWithOnlyA = persistable.Save();

            Assert.True(h.Host.Accept(Player, questB));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questB));

            persistable.Load(snapshotWithOnlyA);

            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questA));
            Assert.Equal(QuestState.Available, h.Host.GetState(Player, questB));
            var log = h.Host.GetLog(Player);
            Assert.Single(log);
            Assert.Equal(questA, log[0].QuestId);
        }

        /// <summary>daily 任务完成计数/每日记录必须随快照回滚，不能停留在读档后又推进过的值上。</summary>
        [Fact]
        public void Load_OldSnapshot_RestoresCompletionCountAndLastCompletedDay()
        {
            var questId = new Id("quest.sample_daily");
            var h = new Harness(new[] { SimpleEscortQuest(questId, QuestRepeatable.Daily) });
            var persistable = new QuestPersistable(h.Host, () => Player);

            h.CurrentDay = 1;
            Assert.True(h.Host.Accept(Player, questId));
            h.Host.UpdateProgress(Player, questId, 0, 1);
            Assert.True(h.Host.TurnIn(Player, questId));
            var savedAfterFirstTurnIn = persistable.Save();
            var savedProgress = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questId));
            Assert.Equal(1, savedProgress.CompletionCount);
            Assert.Equal(1, savedProgress.LastCompletedDay);

            h.CurrentDay = 2;
            Assert.True(h.Host.Accept(Player, questId));
            h.Host.UpdateProgress(Player, questId, 0, 1);
            Assert.True(h.Host.TurnIn(Player, questId));
            var advancedProgress = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questId));
            Assert.Equal(2, advancedProgress.CompletionCount);
            Assert.Equal(2, advancedProgress.LastCompletedDay);

            persistable.Load(savedAfterFirstTurnIn);

            var restored = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questId));
            Assert.Equal(1, restored.CompletionCount);
            Assert.Equal(1, restored.LastCompletedDay);
            // GetLog 返回的是存储的原始 State（TurnedIn），"当天不可再接"是 GetState 结合
            // dayProvider 动态推导的结果——day 2 时应仍能据 LastCompletedDay=1 判定可再接。
            Assert.Equal(QuestState.TurnedIn, restored.State);
            Assert.Equal(QuestState.Available, h.Host.GetState(Player, questId));
        }

        /// <summary>跨单位隔离：Load 只按 <c>_playerUnitProvider</c> 解析出的单位替换其任务状态，
        /// 不得影响同一 <see cref="QuestHost"/> 内其他单位（例如 NPC/同伴）已有的独立进度——
        /// GP-01 验收"跨槽隔离"的字面覆盖之外，这里额外验证 <c>ReplaceAllProgress</c> 的
        /// 单位过滤本身正确，不是靠"每个存档槽各自一个全新 Host 实例"侥幸绕过。</summary>
        [Fact]
        public void Load_ForOneUnit_DoesNotAffectAnotherUnitsProgressOnSameHost()
        {
            var questId = new Id("quest.sample_shared_def");
            var otherUnit = new Id("unit.companion");
            var h = new Harness(new[] { SimpleEscortQuest(questId) });
            var persistable = new QuestPersistable(h.Host, () => Player);

            Assert.True(h.Host.Accept(otherUnit, questId));
            Assert.True(h.Host.Accept(Player, questId));

            // 一份"Player 从未接过任何任务"的空快照，来自另一个全新 host（模拟存档最初状态）。
            var freshHost = new Harness(new[] { SimpleEscortQuest(questId) });
            var trulyEmptySnapshot = new QuestPersistable(freshHost.Host, () => Player).Save();

            persistable.Load(trulyEmptySnapshot);

            Assert.Equal(QuestState.Available, h.Host.GetState(Player, questId));
            Assert.Equal(QuestState.Active, h.Host.GetState(otherUnit, questId));
        }

        /// <summary>重复 Load 同一份快照必须幂等——不会因为"先删后插"的实现方式产生重复记录或
        /// 状态漂移。</summary>
        [Fact]
        public void Load_SameSnapshotTwice_IsIdempotent()
        {
            var questId = new Id("quest.sample_escort");
            var h = new Harness(new[] { SimpleEscortQuest(questId) });
            var persistable = new QuestPersistable(h.Host, () => Player);

            Assert.True(h.Host.Accept(Player, questId));
            var snapshot = persistable.Save();

            persistable.Load(snapshot);
            persistable.Load(snapshot);

            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));
            Assert.Single(h.Host.GetLog(Player));
        }
    }
}
