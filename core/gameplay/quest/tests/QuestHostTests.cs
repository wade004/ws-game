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

        /// <summary>N02 复现与根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
        /// 满背包（<c>InventoryFullPolicy.Reject</c>，此处用 <see cref="FakeRewardDispatcher.ShouldFail"/>
        /// 模拟其 <c>AddItem</c> 失败的效果）时奖励发放失败——旧实现忽略失败、任务仍置
        /// <see cref="QuestState.TurnedIn"/>，玩家永久丢失奖励且不能重试；修复后交付整体失败于
        /// <see cref="QuestTurnInFailure.InventoryFull"/>，已扣除的 collect 物品回滚放回背包，任务
        /// 保持 <see cref="QuestState.ObjectivesComplete"/>，不发 <c>quest.turned_in</c>。</summary>
        [Fact]
        public void TurnIn_RewardGrantFails_RollsBackConsumedItems_KeepsObjectivesCompleteState()
        {
            var questId = new Id("quest.sample_collect_reward_full");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, new Id("item.flower"), 3, consumeOnProgress: false) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);
            h.Inventory.AddItem(Player, new Id("item.flower"), 3);
            h.Bus.PublishImmediate(new ItemAddedEvent(Player, new Id("item.instance_flower"), new Id("item.flower"), 3));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));

            h.Rewards.ShouldFail = true; // 模拟背包已满、物品奖励无法发放

            var turnedIn = h.Host.TurnIn(Player, questId, out var failure);

            Assert.False(turnedIn);
            Assert.Equal(QuestTurnInFailure.InventoryFull, failure);
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
            Assert.Empty(h.PublishedOf<QuestTurnedInEvent>());
            // 已上交的 3 朵花全部回滚放回背包，没有丢失。
            Assert.Equal(3, h.Inventory.CountOf(Player, new Id("item.flower")));
        }

        /// <summary>N11 复现与根治：两个任务各需要 3 件同种物品，库存只有 3 件；在同一次调用序列里
        /// 连续对两个任务执行 TurnIn（模拟同一 gossip 执行两个交任务动作、事件派发之间没有间隙）。
        /// 旧实现的交付流程只看 <c>ObjectiveCounts</c> 缓存进度，不检查 <c>RemoveCollectedItems</c>
        /// 的实际移除结果——第二个任务在物品已被第一个任务扣光之后仍能"交付成功"。修复后：交付前先
        /// 按实际库存核验，第二个任务应失败于 <see cref="QuestTurnInFailure.InsufficientItems"/>，
        /// 总扣除量不超过库存持有量（3），且第二个任务保持 ObjectivesComplete 可等玩家补齐后再交。</summary>
        [Fact]
        public void TurnIn_TwoQuestsShareSameItem_SecondTurnInFailsWhenInventoryExhaustedByFirst()
        {
            var templateId = new Id("item.shared_ore");
            var questAId = new Id("quest.sample_deliver_ore_a");
            var questBId = new Id("quest.sample_deliver_ore_b");
            var questA = new QuestDefinition(
                questAId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, templateId, 3, consumeOnProgress: false) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var questB = new QuestDefinition(
                questBId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, templateId, 3, consumeOnProgress: false) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { questA, questB });
            h.Host.Accept(Player, questAId);
            h.Host.Accept(Player, questBId);

            // 库存只有 3 件（两个任务各需要 3 件，合计需要 6 件，明显不足），但事件派发前两个任务的
            // ObjectiveCounts 都已经因为"接取时按现有库存直接算出进度"（GP-08）而算作 3/3 达标。
            h.Inventory.AddItem(Player, templateId, 3);
            h.Bus.PublishImmediate(new ItemAddedEvent(Player, new Id("item.instance_ore"), templateId, 3));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questAId));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questBId));

            Assert.True(h.Host.TurnIn(Player, questAId, out var failureA));
            Assert.Equal(QuestTurnInFailure.None, failureA);
            Assert.Equal(QuestState.TurnedIn, h.Host.GetState(Player, questAId));
            Assert.Equal(0, h.Inventory.CountOf(Player, templateId));

            var turnedInB = h.Host.TurnIn(Player, questBId, out var failureB);

            Assert.False(turnedInB);
            Assert.Equal(QuestTurnInFailure.InsufficientItems, failureB);
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questBId));
            Assert.Single(h.Rewards.Calls); // 只有任务 A 发放过奖励，B 从未走到发奖励这一步
            Assert.Equal(0, h.Inventory.CountOf(Player, templateId)); // 没有变成负数/被重复扣除
        }

        /// <summary>
        /// C06 复现与根治，路径二（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：
        /// 单个任务内两个目标共享同一模板（各需要 3 件，合计需要 6 件），库存只有 4 件——步骤 1 预检
        /// 按目标逐个核验，两个目标各自单独核验时库存（4）都 &gt;= 单个目标需求（3），预检"看起来"
        /// 都通过；真正开始移除时，第一个目标顺利扣走 3 件（剩 1），第二个目标只够扣 1 件、不足 3。
        /// 旧实现的 <c>RemoveCollectedItems</c> 边遍历边改——即使最终返回 false，也已经把这仅剩的
        /// 1 件真的移出了背包；<c>TurnIn</c> 步骤 2 的失败回滚只按 <c>removed</c> 列表把"已确认完整
        /// 移除成功"的第一个目标那 3 件放回，第二个目标那 1 件的部分移除从未被记录、也就永远回不来
        /// ——库存净丢失 1 件，且交付仍然按 <see cref="QuestTurnInFailure.InsufficientItems"/> 整体
        /// 失败（"失败了却还丢东西"）。根治后 <see cref="Core.Gameplay.Quest.QuestHost.RemoveCollectedItems"/>
        /// 变成"先核验总量、不够直接不动库存"的原子操作，第二个目标的移除请求会在核验阶段直接失败、
        /// 不产生任何部分移除，交付失败后库存精确回到交付前的 4 件。
        /// </summary>
        [Fact]
        public void TurnIn_SingleQuestTwoObjectivesShareSameItem_InsufficientTotal_FailsWithoutLosingAnyItem()
        {
            var templateId = new Id("item.shared_ore");
            var questId = new Id("quest.sample_deliver_ore_two_objectives");
            var quest = new QuestDefinition(
                questId,
                new[]
                {
                    new QuestObjective(QuestObjectiveType.Collect, templateId, 3, consumeOnProgress: false),
                    new QuestObjective(QuestObjectiveType.Collect, templateId, 3, consumeOnProgress: false),
                },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            // 两个目标各需要 3 件、合计 6 件，库存只有 4 件——明显不足以两个目标都真正满足，但每个
            // 目标各自核对当前库存（4）时都 >= 单个目标需求（3），会各自被记满 3/3（GP-08 直接按现有
            // 库存计算进度，不代表两个目标加起来的量真的够）。
            h.Inventory.AddItem(Player, templateId, 4);
            h.Bus.PublishImmediate(new ItemAddedEvent(Player, new Id("item.instance_ore"), templateId, 4));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));

            var turnedIn = h.Host.TurnIn(Player, questId, out var failure);

            Assert.False(turnedIn);
            Assert.Equal(QuestTurnInFailure.InsufficientItems, failure);
            Assert.Equal(4, h.Inventory.CountOf(Player, templateId)); // C06 根治：一件都不丢，精确回到交付前。
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId)); // 保持可重试。
            Assert.Empty(h.Rewards.Calls); // 从未走到发奖励这一步。
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

        /// <summary>GP-08 复现与回归（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        /// 玩家先攒够目标物品，再去接取一条非消耗 collect 任务——旧实现 <c>Accept</c> 把
        /// <c>ObjectiveCounts</c> 无条件清零，必须再等一次 item.added/item.removed 事件才会被动纠正
        /// 成当前库存值，接取瞬间任务显示"尚未收集"。修复后：接取时就应按现有库存直接算出进度，
        /// 数量已经足够时甚至可以立即 ObjectivesComplete。</summary>
        [Fact]
        public void Accept_NonConsumeCollect_InitializesProgressFromExistingInventory_CanCompleteImmediately()
        {
            var questId = new Id("quest.sample_collect_preexisting");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, new Id("item.flower"), 3, consumeOnProgress: false) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });

            // 接取之前就已经持有 3 个（达标数量）——不经由 item.added 事件，直接调用背包 API 模拟
            // "早就攒够了、现在才来接任务"的顺序。
            h.Inventory.AddItem(Player, new Id("item.flower"), 3);

            var accepted = h.Host.Accept(Player, questId);

            Assert.True(accepted);
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
            Assert.Equal(3, h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questId)).ObjectiveCounts[0]);
            Assert.Single(h.PublishedOf<QuestCompletedEvent>());
        }

        /// <summary>同上，但持有量不足以达标——接取后进度应等于当前持有量（不是 0），后续再补齐才完成。</summary>
        [Fact]
        public void Accept_NonConsumeCollect_PartialExistingInventory_InitializesPartialProgress()
        {
            var questId = new Id("quest.sample_collect_partial_preexisting");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, new Id("item.flower"), 3, consumeOnProgress: false) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Inventory.AddItem(Player, new Id("item.flower"), 2);

            h.Host.Accept(Player, questId);

            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));
            Assert.Equal(2, h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questId)).ObjectiveCounts[0]);
        }

        /// <summary>消耗型（consumeOnProgress: true）目标不受 GP-08 影响——接取时不应该倒扣已有库存
        /// （语义是"接取后主动上交/消耗"，不是"统计当前持有量"），确认改动没有误伤这条路径。</summary>
        [Fact]
        public void Accept_ConsumeOnProgressCollect_DoesNotInitializeFromExistingInventory()
        {
            var questId = new Id("quest.sample_collect_consume_preexisting");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, new Id("item.herb"), 2, consumeOnProgress: true) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Inventory.AddItem(Player, new Id("item.herb"), 2);

            h.Host.Accept(Player, questId);

            Assert.Equal(QuestState.Active, h.Host.GetState(Player, questId));
            Assert.Equal(0, h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questId)).ObjectiveCounts[0]);
            Assert.Equal(2, h.Inventory.CountOf(Player, new Id("item.herb"))); // 未被倒扣
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

        /// <summary>GP-07 复现与回归（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        /// 两条任务都要求消耗同一种物品各 1 个，背包里只有 1 个——旧实现在 <c>RemoveItem</c> 对第二条
        /// 任务的扣除失败（背包已经被第一条任务的成功扣除清空）时，仍无条件按事件携带的数量把进度
        /// 记满，导致同一件物品"喂饱"了两条任务。修复后：只有真正扣除成功的那一条任务能推进/完成，
        /// 另一条应保持 0 进度，背包最终数量为 0（不会因为"扣两次"变成负数——<see cref="FakeInventoryHost.RemoveItem"/>
        /// 对超额扣除直接返回 false、不改变库存，见 TestSupport 判断记录）。</summary>
        /// <summary>N12 复现与根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
        /// 真实 <c>InventoryHost.AddItem</c> 跨堆叠合并新增时，<c>ItemAddedEvent</c> 只携带
        /// <c>touchedInstanceId</c>（最后一个被触碰的实例）与 <c>toAdd</c>（总新增数）——若这次新增
        /// 的物品分散在多个独立堆叠实例上（例如库存原本已有两个各缺 1 件的同模板堆叠，一次性加 2
        /// 件各补满 1 件），旧实现只从 <c>touchedInstanceId</c> 单个实例尝试扣除完整数量，该实例
        /// 未必持有这么多，扣除整体失败，consume 型目标进度永远推进不了。本用例用
        /// <see cref="FakeInventoryHost.AddSeparateInstanceForTest"/> 直接构造"同模板分布在两个独立
        /// 实例上"的真实跨堆叠场景，手工发出与之匹配的 <c>ItemAddedEvent</c>（同真实
        /// <c>InventoryHost.AddItem</c> 的既有报告惯例：<c>ItemInstanceId</c> 只是其中一个实例，
        /// <c>Count</c> 是两个实例合计的新增总量），断言 consume 目标进度正确推进到 2、两个实例都
        /// 被扣空。</summary>
        [Fact]
        public void Objective_Collect_ConsumeOnProgress_CrossStackAdd_AdvancesProgressAcrossBothInstances()
        {
            var questId = new Id("quest.sample_collect_cross_stack");
            var templateId = new Id("item.shared_ore");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, templateId, 2, consumeOnProgress: true) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { quest });
            h.Host.Accept(Player, questId);

            // 库存原本已有两个各持有 1 件的独立堆叠实例（同模板）——不是一次 AddItem 自然合并出来的
            // 单一堆叠，模拟"接取任务前就分散持有"或"两次不同来源各得 1 件"的既有状态。
            var instanceA = h.Inventory.AddSeparateInstanceForTest(Player, templateId, 1);
            h.Inventory.AddSeparateInstanceForTest(Player, templateId, 1);
            Assert.Equal(2, h.Inventory.CountOf(Player, templateId));

            // 真实 InventoryHost.AddItem 对"这次新增"只报告 touchedInstanceId（这里取 instanceA）与
            // 合计新增数 2（即便这 2 件实际分布在 instanceA/instanceB 两个独立实例上）。
            h.Bus.PublishImmediate(new ItemAddedEvent(Player, instanceA, templateId, 2));

            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
            Assert.Equal(2, h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questId)).ObjectiveCounts[0]);
            Assert.Equal(0, h.Inventory.CountOf(Player, templateId)); // 两个实例都被扣空，不是只扣了一个。
        }

        [Fact]
        public void Objective_Collect_ConsumeOnProgress_SingleItem_DoesNotDoubleCreditTwoQuests()
        {
            var questA = new Id("quest.sample_consume_a");
            var questB = new Id("quest.sample_consume_b");
            var itemId = new Id("item.rare_ore");
            var defA = new QuestDefinition(
                questA, new[] { new QuestObjective(QuestObjectiveType.Collect, itemId, 1, consumeOnProgress: true) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var defB = new QuestDefinition(
                questB, new[] { new QuestObjective(QuestObjectiveType.Collect, itemId, 1, consumeOnProgress: true) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { defA, defB });
            h.Host.Accept(Player, questA);
            h.Host.Accept(Player, questB);

            var instanceId = h.Inventory.AddItemForTest(Player, itemId, 1);
            h.Bus.PublishImmediate(new ItemAddedEvent(Player, instanceId, itemId, 1));

            var completeCount =
                (h.Host.GetState(Player, questA) == QuestState.ObjectivesComplete ? 1 : 0) +
                (h.Host.GetState(Player, questB) == QuestState.ObjectivesComplete ? 1 : 0);
            Assert.Equal(1, completeCount); // 不能两条都完成——只有 1 个物品

            var countA = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questA)).ObjectiveCounts[0];
            var countB = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questB)).ObjectiveCounts[0];
            Assert.Equal(1, countA + countB); // 记录的总进度等于实际消耗的物品数量，不多不少

            Assert.Equal(0, h.Inventory.CountOf(Player, itemId));
        }

        /// <summary>
        /// C06 复现与根治，路径一（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：
        /// 两个 consume 型目标分别需要 1 件、2 件同种物品（合计 3 件），一次性只新增 2 件——两个目标
        /// 不可能都拿满，其中一个必然拿不到足量。旧实现的 <c>RemoveCollectedItems</c> 边遍历边改，
        /// 即使最终因为凑不满 <c>take</c> 数量返回 false，也已经把库存里实际能找到的那部分真的移出了
        /// 背包——"扣了但没记进度"，记录的总进度会小于背包实际减少的数量（净丢失）。根治后
        /// <c>RemoveCollectedItems</c> 先核验总量、不够就不碰库存，两者必然精确相等：记录的总进度
        /// 恰好等于背包实际减少的数量，不多不少，不论两个目标谁先被处理。
        /// </summary>
        [Fact]
        public void HandleItemAdded_ConsumeOnProgress_TwoQuestsInsufficientForSecond_CreditedProgressMatchesActualConsumption()
        {
            var questA = new Id("quest.sample_consume_needs_1");
            var questB = new Id("quest.sample_consume_needs_2");
            var itemId = new Id("item.rare_ore");
            var defA = new QuestDefinition(
                questA, new[] { new QuestObjective(QuestObjectiveType.Collect, itemId, 1, consumeOnProgress: true) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var defB = new QuestDefinition(
                questB, new[] { new QuestObjective(QuestObjectiveType.Collect, itemId, 2, consumeOnProgress: true) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var h = new Harness(new[] { defA, defB });
            h.Host.Accept(Player, questA);
            h.Host.Accept(Player, questB);

            // 一次新增 2 件——两个 consume 型目标合计需要 1+2=3 件，明显不足，其中一个目标必然拿不到
            // 完整数量。
            var instanceId = h.Inventory.AddItemForTest(Player, itemId, 2);
            h.Bus.PublishImmediate(new ItemAddedEvent(Player, instanceId, itemId, 2));

            var countA = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questA)).ObjectiveCounts[0];
            var countB = h.Host.GetLog(Player).Single(p => p.QuestId.Equals(questB)).ObjectiveCounts[0];
            var actualConsumed = 2 - h.Inventory.CountOf(Player, itemId);

            Assert.True(h.Inventory.CountOf(Player, itemId) >= 0); // 不会扣成负数。
            Assert.Equal(actualConsumed, countA + countB); // C06 根治核心断言：记的进度==实际扣的量。
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

        /// <summary>GP-05 收边新增（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        /// quest.is_objectives_complete——此前只有 is_active/is_available/is_completed 三档，
        /// Available -&gt; Active -&gt; ObjectivesComplete -&gt; TurnedIn 状态机唯独 ObjectivesComplete
        /// 这一档没有对应查询，导致"任务可交付时显示交任务选项"这类内容写法（只能用 is_active）
        /// 在目标全部达标后反而把选项隐藏（见 data/_sample/dialog/dialog.gossip_menu.json
        /// "option_turn_in" 勘误、Tests.Gameplay.EndToEndTests 的复现）。</summary>
        [Fact]
        public void QuestIsObjectivesComplete_TrueOnlyInObjectivesCompleteState()
        {
            var questId = new Id("quest.sample_kill_wolves");
            var quest = SimpleKillQuest(questId, new Id("creature.wolf"), 1);
            var h = new Harness(new[] { quest });
            var provider = new QuestExprGroupProvider(h.Host, () => Player);
            var args = new[] { ExprValue.OfId(questId) };

            bool IsObjectivesComplete() => provider.Query("is_objectives_complete", args).AsBool;

            Assert.False(IsObjectivesComplete());

            h.Host.Accept(Player, questId);
            Assert.False(IsObjectivesComplete(), "Active 阶段尚不算 ObjectivesComplete");

            h.Units.SetTemplate(new Id("unit.wolf_1"), new Id("creature.wolf"));
            h.Bus.PublishImmediate(new UnitDiedEvent(new Id("unit.wolf_1"), Player));
            Assert.Equal(QuestState.ObjectivesComplete, h.Host.GetState(Player, questId));
            Assert.True(IsObjectivesComplete());

            h.Host.TurnIn(Player, questId);
            Assert.False(IsObjectivesComplete(), "TurnedIn 之后不再是 ObjectivesComplete");
        }

        /// <summary>与 <see cref="QuestIsObjectivesComplete_TrueOnlyInObjectivesCompleteState"/> 同一
        /// 判断记录：确认新谓词已经登记进 <see cref="QuestExprSchemaEntries.BuildParsingSchema"/>，
        /// 内容作者能在 <c>visible_if</c>/<c>condition</c>/<c>prerequisite</c> 里直接写这个函数名，
        /// 不会被 ADR-0015 规则误判成未知引用退化解析。</summary>
        [Fact]
        public void QuestIsObjectivesComplete_ParsesUnderQuestExprSchemaEntries()
        {
            var expr = ExprParser.Parse("quest.is_objectives_complete(quest.sample_kill_wolves)", Schema);

            Assert.NotNull(expr);
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
