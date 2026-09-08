using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Item;
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

sealed class ProbeFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    public string GetUserDataDir() => "user://";
    public string GetContentRootDir() => "content://";
    public string? ReadText(string path) => _files.TryGetValue(path, out var value) ? value : null;
    public bool WriteTextAtomic(string path, string content) { _files[path] = content; return true; }
    public bool Exists(string path) => _files.ContainsKey(path);
    public IReadOnlyList<string> ListFiles(string dirPath) => Array.Empty<string>();
    public bool DeleteFile(string path) => _files.Remove(path);
}

sealed class ProbeThrowingPersistable : IPersistable
{
    public string SectionKey => "zz.followup.failure";
    public JsonValue Save() => JsonNull.Instance;
    public void Load(JsonValue data) => throw new InvalidOperationException("followup failure");
}

sealed class FollowupFixture
{
    public EventBus Bus = null!;
    public ProbeFileSystem Fs = null!;
    public SaveSystem Save = null!;
    public GameplayAssembly Gameplay = null!;
    public PlayerUnit Player = null!;
}

static class FollowupCoreProbe
{
    static readonly Id Unit = new("unit.followup_core");
    static readonly Id Map = new("world.followup_core");
    static readonly Id Faction = new("fac.followup_core");
    static readonly Id Class = new("arch.class.followup_core");
    static readonly Id Curve = new("prog.curve.followup_core");
    static readonly Id Rating = new("stat.followup_rating");
    static readonly Id MaxHealth = new("stat.max_health");
    static readonly Id Health = WellKnownPowers.Health;
    static readonly Id Slot = new("item.slot.followup");
    static readonly Id Item = new("item.followup_level2");
    static readonly Id Quality = new("item.quality.followup");
    static readonly Id RaceA = new("arch.race.followup_a");
    static readonly Id RaceB = new("arch.race.followup_b");
    static readonly Id StatRacePower = new("stat.followup_race_power");
    static readonly Id AuraA = new("skill.aura_def.followup_a");
    static readonly Id AuraB = new("skill.aura_def.followup_b");

    public static void Main()
    {
        Console.WriteLine("FOLLOWUP-CORE-PROBE baseline=e070e3f version=1.8.0");
        RunSuccessfulLoadEventSuppression();
        RunSuccessfulLoadPowerMaxSuppression();
        RunRollbackEquipmentBeforeProgression();
        RunSameMapRaceLoad();
    }

    static FollowupFixture Build(int level = 1, Id? raceId = null)
    {
        var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
        var registry = new DataRegistry(Data(), bus, new DataRegistryOptions { FailOnUnknownTable = false });
        GameplaySchemaCatalog.RegisterAll(registry);
        var report = registry.LoadAll();
        if (report.IsBlocking) throw new InvalidOperationException(string.Join(";", report.Issues));
        var world = new WorldSim(bus);
        var fs = new ProbeFileSystem();
        var save = new SaveSystem(fs, new SaveSystemOptions(new Id("game.followup_core")), bus);
        var gameplay = new GameplayAssembly(bus, registry, new RngHost(1), world, new StubSpatialQuery(), save,
            playerUnitProvider: () => Unit, playerFactionId: Faction,
            statOptions: new StatHostOptions { EnableRatingConversion = true });
        var player = new PlayerUnit(Unit, Map, Faction, Class) { Position = Vec2.Zero, RaceId = raceId };
        world.AddEntity(player);
        gameplay.Carriers.Rules.RegisterUnit(Unit, Class, raceId: raceId, level: level);
        gameplay.RegisterPersistables(save, player);
        return new FollowupFixture { Bus = bus, Fs = fs, Save = save, Gameplay = gameplay, Player = player };
    }

