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
using Presentation.Assembly;
using Presentation.VfxSfx.Core;

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

sealed class ProbeThrowingRebuilder : IDerivedStateRebuilder
{
    public int BeforeCalls { get; private set; }
    public int SectionCalls { get; private set; }
    public void BeforeLoad() { BeforeCalls++; throw new InvalidOperationException("probe BeforeLoad failure"); }
    public void OnSectionLoaded(string sectionKey) { SectionCalls++; throw new InvalidOperationException("probe OnSectionLoaded failure"); }
}

sealed class FollowupFixture
{
    public EventBus Bus = null!;
    public ProbeFileSystem Fs = null!;
    public SaveSystem Save = null!;
    public InMemorySaveDiagnostics Diagnostics = null!;
    public GameplayAssembly Gameplay = null!;
    public DataRegistry Registry = null!;
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
    static readonly Id ClassA = new("arch.class.followup_a");
    static readonly Id ClassB = new("arch.class.followup_b");
    static readonly Id StatClassPower = new("stat.followup_class_power");
    static readonly Id StatClassLegacy = new("stat.followup_class_legacy");
    static readonly Id Mana = new("arch.power.mana");
    static readonly Id MainHandSlot = new("item.slot.main_hand");
    static readonly Id WeaponA = new("item.weapon.followup_a");
    static readonly Id WeaponB = new("item.weapon.followup_b");
    static readonly Id WeaponStyleA = new("display.weapon_style.followup_a");
    static readonly Id WeaponStyleB = new("display.weapon_style.followup_b");

    public static void Main()
    {
        Console.WriteLine("FOLLOWUP-CORE-PROBE baseline=6739f50 version=1.11.0");
        RunSuccessfulLoadEventSuppression();
        RunSuccessfulLoadPowerMaxSuppression();
        RunRollbackEquipmentBeforeProgression();
        RunSameMapRaceLoad();
        RunDerivedHookExceptionBoundary();
        RunRollbackDerivedStateAfterFailure();
        RunRollbackVitalsAfterEquipment();
        RunSameMapClassLoad();
        RunRollbackClassPowerState();
        RunRollbackClassBeforeRaceFailure();
        RunWeaponStyleCacheAfterLoad();
        RunInventoryViewModelAfterLoad();
    }

    static FollowupFixture Build(int level = 1, Id? raceId = null, Id? classId = null)
    {
        var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
        var registry = new DataRegistry(Data(), bus, new DataRegistryOptions { FailOnUnknownTable = false });
        PresentationSchemaCatalog.RegisterAll(registry);
        var report = registry.LoadAll();
        if (report.IsBlocking) throw new InvalidOperationException(string.Join(";", report.Issues));
        var world = new WorldSim(bus);
        var fs = new ProbeFileSystem();
        var diagnostics = new InMemorySaveDiagnostics();
        var save = new SaveSystem(fs, new SaveSystemOptions(new Id("game.followup_core")), bus, diagnostics);
        var gameplay = new GameplayAssembly(bus, registry, new RngHost(1), world, new StubSpatialQuery(), save,
            playerUnitProvider: () => Unit, playerFactionId: Faction,
            statOptions: new StatHostOptions { EnableRatingConversion = true });
        var resolvedClass = classId ?? Class;
        var player = new PlayerUnit(Unit, Map, Faction, resolvedClass) { Position = Vec2.Zero, RaceId = raceId };
        world.AddEntity(player);
        gameplay.Carriers.Rules.RegisterUnit(Unit, resolvedClass, raceId: raceId, level: level);
        gameplay.RegisterPersistables(save, player);
        return new FollowupFixture { Bus = bus, Fs = fs, Save = save, Diagnostics = diagnostics, Gameplay = gameplay, Player = player, Registry = registry };
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
        Console.WriteLine("ORACLE-CURRENT=save_a_valid=True;save_b_valid=True;race_b_oracle_stat=91;load_status=Loaded;after_load_race=arch.race.followup_b;stat=91;auraA=False;auraB=True");
        Console.WriteLine("INTERPRETATION-CURRENT=Two separately built valid role saves were used. Same-map RestoreFromSlot on A loads B and rebuilds B stat_mods/aura while clearing A state.");
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
        Console.WriteLine("ORACLE-CURRENT=load_status=Loaded;after_load_level=10;rating_after_load=10");
        Console.WriteLine("INTERPRETATION-CURRENT=SaveSystem suppression remains active, but v1.11 DerivedStateRebuilder directly recomputes after the relevant section; rating follows the restored level.");
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
        Console.WriteLine($"ORACLE-CURRENT=load_status=PersistableThrew;after_level=2;entity_level=2;equipped_after=true;inventory_count_after={baselineInventory}");
        Console.WriteLine("INTERPRETATION-CURRENT=Positive rollback restores Progression before Equipment, so the level-2 item is re-equipped after the level is restored.");
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
        Console.WriteLine("ORACLE-CURRENT=load_status=Loaded;after_load_max=200;after_load_health=200;equipped_after=true");
        Console.WriteLine("INTERPRETATION-CURRENT=The v1.11 section hook recomputes Power max before PlayerVitals.Load, so saved health is applied against max 200 despite event suppression.");
    }

