using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Loot;
using Core.Rules.Common;
using Presentation.Common;
using Presentation.ViewBinding;

sealed class NullUnitAccess : IUnitAccess
{
    public bool Exists(Id id) => false;
    public IReadOnlyList<Id> AllUnits => Array.Empty<Id>();
    public Vec2 GetPosition(Id id) => throw new InvalidOperationException();
    public void SetPosition(Id id, Vec2 value) => throw new InvalidOperationException();
    public Id GetFaction(Id id) => throw new InvalidOperationException();
    public int GetLevel(Id id) => throw new InvalidOperationException();
    public double GetFacing(Id id) => throw new InvalidOperationException();
    public bool IsAlive(Id id) => false;
    public void SetAlive(Id id, bool alive) => throw new InvalidOperationException();
    public Id? GetTemplateId(Id id) => null;
    public IReadOnlyList<Id> GetTags(Id id) => Array.Empty<Id>();
}

sealed class NullInventory : IInventoryHost
{
    public bool AddItem(Id unitId, Id templateId, int count) => false;
    public bool RemoveItem(Id unitId, Id instanceId, int count) => false;
    public IReadOnlyList<ItemInstance> ListItems(Id unitId) => Array.Empty<ItemInstance>();
    public int CountOf(Id unitId, Id templateId) => 0;
    public ItemInstance? FindInstance(Id unitId, Id instanceId) => null;
}

sealed class NullExprFactory : IExprHostFactory
{
    private sealed class Host : IExprHost
    {
        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) => default;
    }
    public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new Host();
}

sealed class NullExprDiagnostics : IExprDiagnostics
{
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

sealed class DisplayRegistry : IDisplayInfoRegistry
{
    public DisplayInfo? Lookup(Id logicalId) => null;
    public IReadOnlyList<DisplayInfo> LookupByCategory(DisplayCategory category) => Array.Empty<DisplayInfo>();
    public IReadOnlyList<DisplayInfo> All => Array.Empty<DisplayInfo>();
    public void Reload() { }
}

sealed class ProbeView : IView
{
    public Id EntityId { get; private set; }
    public bool IsAlive { get; private set; }
    public void Bind(Id entityId) { EntityId = entityId; IsAlive = true; }
    public void OnEvent(IEvent evt) { }
    public void SyncPose(Vec2 pos, Direction facing, double height) { }
    public void Destroy() { IsAlive = false; }
}

sealed class ProbeViewFactory : IViewFactory
{
    public int Created { get; private set; }
    public IView CreateView(ViewKind kind, Id displayId, Id entityId) { Created++; return new ProbeView(); }
}

sealed class ProbeExprHostFactory : IExprHostFactory
{
    private readonly NullExprFactory _inner = new NullExprFactory();
    public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => _inner.CreateFor(selfId, targetId, triggeringEvent);
}

static class Program
{
    static int Main()
    {
        var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
        var world = new WorldSim(bus);
        var registry = new DataRegistry(new InMemoryDataSource(), bus, new DataRegistryOptions());
        registry.LoadAll();
        var loot = new LootHost(registry, new RngHost(7), bus, world, new NullUnitAccess(), new NullInventory(), new ProbeExprHostFactory(), () => 0, new LootOptions());
        var binderFactory = new ProbeViewFactory();
        using var binder = new ViewBinder(bus, binderFactory, new WorldSimSnapshot(world), new DisplayRegistry());
        var lootId = loot.Drop(new Id("map.sample"), new Vec2(3, 4), new[] { new ItemStack(new Id("item.sample"), 1) });
        world.Tick(SimStep.Continuous(0.016));
        if (binder.Count != 1 || binderFactory.Created != 1) throw new Exception($"setup view missing count={binder.Count} created={binderFactory.Created}");

        var fs = new Adapters.Stub.StubFileSystem();
        var save = new SaveSystem(fs, new SaveSystemOptions(new Id("game.presentation_probe")), bus);
        save.RegisterPersistable(new DroppedLootPersistable(loot));
        var slot = new Id("slot.presentation_probe");
        var saved = save.Save(new SaveRequest(slot, "2026-09-08T00:00:00Z"));
        if (!saved.Success) throw new Exception("save failed: " + saved.Message);

        // Simulate an already-cleared same-map world while LootHost still retains its persisted record.
        loot.ClearDroppedExcept(Array.Empty<Id>());
        world.Tick(SimStep.Continuous(0.016));
        if (binder.Count != 0) throw new Exception("destroy event did not remove the old view");
        var loaded = save.Load(slot);
        if (loaded.Status != LoadStatus.Loaded) throw new Exception("load failed: " + loaded.Message);
        if (world.GetEntity(lootId) == null) throw new Exception("loot entity was not restored");
        world.Tick(SimStep.Continuous(0.016));
        if (binder.Count != 0) throw new Exception("probe did not reproduce missing view");
        Console.WriteLine($"RESULT=PASS status={loaded.Status} entity_present=True binder_views={binder.Count} created_views={binderFactory.Created} loot_id={lootId}");
        return 0;
    }
}