    static void RunSameMapRaceLoad()
    {
        var fxA = Build(level: 1, raceId: RaceA);
        fxA.Bus.DispatchPending();
        var statAtA = fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatRacePower);
        var auraAAtA = fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraA);
        var auraBAtA = fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraB);
        var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.followup_race_a"), "probe"));

        var fxB = Build(level: 1, raceId: RaceB);
        fxB.Bus.DispatchPending();
        var statAtB = fxB.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatRacePower);
        var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.followup_race_b"), "probe"));
        var savedBJson = fxB.Fs.ReadText("user://saves/slot.followup_race_b.json")!;
        fxA.Fs.WriteTextAtomic("user://saves/slot.followup_race_b.json", savedBJson);

        var loaded = fxA.Gameplay.RestoreFromSlot(new Id("slot.followup_race_b"));
        fxA.Bus.DispatchPending();
        var statAfter = fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatRacePower);
        var auraAAfter = fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraA);
        var auraBAfter = fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraB);
        Console.WriteLine("SAME-MAP-RACE-SAVE-LOAD");
        Console.WriteLine($"save_a_valid={savedA.Success};save_b_valid={savedB.Success};race_b_oracle_stat={statAtB};load_status={loaded.Status};map={fxA.Player.MapId.Value}");
        Console.WriteLine($"before_load_race={RaceA.Value};stat={statAtA};auraA={auraAAtA};auraB={auraBAtA}");
        Console.WriteLine($"after_load_race={fxA.Player.RaceId?.Value ?? "<null>"};stat={statAfter};auraA={auraAAfter};auraB={auraBAfter}");
        Console.WriteLine("EXPECTED=save_a_valid=True;save_b_valid=True;race_b_oracle_stat=91;load_status=Loaded;after_load_race=arch.race.followup_b;stat=91;auraA=False;auraB=True");
        Console.WriteLine("INTERPRETATION=Two separately built valid role saves were used. Same-map RestoreFromSlot on A loads B's race field but does not reapply B stat_mods/aura or clear A state.");
    }

    static void RunSuccessfulLoadEventSuppression()
    {
        var fx = Build(level: 1);
        fx.Gameplay.Carriers.Rules.Stats.SetBase(Unit, Rating, 50);
        fx.Gameplay.Carriers.Rules.Progression.RestoreState(Unit, Curve, 10, 0);
        fx.Bus.DispatchPending();
        var ratingAt10 = fx.Gameplay.Carriers.Rules.Stats.GetStat(Unit, Rating);
        var saved = fx.Save.Save(new SaveRequest(new Id("slot.followup_rating"), "probe"));
        fx.Gameplay.Carriers.Rules.Progression.RestoreState(Unit, Curve, 1, 0);
        fx.Bus.DispatchPending();
        var ratingAt1 = fx.Gameplay.Carriers.Rules.Stats.GetStat(Unit, Rating);
        var loaded = fx.Save.Load(new Id("slot.followup_rating"));
        var ratingAfterLoadBeforeDispatch = fx.Gameplay.Carriers.Rules.Stats.GetStat(Unit, Rating);
        fx.Bus.DispatchPending();
        var ratingAfterLoad = fx.Gameplay.Carriers.Rules.Stats.GetStat(Unit, Rating);
        Console.WriteLine("SUCCESSFUL-LOAD-INTERNAL-EVENTS");
        Console.WriteLine($"save_success={saved.Success};load_status={loaded.Status}");
        Console.WriteLine($"before_save_level=10;rating_at_10={ratingAt10}");
        Console.WriteLine($"mutated_level=1;rating_at_1={ratingAt1}");
        Console.WriteLine($"after_load_level={fx.Gameplay.Carriers.Rules.Progression.GetLevel(Unit)};entity_level={fx.Player.Level};rating_after_load_before_dispatch={ratingAfterLoadBeforeDispatch};rating_after_dispatch={ratingAfterLoad}");
        Console.WriteLine("EXPECTED=load_status=Loaded;after_load_level=10;rating_after_load=10");
        Console.WriteLine("INTERPRETATION=ProgressionPersistable.Load calls RestoreState, whose ProgressionRestoredEvent is consumed by RulesAssembly to recompute rating; if SaveSystem suppression drops it, level restores but cached rating remains 5.");
    }

    static void RunRollbackEquipmentBeforeProgression()
    {
        var fx = Build(level: 2);
        fx.Save.RegisterPersistable(new ProbeThrowingPersistable());
        fx.Gameplay.Carriers.Inventory.AddItem(Unit, Item, 1);
        var instance = fx.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
        var equip = fx.Gameplay.Carriers.Equipment.Equip(Unit, instance, Slot);
        fx.Bus.DispatchPending();
        var baselineInventory = fx.Gameplay.Carriers.Inventory.ListItems(Unit).Count;
        var saved = fx.Save.Save(new SaveRequest(new Id("slot.followup_rollback"), "probe"));
        var savedDoc = (JsonObject)JsonReader.Parse(fx.Fs.ReadText("user://saves/slot.followup_rollback.json")!);
        var sections = (JsonObject)savedDoc["sections"];
        var lowProgression = new JsonObjectBuilder().Add("curve_id", new JsonString(Curve.Value)).Add("level", new JsonNumber(1)).Add("xp", new JsonNumber(0)).Build();
        var lowEquipment = new JsonObjectBuilder().Build();
        var patchedSections = new JsonObjectBuilder();
        foreach (var entry in sections)
        {
            patchedSections.Add(entry.Key,
                entry.Key == SaveSections.PlayerProgression ? lowProgression :
                entry.Key == SaveSections.PlayerEquipment ? lowEquipment : entry.Value);
        }
        var patched = new JsonObjectBuilder();
        foreach (var entry in savedDoc)
        {
            patched.Add(entry.Key, entry.Key == "sections" ? patchedSections.Build() : entry.Value);
        }
        fx.Fs.WriteTextAtomic("user://saves/slot.followup_rollback.json", JsonWriter.Write(patched.Build()));

        var loaded = fx.Save.Load(new Id("slot.followup_rollback"));
        var equippedAfter = fx.Gameplay.Carriers.Equipment.GetEquipped(Unit, Slot).HasValue;
        var inventoryAfter = fx.Gameplay.Carriers.Inventory.ListItems(Unit).Count;
        Console.WriteLine("ROLLBACK-EQUIPMENT-BEFORE-PROGRESSION");
        Console.WriteLine($"initial_equip={equip.Success};baseline_inventory_count={baselineInventory};save_success={saved.Success};load_status={loaded.Status}");
        Console.WriteLine($"after_level={fx.Gameplay.Carriers.Rules.Progression.GetLevel(Unit)};entity_level={fx.Player.Level};equipped_after={equippedAfter};inventory_count_after={inventoryAfter}");
        Console.WriteLine($"EXPECTED=load_status=PersistableThrew;after_level=2;entity_level=2;equipped_after=true;inventory_count_after={baselineInventory}");
        Console.WriteLine("INTERPRETATION=rollback order restores Equipment before Progression; at level 1 the level-2 item cannot be re-equipped, so the pre-load equipped state may be lost even though Progression later restores level 2.");
    }

    static void RunSuccessfulLoadPowerMaxSuppression()
    {
        var fx = Build(level: 2);
        fx.Gameplay.Carriers.Inventory.AddItem(Unit, Item, 1);
        var instance = fx.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
        var equip = fx.Gameplay.Carriers.Equipment.Equip(Unit, instance, Slot);
        fx.Bus.DispatchPending();
        var maxWithEquip = fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health);
        fx.Gameplay.Carriers.Rules.Powers.ModifyPower(Unit, Health,
            maxWithEquip - fx.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health), new Id("probe.fill_health"));
        fx.Bus.DispatchPending();
        var healthWithEquip = fx.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health);
        var saved = fx.Save.Save(new SaveRequest(new Id("slot.followup_vitals"), "probe"));
        fx.Gameplay.Carriers.Equipment.Unequip(Unit, Slot);
        fx.Bus.DispatchPending();
        var maxAfterUnequip = fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health);
        var loaded = fx.Save.Load(new Id("slot.followup_vitals"));
        var maxAfterLoadBeforeDispatch = fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health);
        var healthAfterLoadBeforeDispatch = fx.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health);
        fx.Bus.DispatchPending();
        var maxAfterLoad = fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health);
        var healthAfterLoad = fx.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health);
        Console.WriteLine("SUCCESSFUL-LOAD-POWER-MAX-INTERNAL-EVENT");
        Console.WriteLine($"equip_success={equip.Success};save_success={saved.Success};load_status={loaded.Status}");
        Console.WriteLine($"saved_max={maxWithEquip};saved_health={healthWithEquip};mutated_max={maxAfterUnequip}");
        Console.WriteLine($"after_load_before_dispatch_max={maxAfterLoadBeforeDispatch};after_load_before_dispatch_health={healthAfterLoadBeforeDispatch}");
        Console.WriteLine($"after_load_max={maxAfterLoad};after_load_health={healthAfterLoad};equipped_after={fx.Gameplay.Carriers.Equipment.GetEquipped(Unit, Slot).HasValue}");
        Console.WriteLine("EXPECTED=load_status=Loaded;after_load_max=200;after_load_health=200;equipped_after=true");
        Console.WriteLine("INTERPRETATION=Equipment.Load publishes StatChanged while SaveSystem suppression is active; RulesAssembly.StatChanged subscription is the production path that recomputes PowerHost max. PlayerVitals.Load then applies saved health against that max, so dropped StatChanged leaves derived max/health stale.");
    }

    static InMemoryDataSource Data()
    {
        string E(string table, string rows) => "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";
        var levels = string.Join(",", Enumerable.Range(1, 10).Select(i => "{\"level\":" + i + ",\"xp_to_next\":100,\"growth\":{}}"));
        return new InMemoryDataSource()
            .Add("stat.definition", E("stat.definition", "[{\"id\":\"stat.followup_rating\",\"name_key\":\"l10n.rating\",\"group\":\"primary\",\"default_base\":0,\"is_rating\":true,\"rating_conversion_ref\":\"stat.rating.followup\"},{\"id\":\"stat.max_health\",\"name_key\":\"l10n.max\",\"group\":\"primary\",\"default_base\":100},{\"id\":\"stat.followup_race_power\",\"name_key\":\"l10n.race.power\",\"group\":\"primary\",\"default_base\":1}]"))
            .Add("stat.rating_conversion", E("stat.rating_conversion", "[{\"id\":\"stat.rating.followup\",\"entries\":[{\"level\":1,\"points_per_percent\":10},{\"level\":10,\"points_per_percent\":5}]}]"))
            .Add("arch.power_type", E("arch.power_type", "[{\"id\":\"arch.power.health\",\"name_key\":\"l10n.health\",\"max_source\":{\"kind\":\"stat\",\"stat\":\"stat.max_health\"},\"start_full\":true}]"))
            .Add("arch.class", E("arch.class", "[{\"id\":\"" + Class.Value + "\",\"name_key\":\"l10n.class\",\"primary_stat\":\"stat.max_health\",\"base_stats\":{},\"power_types\":[\"arch.power.health\"],\"level_curve_ref\":\"" + Curve.Value + "\"}]"))
            .Add("arch.race", E("arch.race", "[{\"id\":\"" + RaceA.Value + "\",\"name_key\":\"l10n.race.a\",\"stat_mods\":{\"" + StatRacePower.Value + "\":10},\"passive_auras\":[\"" + AuraA.Value + "\"]},{\"id\":\"" + RaceB.Value + "\",\"name_key\":\"l10n.race.b\",\"stat_mods\":{\"" + StatRacePower.Value + "\":20},\"passive_auras\":[\"" + AuraB.Value + "\"]}]"))
            .Add("skill.aura_def", E("skill.aura_def", "[{\"id\":\"" + AuraA.Value + "\",\"duration\":60,\"max_stacks\":1,\"effects\":[{\"kind\":\"mod_stat\",\"params\":{\"stat\":\"" + StatRacePower.Value + "\",\"op\":\"flat\",\"value\":50}}]},{\"id\":\"" + AuraB.Value + "\",\"duration\":60,\"max_stacks\":1,\"effects\":[{\"kind\":\"mod_stat\",\"params\":{\"stat\":\"" + StatRacePower.Value + "\",\"op\":\"flat\",\"value\":70}}]}]"))
            .Add("prog.level_curve", E("prog.level_curve", "[{\"id\":\"" + Curve.Value + "\",\"max_level\":10,\"entries\":[" + levels + "]}]"))
            .Add("item.slot_definition", E("item.slot_definition", "[{\"id\":\"" + Slot.Value + "\",\"name_key\":\"l10n.slot\"}]"))
            .Add("item.quality_definition", E("item.quality_definition", "[{\"id\":\"" + Quality.Value + "\",\"name_key\":\"l10n.quality\"}]"))
            .Add("item.template", E("item.template", "[{\"id\":\"" + Item.Value + "\",\"slot\":\"" + Slot.Value + "\",\"quality\":\"" + Quality.Value + "\",\"item_level\":1,\"display_ref\":\"display.followup\",\"stack_size\":1,\"name_key\":\"l10n.item\",\"requirements\":{\"level\":2},\"stats\":[{\"stat\":\"stat.max_health\",\"op\":\"flat\",\"value\":100}]}]"))
            .Add("item.budget_curve", E("item.budget_curve", "[{\"id\":\"item.budget.default\",\"entries\":[{\"item_level\":1,\"budget\":200}]}]"))
            .Add("combat.hit_table_config", E("combat.hit_table_config", "[]"))
            .Add("combat.resist_curve", E("combat.resist_curve", "[]"));
    }
}
