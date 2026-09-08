using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
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

internal sealed class ThrowingPersistable : IPersistable
{
    public string SectionKey => "zz.core_probe_failure";
    public JsonValue Save() => JsonNull.Instance;
    public void Load(JsonValue data) => throw new InvalidOperationException("probe failure after real sections");
}

internal sealed class ProbeFixture
{
    public EventBus Bus = null!;
    public WorldSim World = null!;
    public GameplayAssembly Gameplay = null!;
    public PlayerUnit Player = null!;
    public List<string> Events = null!;
}

internal static class Program
{
    private static readonly Id Map = new("world.core_probe_map");
    private static readonly Id Unit = new("unit.core_probe_player");
    private static readonly Id Faction = new("fac.core_probe_player");
    private static readonly Id Class = new("arch.class.core_probe");
    private static readonly Id Race = new("arch.race.core_probe");
    private static readonly Id Aura = new("skill.aura_def.core_probe_shared");
    private static readonly Id AuraStat = new("stat.power");
    private static readonly Id MaxHealthStat = new("stat.max_health");
    private static readonly Id Health = WellKnownPowers.Health;
    private static readonly Id AuraItem = new("item.core_probe_aura");
    private static readonly Id MaxHealthItem = new("item.core_probe_max_health");
    private static readonly Id AuraSlot = new("item.slot.core_probe_aura");
    private static readonly Id MaxHealthSlot = new("item.slot.core_probe_max_health");
    private static readonly Id Quality = new("item.quality.core_probe");
    private static readonly Id Achievement = new("achv.core_probe_rollback");

    public static void Main()
    {
        Console.WriteLine("CORE-PERSISTENCE-PROBE baseline=8160178b76fb51ae704a8f14b428decf228cc33e version=1.7.0");
        RunRaceEquipmentCollision();
        RunFailedEquipmentSegment();
        RunRollbackDerivedState();
        RunRollbackRequirementOrder();
    }

    private static void RunRaceEquipmentCollision()
    {
        var fx = BuildFixture();
        fx.Gameplay.Carriers.Inventory.AddItem(Unit, AuraItem, 1);
        var instance = fx.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
        var equip = fx.Gameplay.Carriers.Equipment.Equip(Unit, instance, AuraSlot);
        fx.Bus.DispatchPending();
        var initial = AuraState(fx);

        fx.World.ClearAll();
        fx.Bus.DispatchPending();
        var cleared = AuraState(fx);
        fx.World.AddEntity(fx.Player);
        fx.Bus.DispatchPending();
        fx.Gameplay.EnterMap(Map, Unit);
        var reapplied = AuraState(fx);

        var unequipped = fx.Gameplay.Carriers.Equipment.Unequip(Unit, AuraSlot);
        fx.Bus.DispatchPending();
        var afterUnequip = AuraState(fx);

        Console.WriteLine("RACE-EQUIPMENT-SHARED-AURA");
        Console.WriteLine($"equip_success={equip.Success}");
        Console.WriteLine($"initial={initial}");
        Console.WriteLine($"after_clearall={cleared}");
        Console.WriteLine($"after_enter_map={reapplied}");
        Console.WriteLine($"unequip_success={unequipped.HasValue}");
        Console.WriteLine($"after_unequip={afterUnequip}");
        Console.WriteLine("EXPECTED=after_enter_map=hasAura=true,stacks=1,power=61;after_unequip=hasAura=true,stacks=1,power=61");
        Console.WriteLine($"ACTUAL=after_enter_map={reapplied};after_unequip={afterUnequip}");
        Console.WriteLine("INTERPRETATION=if after_unequip hasAura=false, race source was lost because EnterMap equipment replay ran before race replay and ReapplyRacePassiveAuras skipped on HasAura");
    }

