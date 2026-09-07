using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// 第五轮外部审核相邻缺口根治（architecture/落地计划/audit-5e779c6-20260907，WD 报告"未修复但已
    /// 如实记录的相邻缺口"第 1 条）复现与验收：<c>QuestHost.TurnIn</c> 步骤 2/3 失败时，此前用
    /// "把已移除的 collect 物品重新 <c>AddItem</c> 放回背包"来回滚——这一"移除又放回"会产生一条真实
    /// 的 <c>item.added</c> 事件，可能被玩家对同一物品模板持有的另一个 <c>consumeOnProgress</c> 目标
    /// 误当作"新获得"而错误推进进度。本文件用真实 <see cref="InventoryHost"/>/<see
    /// cref="RewardDispatcher"/>/<see cref="EventBus"/>（假实现无法复现——见
    /// <c>QuestHostTests.Harness</c> 用的 <c>FakeInventoryHost</c> 不实现 <see
    /// cref="IBatchableInventoryHost"/>，走的是历史"手动 AddItem 放回"分支，观察不到事件泄漏）复现
    /// 两条独立任务共享同一物品模板的场景，验证修复后事务整体回滚、不产生任何虚假事件。
    /// </summary>
    public class QuestTurnInTransactionRollbackTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly Id Mat = new Id("item.wd_mat");
        private static readonly Id Filler = new Id("item.wd_filler");
        private static readonly Id RewardItem = new Id("item.wd_reward");

        private static IEventBus CreateRealBus()
        {
            var definitions = new List<EventDefinition>
            {
                new EventDefinition(Core.Gameplay.Quest.QuestEventKeys.Accepted, "quest", new[] { "unitId", "questId" }),
                new EventDefinition(Core.Gameplay.Quest.QuestEventKeys.ObjectiveProgress, "quest", new[] { "unitId", "questId", "objectiveIndex", "progress" }),
                new EventDefinition(Core.Gameplay.Quest.QuestEventKeys.Completed, "quest", new[] { "unitId", "questId" }),
                new EventDefinition(Core.Gameplay.Quest.QuestEventKeys.TurnedIn, "quest", new[] { "unitId", "questId" }),
                new EventDefinition(Core.Gameplay.Quest.QuestEventKeys.Failed, "quest", new[] { "unitId", "questId", "reason" }),
                new EventDefinition(Core.Gameplay.Dialog.DialogEventKeys.GossipOpened, "dialog", new[] { "unitId", "npcId", "menuId" }),
                new EventDefinition(Core.Gameplay.Dialog.DialogEventKeys.StoryNodeEntered, "dialog", new[] { "unitId", "treeId", "nodeId" }),
                new EventDefinition(RulesEventKeys.UnitDied, "unit", new[] { "unitId", "killerId" }),
                new EventDefinition(RulesEventKeys.SkillCastSuccess, "skill", new[] { "casterId", "skillId" }),
                new EventDefinition(CarriersEventKeys.ItemAdded, "item", new[] { "unitId", "itemInstanceId", "itemTemplateId", "count" }),
                new EventDefinition(CarriersEventKeys.ItemRemoved, "item", new[] { "unitId", "itemInstanceId", "count", "reason" }),
                new EventDefinition(CarriersEventKeys.GobjInteracted, "gobj", new[] { "unitId", "gobjInstanceId" }),
                new EventDefinition(new Id("area.trigger_entered"), "area", new[] { "triggerId", "unitId" }),
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
            };
            return new EventBus(EventCatalog.FromDefinitions(definitions));
        }

        private static DataRegistry BuildRealItemRegistry(IEventBus bus)
        {
            string Table(string name, string rows) => "{\"table\":\"" + name + "\",\"schema_version\":1,\"rows\":" + rows + "}";

            string TemplateRow(string id) =>
                "{\"id\":\"" + id + "\",\"slot\":\"item.slot.consumable\",\"quality\":\"item.quality.common\"," +
                "\"item_level\":1,\"display_ref\":\"display.item.placeholder\",\"stack_size\":1,\"name_key\":\"l10n." + id + "\"}";

            var source = new InMemoryDataSource()
                .Add("item.slot_definition", Table("item.slot_definition",
                    "[{\"id\":\"item.slot.consumable\",\"name_key\":\"l10n.slot.consumable\"}]"))
                .Add("item.quality_definition", Table("item.quality_definition",
                    "[{\"id\":\"item.quality.common\",\"name_key\":\"l10n.quality.common\"}]"))
                .Add("item.template", Table("item.template",
                    "[" + TemplateRow(Mat.Value) + "," + TemplateRow(Filler.Value) + "," + TemplateRow(RewardItem.Value) + "]"));

            var registry = new DataRegistry(source, bus);
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join(";", report.Issues.Select(i => i.ToString())));
            return registry;
        }

        /// <summary>不需要求值前置条件/事件过滤表达式的最小 <see cref="IExprHostFactory"/>——真正调用
        /// 即视为测试装配有误，直接抛异常以便及早发现。</summary>
        private sealed class ThrowingExprHostFactory : IExprHostFactory
        {
            public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) =>
                throw new InvalidOperationException("本测试的任务定义不应触发前置条件/事件过滤求值");
        }

        /// <summary>
        /// 复现：任务 A 的 collect 目标（非消耗）与任务 B 的 collect 目标（消耗型）共享同一物品模板
        /// <see cref="Mat"/>。任务 A 接取即达标（背包已有 1 个 Mat），任务 B 接取后仍在 Active、进度
        /// 0（consumeOnProgress 目标接取时不倒扣已有库存，见 <c>QuestHost.Accept</c> 判断记录）。
        /// 背包容量固定为 2 格，<see cref="Filler"/> 常驻占 1 格、<see cref="Mat"/> 占另 1 格（已满）；
        /// 任务 A 的奖励是 2 个 <see cref="RewardItem"/>（各占 1 格，<c>stack_size=1</c>），发放时
        /// 即使步骤 2 移除 Mat 腾出 1 格，也只够放 1 个而不是 2 个——<c>InventoryFullPolicy.Reject</c>
        /// 下整批奖励发放确定性失败，触发步骤 3 的回滚路径。
        /// <para>
        /// 断言修复后的行为：(1) <c>TurnIn</c> 返回 false、失败原因 <see
        /// cref="QuestTurnInFailure.InventoryFull"/>；(2) 事务整体回滚，<c>DispatchPending</c> 派发
        /// 0 条事件（不是"移除又放回"两条互相抵消的事件，而是从未真正入队）；(3) 背包状态精确回到
        /// 交付前（Mat 仍是 1、Filler 仍是 1、RewardItem 仍是 0）；(4) 任务 B 的进度与状态完全不受
        /// 影响（仍是 Active、进度 0）——这正是此前会被虚假 <c>item.added</c> 误判推进的那条任务。
        /// </para>
        /// </summary>
        [Fact]
        public void TurnIn_RewardGrantFails_RollsBackViaTransaction_NoLeakedItemAddedForOtherQuest()
        {
            var bus = CreateRealBus();
            var registry = BuildRealItemRegistry(bus);
            var inventory = new InventoryHost(registry, bus, new InventoryOptions
            {
                MaxSlots = 2,
                FullPolicy = InventoryFullPolicy.Reject,
            });
            var dispatcher = new RewardDispatcher(inventory: inventory);

            var questAId = new Id("quest.wd_turnin_a");
            var questBId = new Id("quest.wd_turnin_b");
            var questA = new Core.Gameplay.Quest.QuestDefinition(
                questAId,
                new[] { new Core.Gameplay.Quest.QuestObjective(Core.Gameplay.Quest.QuestObjectiveType.Collect, Mat, 1, consumeOnProgress: false) },
                Core.Gameplay.Quest.QuestStartMethod.NpcGossip, Core.Gameplay.Quest.QuestTurnInMethod.NpcGossip,
                Core.Gameplay.Quest.QuestRepeatable.None,
                rewards: new RewardBundle(
                    items: new[] { new ItemStack(RewardItem, 2) },
                    xp: 0, currency: Array.Empty<(Id, long)>(), skills: Array.Empty<Id>(),
                    worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 0));
            var questB = new Core.Gameplay.Quest.QuestDefinition(
                questBId,
                new[] { new Core.Gameplay.Quest.QuestObjective(Core.Gameplay.Quest.QuestObjectiveType.Collect, Mat, 1, consumeOnProgress: true) },
                Core.Gameplay.Quest.QuestStartMethod.NpcGossip, Core.Gameplay.Quest.QuestTurnInMethod.NpcGossip,
                Core.Gameplay.Quest.QuestRepeatable.None);

            var units = new FakeUnitAccess();
            var host = new Core.Gameplay.Quest.QuestHost(
                new[] { questA, questB }, bus, new ThrowingExprHostFactory(), dispatcher, inventory, units);

            inventory.AddItem(Player, Filler, 1);
            inventory.AddItem(Player, Mat, 1);
            bus.DispatchPending(); // 铺底：清空建仓事件，不属于本用例观察范围。

            Assert.True(host.Accept(Player, questAId));
            Assert.True(host.Accept(Player, questBId));
            bus.DispatchPending();

            Assert.Equal(Core.Gameplay.Quest.QuestState.ObjectivesComplete, host.GetState(Player, questAId));
            Assert.Equal(Core.Gameplay.Quest.QuestState.Active, host.GetState(Player, questBId));

            var itemAddedCount = 0;
            var itemRemovedCount = 0;
            bus.Subscribe(CarriersEventKeys.ItemAdded, _ => itemAddedCount++);
            bus.Subscribe(CarriersEventKeys.ItemRemoved, _ => itemRemovedCount++);

            var turnedIn = host.TurnIn(Player, questAId, out var failure);

            Assert.False(turnedIn);
            Assert.Equal(Core.Gameplay.Quest.QuestTurnInFailure.InventoryFull, failure);
            Assert.Equal(Core.Gameplay.Quest.QuestState.ObjectivesComplete, host.GetState(Player, questAId));

            var dispatchedCount = bus.DispatchPending();
            Assert.Equal(0, dispatchedCount); // 事务整体回滚：从未真正入队，不是"移除+放回"两条互相抵消的事件。
            Assert.Equal(0, itemAddedCount);
            Assert.Equal(0, itemRemovedCount);

            // 背包状态精确回到交付前。
            Assert.Equal(1, inventory.CountOf(Player, Mat));
            Assert.Equal(1, inventory.CountOf(Player, Filler));
            Assert.Equal(0, inventory.CountOf(Player, RewardItem));

            // 任务 B（另一个持有同模板 consumeOnProgress 目标的任务）完全不受这次失败交付的内部纠正影响。
            var logB = host.GetLog(Player).Single(p => p.QuestId.Equals(questBId));
            Assert.Equal(Core.Gameplay.Quest.QuestState.Active, logB.State);
            Assert.Equal(0, logB.ObjectiveCounts[0]);
        }

        /// <summary>回归：奖励能正常发放时（背包足够）交付照常成功，事务提交后事件正常派发，任务 B
        /// 的 consumeOnProgress 目标能观察到真实发生的 <c>item.removed</c>/<c>item.added</c>（本用例
        /// 不涉及 Mat，只确认事务化改动没有影响正常成功路径的事件时序）。</summary>
        [Fact]
        public void TurnIn_RewardGrantSucceeds_CommitsTransactionAndDispatchesEventsNormally()
        {
            var bus = CreateRealBus();
            var registry = BuildRealItemRegistry(bus);
            var inventory = new InventoryHost(registry, bus, new InventoryOptions
            {
                MaxSlots = 4,
                FullPolicy = InventoryFullPolicy.Reject,
            });
            var dispatcher = new RewardDispatcher(inventory: inventory);

            var questId = new Id("quest.wd_turnin_success");
            var quest = new Core.Gameplay.Quest.QuestDefinition(
                questId,
                new[] { new Core.Gameplay.Quest.QuestObjective(Core.Gameplay.Quest.QuestObjectiveType.Collect, Mat, 1, consumeOnProgress: false) },
                Core.Gameplay.Quest.QuestStartMethod.NpcGossip, Core.Gameplay.Quest.QuestTurnInMethod.NpcGossip,
                Core.Gameplay.Quest.QuestRepeatable.None,
                rewards: new RewardBundle(
                    items: new[] { new ItemStack(RewardItem, 1) },
                    xp: 0, currency: Array.Empty<(Id, long)>(), skills: Array.Empty<Id>(),
                    worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 0));

            var units = new FakeUnitAccess();
            var host = new Core.Gameplay.Quest.QuestHost(
                new[] { quest }, bus, new ThrowingExprHostFactory(), dispatcher, inventory, units);

            inventory.AddItem(Player, Mat, 1);
            bus.DispatchPending();
            Assert.True(host.Accept(Player, questId));
            bus.DispatchPending();
            Assert.Equal(Core.Gameplay.Quest.QuestState.ObjectivesComplete, host.GetState(Player, questId));

            var turnedIn = host.TurnIn(Player, questId, out var failure);

            Assert.True(turnedIn);
            Assert.Equal(Core.Gameplay.Quest.QuestTurnInFailure.None, failure);
            Assert.Equal(Core.Gameplay.Quest.QuestState.TurnedIn, host.GetState(Player, questId));

            var dispatchedCount = bus.DispatchPending();
            Assert.Equal(2, dispatchedCount); // item.removed（Mat）+ item.added（RewardItem）。
            Assert.Equal(0, inventory.CountOf(Player, Mat));
            Assert.Equal(1, inventory.CountOf(Player, RewardItem));
        }
    }
}
