using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Gobj;
using Core.Carriers.Item;
using Core.Gameplay.Economy;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SaveSystem;
using Tests.Carriers.Gobj;
using Core.Rules.Common;

internal sealed class MemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    public string GetUserDataDir() => "user://";
    public string GetContentRootDir() => "content://";
    public string? ReadText(string path) => _files.TryGetValue(path, out var text) ? text : null;
    public bool WriteTextAtomic(string path, string content) { _files[path] = content; return true; }
    public bool Exists(string path) => _files.ContainsKey(path);
    public IReadOnlyList<string> ListFiles(string dirPath) => Array.Empty<string>();
    public bool DeleteFile(string path) => _files.Remove(path);
}

internal sealed class ProbeInventoryHost : IInventoryHost
{
    private readonly Dictionary<Id, List<ItemInstance>> _items = new();
    public bool AddItem(Id unitId, Id templateId, int count) { if (!_items.TryGetValue(unitId, out var list)) _items[unitId] = list = new List<ItemInstance>(); list.Add(new ItemInstance(new Id("item.probe_instance"), templateId, count)); return true; }
    public bool RemoveItem(Id unitId, Id instanceId, int count) => false;
    public IReadOnlyList<ItemInstance> ListItems(Id unitId) => _items.TryGetValue(unitId, out var list) ? list : Array.Empty<ItemInstance>();
    public int CountOf(Id unitId, Id templateId) { var total = 0; foreach (var item in ListItems(unitId)) if (item.TemplateId.Equals(templateId)) total += item.Count; return total; }
    public ItemInstance? FindInstance(Id unitId, Id instanceId) { foreach (var item in ListItems(unitId)) if (item.InstanceId.Equals(instanceId)) return item; return null; }
}

internal sealed class ProbeExprHostFactory : IExprHostFactory
{
    public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new Host();
    private sealed class Host : IExprHost { public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) => ExprValue.OfBool(false); }
}

internal static class Program
{
    private static readonly Id Slot = new("slot.core_probe");
    private static readonly Id Unit = new("unit.core_probe");
    private static readonly Id Item = new("item.core_probe_token");
    private static readonly Id ItemSlot = new("item.slot.core_probe");
    private static readonly Id ItemQuality = new("item.quality.core_probe");

    public static void Main()
    {
        Console.WriteLine("CORE-PERSISTENCE-PROBE baseline=85f1f4fbaff7aa3f01292f1b4b469a6a48bcc570 version=1.6.0");
        ProbeLegacyGobjPendingField();
        ProbeMissingInventorySection();
        ProbeMissingVendorSection();
        ProbeVendorTimerAfterSameHostLoad();
    }