    private static void RunFailedEquipmentSegment()
    {
        var fx = BuildFixture(withRace: false, startLevel: 10);
        fx.Gameplay.Carriers.Inventory.AddItem(Unit, MaxHealthItem, 1);
        var instance = fx.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
        var equip = fx.Gameplay.Carriers.Equipment.Equip(Unit, instance, MaxHealthSlot);
        fx.Bus.DispatchPending();
        fx.Events.Clear();

        var fs = new MemoryFileSystem();
        var save = new SaveSystem(fs, new SaveSystemOptions(new Id("game.core_probe")), fx.Bus);
        save.RegisterPersistable(new Core.Carriers.Item.EquipmentPersistable(Unit, fx.Gameplay.Carriers.Inventory, fx.Gameplay.Carriers.Equipment));
        var malformed = new JsonObjectBuilder()
            .Add("save_version", new JsonNumber(1))
            .Add("sections", new JsonObjectBuilder()
                .Add("meta", Meta())
                .Add("player.equipment", new JsonString("wrong-shape"))
                .Build())
            .Build();
        fs.WriteTextAtomic("user://saves/slot.failed_equipment.json", JsonWriter.Write(malformed));

        var before = fx.Gameplay.Carriers.Equipment.GetEquipped(Unit, MaxHealthSlot).HasValue;
        var result = save.Load(new Id("slot.failed_equipment"));
        var afterLoad = fx.Gameplay.Carriers.Equipment.GetEquipped(Unit, MaxHealthSlot).HasValue;
        var queuedEvents = fx.Events.Count;
        fx.Bus.DispatchPending();
        var afterDispatch = fx.Gameplay.Carriers.Equipment.GetEquipped(Unit, MaxHealthSlot).HasValue;

        Console.WriteLine("FAILED-EQUIPMENT-SEGMENT");
        Console.WriteLine($"equip_success={equip.Success}");
        Console.WriteLine($"before={before}");
        Console.WriteLine($"load_status={result.Status}");
        Console.WriteLine($"after_load_before_dispatch={afterLoad}");
        Console.WriteLine($"queued_event_count_before_dispatch={queuedEvents}");
        Console.WriteLine($"after_dispatch={afterDispatch}");
        Console.WriteLine("EXPECTED=load_status=PersistableThrew;after_load_before_dispatch=true;after_dispatch=true");
        Console.WriteLine($"ACTUAL=load_status={result.Status};after_load_before_dispatch={afterLoad};after_dispatch={afterDispatch};events={string.Join(",", fx.Events)}");
        Console.WriteLine("INTERPRETATION=EquipmentPersistable clears live equipment before validating the JSON shape; because the failing section is never added to SaveSystem.loadedKeysInOrder, rollback does not restore it");
    }

