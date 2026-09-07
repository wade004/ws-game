using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Core.Gameplay.Common;
using Adapters.Stub;

static class Program
{
    private static readonly Id GameId = new("game.boundary.repro");
    private static readonly Id UnitId = new("unit.hero");

    public static int Main()
    {
        Console.WriteLine("BOUNDARY_REPROS HEAD=7e63d6695644644e20f30e006b9efa7e531afa34");
        var a = ReproSaveLookAlikeSlot();
        var b = ReproPartialRewardRollback();
        Console.WriteLine($"SUMMARY CORE-A={(a ? "REPRODUCED" : "NOT_REPRODUCED")} CORE-B={(b ? "REPRODUCED" : "NOT_REPRODUCED")}");
        return a && b ? 0 : 1;
    }

    private static bool ReproSaveLookAlikeSlot()
    {
        var fs = new StubFileSystem();
        var save = new Core.Foundation.SaveSystem.SaveSystem(
            fs, new SaveSystemOptions(GameId) { BackupCount = 1 });
        var markerPersistable = new MarkerPersistable("repro.marker", "from-other-slot");
        save.RegisterPersistable(markerPersistable);
        var lookAlike = new Id("slot.a.bak1");
        var saveResult = save.Save(new SaveRequest(
            lookAlike, "2026-09-07T17:00:00Z",
            displaySummary: new Dictionary<string, string> { ["marker"] = "look-alike-formal-slot" }));
        var formal = fs.ReadText("user://saves/slot.a.bak1.json");
        markerPersistable.Value = new JsonString("current-slot-a");
        var load = save.Load(new Id("slot.a"));
        var marker = load.Meta?.DisplaySummary != null && load.Meta.DisplaySummary.TryGetValue("marker", out var value) ? value : "<none>";
        var sectionMarker = markerPersistable.Value is JsonString markerJson ? markerJson.Value : "<non-string>";
        var migratedBackup = fs.Exists("user://saves/backups/slot.a.bak1.json");
        var reproduced = saveResult.Success && load.Status == LoadStatus.LoadedFromBackup && marker == "look-alike-formal-slot" && sectionMarker == "from-other-slot";
        Console.WriteLine("CORE-A expectedCorrectBehavior=NotFound (slot.a has no formal/new backup; slot.a.bak1 is independent formal slot)");
        Console.WriteLine($"CORE-A setup=Save(slot.a.bak1) success={saveResult.Success} formalBytes={(formal?.Length ?? 0)}");
        Console.WriteLine($"CORE-A actual status={load.Status} metaMarker={marker} loadedSectionMarker={sectionMarker} generatedNewLayoutBackup={migratedBackup} reproduced={reproduced}");
        Console.WriteLine("CORE-A boundary=legacy top-level fallback path aliases an independent formal slot name");
        return reproduced;
    }

    private sealed class MarkerPersistable : IPersistable
    {
        public string SectionKey { get; }
        public JsonValue Value { get; set; }

        public MarkerPersistable(string sectionKey, string value)
        {
            SectionKey = sectionKey;
            Value = new JsonString(value);
        }

        public JsonValue Save() => Value;
        public void Load(JsonValue data) => Value = data;
    }

    private static bool ReproPartialRewardRollback()
    {
        var bus = CreateBus();
        var registry = BuildInventoryRegistry(bus);
        var inventory = new InventoryHost(registry, bus, new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Partial });
        inventory.AddItem(UnitId, new Id("item.a"), 5);
        var before = inventory.CountOf(UnitId, new Id("item.a"));
        var dispatcher = new RewardDispatcher(inventory: inventory);
        var bundle = new RewardBundle(
            items: new[] { new ItemStack(new Id("item.a"), 10), new ItemStack(new Id("item.b"), 1) },
            xp: 0, currency: Array.Empty<(Id, long)>(), skills: Array.Empty<Id>(),
            worldFlags: Array.Empty<(Id, Core.Foundation.Expr.ExprValue)>(), talentPoints: 0);
        var granted = dispatcher.Grant(UnitId, bundle, new Id("reward.boundary"));
        var afterA = inventory.CountOf(UnitId, new Id("item.a"));
        var afterB = inventory.CountOf(UnitId, new Id("item.b"));
        var actualItems = string.Join(",", inventory.ListItems(UnitId).Select(i => $"{i.TemplateId.Value}:{i.Count}"));
        var reproduced = !granted && before == 5 && afterA == 0 && afterB == 0;
        Console.WriteLine("CORE-B expectedCorrectBehavior=failed reward leaves pre-existing A5 intact; actual partial A add must not be rolled back past the delta");
        Console.WriteLine($"CORE-B setup=MaxSlots=1 FullPolicy=Partial initial=A5 stack_size=10 reward=[A10,B1]");
        Console.WriteLine($"CORE-B actual granted={granted} beforeA={before} afterA={afterA} afterB={afterB} items=[{actualItems}] reproduced={reproduced}");
        Console.WriteLine("CORE-B boundary=RewardDispatcher records requested count 10, while InventoryHost.Partial adds only 5; rollback removes 10 from the now-full A stack");
        return reproduced;
    }

    private static DataRegistry BuildInventoryRegistry(IEventBus bus)
    {
        var source = new InMemoryDataSource()
            .Add("item.slot_definition", Table("item.slot_definition", "[{\"id\":\"item.slot.consumable\",\"name_key\":\"l10n.slot.consumable\"}]"))
            .Add("item.quality_definition", Table("item.quality_definition", "[{\"id\":\"item.quality.common\",\"name_key\":\"l10n.quality.common\"}]"))
            .Add("item.template", Table("item.template", "["
                + "{\"id\":\"item.a\",\"slot\":\"item.slot.consumable\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.item.a\",\"stack_size\":10,\"name_key\":\"l10n.item.a\"},"
                + "{\"id\":\"item.b\",\"slot\":\"item.slot.consumable\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.item.b\",\"stack_size\":10,\"name_key\":\"l10n.item.b\"}]"));
        var registry = new DataRegistry(source, bus);
        registry.RegisterSchema(ItemSchemas.Template);
        registry.RegisterSchema(ItemSchemas.SlotDefinition);
        registry.RegisterSchema(ItemSchemas.QualityDefinition);
        var report = registry.LoadAll();
        if (report.IsBlocking) throw new InvalidOperationException("inventory fixture blocked: " + string.Join(";", report.Issues.Select(i => i.ToString())));
        return registry;
    }

    private static IEventBus CreateBus()
    {
        var catalog = EventCatalog.FromDefinitions(new[]
        {
            new EventDefinition(CarriersEventKeys.ItemAdded, "item", new[] { "unitId", "itemInstanceId", "itemTemplateId", "count" }),
            new EventDefinition(CarriersEventKeys.ItemRemoved, "item", new[] { "unitId", "itemInstanceId", "count", "reason" }),
            new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
            new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
        });
        return new EventBus(catalog);
    }

    private static string Table(string name, string rows) =>
        $"{{\"table\":\"{name}\",\"schema_version\":1,\"rows\":{rows}}}";
}
