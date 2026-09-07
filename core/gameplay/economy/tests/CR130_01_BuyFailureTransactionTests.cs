using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Gameplay.Economy;
using Core.Gameplay.Quest;
using Core.Rules.Common;
using Tests.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>
    /// CR130-01（外部审计 audit-5c444f1-20260908，P1）复现与根治：<see cref="EconomyHost.Buy"/>
    /// 购买失败（背包放不下）后仍会先派发 pending 的 <c>item.added</c>，<see cref="QuestHost"/> 按
    /// <c>consumeOnProgress</c> 立即消费玩家原有的同模板物品，回滚补的 <c>item.removed</c> 抵消不了
    /// 这个副作用——本文件基于原始审计探针（<c>architecture/落地计划/audit-5c444f1-20260908/repro/core/
    /// AuditEconomyProbeTests.cs</c>）改造为断言"修复后正确行为"的回归测试：探针原样断言了
    /// <c>quest_progress == 1</c>（复现 bug 本身），本文件断言 <c>quest_progress == 0</c>（购买失败
    /// 不应产生任何任务进度副作用，库存数量也应完全回到调用前）。
    /// </summary>
    public sealed class CR130_01_BuyFailureTransactionTests
    {
        private static DataRegistry BuildItemRegistry(IEventBus bus)
        {
            string Envelope(string table, string rows) =>
                "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";
            var source = new InMemoryDataSource()
                .Add("item.slot_definition", Envelope("item.slot_definition",
                    "[{\"id\":\"item.slot.cr130_01_consumable\",\"name_key\":\"l10n.slot.cr130_01_consumable\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition",
                    "[{\"id\":\"item.quality.cr130_01_common\",\"name_key\":\"l10n.quality.cr130_01_common\"}]"))
                .Add("item.template", Envelope("item.template", "["
                    + "{\"id\":\"item.cr130_01_a\",\"slot\":\"item.slot.cr130_01_consumable\",\"quality\":\"item.quality.cr130_01_common\",\"item_level\":1,\"display_ref\":\"display.item.cr130_01_a\",\"stack_size\":10,\"name_key\":\"l10n.item.cr130_01_a\"},"
                    + "{\"id\":\"item.cr130_01_b\",\"slot\":\"item.slot.cr130_01_consumable\",\"quality\":\"item.quality.cr130_01_common\",\"item_level\":1,\"display_ref\":\"display.item.cr130_01_b\",\"stack_size\":10,\"name_key\":\"l10n.item.cr130_01_b\"}]"));
            var registry = new DataRegistry(source, bus);
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join(";", report.Issues.Select(i => i.ToString())));
            return registry;
        }

        [Fact]
        public void Buy_PartialExistingStack_WithConsumeQuest_RollsBackCleanly_NoQuestProgressLeak()
        {
            var bus = EconomyTestSupport.NewEventBus();
            var itemRegistry = BuildItemRegistry(bus);
            var inventory = new InventoryHost(itemRegistry, bus,
                new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Partial });
            var unit = new Id("unit.cr130_01_hero");
            var itemA = new Id("item.cr130_01_a");
            inventory.AddItem(unit, itemA, 5);
            bus.DispatchPending();

            var deferred = new DeferredExprHostFactory();
            var questId = new Id("quest.cr130_01_buy_consume");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, itemA, 1, consumeOnProgress: true) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var questHost = new QuestHost(
                new[] { quest }, bus, deferred, new FakeRewardDispatcher(), inventory, new FakeUnitAccess());
            deferred.Inner = new FakeNumericExprHostFactory();
            Assert.True(questHost.Accept(unit, questId));

            var currency = "[{\"id\":\"econ.currency.cr130_01_coin\",\"name_key\":\"l10n.coin\",\"cap\":1000,\"display_ref\":\"display.coin\"}]";
            var vendor = "[{\"id\":\"econ.vendor.cr130_01_shop\",\"name_key\":\"l10n.shop\",\"sell_items\":["
                + "{\"item_id\":\"item.cr130_01_a\",\"price_currency_id\":\"econ.currency.cr130_01_coin\",\"price_amount\":1,\"stock_limit\":10}]}]";
            var economyRegistry = EconomyTestSupport.MakeRegistry(bus, currency, vendor);
            var economy = new EconomyHost(economyRegistry, bus, inventory, new FakeNumericExprHostFactory());
            var currencyId = new Id("econ.currency.cr130_01_coin");
            economy.Add(unit, currencyId, 10, unit);
            bus.DispatchPending();

            // 容量为 1 的 Partial 背包已有 A 堆叠 5（stack_size=10，还能续填 5），购买 6 只能续填 5、
            // 差 1——added(5) < count(6)，触发失败回滚路径（同 EconomyHost.Buy 判断记录 2）。
            var result = economy.Buy(unit, new Id("econ.vendor.cr130_01_shop"), itemA, 6);
            var beforeDispatch = inventory.CountOf(unit, itemA);
            var pending = bus.DispatchPending();
            var afterDispatch = inventory.CountOf(unit, itemA);
            var objective = questHost.GetLog(unit).Single(x => x.QuestId.Equals(questId)).ObjectiveCounts[0];

            Assert.False(result.Success);
            Assert.Equal(PurchaseFailureReason.InventoryFull, result.Reason);

            // CR130-01 核心验收：事务未 Commit 即整体回滚（含缓存事件），DispatchPending 前后库存
            // 数量都应停留在调用前的 5——不像修复前那样先经历"5 加到 10 排队 item.added，DispatchPending
            // 时又补一条 item.removed 减回 5"这种"中途曾经是 10"的可观察状态。
            Assert.Equal(5, beforeDispatch);
            Assert.Equal(5, afterDispatch);
            Assert.Equal(0, pending);

            // 修复前该断言会失败：先到的 item.added 被 QuestHost.HandleItemAdded 当真，consumeOnProgress
            // 立即扣了一份玩家原有的库存并把进度计到 1；修复后购买失败不应产生任何任务进度副作用。
            Assert.Equal(0, objective);
        }
    }
}