    private static void RunRollbackDerivedState()
    {
        var fx = BuildFixture(withRace: false, startLevel: 10);
        fx.Gameplay.Carriers.Inventory.AddItem(Unit, MaxHealthItem, 1);
        var instance = fx.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
        fx.Gameplay.Carriers.Equipment.Equip(Unit, instance, MaxHealthSlot);
        fx.Bus.DispatchPending();
        var maxBefore = fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health);
        var achievementBefore = fx.Gameplay.Achievement.GetProgress(Unit, Achievement)[0].Current;
        fx.Gameplay.Carriers.Rules.Powers.ModifyPower(Unit, Health, maxBefore - fx.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health), new Id("probe.fill"));
        fx.Bus.DispatchPending();

        var fs = new MemoryFileSystem();
        var save = new SaveSystem(fs, new SaveSystemOptions(new Id("game.core_probe")), fx.Bus);
        var equipment = new Core.Carriers.Item.EquipmentPersistable(Unit, fx.Gameplay.Carriers.Inventory, fx.Gameplay.Carriers.Equipment);
        var vitals = new PlayerVitalsPersistable(fx.Player, fx.Gameplay.Carriers.Rules.Powers);
        save.RegisterPersistable(equipment);
        save.RegisterPersistable(vitals);
        save.RegisterPersistable(fx.Gameplay.Achievement);
        save.RegisterPersistable(new ThrowingPersistable());

        var malformedAfterRealSections = new JsonObjectBuilder()
            .Add("save_version", new JsonNumber(1))
            .Add("sections", new JsonObjectBuilder()
                .Add("meta", Meta())
                .Add("player.equipment", new JsonObjectBuilder().Build())
                .Add("player.vitals", new JsonObjectBuilder()
                    .Add("alive", JsonBool.True)
                    .Add("health", new JsonNumber(100))
                    .Build())
                .Add(SaveSections.PlayerAchievementState, JsonNull.Instance)
                .Add("zz.core_probe_failure", new JsonString("boom"))
                .Build())
            .Build();
        fs.WriteTextAtomic("user://saves/slot.core_probe.json", JsonWriter.Write(malformedAfterRealSections));

        var beforeEquip = fx.Gameplay.Carriers.Equipment.GetEquipped(Unit, MaxHealthSlot).HasValue;
        var beforeHealth = fx.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health);
        var result = save.Load(new Id("slot.core_probe"));
        var beforeDispatchEquip = fx.Gameplay.Carriers.Equipment.GetEquipped(Unit, MaxHealthSlot).HasValue;
        var beforeDispatchHealth = fx.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health);
        var beforeDispatchAchievement = fx.Gameplay.Achievement.GetProgress(Unit, Achievement)[0].Current;
        var beforeDispatchUnlocked = fx.Gameplay.Achievement.IsUnlocked(Unit, Achievement);
        var queuedEvents = fx.Events.Count;
        fx.Bus.DispatchPending();
        var afterDispatchEquip = fx.Gameplay.Carriers.Equipment.GetEquipped(Unit, MaxHealthSlot).HasValue;
        var afterDispatchHealth = fx.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health);
        var afterDispatchMax = fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health);
        var afterDispatchAchievement = fx.Gameplay.Achievement.GetProgress(Unit, Achievement)[0].Current;
        var afterDispatchUnlocked = fx.Gameplay.Achievement.IsUnlocked(Unit, Achievement);

        Console.WriteLine("SAVE-ROLLBACK-DERIVED-STATE");
        Console.WriteLine($"before=equipped:{beforeEquip},health:{beforeHealth},max:{maxBefore}");
        Console.WriteLine($"achievement_before={achievementBefore}");
        Console.WriteLine($"load_status={result.Status}");
        Console.WriteLine($"before_event_dispatch=equipped:{beforeDispatchEquip},health:{beforeDispatchHealth},max:{fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health)}");
        Console.WriteLine($"achievement_before_event_dispatch={beforeDispatchAchievement},unlocked:{beforeDispatchUnlocked}");
        Console.WriteLine($"queued_event_count_before_dispatch={queuedEvents}");
        Console.WriteLine($"after_event_dispatch=equipped:{afterDispatchEquip},health:{afterDispatchHealth},max:{afterDispatchMax}");
        Console.WriteLine($"achievement_after_event_dispatch={afterDispatchAchievement},unlocked:{afterDispatchUnlocked}");
        Console.WriteLine("EXPECTED=load_status=PersistableThrew;after_event_dispatch=equipped:true,health:200,max:200;achievement remains progress=1 and locked");
        Console.WriteLine($"ACTUAL=load_status={result.Status};after_event_dispatch=equipped:{afterDispatchEquip},health:{afterDispatchHealth},max:{afterDispatchMax};achievement={afterDispatchAchievement},unlocked:{afterDispatchUnlocked};queued_event_count={queuedEvents};events={string.Join(",", fx.Events)}");
        Console.WriteLine("INTERPRETATION=state values are restored, but rollback emits ItemUnequipped then ItemEquipped after Achievement state was rolled back; the real AchievementHost counts the replay and unlocks at count 2");
    }

    private static void RunRollbackRequirementOrder()
    {
        // Production bootstraps pass the level to RulesAssembly but construct PlayerUnit
        // without copying it; use that exact unsynchronised setup here.
        var fx = BuildFixture(withRace: false, startLevel: 1, syncEntityLevel: false);
        fx.Gameplay.Carriers.Rules.Progression.AddXp(Unit, new Id("probe.xp"), 100);
        fx.Gameplay.Carriers.Inventory.AddItem(Unit, MaxHealthItem, 1);
        var instance = fx.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
        var equip = fx.Gameplay.Carriers.Equipment.Equip(Unit, instance, MaxHealthSlot);
        fx.Bus.DispatchPending();

        fx.Bus.DispatchPending();
        Console.WriteLine("UNIT-PROGRESSION-LEVEL-DIVERGENCE");
        Console.WriteLine($"rules_progression_level={fx.Gameplay.Carriers.Rules.Progression.GetLevel(Unit)};entity_level={fx.Player.Level}");
        Console.WriteLine($"equip_result={equip.Success};equip_reason={equip.Reason}");
        Console.WriteLine("EXPECTED=rules_progression_level=2;entity_level=2;equip_result=True for the configured level-2 requirement");
        Console.WriteLine($"ACTUAL=rules_progression_level={fx.Gameplay.Carriers.Rules.Progression.GetLevel(Unit)};entity_level={fx.Player.Level};equip_result={equip.Success};equip_reason={equip.Reason}");
        Console.WriteLine("INTERPRETATION=production bootstraps pass PlayerLevel only to Rules.RegisterUnit; PlayerUnit.Level remains its constructor default, so requirements.level and any IUnitAccess consumer read stale level");
    }

    private static string AuraState(ProbeFixture fx) =>
        $"hasAura={fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, Aura).ToString().ToLowerInvariant()},stacks={fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(Unit, Aura)},power={fx.Gameplay.Carriers.Rules.Stats.GetStat(Unit, AuraStat)}";

    private static ProbeFixture BuildFixture(bool withRace = true, int startLevel = 1, bool syncEntityLevel = true)
    {
        var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
        var registry = new DataRegistry(BuildDataSource(), bus, new DataRegistryOptions { FailOnUnknownTable = false });
        GameplaySchemaCatalog.RegisterAll(registry);
        var report = registry.LoadAll();
        if (report.IsBlocking) throw new InvalidOperationException(string.Join("; ", report.Issues));
        var world = new WorldSim(bus);
        var saveSystem = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.core_probe")), bus);
        var events = new List<string>();
        bus.Subscribe<ItemEquippedEvent>(CarriersEventKeys.ItemEquipped, _ => events.Add("ItemEquipped"));
        bus.Subscribe<ItemUnequippedEvent>(CarriersEventKeys.ItemUnequipped, _ => events.Add("ItemUnequipped"));
        bus.Subscribe<PowerChangedEvent>(PowerEventKeys.Changed, _ => events.Add("PowerChanged"));
        bus.Subscribe<StatChangedEvent>(StatBlockEventKeys.StatChanged, _ => events.Add("StatChanged"));
        bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, _ => events.Add("EntityDestroyed"));
        bus.Subscribe<AuraAppliedEvent>(RulesEventKeys.AuraApplied, _ => events.Add("AuraApplied"));
        bus.Subscribe<AuraRemovedEvent>(RulesEventKeys.AuraRemoved, _ => events.Add("AuraRemoved"));
        var gameplay = new GameplayAssembly(bus, registry, new RngHost(1), world, new StubSpatialQuery(), saveSystem,
            playerUnitProvider: () => Unit, playerFactionId: Faction);
        var player = new PlayerUnit(Unit, Map, Faction, Class) { Position = Vec2.Zero, Level = syncEntityLevel ? startLevel : 1, RaceId = withRace ? Race : null };
        world.AddEntity(player);
        gameplay.Carriers.Rules.RegisterUnit(Unit, Class, withRace ? Race : null, level: startLevel);
        bus.DispatchPending();
        events.Clear();
        return new ProbeFixture { Bus = bus, World = world, Gameplay = gameplay, Player = player, Events = events };
    }

    private static InMemoryDataSource BuildDataSource()
    {
        var auraRows = "[{\"id\":\"" + Aura.Value + "\",\"duration\":null,\"max_stacks\":1,\"effects\":[{\"kind\":\"mod_stat\",\"params\":{\"stat\":\"" + AuraStat.Value + "\",\"op\":\"flat\",\"value\":50}}]}]";
        var raceRows = "[{\"id\":\"" + Race.Value + "\",\"name_key\":\"l10n.race.core_probe\",\"stat_mods\":{\"" + AuraStat.Value + "\":10},\"passive_auras\":[\"" + Aura.Value + "\"]}]";
        var classRows = "[{\"id\":\"" + Class.Value + "\",\"name_key\":\"l10n.class.core_probe\",\"primary_stat\":\"" + AuraStat.Value + "\",\"base_stats\":{\"" + AuraStat.Value + "\":1,\"" + MaxHealthStat.Value + "\":100},\"power_types\":[\"arch.power.health\"],\"level_curve_ref\":\"prog.curve.core_probe\"}]";
        var templates = "[{\"id\":\"" + AuraItem.Value + "\",\"slot\":\"" + AuraSlot.Value + "\",\"quality\":\"" + Quality.Value + "\",\"item_level\":1,\"display_ref\":\"display.aura\",\"stack_size\":1,\"name_key\":\"l10n.item.aura\",\"grants\":{\"auras\":[\"" + Aura.Value + "\"]}},{\"id\":\"" + MaxHealthItem.Value + "\",\"slot\":\"" + MaxHealthSlot.Value + "\",\"quality\":\"" + Quality.Value + "\",\"item_level\":1,\"display_ref\":\"display.health\",\"stack_size\":1,\"name_key\":\"l10n.item.health\",\"requirements\":{\"level\":2},\"stats\":[{\"stat\":\"" + MaxHealthStat.Value + "\",\"op\":\"flat\",\"value\":100}]}]";
        var curveEntries = string.Join(",", Enumerable.Range(1, 10).Select(i => "{\"level\":" + i + ",\"xp_to_next\":100,\"growth\":{}}"));
        return new InMemoryDataSource()
            .Add("stat.definition", Envelope("stat.definition", "[{\"id\":\"stat.power\",\"name_key\":\"l10n.power\",\"group\":\"primary\",\"default_base\":1},{\"id\":\"stat.max_health\",\"name_key\":\"l10n.maxhp\",\"group\":\"primary\",\"default_base\":100}]"))
            .Add("arch.power_type", Envelope("arch.power_type", "[{\"id\":\"arch.power.health\",\"name_key\":\"l10n.health\",\"max_source\":{\"kind\":\"stat\",\"stat\":\"stat.max_health\"},\"start_full\":true}]"))
            .Add("arch.class", Envelope("arch.class", classRows))
            .Add("prog.level_curve", Envelope("prog.level_curve", "[{\"id\":\"prog.curve.core_probe\",\"max_level\":10,\"entries\":[" + curveEntries + "]}]"))
            .Add("arch.race", Envelope("arch.race", raceRows))
            .Add("achv.def", Envelope("achv.def", "[{\"id\":\"" + Achievement.Value + "\",\"name_key\":\"l10n.achv.core_probe\",\"criteria\":[{\"type\":\"custom_event\",\"observe_event\":\"item.equipped\",\"count\":2}]}]"))
            .Add("skill.aura_def", Envelope("skill.aura_def", auraRows))
            .Add("item.slot_definition", Envelope("item.slot_definition", "[{\"id\":\"" + AuraSlot.Value + "\",\"name_key\":\"l10n.slot.aura\"},{\"id\":\"" + MaxHealthSlot.Value + "\",\"name_key\":\"l10n.slot.hp\"}]"))
            .Add("item.quality_definition", Envelope("item.quality_definition", "[{\"id\":\"" + Quality.Value + "\",\"name_key\":\"l10n.quality\"}]"))
            .Add("item.template", Envelope("item.template", templates))
            .Add("item.budget_curve", Envelope("item.budget_curve", "[{\"id\":\"item.budget.default\",\"entries\":[{\"item_level\":1,\"budget\":200}]}]"))
            .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
            .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"));
    }

    private static string Envelope(string table, string rows) => "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";
    private static JsonObject Meta() => new JsonObjectBuilder().Add("save_version", new JsonNumber(1)).Add("slot_id", new JsonString("slot.core_probe")).Add("created_at", new JsonString("probe")).Add("updated_at", new JsonString("probe")).Add("game_id", new JsonString("game.core_probe")).Build();
}