    static void RunDerivedHookExceptionBoundary()
    {
        var fx = Build(level: 1);
        fx.Gameplay.Carriers.Rules.Stats.SetBase(Unit, Rating, 50);
        fx.Gameplay.Carriers.Rules.Progression.RestoreState(Unit, Curve, 10, 0);
        fx.Bus.DispatchPending();
        var saved = fx.Save.Save(new SaveRequest(new Id("slot.followup_hook_exception"), "probe"));
        fx.Gameplay.Carriers.Rules.Progression.RestoreState(Unit, Curve, 1, 0);
        fx.Bus.DispatchPending();
        var throwing = new ProbeThrowingRebuilder();
        fx.Save.SetDerivedStateRebuilder(throwing);
        var loaded = fx.Save.Load(new Id("slot.followup_hook_exception"));
        fx.Bus.DispatchPending();
        Console.WriteLine("DERIVED-HOOK-EXCEPTION-BOUNDARY");
        Console.WriteLine($"save_success={saved.Success};load_status={loaded.Status};before_calls={throwing.BeforeCalls};section_calls={throwing.SectionCalls};warning_count={fx.Diagnostics.Warnings.Count};rating_after={fx.Gameplay.Carriers.Rules.Stats.GetStat(Unit, Rating)}");
        Console.WriteLine("ORACLE-CURRENT=save_success=True;load_status=Loaded;before_calls=1;section_calls>0;warning_count>0");
        Console.WriteLine("INTERPRETATION=SaveSystem contains BeforeLoad and OnSectionLoaded exceptions, returns Loaded, and records warnings; replacing the production rebuilder intentionally leaves derived state at the stale value.");
    }

