using System.Linq;
using Core.Foundation.Common;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// CORE114-01 根治验收（外部审计 audit-76d16a5-20260910，见 <c>QuestHost.Reload</c>/
    /// <c>MigrateProgressAfterReload</c> 判断记录）：<c>Reload</c> 替换定义后，活跃任务的
    /// <c>ObjectiveCounts</c> 必须安全迁移到新定义的目标数组形状——目标增加、减少、重排三类结构
    /// 变化都不能让后续 <see cref="IQuestHost.UpdateProgress"/> 撞上越界数组，且旧进度按
    /// (Type, TargetRef) 尽量保留、状态按新形状重新判定。
    /// </summary>
    public sealed class CORE114_01_QuestHostReloadMigrationTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly Id Quest = new Id("quest.cov_reload");
        private static readonly Id Wolf = new Id("creature.cov_wolf");
        private static readonly Id Bear = new Id("creature.cov_bear");

        private static QuestDefinition OneKillObjective(int count = 5) => new QuestDefinition(
            Quest, new[] { new QuestObjective(QuestObjectiveType.Kill, Wolf, count) },
            QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);

        [Fact]
        public void Reload_ObjectiveCountIncreases_NewIndexUpdatesWithoutException()
        {
            var h = new Harness(new[] { OneKillObjective() });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 1); // 旧目标 0：1/5

            var reloaded = new QuestDefinition(
                Quest, new[]
                {
                    new QuestObjective(QuestObjectiveType.Kill, Wolf, 5),
                    new QuestObjective(QuestObjectiveType.Kill, Wolf, 1),
                },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            h.Host.Reload(new[] { reloaded });

            // 越界曾经在这里抛 IndexOutOfRangeException（外部审计复现 QUEST-OBJECTIVE-RELOAD）。
            var ok = h.Host.UpdateProgress(Player, Quest, 1, 1);
            Assert.True(ok);

            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Equal(2, log.ObjectiveCounts.Count);
            Assert.Equal(1, log.ObjectiveCounts[0]); // 旧目标 0（Wolf 计数 1）按 (Kill, Wolf) 匹配保留
            Assert.Equal(1, log.ObjectiveCounts[1]); // 新目标 1（同样 Kill/Wolf，但先到先得已被目标 0 认领——
                                                       // 这里断言的是"新目标 1 未越界地累加了自己的进度"
        }

        [Fact]
        public void Reload_ObjectiveRemoved_ProgressForRemainingObjectivePreserved()
        {
            var twoObjectives = new QuestDefinition(
                Quest, new[]
                {
                    new QuestObjective(QuestObjectiveType.Kill, Wolf, 5),
                    new QuestObjective(QuestObjectiveType.Kill, Bear, 3),
                },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { twoObjectives });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 2); // wolf 2/5
            h.Host.UpdateProgress(Player, Quest, 1, 1); // bear 1/3

            var reduced = new QuestDefinition(
                Quest, new[] { new QuestObjective(QuestObjectiveType.Kill, Wolf, 5) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            h.Host.Reload(new[] { reduced });

            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Single(log.ObjectiveCounts);
            Assert.Equal(2, log.ObjectiveCounts[0]); // 匹配 (Kill, Wolf) 保留旧计数 2
            Assert.Equal(QuestState.Active, log.State);
        }

        [Fact]
        public void Reload_ObjectivesReordered_CountsFollowByTypeAndTargetRef()
        {
            var twoObjectives = new QuestDefinition(
                Quest, new[]
                {
                    new QuestObjective(QuestObjectiveType.Kill, Wolf, 5),
                    new QuestObjective(QuestObjectiveType.Kill, Bear, 3),
                },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { twoObjectives });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 4); // wolf 4/5
            h.Host.UpdateProgress(Player, Quest, 1, 2); // bear 2/3

            // 顺序颠倒：新目标 0 是 Bear，新目标 1 是 Wolf。
            var reordered = new QuestDefinition(
                Quest, new[]
                {
                    new QuestObjective(QuestObjectiveType.Kill, Bear, 3),
                    new QuestObjective(QuestObjectiveType.Kill, Wolf, 5),
                },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            h.Host.Reload(new[] { reordered });

            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Equal(2, log.ObjectiveCounts[0]); // 新索引 0（Bear）继承旧 Bear 计数 2
            Assert.Equal(4, log.ObjectiveCounts[1]); // 新索引 1（Wolf）继承旧 Wolf 计数 4
        }

        [Fact]
        public void Reload_IncompatibleChange_ObjectivesCompleteFallsBackToActive()
        {
            var h = new Harness(new[] { OneKillObjective(count: 1) });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 1); // 1/1 -> ObjectivesComplete
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, Quest));

            // 同一目标类型/targetRef，但 Count 提高到 5：迁移保留计数 1，clamp 后仍是 1，
            // 但相对新的 5 已不再"全部达标"，应回落 Active（ApplyObjectiveCount 同款双向同步）。
            var raisedCount = OneKillObjective(count: 5);
            h.Host.Reload(new[] { raisedCount });

            Assert.Equal(QuestState.Active, h.Host.GetState(Player, Quest));
            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Equal(1, log.ObjectiveCounts[0]);
        }

        [Fact]
        public void Reload_ObjectiveCountLowered_ActiveBecomesObjectivesCompleteAndFiresEvent()
        {
            var h = new Harness(new[] { OneKillObjective(count: 5) });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 3); // 3/5，仍 Active

            var loweredCount = OneKillObjective(count: 3);
            h.Host.Reload(new[] { loweredCount });

            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, Quest));
            Assert.Single(h.PublishedOf<QuestCompletedEvent>());
        }

        [Fact]
        public void Reload_NewUnmatchedCollectObjective_RecomputesFromCurrentInventory()
        {
            var h = new Harness(new[] { OneKillObjective() });
            h.Host.Accept(Player, Quest);
            var tokenId = new Id("item.cov_token");
            h.Inventory.AddItem(Player, tokenId, 2);

            var withCollect = new QuestDefinition(
                Quest, new[]
                {
                    new QuestObjective(QuestObjectiveType.Kill, Wolf, 5),
                    new QuestObjective(QuestObjectiveType.Collect, tokenId, 5, consumeOnProgress: false),
                },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            h.Host.Reload(new[] { withCollect });

            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Equal(2, log.ObjectiveCounts[1]); // 新出现的非消耗 collect 目标按当前持有量 2 重算
        }

        [Fact]
        public void Reload_NewUnmatchedNonCollectObjective_StartsAtZero()
        {
            var h = new Harness(new[] { OneKillObjective() });
            h.Host.Accept(Player, Quest);

            var withExtraKill = new QuestDefinition(
                Quest, new[]
                {
                    new QuestObjective(QuestObjectiveType.Kill, Wolf, 5),
                    new QuestObjective(QuestObjectiveType.Kill, Bear, 2),
                },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            h.Host.Reload(new[] { withExtraKill });

            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Equal(0, log.ObjectiveCounts[1]);
        }

        [Fact]
        public void Reload_QuestDefinitionRemovedWhileActive_ProgressUntouchedNoException()
        {
            var other = new QuestDefinition(
                new Id("quest.cov_other"), new[] { new QuestObjective(QuestObjectiveType.Kill, Bear, 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { OneKillObjective(), other });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 2);

            // 新定义集合里彻底不再包含 Quest——判断记录 5：保持现状，不抛异常。
            var reloadEx = Record.Exception(() => h.Host.Reload(new[] { other }));
            Assert.Null(reloadEx);

            // GetState 仍按既有 RequireDef 契约行为（未登记 id 抛异常），本次 CORE114-01 不改变该契约。
            Assert.Throws<System.ArgumentException>(() => h.Host.GetState(Player, Quest));
        }

        [Fact]
        public void Reload_FailedQuestProgressArray_NotTouchedAndNoException()
        {
            var h = new Harness(new[] { OneKillObjective() }, new QuestOptions { AllowFail = true });
            h.Host.Accept(Player, Quest);
            h.Host.Fail(Player, Quest, "test");
            Assert.Equal(QuestState.Failed, h.Host.GetState(Player, Quest));

            var reloaded = new QuestDefinition(
                Quest, new[]
                {
                    new QuestObjective(QuestObjectiveType.Kill, Wolf, 5),
                    new QuestObjective(QuestObjectiveType.Kill, Bear, 1),
                },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var ex = Record.Exception(() => h.Host.Reload(new[] { reloaded }));
            Assert.Null(ex);
            Assert.Equal(QuestState.Failed, h.Host.GetState(Player, Quest));
        }
    }
}