    private static void ProbeLegacyGobjPendingField()
    {
        var fixture = new GobjWorldBuilder()
            .Template(J.O(
                ("id", J.S("gobj.core_probe_chest")),
                ("name_key", J.S("l10n.gobj.core_probe_chest")),
                ("kind", J.S("chest")),
                ("type_data", J.O(("loot_table_ref", J.S("loot.core_probe")))),
                ("display_ref", J.S("display.core_probe_chest"))))
            .Build();

        var origin = new Id("gobj.origin.map_probe.gobj_core_probe_chest.p000000_p000000");
        var legacyInstance = new Id("gobj.inst_1");
        fixture.Host.RestorePendingLoot(new Dictionary<Id, IReadOnlyList<ItemStack>>
        {
            [origin] = new[] { new ItemStack(Item, 3) },
        });
        var itemBus = NewBus();
        var itemRegistry = BuildItemRegistry(itemBus);
        var inventory = new InventoryHost(itemRegistry, itemBus);
        inventory.RegisterUnit(Unit);
        inventory.AddItem(Unit, Item, 1);
        var inventoryPersistable = new InventoryPersistable(Unit, inventory);

        // This is the exact 1.5.0 on-disk shape from git show 3224ca1, whose
        // entry key was gobjInstanceId. Keep a non-empty entry so the direct
        // current reader indexer is exercised.
        var legacy = new JsonObjectBuilder()
            .Add("save_version", new JsonNumber(1))
            .Add("sections", new JsonObjectBuilder()
                .Add("meta", new JsonObjectBuilder()
                    .Add("save_version", new JsonNumber(1))
                    .Add("slot_id", new JsonString(Slot.Value))
                    .Add("created_at", new JsonString("probe"))
                    .Add("updated_at", new JsonString("probe"))
                    .Add("game_id", new JsonString("game.core_probe"))
                    .Build())
                .Add("player.inventory", new JsonArray(new JsonValue[]
                {
                    new JsonObjectBuilder().Add("instance_id", new JsonString("item.inst_1")).Add("template_id", new JsonString(Item.Value)).Add("count", new JsonNumber(2)).Add("extra", new JsonObjectBuilder().Build()).Build(),
                }))
                .Add("world.gobj_pending_loot", new JsonObjectBuilder()
                    .Add("pending_loot", new JsonArray(new JsonValue[]
                    {
                        new JsonObjectBuilder()
                            .Add("gobjInstanceId", new JsonString(legacyInstance.Value))
                            .Add("items", new JsonArray(new JsonValue[]
                            {
                                new JsonObjectBuilder().Add("templateId", new JsonString(Item.Value)).Add("count", new JsonNumber(3)).Build(),
                            }))
                            .Build(),
                    }))
                    .Build())
                .Build())
            .Build();

        var fs = new MemoryFileSystem();
        var path = "user://saves/" + Slot.Value + ".json";
        fs.WriteTextAtomic(path, JsonWriter.Write(legacy));
        var save = new Core.Foundation.SaveSystem.SaveSystem(fs, new SaveSystemOptions(new Id("game.core_probe")));
        save.RegisterPersistable(inventoryPersistable);
        save.RegisterPersistable(new GobjPendingLootPersistable(fixture.Host));
        var before = fixture.Host.PendingLootSnapshot().Count;
        var result = save.Load(Slot);
        var after = fixture.Host.PendingLootSnapshot().Count;

        Console.WriteLine("LEGACY-GOBJ-PENDING");
        Console.WriteLine("on_disk_entry_key=gobjInstanceId");
        Console.WriteLine("registered_reader=originKey");
        Console.WriteLine($"before_pending_entries={before}");
        Console.WriteLine($"load_status={result.Status}");
        Console.WriteLine($"load_message={result.Message}");
        Console.WriteLine($"after_pending_entries={after}");
        Console.WriteLine($"after_inventory_count={inventory.CountOf(Unit, Item)}");
        Console.WriteLine("EXPECTED=load_status=Loaded;after_inventory_count=2;pending restored");
        Console.WriteLine($"ACTUAL=load_status={result.Status};after_inventory_count={inventory.CountOf(Unit, Item)};pending_entries={after}");
    }

    private static void ProbeMissingInventorySection()
    {
        var bus = NewBus();
        var source = new InMemoryDataSource()
            .Add("item.slot_definition", Envelope("item.slot_definition", $"[{{\"id\":\"{ItemSlot.Value}\",\"name_key\":\"l10n.slot.core_probe\"}}]"))
            .Add("item.quality_definition", Envelope("item.quality_definition", $"[{{\"id\":\"{ItemQuality.Value}\",\"name_key\":\"l10n.quality.core_probe\"}}]"))
            .Add("item.template", Envelope("item.template", $"[{{\"id\":\"{Item.Value}\",\"slot\":\"{ItemSlot.Value}\",\"quality\":\"{ItemQuality.Value}\",\"item_level\":1,\"display_ref\":\"display.core_probe_token\",\"stack_size\":10,\"name_key\":\"l10n.item.core_probe_token\"}}]"));
        var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
        registry.RegisterSchema(ItemSchemas.SlotDefinition);
        registry.RegisterSchema(ItemSchemas.QualityDefinition);
        registry.RegisterSchema(ItemSchemas.Template);
        var report = registry.LoadAll();
        Console.WriteLine("MISSING-INVENTORY-SECTION");
        Console.WriteLine($"registry_blocking={report.IsBlocking}");

        var inventory = new InventoryHost(registry, bus);
        inventory.RegisterUnit(Unit);
        inventory.AddItem(Unit, Item, 1);
        var persistable = new InventoryPersistable(Unit, inventory);

        var doc = new JsonObjectBuilder()
            .Add("save_version", new JsonNumber(1))
            .Add("sections", new JsonObjectBuilder()
                .Add("meta", new JsonObjectBuilder()
                    .Add("save_version", new JsonNumber(1))
                    .Add("slot_id", new JsonString(Slot.Value))
                    .Add("created_at", new JsonString("probe"))
                    .Add("updated_at", new JsonString("probe"))
                    .Add("game_id", new JsonString("game.core_probe"))
                    .Build())
                .Build())
            .Build();
        var fs = new MemoryFileSystem();
        fs.WriteTextAtomic("user://saves/" + Slot.Value + ".json", JsonWriter.Write(doc));
        var save = new Core.Foundation.SaveSystem.SaveSystem(fs, new SaveSystemOptions(new Id("game.core_probe")));
        save.RegisterPersistable(persistable);
        var before = inventory.CountOf(Unit, Item);
        var result = save.Load(Slot);
        var after = inventory.CountOf(Unit, Item);
        Console.WriteLine($"persistable_keep_state_default={((IPersistable)persistable).KeepStateWhenSectionMissing}");
        Console.WriteLine($"before_inventory_count={before}");
        Console.WriteLine($"load_status={result.Status}");
        Console.WriteLine($"after_inventory_count={after}");
        Console.WriteLine("EXPECTED=load_status=Loaded;after_inventory_count=0");
        Console.WriteLine($"ACTUAL=load_status={result.Status};after_inventory_count={after}");
    }

