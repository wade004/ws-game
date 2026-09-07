using System;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Gameplay.Economy;
using Core.Gameplay.Quest;
using Core.Rules.Common;
using Tests.Gameplay.Economy;
using Tests.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Audit
{
    public sealed class AuditEconomyProbeTests
    {
        private static DataRegistry BuildItemRegistry(IEventBus bus)
        {
            string Envelope(string table, string rows) =>
                "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";
            var source = new InMemoryDataSource()
                .Add("item.slot_definition", Envelope("item.slot_definition",
                    "[{\"id\":\"item.slot.consumable\",\"name_key\":\"l10n.slot.consumable\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition",
                    "[{\"id\":\"item.quality.common\",\"name_key\":\"l10n.quality.common\"}]"))
                .Add("item.template", Envelope("item.template", "["
                    + "{\"id\":\"item.a\",\"slot\":\"item.slot.consumable\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.item.a\",\"stack_size\":10,\"name_key\":\"l10n.item.a\"},"
                    + "{\"id\":\"item.b\",\"slot\":\"item.slot.consumable\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.item.b\",\"stack_size\":10,\"name_key\":\"l10n.item.b\"}]"));
            var registry = new DataRegistry(source, bus);
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join(";", report.Issues.Select(i => i.ToString())));
            return registry;
        }

        [Fact]
        public void Buy_PartialExistingStack_WithConsumeQuest_DrainsPreexistingItemAfterRollback()
        {
            var bus = EconomyTestSupport.NewEventBus();
            var itemRegistry = BuildItemRegistry(bus);
            var inventory = new InventoryHost(itemRegistry, bus,
                new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Partial });
            var unit = new Id("unit.player_hero");
            var itemA = new Id("item.a");
            inventory.AddItem(unit, itemA, 5);
            bus.DispatchPending();

            var deferred = new DeferredExprHostFactory();
            var questId = new Id("quest.audit_buy_consume");
            var quest = new QuestDefinition(
                questId,
                new[] { new QuestObjective(QuestObjectiveType.Collect, itemA, 1, consumeOnProgress: true) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
            var questHost = new QuestHost(
                new[] { quest }, bus, deferred, new FakeRewardDispatcher(), inventory, new FakeUnitAccess());
            deferred.Inner = new FakeNumericExprHostFactory();
            Assert.True(questHost.Accept(unit, questId));

            var currency = "[{\"id\":\"econ.currency.coin\",\"name_key\":\"l10n.coin\",\"cap\":1000,\"display_ref\":\"display.coin\"}]";
            var vendor = "[{\"id\":\"econ.vendor.shop\",\"name_key\":\"l10n.shop\",\"sell_items\":["
                + "{\"item_id\":\"item.a\",\"price_currency_id\":\"econ.currency.coin\",\"price_amount\":1,\"stock_limit\":10}]}]";
            var economyRegistry = EconomyTestSupport.MakeRegistry(bus, currency, vendor);
            var economy = new EconomyHost(economyRegistry, bus, inventory, new FakeNumericExprHostFactory());
            var currencyId = new Id("econ.currency.coin");
            economy.Add(unit, currencyId, 10, unit);
            bus.DispatchPending();

            var result = economy.Buy(unit, new Id("econ.vendor.shop"), itemA, 6);
            var beforeDispatch = inventory.CountOf(unit, itemA);
            var pending = bus.DispatchPending();
            var afterDispatch = inventory.CountOf(unit, itemA);
            var objective = questHost.GetLog(unit).Single(x => x.QuestId.Equals(questId)).ObjectiveCounts[0];

            Console.WriteLine($"buy_success={result.Success} reason={result.Reason} before_dispatch={beforeDispatch} dispatched={pending} after_dispatch={afterDispatch} quest_progress={objective}");
            Assert.False(result.Success);
            Assert.Equal(PurchaseFailureReason.InventoryFull, result.Reason);
            Assert.Equal(5, beforeDispatch);
            Assert.Equal(4, afterDispatch);
            Assert.Equal(1, objective);
        }
    }
}