    static void RunRollbackDerivedStateAfterFailure()
    {
        var fxA = Build(level: 1, raceId: RaceA);
        fxA.Bus.DispatchPending();
        var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.followup_rollback_race_a"), "probe"));
        fxA.Save.RegisterPersistable(new ProbeThrowingPersistable());
        var savedAWithFailure = fxA.Save.Save(new SaveRequest(new Id("slot.followup_rollback_race_a"), "probe"));
        var fxB = Build(level: 1, raceId: RaceB);
        fxB.Bus.DispatchPending();
        var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.followup_rollback_race_b"), "probe"));
        fxA.Fs.WriteTextAtomic("user://saves/slot.followup_rollback_race_b.json", fxB.Fs.ReadText("user://saves/slot.followup_rollback_race_b.json")!);

        var loaded = fxA.Save.Load(new Id("slot.followup_rollback_race_b"));
        fxA.Bus.DispatchPending();
        var statAfter = fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatRacePower);
        var auraAAfter = fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraA);
        var auraBAfter = fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraB);
        Console.WriteLine("ROLLBACK-DERIVED-STATE-RACE");
        Console.WriteLine($"save_a_initial={savedA.Success};save_a_with_failure={savedAWithFailure.Success};save_b_valid={savedB.Success};load_status={loaded.Status};after_race={fxA.Player.RaceId?.Value ?? "<null>"};stat={statAfter};auraA={auraAAfter};auraB={auraBAfter}");
        Console.WriteLine("ORACLE-CURRENT=load_status=PersistableThrew;after_race=arch.race.followup_a;stat=61;auraA=True;auraB=False");
        Console.WriteLine("INTERPRETATION-CURRENT=Forward load applies B and its rebuild hook, then the missing custom section throws. v1.11 positive rollback restores A fields and re-runs the derived hook, restoring A stat/aura.");
    }

    static void RunRollbackVitalsAfterEquipment()
    {
        var fxA = Build(level: 2);
        fxA.Gameplay.Carriers.Inventory.AddItem(Unit, Item, 1);
        var instance = fxA.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
        var equip = fxA.Gameplay.Carriers.Equipment.Equip(Unit, instance, Slot);
        fxA.Bus.DispatchPending();
        var maxA = fxA.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health);
        fxA.Gameplay.Carriers.Rules.Powers.ModifyPower(Unit, Health, maxA - fxA.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health), new Id("probe.fill_health"));
        fxA.Bus.DispatchPending();
        var healthA = fxA.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health);
        var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.followup_rollback_vitals_a"), "probe"));

        var fxB = Build(level: 1);
        fxB.Bus.DispatchPending();
        var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.followup_rollback_vitals_b"), "probe"));
        var savedBDoc = (JsonObject)JsonReader.Parse(fxB.Fs.ReadText("user://saves/slot.followup_rollback_vitals_b.json")!);
        var sections = (JsonObject)savedBDoc["sections"];
        var patchedSections = new JsonObjectBuilder();
        foreach (var entry in sections)
        {
            patchedSections.Add(entry.Key, entry.Key == SaveSections.PlayerKnownSkills ? new JsonString("fault") : entry.Value);
        }
        var patched = new JsonObjectBuilder();
        foreach (var entry in savedBDoc)
        {
            patched.Add(entry.Key, entry.Key == "sections" ? patchedSections.Build() : entry.Value);
        }
        fxA.Fs.WriteTextAtomic("user://saves/slot.followup_rollback_vitals_b.json", JsonWriter.Write(patched.Build()));

        var loaded = fxA.Save.Load(new Id("slot.followup_rollback_vitals_b"));
        var maxAfter = fxA.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health);
        var healthAfter = fxA.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health);
        var equippedAfter = fxA.Gameplay.Carriers.Equipment.GetEquipped(Unit, Slot).HasValue;
        Console.WriteLine("ROLLBACK-DERIVED-STATE-KNOWN-SKILLS-BEFORE-VITALS");
        Console.WriteLine($"fault_section={SaveSections.PlayerKnownSkills};equip_success={equip.Success};saved_max={maxA};saved_health={healthA};save_a_valid={savedA.Success};save_b_valid={savedB.Success};load_status={loaded.Status};after_level={fxA.Player.Level};equipped_after={equippedAfter};max_after={maxAfter};health_after={healthAfter}");
        Console.WriteLine("ORACLE-CURRENT=load_status=PersistableThrew;after_level=2;equipped_after=True;max_after=200;health_after=200");
        Console.WriteLine("INTERPRETATION-CURRENT=The B save is valid before fault injection; malformed player.known_skills fails after equipment and before player.vitals. v1.11 positive rollback restores equipment, re-runs the derived hook, and restores max/health to 200.");
    }

    static void RunSameMapClassLoad()
    {
        var fxA = Build(level: 1, classId: ClassA);
        fxA.Bus.DispatchPending();
        var statA = fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassPower);
        var legacyA = fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassLegacy);
        var manaA = fxA.Gameplay.Carriers.Rules.Powers.HasPower(Unit, Mana);
        var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.followup_class_a"), "probe"));
        var fxB = Build(level: 1, classId: ClassB);
        fxB.Bus.DispatchPending();
        var statB = fxB.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassPower);
        var legacyB = fxB.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassLegacy);
        var manaB = fxB.Gameplay.Carriers.Rules.Powers.HasPower(Unit, Mana);
        var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.followup_class_b"), "probe"));
        fxA.Fs.WriteTextAtomic("user://saves/slot.followup_class_b.json", fxB.Fs.ReadText("user://saves/slot.followup_class_b.json")!);

        var loaded = fxA.Gameplay.RestoreFromSlot(new Id("slot.followup_class_b"));
        fxA.Bus.DispatchPending();
        var statAfter = fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassPower);
        var legacyAfter = fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassLegacy);
        var manaAfter = fxA.Gameplay.Carriers.Rules.Powers.HasPower(Unit, Mana);
        Console.WriteLine("SAME-MAP-CLASS-SAVE-LOAD");
        Console.WriteLine($"save_a_valid={savedA.Success};save_b_valid={savedB.Success};a_stat={statA};a_legacy={legacyA};a_mana={manaA};b_oracle_stat={statB};b_oracle_legacy={legacyB};b_oracle_mana={manaB};load_status={loaded.Status};after_class={fxA.Player.ArchetypeId.Value};after_stat={statAfter};after_legacy={legacyAfter};after_mana={manaAfter}");
        Console.WriteLine("ORACLE-CURRENT=save_a_valid=True;save_b_valid=True;b_oracle_stat=20;b_oracle_legacy=0;b_oracle_mana=False;load_status=Loaded;after_class=arch.class.followup_b;after_stat=20;after_legacy=0;after_mana=False");
        Console.WriteLine("INTERPRETATION-CURRENT=Class B omits A's legacy base-stat key and mana power type. v1.11 ReloadArchetypeAndRace rebuilds the class contribution and resource set to match B.");
    }

    static void RunRollbackClassPowerState()
    {
        var fxA = Build(level: 1, raceId: RaceA, classId: ClassA);
        fxA.Bus.DispatchPending();
        var powersA = fxA.Gameplay.Carriers.Rules.Powers;
        powersA.SetInCombat(Unit, true);
        powersA.ModifyPower(Unit, Mana, 30, new Id("probe.mana_spend"));
        fxA.Bus.DispatchPending();
        var manaBefore = powersA.GetPower(Unit, Mana);
        var savedAInitial = fxA.Save.Save(new SaveRequest(new Id("slot.followup_rollback_class_power_a"), "probe"));
        fxA.Save.RegisterPersistable(new ProbeThrowingPersistable());
        var savedAWithFailure = fxA.Save.Save(new SaveRequest(new Id("slot.followup_rollback_class_power_a"), "probe"));

        var fxB = Build(level: 1, raceId: RaceA, classId: ClassB);
        fxB.Bus.DispatchPending();
        var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.followup_rollback_class_power_b"), "probe"));
        fxA.Fs.WriteTextAtomic("user://saves/slot.followup_rollback_class_power_b.json", fxB.Fs.ReadText("user://saves/slot.followup_rollback_class_power_b.json")!);

        var loaded = fxA.Gameplay.RestoreFromSlot(new Id("slot.followup_rollback_class_power_b"));
        fxA.Bus.DispatchPending();
        var manaAfterRollback = powersA.HasPower(Unit, Mana) ? powersA.GetPower(Unit, Mana) : -1;
        powersA.Advance(Unit, 1);
        var manaAfterAdvance = powersA.HasPower(Unit, Mana) ? powersA.GetPower(Unit, Mana) : -1;
        Console.WriteLine("ROLLBACK-CLASS-POWER-CURRENT-AND-COMBAT-STATE");
        Console.WriteLine($"save_a_initial={savedAInitial.Success};save_a_with_failure={savedAWithFailure.Success};save_b_valid={savedB.Success};race_before={RaceA.Value};race_after={fxA.Player.RaceId?.Value ?? "<null>"};class_after={fxA.Player.ArchetypeId.Value};mana_before={manaBefore};load_status={loaded.Status};mana_present_after={powersA.HasPower(Unit, Mana)};mana_after_rollback={manaAfterRollback};mana_after_advance_1={manaAfterAdvance}");
        Console.WriteLine("ORACLE-CURRENT=load_status=PersistableThrew;race_after=arch.race.followup_a;class_after=arch.class.followup_a;mana_present_after=True;mana_after_rollback=30;mana_after_advance_1=30");
        Console.WriteLine("INTERPRETATION-CURRENT=The valid A save has the same race, class A, and partially consumed Mana=30 while in combat. Forward B load removes Mana, then failure rollback rebuilds class A resources, but PlayerVitals persists health only; re-registration resets Mana and InCombat instead of restoring the saved current value and combat flag, demonstrated by Mana changing again after Advance(1).");
    }

    static void RunRollbackClassBeforeRaceFailure()
    {
        var fxA = Build(level: 1, raceId: RaceA, classId: ClassA);
        fxA.Bus.DispatchPending();
        var aurasA = fxA.Gameplay.Carriers.Rules.Skill.AuraQuery;
        fxA.Gameplay.Carriers.Rules.Powers.SetInCombat(Unit, true);
        fxA.Gameplay.Carriers.Rules.Powers.ModifyPower(Unit, Mana, 30, new Id("probe.race_failure_mana"));
        fxA.Bus.DispatchPending();
        var manaBefore = fxA.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Mana);
        var auraBefore = aurasA.HasAura(Unit, AuraA);
        var auraStacksBefore = aurasA.GetStacks(Unit, AuraA);
        var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.followup_rollback_class_race_failure_a"), "probe"));
        fxA.Save.RegisterPersistable(new ProbeThrowingPersistable());
        var savedAWithFailure = fxA.Save.Save(new SaveRequest(new Id("slot.followup_rollback_class_race_failure_a"), "probe"));

        var fxB = Build(level: 1, raceId: RaceA, classId: ClassB);
        fxB.Bus.DispatchPending();
        var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.followup_rollback_class_race_failure_b"), "probe"));
        var savedBDoc = (JsonObject)JsonReader.Parse(fxB.Fs.ReadText("user://saves/slot.followup_rollback_class_race_failure_b.json")!);
        var sections = (JsonObject)savedBDoc["sections"];
        var patchedSections = new JsonObjectBuilder();
        foreach (var entry in sections)
        {
            patchedSections.Add(entry.Key, entry.Key == SaveSections.PlayerRaceId ? new JsonNumber(123) : entry.Value);
        }
        var patched = new JsonObjectBuilder();
        foreach (var entry in savedBDoc)
        {
            patched.Add(entry.Key, entry.Key == "sections" ? patchedSections.Build() : entry.Value);
        }
        fxA.Fs.WriteTextAtomic("user://saves/slot.followup_rollback_class_race_failure_b.json", JsonWriter.Write(patched.Build()));

        var loaded = fxA.Gameplay.RestoreFromSlot(new Id("slot.followup_rollback_class_race_failure_b"));
        fxA.Bus.DispatchPending();
        var manaAfter = fxA.Gameplay.Carriers.Rules.Powers.HasPower(Unit, Mana) ? fxA.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Mana) : -1;
        var auraAfter = aurasA.HasAura(Unit, AuraA);
        var auraStacksAfter = aurasA.GetStacks(Unit, AuraA);
        Console.WriteLine("ROLLBACK-CLASS-WRITTEN-BEFORE-RACE-LOAD-FAILURE");
        Console.WriteLine($"save_a_valid={savedA.Success};save_a_with_failure={savedAWithFailure.Success};save_b_valid={savedB.Success};before_class={ClassA.Value};before_race={RaceA.Value};mana_before={manaBefore};auraA_before={auraBefore};auraA_stacks_before={auraStacksBefore};load_status={loaded.Status};after_class={fxA.Player.ArchetypeId.Value};after_race={fxA.Player.RaceId?.Value ?? "<null>"};mana_present_after={fxA.Gameplay.Carriers.Rules.Powers.HasPower(Unit, Mana)};mana_after={manaAfter};auraA_after={auraAfter};auraA_stacks_after={auraStacksAfter};auraB_after={aurasA.HasAura(Unit, AuraB)}");
        Console.WriteLine("ORACLE-CURRENT=load_status=PersistableThrew;after_class=arch.class.followup_a;after_race=arch.race.followup_a;mana_present_after=True;mana_after=30;auraA_after=True;auraA_stacks_after=1;auraB_after=False");
        Console.WriteLine("INTERPRETATION-CURRENT=The valid B document writes class B before its malformed race section fails. v1.11 rollback restores the same race and different class in forward order, re-runs derived rebuilding, and preserves the partially consumed Mana and single RaceA aura reference.");
    }

    static void RunWeaponStyleCacheAfterLoad()
    {
        var fxA = Build(level: 1);
        fxA.Gameplay.Carriers.Inventory.AddItem(Unit, WeaponA, 1);
        var instanceA = fxA.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
        var equipA = fxA.Gameplay.Carriers.Equipment.Equip(Unit, instanceA, MainHandSlot);
        fxA.Bus.DispatchPending();
        var displayA = new Core.Foundation.DisplayInfo.DisplayInfoRegistry(fxA.Registry, fxA.Bus);
        var sourceA = new EquipmentWeaponStyleSource(fxA.Bus,
            unitId => fxA.Gameplay.Carriers.Equipment.GetAllEquippedInstances(unitId).TryGetValue(MainHandSlot, out var instance)
                ? instance.TemplateId
                : (Id?)null,
            displayA);
        var templateBefore = fxA.Gameplay.Carriers.Equipment.GetAllEquippedInstances(Unit)[MainHandSlot].TemplateId;
        var styleBefore = sourceA.GetWeaponStyleRef(Unit);
        var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.followup_weapon_a"), "probe"));

        var fxB = Build(level: 1);
        fxB.Gameplay.Carriers.Inventory.AddItem(Unit, WeaponB, 1);
        var instanceB = fxB.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
        var equipB = fxB.Gameplay.Carriers.Equipment.Equip(Unit, instanceB, MainHandSlot);
        fxB.Bus.DispatchPending();
        var displayB = new Core.Foundation.DisplayInfo.DisplayInfoRegistry(fxB.Registry, fxB.Bus);
        var sourceB = new EquipmentWeaponStyleSource(fxB.Bus,
            unitId => fxB.Gameplay.Carriers.Equipment.GetAllEquippedInstances(unitId).TryGetValue(MainHandSlot, out var instance)
                ? instance.TemplateId
                : (Id?)null,
            displayB);
        var oracleTemplateB = fxB.Gameplay.Carriers.Equipment.GetAllEquippedInstances(Unit)[MainHandSlot].TemplateId;
        var oracleStyleB = sourceB.GetWeaponStyleRef(Unit);
        var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.followup_weapon_b"), "probe"));
        fxA.Fs.WriteTextAtomic("user://saves/slot.followup_weapon_b.json", fxB.Fs.ReadText("user://saves/slot.followup_weapon_b.json")!);

        var loaded = fxA.Gameplay.RestoreFromSlot(new Id("slot.followup_weapon_b"));
        var templateAfterBeforeDispatch = fxA.Gameplay.Carriers.Equipment.GetAllEquippedInstances(Unit)[MainHandSlot].TemplateId;
        var styleAfterBeforeDispatch = sourceA.GetWeaponStyleRef(Unit);
        fxA.Bus.DispatchPending();
        var templateAfter = fxA.Gameplay.Carriers.Equipment.GetAllEquippedInstances(Unit)[MainHandSlot].TemplateId;
        var styleAfter = sourceA.GetWeaponStyleRef(Unit);
        Console.WriteLine("WEAPON-STYLE-CACHE-SAME-MAP-LOAD");
        Console.WriteLine($"equip_a_success={equipA.Success};equip_b_success={equipB.Success};save_a_valid={savedA.Success};save_b_valid={savedB.Success};load_status={loaded.Status}");
        Console.WriteLine($"equipped_template_before={templateBefore};style_before={styleBefore?.Value ?? "<null>"}");
        Console.WriteLine($"equipped_template_after_before_dispatch={templateAfterBeforeDispatch};style_after_before_dispatch={styleAfterBeforeDispatch?.Value ?? "<null>"}");
        Console.WriteLine($"equipped_template_after={templateAfter};style_after={styleAfter?.Value ?? "<null>"};oracle_b_template={oracleTemplateB};oracle_b_style={oracleStyleB?.Value ?? "<null>"}");
        Console.WriteLine("ORACLE-CURRENT=load_status=Loaded;style_before=display.weapon_style.followup_a;style_after_before_dispatch=display.weapon_style.followup_b;style_after=display.weapon_style.followup_b;oracle_b_style=display.weapon_style.followup_b");
        Console.WriteLine("INTERPRETATION-CURRENT=Same-map load changes the real EquipmentHost template from A to B; v1.11 clears the weapon style cache on successful load so both immediate and drained queries match the independently primed B source oracle.");
        sourceA.Dispose();
        sourceB.Dispose();
    }

    static void RunInventoryViewModelAfterLoad()
    {
        var fxA = Build(level: 1);
        fxA.Gameplay.Carriers.Inventory.AddItem(Unit, Item, 1);
        fxA.Gameplay.Carriers.Inventory.AddItem(Unit, WeaponA, 1);
        var instanceA = fxA.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
        var equipA = fxA.Gameplay.Carriers.Equipment.Equip(Unit, instanceA, MainHandSlot);
        fxA.Bus.DispatchPending();

        // Construct the same production path-provider shape used by PresentationAssembly:
        // the provider answers live Gameplay carrier queries; InventoryViewModel owns its
        // materialized Slots/EquippedSlots snapshot and only refreshes on business item events.
        var skillBookA = new Presentation.Ui.SkillHostSkillBookQuery(fxA.Gameplay.Carriers.Rules.Skill);
        var playerProviderA = new Presentation.Ui.PlayerPathProvider(
            Unit, fxA.Gameplay.Carriers.Rules.Stats, fxA.Gameplay.Carriers.Rules.Powers,
            fxA.Gameplay.Carriers.Rules.Progression, fxA.Gameplay.Carriers.Inventory,
            fxA.Gameplay.Carriers.Equipment, fxA.Gameplay.Quest, fxA.Gameplay.Economy, skillBookA);
        var uiDataA = new Presentation.Ui.UiDataSource(fxA.Bus, new[] { playerProviderA });
        using var inventoryVm = new Presentation.Ui.InventoryViewModel(uiDataA, new[] { MainHandSlot });

        var aCount = fxA.Gameplay.Carriers.Inventory.ListItems(Unit).Count;
        var aInventoryCount = uiDataA.Query("player.inventory.count")!.Value.AsInt;
        var aItemCount = uiDataA.Query("player.inventory[0].count")!.Value.AsInt;
        var aEquipped = fxA.Gameplay.Carriers.Equipment.GetEquipped(Unit, MainHandSlot)?.InstanceId.Value ?? "<null>";
        var vmAItemCount = inventoryVm.Slots.Count == 0 ? -1 : inventoryVm.Slots[0].Count;
        var vmAEquipped = inventoryVm.EquippedSlots.TryGetValue(MainHandSlot, out var vmAInstance) ? vmAInstance.Value : "<null>";

        var fxB = Build(level: 1);
        fxB.Gameplay.Carriers.Inventory.AddItem(Unit, Item, 2);
        fxB.Gameplay.Carriers.Inventory.AddItem(Unit, WeaponB, 1);
        var instanceB = fxB.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
        var equipB = fxB.Gameplay.Carriers.Equipment.Equip(Unit, instanceB, MainHandSlot);
        fxB.Bus.DispatchPending();
        var bEntriesBeforeSave = string.Join(",", fxB.Gameplay.Carriers.Inventory.ListItems(Unit).Select(i => $"{i.TemplateId.Value}:{i.Count}"));
        var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.followup_inventory_b"), "probe"));
        fxA.Fs.WriteTextAtomic("user://saves/slot.followup_inventory_b.json",
            fxB.Fs.ReadText("user://saves/slot.followup_inventory_b.json")!);

        var loaded = fxA.Gameplay.RestoreFromSlot(new Id("slot.followup_inventory_b"));
        fxA.Bus.DispatchPending();
        var directCountAfter = uiDataA.Query("player.inventory.count")!.Value.AsInt;
        var directEntriesAfter = string.Join(",", Enumerable.Range(0, (int)directCountAfter).Select(i =>
            $"{uiDataA.Query($"player.inventory[{i}].template")!.Value.AsId.Value}:{uiDataA.Query($"player.inventory[{i}].count")!.Value.AsInt}"));
        var directEquippedAfter = uiDataA.Query($"player.equipment.{MainHandSlot.Value}")!.Value.AsId.Value;
        var directEquippedTemplateAfter = fxA.Gameplay.Carriers.Equipment.GetAllEquippedInstances(Unit)[MainHandSlot].TemplateId.Value;
        var vmEntriesAfter = string.Join(",", inventoryVm.Slots.Select(s => $"{s.TemplateId.Value}:{s.Count}"));
        var vmEquippedAfter = inventoryVm.EquippedSlots.TryGetValue(MainHandSlot, out var vmInstanceAfter)
            ? vmInstanceAfter.Value : "<null>";

        Console.WriteLine("INVENTORY-VM-SAME-MAP-LOAD");
        Console.WriteLine($"a_inventory_count={aCount};a_direct_count={aInventoryCount};a_direct_item_count={aItemCount};a_equipped_instance={aEquipped};a_vm_item_count={vmAItemCount};a_vm_equipped_instance={vmAEquipped}");
        Console.WriteLine($"b_entries_before_save={bEntriesBeforeSave};save_b_valid={savedB.Success};load_status={loaded.Status};direct_count_after={directCountAfter};direct_entries_after={directEntriesAfter};direct_equipped_instance_after={directEquippedAfter};direct_equipped_template_after={directEquippedTemplateAfter};vm_entries_after={vmEntriesAfter};vm_equipped_instance_after={vmEquippedAfter}");
        Console.WriteLine("CORRECTNESS-ORACLE-CURRENT=after successful load, VM entries/equipped must equal live B direct query: direct_entries_after=item.followup_level2:1,item.followup_level2:1;direct_equipped_template_after=item.weapon.followup_b;vm_entries_after=item.followup_level2:1,item.followup_level2:1;vm_equipped_template_after=item.weapon.followup_b");
        Console.WriteLine("BUG-SIGNATURE-CURRENT=vm_entries_after=item.followup_level2:1;vm_equipped_instance_after=item.inst_2 while live B direct entries count=2 and equipped instance=item.inst_3");
        Console.WriteLine("INTERPRETATION-CURRENT=UiDataSource queries the live B inventory/equipment after same-map load, while InventoryViewModel retains the A snapshot because it subscribes only to business item events and not SaveLoaded. Refresh is an explicit oracle only and is not part of the production load path.");
        inventoryVm.Refresh();
        Console.WriteLine($"vm_after_manual_refresh_entries={string.Join(",", inventoryVm.Slots.Select(s => $"{s.TemplateId.Value}:{s.Count}"))};vm_after_manual_refresh_equipped_instance={inventoryVm.EquippedSlots[MainHandSlot].Value}");
    }

    static InMemoryDataSource Data()
    {
        string E(string table, string rows) => "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";
        var levels = string.Join(",", Enumerable.Range(1, 10).Select(i => "{\"level\":" + i + ",\"xp_to_next\":100,\"growth\":{}}"));
        return new InMemoryDataSource()
            .Add("stat.definition", E("stat.definition", "[{\"id\":\"stat.followup_rating\",\"name_key\":\"l10n.rating\",\"group\":\"primary\",\"default_base\":0,\"is_rating\":true,\"rating_conversion_ref\":\"stat.rating.followup\"},{\"id\":\"stat.max_health\",\"name_key\":\"l10n.max\",\"group\":\"primary\",\"default_base\":100},{\"id\":\"stat.followup_race_power\",\"name_key\":\"l10n.race.power\",\"group\":\"primary\",\"default_base\":1},{\"id\":\"stat.followup_class_power\",\"name_key\":\"l10n.class.power\",\"group\":\"primary\",\"default_base\":0},{\"id\":\"stat.followup_class_legacy\",\"name_key\":\"l10n.class.legacy\",\"group\":\"primary\",\"default_base\":0}]"))
            .Add("stat.rating_conversion", E("stat.rating_conversion", "[{\"id\":\"stat.rating.followup\",\"entries\":[{\"level\":1,\"points_per_percent\":10},{\"level\":10,\"points_per_percent\":5}]}]"))
            .Add("arch.power_type", E("arch.power_type", "[{\"id\":\"arch.power.health\",\"name_key\":\"l10n.health\",\"max_source\":{\"kind\":\"stat\",\"stat\":\"stat.max_health\"},\"start_full\":true},{\"id\":\"" + Mana.Value + "\",\"name_key\":\"l10n.mana\",\"max_source\":{\"kind\":\"fixed\",\"value\":100},\"regen_in_combat\":0,\"regen_out_of_combat\":10,\"start_full\":false}]"))
            .Add("arch.class", E("arch.class", "[{\"id\":\"" + Class.Value + "\",\"name_key\":\"l10n.class\",\"primary_stat\":\"stat.max_health\",\"base_stats\":{},\"power_types\":[\"arch.power.health\"],\"level_curve_ref\":\"" + Curve.Value + "\"},{\"id\":\"" + ClassA.Value + "\",\"name_key\":\"l10n.class.a\",\"primary_stat\":\"stat.max_health\",\"base_stats\":{\"" + StatClassPower.Value + "\":10,\"" + StatClassLegacy.Value + "\":5},\"power_types\":[\"arch.power.health\",\"" + Mana.Value + "\"],\"level_curve_ref\":\"" + Curve.Value + "\"},{\"id\":\"" + ClassB.Value + "\",\"name_key\":\"l10n.class.b\",\"primary_stat\":\"stat.max_health\",\"base_stats\":{\"" + StatClassPower.Value + "\":20},\"power_types\":[\"arch.power.health\"],\"level_curve_ref\":\"" + Curve.Value + "\"}]"))
            .Add("arch.race", E("arch.race", "[{\"id\":\"" + RaceA.Value + "\",\"name_key\":\"l10n.race.a\",\"stat_mods\":{\"" + StatRacePower.Value + "\":10},\"passive_auras\":[\"" + AuraA.Value + "\"]},{\"id\":\"" + RaceB.Value + "\",\"name_key\":\"l10n.race.b\",\"stat_mods\":{\"" + StatRacePower.Value + "\":20},\"passive_auras\":[\"" + AuraB.Value + "\"]}]"))
            .Add("skill.aura_def", E("skill.aura_def", "[{\"id\":\"" + AuraA.Value + "\",\"duration\":60,\"max_stacks\":1,\"effects\":[{\"kind\":\"mod_stat\",\"params\":{\"stat\":\"" + StatRacePower.Value + "\",\"op\":\"flat\",\"value\":50}}]},{\"id\":\"" + AuraB.Value + "\",\"duration\":60,\"max_stacks\":1,\"effects\":[{\"kind\":\"mod_stat\",\"params\":{\"stat\":\"" + StatRacePower.Value + "\",\"op\":\"flat\",\"value\":70}}]}]"))
            .Add("prog.level_curve", E("prog.level_curve", "[{\"id\":\"" + Curve.Value + "\",\"max_level\":10,\"entries\":[" + levels + "]}]"))
            .Add("item.slot_definition", E("item.slot_definition", "[{\"id\":\"" + Slot.Value + "\",\"name_key\":\"l10n.slot\"},{\"id\":\"" + MainHandSlot.Value + "\",\"name_key\":\"l10n.main_hand\"}]"))
            .Add("item.quality_definition", E("item.quality_definition", "[{\"id\":\"" + Quality.Value + "\",\"name_key\":\"l10n.quality\"}]"))
            .Add("item.template", E("item.template", "[{\"id\":\"" + Item.Value + "\",\"slot\":\"" + Slot.Value + "\",\"quality\":\"" + Quality.Value + "\",\"item_level\":1,\"display_ref\":\"display.followup\",\"stack_size\":1,\"name_key\":\"l10n.item\",\"requirements\":{\"level\":2},\"stats\":[{\"stat\":\"stat.max_health\",\"op\":\"flat\",\"value\":100}]},{\"id\":\"" + WeaponA.Value + "\",\"slot\":\"" + MainHandSlot.Value + "\",\"quality\":\"" + Quality.Value + "\",\"item_level\":1,\"display_ref\":\"display.weapon.followup_a\",\"stack_size\":1,\"name_key\":\"l10n.weapon.a\",\"stats\":[]},{\"id\":\"" + WeaponB.Value + "\",\"slot\":\"" + MainHandSlot.Value + "\",\"quality\":\"" + Quality.Value + "\",\"item_level\":1,\"display_ref\":\"display.weapon.followup_b\",\"stack_size\":1,\"name_key\":\"l10n.weapon.b\",\"stats\":[]}]"))
            .Add("item.budget_curve", E("item.budget_curve", "[{\"id\":\"item.budget.default\",\"entries\":[{\"item_level\":1,\"budget\":200}]}]"))
            .Add("display.map", E("display.map", "[{\"id\":\"display.map.weapon.followup_a\",\"category\":\"item\",\"logical_id\":\"" + WeaponA.Value + "\",\"kind\":\"sprite\",\"sprite_set_id\":\"sprite.weapon.followup_a\",\"direction_count\":4,\"weapon_style_ref\":\"" + WeaponStyleA.Value + "\"},{\"id\":\"display.map.weapon.followup_b\",\"category\":\"item\",\"logical_id\":\"" + WeaponB.Value + "\",\"kind\":\"sprite\",\"sprite_set_id\":\"sprite.weapon.followup_b\",\"direction_count\":4,\"weapon_style_ref\":\"" + WeaponStyleB.Value + "\"}]"))
            .Add("display.weapon_style", E("display.weapon_style", "[{\"id\":\"" + WeaponStyleA.Value + "\",\"auto_attack_anim\":\"anim.weapon.followup_a\"},{\"id\":\"" + WeaponStyleB.Value + "\",\"auto_attack_anim\":\"anim.weapon.followup_b\"}]"))
            .Add("combat.hit_table_config", E("combat.hit_table_config", "[]"))
            .Add("combat.resist_curve", E("combat.resist_curve", "[]"));
    }
}
