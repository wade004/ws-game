using Core.Foundation.Common;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// P2-05 关联根治回归测试（外部审计 audit-c9ff301-20260909）：<see cref="QuestHost"/> 的
    /// <c>_definitions</c> 缓存只在构造期从注入的 <c>IEnumerable&lt;QuestDefinition&gt;</c> 建索引，
    /// 此前没有任何刷新入口。<see cref="QuestHost.Reload"/> 补齐后，resident host 用新的
    /// <c>objectives[].count</c> 判定目标是否达标——玩家已有进度（<c>_progress</c>）不受影响。
    /// </summary>
    public sealed class P2_05_QuestHostReloadTests
    {
        private static readonly Id Player = TestSupport.Player;

        private static QuestDefinition SimpleKillQuest(Id id, Id creatureTemplate, int count) =>
            new QuestDefinition(
                id,
                new[] { new QuestObjective(QuestObjectiveType.Kill, creatureTemplate, count) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);

        [Fact]
        public void P2_05_Reload_PicksUpNewObjectiveCount_ExistingProgressPreserved()
        {
            var questId = new Id("quest.p2_05_kill_wolves");
            var creature = new Id("creature.p2_05_wolf");
            var quest = SimpleKillQuest(questId, creature, count: 1);
            var h = new Harness(new[] { quest });

            Assert.True(h.Host.Accept(Player, questId));
            Assert.True(h.Host.UpdateProgress(Player, questId, 0, 1));
            // count=1：进度打到 1 立刻达标。
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));

            // reload 前先手动把这条已完成的任务交回 Active，方便下面用同一条 questId 验证新定义的
            // count（真实场景下 reload 针对的是另一条尚未接取/或另一个玩家的任务，这里为测试简洁
            // 复用同一条 questId，不影响 Reload 本身的断言）。
            Assert.True(h.Host.TurnIn(Player, questId));
            Assert.Equal(QuestState.TurnedIn, h.Host.GetState(Player, questId));
            var progressBeforeReload = h.Host.GetState(Player, questId);

            var reloadedQuest = SimpleKillQuest(questId, creature, count: 5);
            h.Host.Reload(new[] { reloadedQuest });

            // 已有进度（TurnedIn）不因为 Reload 而重置——只刷新定义表本身，不清空 _progress。
            Assert.Equal(progressBeforeReload, h.Host.GetState(Player, questId));

            // 用另一个玩家验证新定义确实生效：count=5 时打 1 点进度不应达标（FakeUnitAccess.Exists
            // 对任意 unitId 恒返回 true，不需要显式注册）。
            var otherPlayer = new Id("unit.p2_05_other_player");
            Assert.Equal(QuestState.Available, h.Host.GetState(otherPlayer, questId));
            Assert.True(h.Host.Accept(otherPlayer, questId));
            Assert.True(h.Host.UpdateProgress(otherPlayer, questId, 0, 1));
            Assert.Equal(QuestState.Active, h.Host.GetState(otherPlayer, questId));

            Assert.True(h.Host.UpdateProgress(otherPlayer, questId, 0, 4));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(otherPlayer, questId));
        }
    }
}
