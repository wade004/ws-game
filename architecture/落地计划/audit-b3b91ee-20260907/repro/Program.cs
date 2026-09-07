using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Foundation.Rng;
using Core.Gameplay.Common;
using Core.Gameplay.Quest;
using Core.Rules.Common;
using Core.Rules.Skill;
using ItemSupport = Tests.Carriers.Item.TestSupport;
using SkillSupport = Tests.Rules.Skill;

internal static class Program
{
    private static readonly Id Player = new Id("player.hero");
    private static readonly List<string> Lines = new List<string>();
    private static int UnexpectedFailures;

    private static void Main()
    {
        Run("R1 QuestPersistable empty snapshot", ReproQuestPersistable);
        Run("R2 EquipmentPersistable empty snapshot", ReproEquipmentEmpty);
        Run("R2b EquipmentPersistable old same-slot snapshot", ReproEquipmentOldSameSlot);
        Run("R3 SaveSystem legal backup-looking slot collision", ReproSaveSystemCollision);
        Run("R4 WorldSim stale timer handle after ClearAll", ReproTimerHandleCollision);
        Run("R5 fixed_order movement AP participant join", ReproFixedOrderMovementAp);
        Run("R6 CastPipeline discrete AP before OutOfRange", ReproCastApBeforeRange);
        Run("R7 CarriersAssembly creature combat Despawn then Tick/Update", ReproCarriersDespawnCombat);
        Lines.Add("SUMMARY | candidateLines=" + Lines.Count + ", unexpectedExceptions=" + UnexpectedFailures);

        var output = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "validation-repros.txt");
        output = Path.GetFullPath(output);
        File.WriteAllLines(output, Lines);
        Console.WriteLine(string.Join(Environment.NewLine, Lines));
        Console.WriteLine("LOG=" + output);
    }

    private static void Run(string name, Func<string> repro)
    {
        try
        {
            Lines.Add(repro());
        }
        catch (Exception ex)
        {
            UnexpectedFailures++;
            Lines.Add(name + " | expected=construct and execute candidate sequence | actual=EXCEPTION " + ex.GetType().Name + ": " + ex.Message + " | NOT_EXECUTED");
        }
    }

    private static string ReproQuestPersistable()
    {
        var questId = new Id("quest.audit_empty_load");
        var quest = new QuestDefinition(
            questId,
            new[] { new QuestObjective(QuestObjectiveType.Escort, new Id("creature.audit_target"), 1) },
            QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);
        var bus = NewBus();
        var exprs = new NullExprFactory();
        var inventory = new NullInventory();
        var units = new NullUnitAccess();
        var host = new QuestHost(new[] { quest }, bus, exprs, new NullRewardDispatcher(), inventory, units);
        var persistable = new QuestPersistable(host, () => Player);

        var oldSnapshot = persistable.Save();
        var accepted = host.Accept(Player, questId);
        persistable.Load(oldSnapshot);
        var actual = host.GetState(Player, questId);
        var defect = accepted && actual == QuestState.Active;
        return "R1 QuestPersistable empty snapshot" +
            " | expected=Load(old empty object) removes the newly accepted quest" +
            " | actual=snapshotKind=" + oldSnapshot.Kind + ", accepted=" + accepted + ", stateAfterLoad=" + actual +
            " | " + (defect ? "PASS_FOR_REPRO (stale quest remains)" : "NOT_REPRODUCED");
    }

    private static ItemFixture BuildItemFixture()
    {
        const string slots = "[{\"id\":\"item.slot.main_hand\",\"name_key\":\"l10n.item.slot.main_hand\",\"is_weapon\":true}]";
        const string qualities = "[{\"id\":\"item.quality.common\",\"name_key\":\"l10n.item.quality.common\"}]";
        const string stats = "[{\"id\":\"stat.strength\",\"name_key\":\"l10n.stat.strength\",\"group\":\"primary\"}]";
        const string templates = "[{\"id\":\"item.audit_weapon_a\",\"slot\":\"item.slot.main_hand\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.audit_weapon_a\",\"stack_size\":1,\"name_key\":\"l10n.item.audit_weapon_a\"},{\"id\":\"item.audit_weapon_b\",\"slot\":\"item.slot.main_hand\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.audit_weapon_b\",\"stack_size\":1,\"name_key\":\"l10n.item.audit_weapon_b\"}]";
        var registry = ItemSupport.BuildRegistry(source =>
        {
            source.Add("item.slot_definition", ItemSupport.Table("item.slot_definition", slots));
            source.Add("item.quality_definition", ItemSupport.Table("item.quality_definition", qualities));
            source.Add("item.template", ItemSupport.Table("item.template", templates));
            source.Add("stat.definition", ItemSupport.Table("stat.definition", stats));
        });
        var bus = ItemSupport.CreateBus();
        var inventory = new InventoryHost(registry, bus);
        var statHost = new Core.Numbers.StatBlock.StatHost(registry, bus);
        var effectSink = new Tests.Carriers.Item.FakeEffectSink();
        var skillGranter = new Tests.Carriers.Item.RecordingSkillGranter();
        var units = new Tests.Carriers.Item.FakeUnitAccess().Add(Player, 10);
        var equipment = new EquipmentHost(registry, bus, inventory, statHost, effectSink, skillGranter.Grant, units);
        statHost.RegisterUnit(Player);
        return new ItemFixture(inventory, equipment);
    }

    private static string ReproEquipmentEmpty()
    {
        var fx = BuildItemFixture();
        fx.Inventory.AddItem(Player, new Id("item.audit_weapon_a"), 1);
        var instance = fx.Inventory.ListItems(Player).Single().InstanceId;
        var equipment = new EquipmentPersistable(Player, fx.Inventory, fx.Equipment);
        var equipped = fx.Equipment.Equip(Player, instance, new Id("item.slot.main_hand"));
        var oldSnapshot = equipment.Save();
        equipment.Load(new JsonObjectBuilder().Build());
        var remains = fx.Equipment.GetEquipped(Player, new Id("item.slot.main_hand")).HasValue;
        return "R2 EquipmentPersistable empty snapshot" +
            " | expected=Load(empty object) unequips current live item" +
            " | actual=equippedInitially=" + equipped.Success + ", oldSnapshot=" + oldSnapshot.Kind + ", remainsEquipped=" + remains +
            " | " + (remains ? "PASS_FOR_REPRO (live equipment remains)" : "NOT_REPRODUCED");
    }

    private static string ReproEquipmentOldSameSlot()
    {
        var fx = BuildItemFixture();
        fx.Inventory.AddItem(Player, new Id("item.audit_weapon_a"), 1);
        var old = fx.Inventory.ListItems(Player).Single().InstanceId;
        var equipment = new EquipmentPersistable(Player, fx.Inventory, fx.Equipment);
        var inventoryPersistable = new InventoryPersistable(Player, fx.Inventory);
        fx.Equipment.Equip(Player, old, new Id("item.slot.main_hand"));
        var oldSnapshot = equipment.Save();
        var oldBagSnapshot = inventoryPersistable.Save();
        fx.Inventory.AddItem(Player, new Id("item.audit_weapon_b"), 1);
        var live = fx.Inventory.ListItems(Player).Single().InstanceId;
        fx.Equipment.Equip(Player, live, new Id("item.slot.main_hand"));
        inventoryPersistable.Load(oldBagSnapshot);
        equipment.Load(oldSnapshot);
        var liveReturned = fx.Inventory.FindInstance(Player, live).HasValue;
        var equippedAfter = fx.Equipment.GetEquipped(Player, new Id("item.slot.main_hand"));
        return "R2b EquipmentPersistable old same-slot snapshot" +
            " | expected=old inventory/equipment snapshot restores A only; live B must not reappear" +
            " | actual=liveBReturnedToBag=" + liveReturned + ", equippedAfter=" + (equippedAfter.HasValue ? equippedAfter.Value.InstanceId.Value : "<none>") +
            " | " + (liveReturned ? "PASS_FOR_REPRO (live item contaminates restored bag)" : "NOT_REPRODUCED");
    }

    private static string ReproSaveSystemCollision()
    {
        var fs = new MemoryFileSystem();
        var options = new SaveSystemOptions(new Id("game.audit")) { BackupCount = 1 };
        var save = new SaveSystem(fs, options);
        var payload = new MutablePersistable();
        save.RegisterPersistable(payload);
        payload.Value = "X";
        var x = save.Save(new SaveRequest(new Id("slot.a.bak1"), "t0"));
        payload.Value = "A";
        var a = save.Save(new SaveRequest(new Id("slot.a"), "t1"));
        var backupAfterA = fs.ReadText("user://saves/slot.a.bak1.json");
        payload.Value = "B";
        var b = save.Save(new SaveRequest(new Id("slot.a"), "t2"));
        var backupAfterB = fs.ReadText("user://saves/slot.a.bak1.json");
        var visible = string.Join(",", save.ListSlots().Select(s => s.SlotId.Value));
        var payloadAfterA = ReadPayload(backupAfterA);
        var payloadAfterB = ReadPayload(backupAfterB);
        var overwritten = payloadAfterA == "X" && payloadAfterB == "A";
        return "R3 SaveSystem legal backup-looking slot collision" +
            " | expected=slot.a.bak1 remains independent from slot.a backup rotation and is listed as a slot" +
            " | actual=saveX=" + x.Success + ", saveA=" + a.Success + ", saveB=" + b.Success + ", backupPayloadAfterA=" + payloadAfterA + ", backupPayloadAfterB=" + payloadAfterB + ", ListSlots=[" + visible + "]" +
            " | " + (overwritten && !visible.Contains("slot.a.bak1", StringComparison.Ordinal) ? "PASS_FOR_REPRO (content overwritten and hidden)" : "NOT_REPRODUCED");
    }

    private static string ReproTimerHandleCollision()
    {
        var world = new WorldSim(NewBus());
        var oldHandle = world.Timers.Create(10);
        world.ClearAll();
        var newHandle = world.Timers.Create(10);
        world.Timers.Cancel(oldHandle);
        var newAlive = world.Timers.IsAlive(newHandle);
        return "R4 WorldSim stale timer handle after ClearAll" +
            " | expected=old handle cannot cancel a newly created timer" +
            " | actual=old=" + oldHandle + ", new=" + newHandle + ", newAliveAfterOldCancel=" + newAlive +
            " | " + (!newAlive && oldHandle == newHandle ? "PASS_FOR_REPRO (handle collision cancels new timer)" : "NOT_REPRODUCED");
    }

    private static string ReproFixedOrderMovementAp()
    {
        var bus = NewBus();
        var world = new WorldSim(bus);
        var scheduler = new TurnScheduler(world, _ => 0, _ => false, bus);
        scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object> { ["action_points_per_turn"] = 3.0 });
        var a = new Id("unit.a");
        var b = new Id("unit.b");
        scheduler.BeginCombat(new[] { a });
        scheduler.AddParticipant(b);
        var aConsumed = scheduler.TryConsumeActionPoints(a, 1.0);
        var bConsumed = scheduler.TryConsumeActionPoints(b, 1.0);
        return "R5 fixed_order movement AP participant join" +
            " | expected=both A and joined B have AP3 ledger; one-unit movement AP consumes for each" +
            " | actual=A_before3_consume=" + aConsumed + ", A_remaining=" + scheduler.GetActionPointsRemaining(a) + ", B_consume=" + bConsumed + ", B_remaining=" + scheduler.GetActionPointsRemaining(b) +
            " | " + (!bConsumed ? "PASS_FOR_REPRO (joined B has no movement AP ledger)" : "NOT_REPRODUCED");
    }

    private static string ReproCastApBeforeRange()
    {
        var builder = new SkillSupport.SkillWorldBuilder();
        builder.Options.IsDiscreteStep = () => true;
        var calls = 0;
        builder.Options.TryConsumeActionPoints = (unit, amount) => { calls++; return true; };
        var skill = SkillSupport.J.O(
            ("id", SkillSupport.J.S("skill.audit_range")),
            ("school", SkillSupport.J.S("skill.school_sample")),
            ("kind", SkillSupport.J.S("active")),
            ("range", SkillSupport.J.N(5)),
            ("cast_time", SkillSupport.J.N(0)),
            ("action_cost", SkillSupport.J.N(2)),
            ("respects_gcd", SkillSupport.J.B(false)),
            ("target_shape_ref", SkillSupport.J.S("target.chain.audit")),
            ("effects", SkillSupport.J.A()));
        var world = builder.SkillDef(skill).Build();
        var caster = new Id("unit.caster");
        var target = new Id("unit.target");
        world.AddUnit(caster, new Vec2(0, 0));
        world.AddUnit(target, new Vec2(100, 0));
        world.Targets.SetChain(new Id("target.chain.audit"), target);
        var result = world.Host.CastSkill(caster, new Id("skill.audit_range"), Array.Empty<Id>());
        return "R6 CastPipeline discrete AP before OutOfRange" +
            " | expected=failed range validation does not consume action points" +
            " | actual=success=" + result.Success + ", reason=" + result.Reason + ", consumeCalls=" + calls +
            " | " + (calls > 0 && result.Reason == CastFailureReason.OutOfRange ? "PASS_FOR_REPRO (AP consumed before range failure)" : "NOT_REPRODUCED");
    }

    private static string ReproCarriersDespawnCombat()
    {
        var bus = NewBus();
        var source = new InMemoryDataSource()
            .Add("item.budget_curve", "{\"table\":\"item.budget_curve\",\"schema_version\":1,\"rows\":[{\"id\":\"item.budget.default\",\"entries\":[{\"item_level\":1,\"budget\":10}]}]}")
            .Add("arch.power_type", "{\"table\":\"arch.power_type\",\"schema_version\":1,\"rows\":[{\"id\":\"arch.power.health\",\"name_key\":\"l10n.power.health.name\",\"max_source\":{\"kind\":\"fixed\",\"value\":100},\"start_full\":true}]}")
            .Add("stat.definition", "{\"table\":\"stat.definition\",\"schema_version\":1,\"rows\":[]}")
            .Add("combat.hit_table_config", "{\"table\":\"combat.hit_table_config\",\"schema_version\":1,\"rows\":[]}")
            .Add("combat.resist_curve", "{\"table\":\"combat.resist_curve\",\"schema_version\":1,\"rows\":[]}");
        var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
        CarriersSchemaCatalog.RegisterAll(registry);
        var report = registry.LoadAll();
        if (report.IsBlocking)
        {
            return "R7 CarriersAssembly creature combat Despawn then Tick/Update | registry=BLOCKING " + string.Join(";", report.Issues) + " | NOT_EXECUTED";
        }

        var world = new WorldSim(bus);
        var assembly = new CarriersAssembly(bus, registry, new RngHost(1), world, new StubSpatialQuery(), new StubNavigation2D());
        var creatureId = new Id("creature.audit_live");
        world.AddEntity(new CreatureUnit(creatureId, new Id("map.audit"), new Id("faction.audit"), new Id("creature.audit_template")));
        bus.DispatchPending();
        assembly.Rules.Stats.RegisterUnit(creatureId);
        assembly.Rules.Powers.RegisterUnit(creatureId, new[] { new Id("arch.power.health") });
        assembly.Rules.Combat.NotifyCombatEvent(creatureId, new Id("creature.audit_hostile"));
        assembly.Creatures.Despawn(creatureId, "despawned");
        world.Tick(SimStep.Continuous(0.1));
        try
        {
            assembly.Rules.Combat.Update(10.0);
            return "R7 CarriersAssembly creature combat Despawn then Tick/Update | expected=despawned creature leaves combat cleanly after delay | actual=Combat.Update completed without exception, inCombat=" + assembly.Rules.Combat.IsInCombat(creatureId) + " | NOT_REPRODUCED";
        }
        catch (Exception ex)
        {
            return "R7 CarriersAssembly creature combat Despawn then Tick/Update | expected=despawned creature leaves combat cleanly after delay | actual=Combat.Update threw " + ex.GetType().Name + ": " + ex.Message + " | PASS_FOR_REPRO (Despawn unregisters Powers while CombatHost still tracks unit)";
        }
    }

    private static IEventBus NewBus() => new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

    private static string? ReadPayload(string? text)
    {
        if (text == null) return null;
        var root = JsonReader.Parse(text) as JsonObject;
        if (root == null || !root.TryGetValue("sections", out var sectionsValue) || sectionsValue is not JsonObject sections) return null;
        return sections.TryGetValue("audit.payload", out var payload) && payload is JsonString s ? s.Value : null;
    }

    private sealed class ItemFixture
    {
        public InventoryHost Inventory { get; }
        public EquipmentHost Equipment { get; }
        public ItemFixture(InventoryHost inventory, EquipmentHost equipment) { Inventory = inventory; Equipment = equipment; }
    }

    private sealed class MutablePersistable : IPersistable
    {
        public string SectionKey => "audit.payload";
        public string Value { get; set; } = "";
        public JsonValue Save() => new JsonString(Value);
        public void Load(JsonValue data) { }
    }

    private sealed class MemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);
        public string GetUserDataDir() => "user://";
        public string GetContentRootDir() => "content://";
        public string? ReadText(string path) => _files.TryGetValue(path, out var text) ? text : null;
        public bool WriteTextAtomic(string path, string content) { _files[path] = content; return true; }
        public bool Exists(string path) => _files.ContainsKey(path);
        public IReadOnlyList<string> ListFiles(string dirPath)
        {
            var prefix = dirPath.TrimEnd('/') + "/";
            return _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .Select(k => k.Substring(prefix.Length)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
        public bool DeleteFile(string path) => _files.Remove(path);
    }

    private sealed class NullExprFactory : IExprHostFactory
    {
        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new NullExprHost();
    }
    private sealed class NullExprHost : IExprHost
    {
        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) => default;
    }
    private sealed class NullRewardDispatcher : IRewardDispatcher
    {
        public void Grant(Id unitId, RewardBundle bundle, Id sourceId) { }
    }
    private sealed class NullInventory : IInventoryHost
    {
        public bool AddItem(Id unitId, Id templateId, int count) => false;
        public bool RemoveItem(Id unitId, Id instanceId, int count) => false;
        public IReadOnlyList<ItemInstance> ListItems(Id unitId) => Array.Empty<ItemInstance>();
        public int CountOf(Id unitId, Id templateId) => 0;
        public ItemInstance? FindInstance(Id unitId, Id instanceId) => null;
    }
    private sealed class NullUnitAccess : IUnitAccess
    {
        public bool Exists(Id unitId) => true;
        public IReadOnlyList<Id> AllUnits => Array.Empty<Id>();
        public Vec2 GetPosition(Id unitId) => Vec2.Zero;
        public void SetPosition(Id unitId, Vec2 position) { }
        public Id GetFaction(Id unitId) => default;
        public int GetLevel(Id unitId) => 1;
        public double GetFacing(Id unitId) => 0;
        public bool IsAlive(Id unitId) => true;
        public void SetAlive(Id unitId, bool alive) { }
        public Id? GetTemplateId(Id unitId) => null;
        public IReadOnlyList<Id> GetTags(Id unitId) => Array.Empty<Id>();
    }
}
