using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Gameplay.Quest;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// CORE-118-QUEST 根治验收（外部审计 audit-d6fda65-20260911，见 <c>QuestHost</c>
    /// <c>MigrateProgressAfterReload</c>/<c>TryGetDefinition</c> 判断记录）：接取任务→热重载删除该
    /// 定义→活跃跟踪查询/领域事件消费不能抛异常、必须保留进度；再恢复同 id（目标增/减/重排）后，
    /// 对合法索引的 <see cref="IQuestHost.UpdateProgress"/> 不能越界。真实探针复现见
    /// <c>D:\workespace\ws-game-artifacts\audit-d6fda65-20260911\core\probe\QuestProbe.cs</c>：
    /// 删除后 <c>GetActiveObjectives</c> 曾抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>，
    /// 恢复为 2 目标后对索引 1 的 <c>UpdateProgress</c> 曾抛 <see cref="System.IndexOutOfRangeException"/>。
    /// </summary>
    public sealed class CORE118_QuestHostDefinitionDeletionTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly Id Quest = new Id("quest.core118_reload");
        private static readonly Id OtherQuest = new Id("quest.core118_other");
        private static readonly Id Wolf = new Id("creature.core118_wolf");
        private static readonly Id Bear = new Id("creature.core118_bear");

        private static QuestDefinition OneKillObjective(int count = 5) => new QuestDefinition(
            Quest, new[] { new QuestObjective(QuestObjectiveType.Kill, Wolf, count) },
            QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);

        private static QuestDefinition TwoKillObjectives(int wolfCount = 5, int bearCount = 3) => new QuestDefinition(
            Quest, new[]
            {
                new QuestObjective(QuestObjectiveType.Kill, Wolf, wolfCount),
                new QuestObjective(QuestObjectiveType.Kill, Bear, bearCount),
            },
            QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);

        private static QuestDefinition OtherDef() => new QuestDefinition(
            OtherQuest, new[] { new QuestObjective(QuestObjectiveType.Kill, Bear, 1) },
            QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);

        // -------------------------------------------------------------
        // 删除定义后：单位级跟踪查询/领域事件消费者不抛异常，进度原样保留
        // -------------------------------------------------------------

        [Fact]
        public void DefinitionRemoved_GetActiveObjectivesForUnit_NoExceptionAndProgressPreserved()
        {
            var h = new Harness(new[] { OneKillObjective() });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 2);

            var ex = Record.Exception(() => h.Host.Reload(System.Array.Empty<QuestDefinition>()));
            Assert.Null(ex);

            // 真实探针复现：此调用未传任何已删除 id，仅按 unitId 枚举——此前直接
            // _definitions[questId] 索引，抛 KeyNotFoundException。
            var activeEx = Record.Exception(() => h.Host.GetActiveObjectives(Player));
            Assert.Null(activeEx);
            Assert.Empty(h.Host.GetActiveObjectives(Player)); // 定义缺失，本次枚举跳过，不是"仍然可跟踪"

            // 进度本身保留（保持现状，见 MigrateProgressAfterReload 判断记录 5）。
            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Equal(QuestState.Active, log.State);
            Assert.Equal(2, log.ObjectiveCounts[0]);
        }

        [Fact]
        public void DefinitionRemoved_OtherActiveQuestStillEnumerated()
        {
            var h = new Harness(new[] { OneKillObjective(), OtherDef() });
            h.Host.Accept(Player, Quest);
            h.Host.Accept(Player, OtherQuest);
            h.Host.Reload(new[] { OtherDef() }); // 只删除 Quest，OtherQuest 仍在

            var active = h.Host.GetActiveObjectives(Player);
            Assert.Single(active);
            Assert.Equal(OtherQuest, active[0].QuestId);
        }

        [Fact]
        public void DefinitionRemoved_UnitDiedEventForFormerKillTarget_NoExceptionAndProgressUnchanged()
        {
            var h = new Harness(new[] { OneKillObjective() });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 1);
            h.Host.Reload(System.Array.Empty<QuestDefinition>());

            // 玩家自己击杀了一只模板与旧目标一致的生物——删除定义后，HandleUnitDied 此前对
            // _definitions[key.QuestId] 直接索引会抛异常；根治后应安全跳过，不推进、不抛。
            var deadUnit = new Id("unit.core118_wolf_instance");
            h.Units.SetTemplate(deadUnit, Wolf);
            var ex = Record.Exception(() => h.Bus.PublishImmediate(new UnitDiedEvent(deadUnit, Player)));
            Assert.Null(ex);

            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Equal(1, log.ObjectiveCounts[0]); // 未被误推进
        }

        [Fact]
        public void DefinitionRemoved_ItemAddedEventForFormerCollectTarget_NoException()
        {
            var tokenId = new Id("item.core118_token");
            var withCollect = new QuestDefinition(
                Quest, new[] { new QuestObjective(QuestObjectiveType.Collect, tokenId, 3, consumeOnProgress: false) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { withCollect });
            h.Host.Accept(Player, Quest);
            h.Host.Reload(System.Array.Empty<QuestDefinition>());

            h.Inventory.AddItem(Player, tokenId, 1);
            var ex = Record.Exception(() => h.Bus.PublishImmediate(
                new ItemAddedEvent(Player, new Id("item.instance_x"), tokenId, 1)));
            Assert.Null(ex);
        }

        // -------------------------------------------------------------
        // 恢复同 id：目标数组必须迁移到新形状，不越界
        // -------------------------------------------------------------

        [Fact]
        public void DefinitionRemovedThenRestoredWithMoreObjectives_UpdateNewObjectiveIndexNoException()
        {
            var h = new Harness(new[] { OneKillObjective() });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 2);
            h.Host.Reload(System.Array.Empty<QuestDefinition>());

            // 恢复同 id，但目标数组从 1 条扩到 2 条——真实探针复现：此前 ObjectiveCounts 因
            // "找不到旧定义" 被跳过迁移，仍是长度 1 的数组，对新索引 1 的 UpdateProgress 直接
            // IndexOutOfRangeException。
            h.Host.Reload(new[] { TwoKillObjectives() });

            var ex = Record.Exception(() => h.Host.UpdateProgress(Player, Quest, 1, 1));
            Assert.Null(ex);

            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Equal(2, log.ObjectiveCounts.Count);
            Assert.Equal(2, log.ObjectiveCounts[0]); // 旧 Wolf 计数按 (Kill, Wolf) 迁移保留
            Assert.Equal(1, log.ObjectiveCounts[1]); // 新目标 Bear 从 0 起、刚推进的 1
        }

        [Fact]
        public void DefinitionRemovedThenRestoredWithFewerObjectives_CountsClampedNoException()
        {
            var h = new Harness(new[] { TwoKillObjectives() });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 3); // wolf 3/5
            h.Host.UpdateProgress(Player, Quest, 1, 2); // bear 2/3
            h.Host.Reload(System.Array.Empty<QuestDefinition>());

            h.Host.Reload(new[] { OneKillObjective() }); // 恢复为只剩 1 条 Wolf 目标

            var ex = Record.Exception(() => h.Host.UpdateProgress(Player, Quest, 0, 1));
            Assert.Null(ex);
            Assert.False(h.Host.UpdateProgress(Player, Quest, 1, 1)); // 索引 1 已不存在，返回 false 而非异常

            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Single(log.ObjectiveCounts);
            Assert.Equal(4, log.ObjectiveCounts[0]); // 3 迁移保留 + 刚推进的 1
        }

        [Fact]
        public void DefinitionRemovedThenRestoredWithReorderedObjectives_CountsFollowByTypeAndTargetRef()
        {
            var h = new Harness(new[] { TwoKillObjectives() });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 4); // wolf 4/5
            h.Host.UpdateProgress(Player, Quest, 1, 1); // bear 1/3
            h.Host.Reload(System.Array.Empty<QuestDefinition>());

            var reordered = new QuestDefinition(
                Quest, new[]
                {
                    new QuestObjective(QuestObjectiveType.Kill, Bear, 3),
                    new QuestObjective(QuestObjectiveType.Kill, Wolf, 5),
                },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            h.Host.Reload(new[] { reordered });

            var ex = Record.Exception(() => h.Host.UpdateProgress(Player, Quest, 1, 1));
            Assert.Null(ex);

            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Equal(1, log.ObjectiveCounts[0]); // 新索引 0（Bear）继承旧 Bear 计数 1
            Assert.Equal(5, log.ObjectiveCounts[1]); // 新索引 1（Wolf）继承旧 Wolf 计数 4 + 刚推进的 1
        }

        [Fact]
        public void DefinitionRemovedTwiceWithInterveningReload_LastKnownShapeSurvivesGap()
        {
            // 中间插入一次"只包含无关任务"的 Reload——验证迁移锚点不依赖"相邻上一次 Reload 的
            // _definitions 快照"，而是每条进度自带的 LastKnownObjectives，不受中间隔了多少次
            // 删除/恢复影响（见 MigrateProgressAfterReload 判断记录）。
            var h = new Harness(new[] { OneKillObjective(), OtherDef() });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 1);

            h.Host.Reload(new[] { OtherDef() }); // 删除 Quest，只留 OtherQuest
            h.Host.Reload(new[] { OtherDef() }); // 再次 Reload，Quest 仍未恢复（多一次无关调用）
            h.Host.Reload(new[] { TwoKillObjectives(), OtherDef() }); // 恢复 Quest 为 2 目标

            var ex = Record.Exception(() => h.Host.UpdateProgress(Player, Quest, 1, 1));
            Assert.Null(ex);

            var log = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(Quest));
            Assert.Equal(1, log.ObjectiveCounts[0]); // 旧 Wolf 计数保留
            Assert.Equal(1, log.ObjectiveCounts[1]); // 新 Bear 目标推进
        }

        [Fact]
        public void DefinitionRemovedThenRestoredSameShape_ObjectivesCompleteRecomputedNoException()
        {
            var h = new Harness(new[] { OneKillObjective(count: 1) });
            h.Host.Accept(Player, Quest);
            h.Host.UpdateProgress(Player, Quest, 0, 1); // 1/1 -> ObjectivesComplete
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, Quest));

            h.Host.Reload(System.Array.Empty<QuestDefinition>());
            var restoreEx = Record.Exception(() => h.Host.Reload(new[] { OneKillObjective(count: 1) }));
            Assert.Null(restoreEx);

            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, Quest));
        }
    }
}
