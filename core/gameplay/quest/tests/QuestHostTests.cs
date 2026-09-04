using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Common;
using Core.Gameplay.Quest;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>构造期需要 <see cref="IExprHostFactory"/>、而该工厂内部的
    /// <see cref="QuestExprGroupProvider"/> 又需要已构造好的 <see cref="QuestHost"/>（互相依赖）：
    /// 用一个可延迟赋值的转发实现打破构造顺序上的循环（先构造 <see cref="QuestHost"/>，
    /// 拿到它之后再补上真正的内层工厂）。</summary>
    internal sealed class DeferredExprHostFactory : IExprHostFactory
    {
        public IExprHostFactory? Inner;

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent)
        {
            if (Inner == null) throw new InvalidOperationException("DeferredExprHostFactory.Inner 尚未赋值");
            return Inner.CreateFor(selfId, targetId, triggeringEvent);
        }
    }

    /// <summary>单个测试用例的最小装配：构造一个可用的 <see cref="QuestHost"/> 及其全部依赖假实现，
    /// 并把总线上派发的全部事件记录下来供断言。</summary>
    internal sealed class Harness
    {
        public readonly FakeInventoryHost Inventory = new FakeInventoryHost();
        public readonly FakeUnitAccess Units = new FakeUnitAccess();
        public readonly FakeRewardDispatcher Rewards = new FakeRewardDispatcher();
        public readonly Tests.Gameplay.Common.FakeProgressionHost Progression = new Tests.Gameplay.Common.FakeProgressionHost();
        public readonly IEventBus Bus = TestSupport.NewEventBus();
        public readonly List<IEvent> Published = new List<IEvent>();
        public readonly QuestHost Host;

        public long CurrentDay;
        public readonly Dictionary<Id, Id> Owners = new Dictionary<Id, Id>();
        public readonly Dictionary<Id, Id> GobjTemplates = new Dictionary<Id, Id>();

        public Harness(IEnumerable<QuestDefinition> definitions, QuestOptions? options = null)
        {
            foreach (var key in new[]
                     {
                         QuestEventKeys.Accepted, QuestEventKeys.ObjectiveProgress, QuestEventKeys.Completed,
                         QuestEventKeys.TurnedIn, QuestEventKeys.Failed,
                     })
            {
                Bus.Subscribe(key, evt => Published.Add(evt));
            }

            var deferred = new DeferredExprHostFactory();
            Host = new QuestHost(
                definitions, Bus, deferred, Rewards, Inventory, Units, options,
                ownerResolver: killerId => Owners.TryGetValue(killerId, out var owner) ? (Id?)owner : null,
                gobjTemplateResolver: gobjInstanceId => GobjTemplates.TryGetValue(gobjInstanceId, out var t) ? (Id?)t : null,
                dayProvider: () => CurrentDay);

            var questGroup = new QuestExprGroupProvider(Host, () => TestSupport.Player);
            var playerGroup = new PlayerExprGroupProvider(Inventory, Progression, () => TestSupport.Player);
            deferred.Inner = new TestExprHostFactory(questGroup, playerGroup);
        }

        public IReadOnlyList<T> PublishedOf<T>() where T : IEvent => Published.OfType<T>().ToArray();
    }

    public class QuestHostTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly IExprSchema Schema = QuestExprSchemaEntries.BuildParsingSchema();

        private static QuestDefinition SimpleKillQuest(Id id, Id creatureTemplate, int count = 1, Id? exclusiveGroup = null, ExprNode? prerequisite = null)
        {
            return new QuestDefinition(
                id,
                new[] { new QuestObjective(QuestObjectiveType.Kill, creatureTemplate, count) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None,
                prerequisite: prerequisite, exclusiveGroup: exclusiveGroup);
        }

        // ---------------------------------------------------------------
        // Accept / 非法转移
        // ---------------------------------------------------------------

        [Fact]
        public void Accept_WhenAvailable_TransitionsToActiveAndFiresAcceptedEvent()
        {
            var quest = SimpleKillQuest(new Id("quest.sample_kill_wolves"), new Id("creature.wolf"), 3);
            var h = new Harness(new[] { quest });

            Assert.Equal(QuestState.Available, h.Host.GetState(Player, quest.Id));
            var accepted = h.Host.Accept(Player, quest.Id);

            Assert.True(accepted);
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, quest.Id));
            var evt = Assert.Single(h.PublishedOf<QuestAcceptedEvent>());
            Assert.Equal(quest.Id, evt.QuestId);
            Assert.Equal(Player, evt.UnitId);
        }

        [Fact]
        public void Accept_WhenNotAvailable_ReturnsFalseAndDoesNotFireEvent()
        {
            var quest = SimpleKillQuest(new Id("quest.sample_kill_wolves"), new Id("creature.wolf"));
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, quest.Id); // 已 Active

            var acceptedAgain = h.Host.Accept(Player, quest.Id);

            Assert.False(acceptedAgain);
            Assert.Single(h.PublishedOf<QuestAcceptedEvent>());
        }

        [Fact]
        public void UpdateProgress_WhenNotActive_ReturnsFalse()
        {
            var quest = SimpleKillQuest(new Id("quest.sample_kill_wolves"), new Id("creature.wolf"), 3);
            var h = new Harness(new[] { quest });
            // 未接取

            var updated = h.Host.UpdateProgress(Player, quest.Id, 0, 1);

            Assert.False(updated);
        }

        [Fact]
        public void TurnIn_WhenNotObjectivesComplete_ReturnsFalse()
        {
            var quest = SimpleKillQuest(new Id("quest.sample_kill_wolves"), new Id("creature.wolf"), 3);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, quest.Id);

            var turnedIn = h.Host.TurnIn(Player, quest.Id);

            Assert.False(turnedIn);
            Assert.Empty(h.PublishedOf<QuestTurnedInEvent>());
        }

        // ---------------------------------------------------------------
        // 四个合法转移的完整链路（accept → progress → objectives_complete → turn_in）
        // ---------------------------------------------------------------

        [Fact]
        public void FullLifecycle_Kill_AcceptProgressCompleteTurnIn_AllTransitionsFireEvents()
        {
            var questId = new Id("quest.sample_kill_wolves");
            var quest = SimpleKillQuest(questId, new Id("creature.wolf"), 2);
            var h = new Harness(new[] { quest });

            Assert.True(h.Host.Accept(Player, questId));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));

            h.Units.SetTemplate(new Id("unit.wolf_1"), new Id("creature.wolf"));
            h.Bus.PublishImmediate(new UnitDiedEvent(new Id("unit.wolf_1"), Player));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));
            Assert.Single(h.PublishedOf<QuestObjectiveProgressEvent>());

            h.Units.SetTemplate(new Id("unit.wolf_2"), new Id("creature.wolf"));
            h.Bus.PublishImmediate(new UnitDiedEvent(new Id("unit.wolf_2"), Player));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
            Assert.Single(h.PublishedOf<QuestCompletedEvent>());

            Assert.True(h.Host.TurnIn(Player, questId));
            Assert.Equal(QuestState.TurnedIn, h.Host.GetState(Player, questId));
            Assert.Single(h.PublishedOf<QuestTurnedInEvent>());
            Assert.Single(h.Rewards.Calls);
            Assert.Equal(questId, h.Rewards.Calls[0].SourceId);
        }

        // ---------------------------------------------------------------
        // 八种目标类型各一
        // ---------------------------------------------------------------

        [Fact]
        public void Objective_Kill_OnlyCreditsQuestHolderOrOwnedSummon()
        {
            var questId = new Id("quest.sample_kill_via_pet");
            var quest = SimpleKillQuest(questId, new Id("creature.wolf"));
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);
            h.Units.SetTemplate(new Id("unit.wolf_1"), new Id("creature.wolf"));

            // 非任务持有者且无归属映射的击杀者：不计入
            h.Bus.PublishImmediate(new UnitDiedEvent(new Id("unit.wolf_1"), new Id("unit.stranger")));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));

            // 该击杀者是玩家召唤物（ownerResolver 解析回玩家）：计入
            h.Units.SetTemplate(new Id("unit.wolf_2"), new Id("creature.wolf"));
            h.Owners[new Id("unit.pet_wolf")] = Player;
            h.Bus.PublishImmediate(new UnitDiedEvent(new Id("unit.wolf_2"), new Id("unit.pet_wolf")));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
        }

        [Fact]
        public void Objective_Collect_NonConsume_ProgressTracksLiveInventoryCount()
        {
            var questId = new Id("quest.sample_collect_flowers");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, new Id("item.flower"), 3, consumeOnProgress: false) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            var instanceId = h.Inventory.AddItemForTest(Player, new Id("item.flower"), 2);
            h.Bus.PublishImmediate(new ItemAddedEvent(Player, instanceId, new Id("item.flower"), 2));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));
            Assert.Equal(2, h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questId)).ObjectiveCounts[0]);

            h.Inventory.AddItem(Player, new Id("item.flower"), 1);
            h.Bus.PublishImmediate(new ItemAddedEvent(Player, instanceId, new Id("item.flower"), 1));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));

            // 交付时 non-consume collect 目标应扣除对应数量的物品
            Assert.True(h.Host.TurnIn(Player, questId));
            Assert.Equal(0, h.Inventory.CountOf(Player, new Id("item.flower")));
        }

        [Fact]
        public void Objective_Collect_NonConsume_RemovingItemsDecreasesProgress()
        {
            var questId = new Id("quest.sample_collect_flowers_remove");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, new Id("item.flower"), 3, consumeOnProgress: false) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            var instanceId = h.Inventory.AddItemForTest(Player, new Id("item.flower"), 3);
            h.Bus.PublishImmediate(new ItemAddedEvent(Player, instanceId, new Id("item.flower"), 3));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));

            h.Inventory.RemoveItem(Player, instanceId, 1);
            h.Bus.PublishImmediate(new ItemRemovedEvent(Player, instanceId, 1, "test"));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));
            Assert.Equal(2, h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questId)).ObjectiveCounts[0]);
        }

        [Fact]
        public void Objective_Collect_ConsumeOnProgress_RemovesItemsImmediatelyAndTracksProgress()
        {
            var questId = new Id("quest.sample_collect_consume");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, new Id("item.herb"), 2, consumeOnProgress: true) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            var instanceId = h.Inventory.AddItemForTest(Player, new Id("item.herb"), 5);
            h.Bus.PublishImmediate(new ItemAddedEvent(Player, instanceId, new Id("item.herb"), 5));

            // 只消耗到达标为止（目标 2），多余的 3 个保留在背包
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
            Assert.Equal(3, h.Inventory.CountOf(Player, new Id("item.herb")));
        }

        [Fact]
        public void Objective_Interact_UsesGobjTemplateResolver()
        {
            var questId = new Id("quest.sample_interact_chest");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Interact, new Id("gobj.chest"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);
            h.GobjTemplates[new Id("gobj.instance_1")] = new Id("gobj.chest");

            h.Bus.PublishImmediate(new GobjInteractedEvent(Player, new Id("gobj.instance_1")));

            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
        }

        [Fact]
        public void Objective_Explore_MatchesAreaTriggerEnteredGenericEvent()
        {
            var questId = new Id("quest.sample_explore_area");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Explore, new Id("area.grove"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            h.Bus.PublishImmediate(new GenericEvent(new Id("area.trigger_entered"), new Dictionary<string, object?>
            {
                // GenericEvent 不实现 IExprReadableEvent，本用例改用强类型事件验证同一路径。
            }));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId)); // GenericEvent 未携带字段，不推进

            h.Bus.PublishImmediate(new FakeAreaTriggerEnteredEvent(new Id("area.grove"), Player));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
        }

        [Fact]
        public void Objective_Cast_MatchesSkillCastSuccessEvent()
        {
            var questId = new Id("quest.sample_cast_fireball");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Cast, new Id("skill.fireball"), 2) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            h.Bus.PublishImmediate(new SkillCastSuccessEvent(Player, new Id("skill.fireball"), Array.Empty<Id>()));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));
            h.Bus.PublishImmediate(new SkillCastSuccessEvent(Player, new Id("skill.fireball"), Array.Empty<Id>()));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
        }

        [Fact]
        public void Objective_Talk_MatchesGossipOpenedEvent()
        {
            var questId = new Id("quest.sample_talk_gossip");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Talk, new Id("dialog.sample_menu"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            h.Bus.PublishImmediate(new Core.Gameplay.Dialog.GossipOpenedEvent(Player, new Id("unit.npc_1"), new Id("dialog.sample_menu")));

            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
        }

        [Fact]
        public void Objective_Talk_MatchesStoryNodeEnteredEvent()
        {
            var questId = new Id("quest.sample_talk_story");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Talk, new Id("dialog.sample_node"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            h.Bus.PublishImmediate(new Core.Gameplay.Dialog.StoryNodeEnteredEvent(Player, new Id("dialog.sample_tree"), new Id("dialog.sample_node")));

            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
        }

        [Fact]
        public void Objective_Escort_HasNoAutomaticEvent_OnlyManualUpdateProgress()
        {
            var questId = new Id("quest.sample_escort");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Escort, new Id("creature.villager"), 1, escortRouteRef: new Id("area.escort_route")) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            // 没有任何自动事件能推进该目标（尝试发布一个不相关事件确认不受影响）
            h.Units.SetTemplate(new Id("unit.random"), new Id("creature.villager"));
            h.Bus.PublishImmediate(new UnitDiedEvent(new Id("unit.random"), Player));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));

            Assert.True(h.Host.UpdateProgress(Player, questId, 0, 1));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
        }

        [Fact]
        public void Objective_Event_WithoutFilter_AnyOccurrenceCounts()
        {
            var questId = new Id("quest.sample_event_any");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Event, new Id("test.custom_event"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            h.Bus.PublishImmediate(new FakeCustomEvent(Player, 1));

            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
        }

        [Fact]
        public void Objective_Event_WithFilter_OnlyMatchingOccurrencesCount()
        {
            var questId = new Id("quest.sample_event_filtered");
            var filter = ExprParser.Parse("event.amount >= 10", Schema);
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Event, new Id("test.custom_event"), 1, eventFilter: filter) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            h.Bus.PublishImmediate(new FakeCustomEvent(Player, 3));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));

            h.Bus.PublishImmediate(new FakeCustomEvent(Player, 10));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
        }

        // ---------------------------------------------------------------
        // prerequisite / exclusive_group
        // ---------------------------------------------------------------

        [Fact]
        public void Prerequisite_UsingQuestIsCompleted_GatesAvailability()
        {
            var firstId = new Id("quest.sample_kill_wolves");
            var first = SimpleKillQuest(firstId, new Id("creature.wolf"), 1);

            var prereq = ExprParser.Parse("quest.is_completed(quest.sample_kill_wolves)", Schema);
            var secondId = new Id("quest.sample_deliver_letter");
            var second = new QuestDefinition(
                secondId,
                new[] { new QuestObjective(QuestObjectiveType.Talk, new Id("dialog.sample_menu"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None,
                prerequisite: prereq);

            var h = new Harness(new[] { first, second });

            Assert.Equal(QuestState.Unavailable, h.Host.GetState(Player, secondId));

            h.Host.Accept(Player, firstId);
            h.Units.SetTemplate(new Id("unit.wolf_1"), new Id("creature.wolf"));
            h.Bus.PublishImmediate(new UnitDiedEvent(new Id("unit.wolf_1"), Player));
            h.Host.TurnIn(Player, firstId);

            Assert.Equal(QuestState.Available, h.Host.GetState(Player, secondId));
        }

        [Fact]
        public void ExclusiveGroup_ActiveQuestBlocksAcceptingSibling()
        {
            var group = new Id("quest.grp.starter");
            var a = SimpleKillQuest(new Id("quest.sample_exclusive_a"), new Id("creature.wolf"), exclusiveGroup: group);
            var b = SimpleKillQuest(new Id("quest.sample_exclusive_b"), new Id("creature.bear"), exclusiveGroup: group);
            var h = new Harness(new[] { a, b });

            Assert.True(h.Host.Accept(Player, a.Id));
            Assert.False(h.Host.Accept(Player, b.Id));
            Assert.Equal(QuestState.Available, h.Host.GetState(Player, b.Id));
        }

        // ---------------------------------------------------------------
        // repeatable: daily / unlimited
        // ---------------------------------------------------------------

        [Fact]
        public void Repeatable_Daily_CannotReacceptSameDayButCanNextDay()
        {
            var questId = new Id("quest.sample_daily");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Escort, new Id("creature.villager"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.Daily);
            var h = new Harness(new[] { quest });
            h.CurrentDay = 1;

            h.Host.Accept(Player, questId);
            h.Host.UpdateProgress(Player, questId, 0, 1);
            Assert.True(h.Host.TurnIn(Player, questId));

            Assert.Equal(QuestState.Unavailable, h.Host.GetState(Player, questId));
            Assert.False(h.Host.Accept(Player, questId));

            h.CurrentDay = 2;
            Assert.Equal(QuestState.Available, h.Host.GetState(Player, questId));
            Assert.True(h.Host.Accept(Player, questId));
        }

        [Fact]
        public void Repeatable_Unlimited_ImmediatelyAvailableAgainAfterTurnIn()
        {
            var questId = new Id("quest.sample_unlimited");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Escort, new Id("creature.villager"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.Unlimited);
            var h = new Harness(new[] { quest });

            h.Host.Accept(Player, questId);
            h.Host.UpdateProgress(Player, questId, 0, 1);
            h.Host.TurnIn(Player, questId);

            Assert.Equal(QuestState.Available, h.Host.GetState(Player, questId));
            Assert.True(h.Host.Accept(Player, questId));
        }

        [Fact]
        public void Repeatable_None_StaysTurnedInForever()
        {
            var questId = new Id("quest.sample_once");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Escort, new Id("creature.villager"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });

            h.Host.Accept(Player, questId);
            h.Host.UpdateProgress(Player, questId, 0, 1);
            h.Host.TurnIn(Player, questId);

            Assert.Equal(QuestState.TurnedIn, h.Host.GetState(Player, questId));
            Assert.False(h.Host.Accept(Player, questId));
        }

        // ---------------------------------------------------------------
        // auto start / turn-in
        // ---------------------------------------------------------------

        [Fact]
        public void Update_AutoStartMethod_AutomaticallyAccepts()
        {
            var questId = new Id("quest.sample_auto_start");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Escort, new Id("creature.villager"), 1) },
                QuestStartMethod.Auto, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });

            Assert.Equal(QuestState.Available, h.Host.GetState(Player, questId));
            h.Host.Update(Player);
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));
        }

        [Fact]
        public void Update_AutoTurnInMethod_AutomaticallyTurnsInWhenObjectivesComplete()
        {
            var questId = new Id("quest.sample_auto_turnin");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Escort, new Id("creature.villager"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.Auto, QuestRepeatable.None);
            var h = new Harness(new[] { quest });

            h.Host.Accept(Player, questId);
            h.Host.UpdateProgress(Player, questId, 0, 1);
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));

            h.Host.Update(Player);
            Assert.Equal(QuestState.TurnedIn, h.Host.GetState(Player, questId));
        }

        // ---------------------------------------------------------------
        // Fail
        // ---------------------------------------------------------------

        [Fact]
        public void Fail_WhenActive_TransitionsToFailedAndFiresEvent()
        {
            var questId = new Id("quest.sample_escort_fail");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Escort, new Id("creature.villager"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            Assert.True(h.Host.Fail(Player, questId, "escort_target_died"));

            Assert.Equal(QuestState.Failed, h.Host.GetState(Player, questId));
            var evt = Assert.Single(h.PublishedOf<QuestFailedEvent>());
            Assert.Equal("escort_target_died", evt.Reason);
        }

        [Fact]
        public void Fail_WhenAllowFailDisabled_ReturnsFalse()
        {
            var questId = new Id("quest.sample_escort_no_fail");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Escort, new Id("creature.villager"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest }, new QuestOptions { AllowFail = false });
            h.Host.Accept(Player, questId);

            Assert.False(h.Host.Fail(Player, questId, "any"));
            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));
        }

        // ---------------------------------------------------------------
        // GetLog / GetActiveObjectives / 持久化往返
        // ---------------------------------------------------------------

        [Fact]
        public void GetActiveObjectives_OnlyListsIncompleteObjectivesOfActiveQuests()
        {
            var questId = new Id("quest.sample_kill_wolves_track");
            var quest = SimpleKillQuest(questId, new Id("creature.wolf"), 2);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            var active = h.Host.GetActiveObjectives(Player);
            var entry = Assert.Single(active);
            Assert.Equal(questId, entry.QuestId);
            Assert.Equal(new Id("creature.wolf"), entry.TargetRef);

            h.Units.SetTemplate(new Id("unit.wolf_1"), new Id("creature.wolf"));
            h.Bus.PublishImmediate(new UnitDiedEvent(new Id("unit.wolf_1"), Player));
            h.Units.SetTemplate(new Id("unit.wolf_2"), new Id("creature.wolf"));
            h.Bus.PublishImmediate(new UnitDiedEvent(new Id("unit.wolf_2"), Player));

            Assert.Empty(h.Host.GetActiveObjectives(Player));
        }

        [Fact]
        public void Persistable_SaveThenLoad_RestoresActiveStateAndObjectiveCounts()
        {
            var questId = new Id("quest.sample_kill_wolves_persist");
            var quest = SimpleKillQuest(questId, new Id("creature.wolf"), 3);
            var h1 = new Harness(new[] { quest });
            h1.Host.Accept(Player, questId);
            h1.Units.SetTemplate(new Id("unit.wolf_1"), new Id("creature.wolf"));
            h1.Bus.PublishImmediate(new UnitDiedEvent(new Id("unit.wolf_1"), Player));

            var persistable1 = new QuestPersistable(h1.Host, () => Player);
            var saved = persistable1.Save();

            var h2 = new Harness(new[] { quest });
            var persistable2 = new QuestPersistable(h2.Host, () => Player);
            persistable2.Load(saved);

            Assert.Equal(QuestState.Active, h2.Host.GetState(Player, questId));
            Assert.Equal(1, h2.Host.GetLog(Player).Single(p => p.QuestId.Equals(questId)).ObjectiveCounts[0]);
        }

        [Fact]
        public void RewardDispatch_TurnIn_PassesQuestIdAsSourceId()
        {
            var questId = new Id("quest.sample_reward_source");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Escort, new Id("creature.villager"), 1) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None,
                rewards: new RewardBundle(Array.Empty<ItemStack>(), 50, Array.Empty<(Id, long)>(), Array.Empty<Id>(), Array.Empty<(Id, ExprValue)>(), 0));
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);
            h.Host.UpdateProgress(Player, questId, 0, 1);

            h.Host.TurnIn(Player, questId);

            var call = Assert.Single(h.Rewards.Calls);
            Assert.Equal(Player, call.UnitId);
            Assert.Equal(questId, call.SourceId);
            Assert.Equal(50, call.Bundle.Xp);
        }

        // ---------------------------------------------------------------
        // 事件辅助类型
        // ---------------------------------------------------------------

        /// <summary>模拟另一 agent 建设的 <c>core/gameplay/area_trigger</c> 模块会发布的
        /// <c>area.trigger_entered</c> 事件（本模块不对其做编译期依赖，见 <c>QuestHost</c>
        /// 类型顶部判断记录），只需满足 <see cref="IExprReadableEvent"/> 暴露 triggerId/unitId。</summary>
        private sealed class FakeAreaTriggerEnteredEvent : IEvent, IExprReadableEvent
        {
            public Id Key => new Id("area.trigger_entered");
            public Id TriggerId { get; }
            public Id UnitId { get; }

            public FakeAreaTriggerEnteredEvent(Id triggerId, Id unitId)
            {
                TriggerId = triggerId;
                UnitId = unitId;
            }

            public bool TryGetField(string name, out ExprValue value)
            {
                switch (name)
                {
                    case "triggerId": value = ExprValue.OfId(TriggerId); return true;
                    case "unitId": value = ExprValue.OfId(UnitId); return true;
                    default: value = default; return false;
                }
            }
        }

        private sealed class FakeCustomEvent : IEvent, IExprReadableEvent
        {
            public Id Key => new Id("test.custom_event");
            public Id UnitId { get; }
            public int Amount { get; }

            public FakeCustomEvent(Id unitId, int amount)
            {
                UnitId = unitId;
                Amount = amount;
            }

            public bool TryGetField(string name, out ExprValue value)
            {
                switch (name)
                {
                    case "unitId": value = ExprValue.OfId(UnitId); return true;
                    case "amount": value = ExprValue.OfInt(Amount); return true;
                    default: value = default; return false;
                }
            }
        }
    }
}