    private static void ProbeMissingVendorSection()
    {
        var bus = NewBus();
        var source = new InMemoryDataSource()
            .Add("econ.currency", Envelope("econ.currency", "[{\"id\":\"econ.currency.core_probe_gold\",\"name_key\":\"l10n.gold\",\"display_ref\":\"display.gold\"}]"))
            .Add("econ.vendor", Envelope("econ.vendor", "[{\"id\":\"econ.vendor.core_probe\",\"name_key\":\"l10n.vendor\",\"sell_items\":[{\"item_id\":\"item.core_probe_token\",\"price_currency_id\":\"econ.currency.core_probe_gold\",\"price_amount\":1,\"stock_limit\":5,\"restock_policy\":\"timer\",\"restock_timer\":10}]}]"));
        var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false }); registry.RegisterSchema(EconomySchemas.Currency); registry.RegisterSchema(EconomySchemas.Vendor); var report = registry.LoadAll(); Console.WriteLine("vendor_registry_issues=" + string.Join(" || ", report.Issues));
        var economy = new EconomyHost(registry, bus, new ProbeInventoryHost(), new ProbeExprHostFactory()); economy.RegisterUnit(Unit); var vendor = new Id("econ.vendor.core_probe"); var item = new Id("item.core_probe_token"); economy.SetStock(vendor, item, 2);
        var persistable = new VendorStockPersistable(economy); var fs = new MemoryFileSystem(); fs.WriteTextAtomic("user://saves/" + Slot.Value + ".json", JsonWriter.Write(MetaOnly())); var save = new Core.Foundation.SaveSystem.SaveSystem(fs, new SaveSystemOptions(new Id("game.core_probe"))); save.RegisterPersistable(persistable); var result = save.Load(Slot);
        Console.WriteLine("MISSING-VENDOR-SECTION"); Console.WriteLine($"registry_blocking={report.IsBlocking}"); Console.WriteLine($"persistable_keep_state_default={((IPersistable)persistable).KeepStateWhenSectionMissing}"); Console.WriteLine("before_vendor_remaining=2"); Console.WriteLine($"load_status={result.Status}"); Console.WriteLine($"after_vendor_remaining={economy.GetStock(vendor, item)}");
        Console.WriteLine("EXPECTED=load_status=Loaded;after_vendor_remaining=5"); Console.WriteLine($"ACTUAL=load_status={result.Status};after_vendor_remaining={economy.GetStock(vendor, item)}");
    }

    private static void ProbeVendorTimerAfterSameHostLoad()
    {
        var bus = NewBus();
        var source = new InMemoryDataSource()
            .Add("econ.currency", Envelope("econ.currency", "[{\"id\":\"econ.currency.core_probe_gold\",\"name_key\":\"l10n.gold\",\"display_ref\":\"display.gold\"}]"))
            .Add("econ.vendor", Envelope("econ.vendor", "[{\"id\":\"econ.vendor.core_probe\",\"name_key\":\"l10n.vendor\",\"sell_items\":[{\"item_id\":\"item.core_probe_token\",\"price_currency_id\":\"econ.currency.core_probe_gold\",\"price_amount\":1,\"stock_limit\":5,\"restock_policy\":\"timer\",\"restock_timer\":10}]}]"));
        var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false }); registry.RegisterSchema(EconomySchemas.Currency); registry.RegisterSchema(EconomySchemas.Vendor); registry.LoadAll();
        var vendor = new Id("econ.vendor.core_probe"); var item = new Id("item.core_probe_token");
        var economy = new EconomyHost(registry, bus, new ProbeInventoryHost(), new ProbeExprHostFactory()); economy.RegisterUnit(Unit); economy.SetStock(vendor, item, 2);
        economy.Update(2); // snapshot is taken with 8 seconds left in the 10-second cycle
        var persistable = new VendorStockPersistable(economy); var fs = new MemoryFileSystem(); var save = new Core.Foundation.SaveSystem.SaveSystem(fs, new SaveSystemOptions(new Id("game.core_probe"))); save.RegisterPersistable(persistable);
        var saveResult = save.Save(new SaveRequest(Slot, "t2"));
        economy.Update(7); // in-memory timer has only 1 second left
        var loadResult = save.Load(Slot); var afterLoad = economy.GetStock(vendor, item); economy.Update(1); var afterTick = economy.GetStock(vendor, item);
        Console.WriteLine("VENDOR-TIMER-SAME-HOST-LOAD");
        Console.WriteLine($"save_status={saveResult.Success}");
        Console.WriteLine("snapshot_timer_remaining=8 (not serialized)");
        Console.WriteLine($"load_status={loadResult.Status}");
        Console.WriteLine($"after_load_remaining={afterLoad}");
        Console.WriteLine($"after_one_second_remaining={afterTick}");
        Console.WriteLine("EXPECTED=after_one_second_remaining=2 (timer restored or reset to full)");
        Console.WriteLine($"ACTUAL=after_one_second_remaining={afterTick} (stale pre-load timer restocked immediately)");
    }

    private static IEventBus NewBus() => new EventBus(
        EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
        new EventBusOptions { StrictCatalog = false });

    private static string Envelope(string table, string rows) =>
        "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";

    private static DataRegistry BuildItemRegistry(IEventBus bus)
    {
        var source = new InMemoryDataSource()
            .Add("item.slot_definition", Envelope("item.slot_definition", $"[{{\"id\":\"{ItemSlot.Value}\",\"name_key\":\"l10n.slot.core_probe\"}}]"))
            .Add("item.quality_definition", Envelope("item.quality_definition", $"[{{\"id\":\"{ItemQuality.Value}\",\"name_key\":\"l10n.quality.core_probe\"}}]"))
            .Add("item.template", Envelope("item.template", $"[{{\"id\":\"{Item.Value}\",\"slot\":\"{ItemSlot.Value}\",\"quality\":\"{ItemQuality.Value}\",\"item_level\":1,\"display_ref\":\"display.core_probe_token\",\"stack_size\":10,\"name_key\":\"l10n.item.core_probe_token\"}}]"));
        var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
        registry.RegisterSchema(ItemSchemas.SlotDefinition); registry.RegisterSchema(ItemSchemas.QualityDefinition); registry.RegisterSchema(ItemSchemas.Template); registry.LoadAll();
        return registry;
    }

    private static JsonObject Meta() => new JsonObjectBuilder().Add("save_version", new JsonNumber(1)).Add("slot_id", new JsonString(Slot.Value)).Add("created_at", new JsonString("probe")).Add("updated_at", new JsonString("probe")).Add("game_id", new JsonString("game.core_probe")).Build();
    private static JsonObject MetaOnly() => new JsonObjectBuilder().Add("save_version", new JsonNumber(1)).Add("sections", new JsonObjectBuilder().Add("meta", Meta()).Build()).Build();
}
