using Adapters.Stub;
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;
using Core.Foundation.Rng;
using Core.Gameplay.Common;
using Core.Gameplay.Quest;
using Core.Rules.Common;
using Core.Carriers.Assembly;

internal static class Program
{
    private static readonly Id Player = new Id("unit.repro.player");
    private static readonly Id ItemA = new Id("item.repro.a");
    private static readonly Id ItemB = new Id("item.repro.b");
    private static readonly Id QuestId = new Id("quest.repro.consume_a");

    public static int Main()
    {
        Console.WriteLine("BOUNDARY_REPROS HEAD=5e779c600d7dd3c9ef995d34e844c48641f20a85");
        var reward = ReproRewardEvents();
        var spawn = Repro.ReproSpawnWorld.Run();
        var equipment = ReproEquipmentDuplicate();
        Console.WriteLine($"SUMMARY ReproRewardEvents={(reward ? "REPRODUCED" : "NOT_REPRODUCED")} ReproSpawnWorld={(spawn ? "REPRODUCED" : "NOT_REPRODUCED")} ReproEquipmentDuplicate={(equipment ? "REPRODUCED" : "NOT_REPRODUCED")}");
        return reward && spawn && equipment ? 0 : 1;
    }

    private static bool ReproRewardEvents()
    {
        var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
        var registry = BuildInventoryRegistry(bus);
        var inventory = new InventoryHost(registry, bus, new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Reject });
        var rewardDispatcher = new RewardDispatcher(inventory: inventory);
        var units = new OneUnitAccess(Player);
        var objective = new QuestObjective(QuestObjectiveType.Collect, ItemA, 1, consumeOnProgress: true);
        var quest = new QuestDefinition(QuestId, new[] { objective }, QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.Unlimited);
        var exprFactory = new FalseExprFactory();
        var quests = new QuestHost(new[] { quest }, bus, exprFactory, rewardDispatcher, inventory, units);

        inventory.AddItem(Player, ItemA, 2);
        bus.DispatchPending();
        var accepted = quests.Accept(Player, QuestId);
        var beforeDispatch = inventory.CountOf(Player, ItemA);
        var beforeProgress = quests.GetLog(Player).Single().ObjectiveCounts[0];
        var granted = rewardDispatcher.Grant(Player, new RewardBundle(
            items: new[] { new ItemStack(ItemA, 1), new ItemStack(ItemB, 1) },
            xp: 0, currency: Array.Empty<(Id, long)>(), skills: Array.Empty<Id>(),
            worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 0), new Id("reward.repro"));
        var afterGrant = inventory.CountOf(Player, ItemA);
        var afterGrantProgress = quests.GetLog(Player).Single().ObjectiveCounts[0];
        bus.DispatchPending();
        var afterDispatch = inventory.CountOf(Player, ItemA);
        var afterProgress = quests.GetLog(Player).Single().ObjectiveCounts[0];
        var reproduced = accepted && !granted && beforeDispatch == 2 && beforeProgress == 0 && afterGrant == 2 && afterGrantProgress == 0 && afterDispatch == 1 && afterProgress == 1;
        Console.WriteLine("ReproRewardEvents expected=GrantFalse_preserves_A2_and_does_not_progress_quest; queued_rollback_event_should_be_cancelled");
        Console.WriteLine($"ReproRewardEvents actual=accepted:{accepted} granted:{granted} beforeDispatch:A{beforeDispatch}/progress{beforeProgress} afterGrant:A{afterGrant}/progress{afterGrantProgress} afterDispatch:A{afterDispatch}/progress{afterProgress} reproduced:{reproduced}");
        Console.WriteLine("ReproRewardEvents boundary=real shared EventBus retained InventoryHost.ItemAdded(A1) after RewardDispatcher rollback; QuestHost consumed it later");
        return reproduced;
    }

    private static bool ReproEquipmentDuplicate()
    {
        var duplicateAssembly = BuildDuplicateAssembly(out var duplicatePlayer);
        var duplicateAura = new Id("skill.aura.repro_shared");
        Equip(duplicateAssembly, duplicatePlayer, new Id("item.repro.duplicate"), new Id("item.slot.repro_duplicate"));
        var duplicateBefore = duplicateAssembly.Rules.Skill.AuraQuery.HasAura(duplicatePlayer, duplicateAura);
        var duplicateBeforeStat = duplicateAssembly.Rules.Stats.GetStat(duplicatePlayer, new Id("stat.power"));
        var duplicateUnequip = duplicateAssembly.Equipment.Unequip(duplicatePlayer, new Id("item.slot.repro_duplicate"));
        var duplicateAfter = duplicateAssembly.Rules.Skill.AuraQuery.HasAura(duplicatePlayer, duplicateAura);
        var duplicateAfterStat = duplicateAssembly.Rules.Stats.GetStat(duplicatePlayer, new Id("stat.power"));

        var setAssembly = BuildDuplicateAssembly(out var setPlayer);
        Equip(setAssembly, setPlayer, new Id("item.repro.ordinary"), new Id("item.slot.repro_ordinary"));
        Equip(setAssembly, setPlayer, new Id("item.repro.set_one"), new Id("item.slot.repro_set_one"));
        Equip(setAssembly, setPlayer, new Id("item.repro.set_two"), new Id("item.slot.repro_set_two"));
        var setBefore = setAssembly.Rules.Skill.AuraQuery.HasAura(setPlayer, duplicateAura);
        var setUnequipOrdinary = setAssembly.Equipment.Unequip(setPlayer, new Id("item.slot.repro_ordinary"));
        var setAfterOrdinary = setAssembly.Rules.Skill.AuraQuery.HasAura(setPlayer, duplicateAura);
        var setAfterStat = setAssembly.Rules.Stats.GetStat(setPlayer, new Id("stat.power"));
        var setUnequipOne = setAssembly.Equipment.Unequip(setPlayer, new Id("item.slot.repro_set_one"));
        var setAfterOne = setAssembly.Rules.Skill.AuraQuery.HasAura(setPlayer, duplicateAura);
        var setUnequipTwo = setAssembly.Equipment.Unequip(setPlayer, new Id("item.slot.repro_set_two"));
        var setAfterAll = setAssembly.Rules.Skill.AuraQuery.HasAura(setPlayer, duplicateAura);

        var duplicateReproduced = duplicateBefore && duplicateUnequip != null && !duplicateAfter && duplicateAfterStat == 1;
        var setReproduced = setBefore && setUnequipOrdinary != null && !setAfterOrdinary && setAfterStat == 1;
        var reproduced = duplicateReproduced || setReproduced;
        Console.WriteLine("ReproEquipmentDuplicate expected=single_item_duplicate_aura_unloads_cleanly; set_aura_survives_ordinary_source_unequip_until_set_breaks");
        Console.WriteLine($"ReproEquipmentDuplicate actual=duplicate:beforeAura:{duplicateBefore} beforeStat:{duplicateBeforeStat} unequip:{duplicateUnequip != null} afterAura:{duplicateAfter} afterStat:{duplicateAfterStat} reproduced:{duplicateReproduced}");
        Console.WriteLine($"ReproEquipmentDuplicate actual=set:beforeAura:{setBefore} ordinaryUnequip:{setUnequipOrdinary != null} afterOrdinaryAura:{setAfterOrdinary} afterOrdinaryStat:{setAfterStat} afterSetOneAura:{setAfterOne} afterAllAura:{setAfterAll} reproduced:{setReproduced}");
        Console.WriteLine("ReproEquipmentDuplicate boundary=real CarriersAssembly, AllowMultiSourceTiming=false, maxStacks=1, StackOverflowPolicy.Replace; duplicate grants and set/ordinary shared aura handles are checked independently");
        return reproduced;
    }

    private static void Equip(CarriersAssembly assembly, Id player, Id template, Id slot)
    {
        assembly.Inventory.AddItem(player, template, 1);
        var item = assembly.Inventory.ListItems(player).Last();
        var result = assembly.Equipment.Equip(player, item.InstanceId, slot);
        if (!result.Success) throw new InvalidOperationException($"equip failed: {template} -> {slot}: {result.Reason}");
    }

    private static CarriersAssembly BuildDuplicateAssembly(out Id playerId)
    {
        var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
        var registry = new DataRegistry(BuildDuplicateSource(), bus, new DataRegistryOptions { FailOnUnknownTable = false });
        CarriersSchemaCatalog.RegisterAll(registry);
        var report = registry.LoadAll();
        if (report.IsBlocking) throw new InvalidOperationException(string.Join(";", report.Issues.Select(i => i.ToString())));
        var world = new WorldSim(bus);
        var assembly = new CarriersAssembly(bus, registry, new RngHost(1), world, new StubSpatialQuery(),
            skillOptions: new Core.Rules.Skill.SkillOptions { StackOverflowPolicy = Core.Rules.Skill.StackOverflowPolicy.Replace });
        playerId = assembly.Creatures.Spawn(new Id("creature.repro_player"), new Id("map.repro"), Vec2.Zero, 0);
        return assembly;
    }

    private static InMemoryDataSource BuildDuplicateSource()
    {
        const string aura = "skill.aura.repro_shared";
        var source = new InMemoryDataSource();
        source.Add("stat.definition", Table("stat.definition", "[{\"id\":\"stat.power\",\"name_key\":\"l10n.stat.power\",\"group\":\"primary\",\"default_base\":1}]"));
        source.Add("arch.power_type", Table("arch.power_type", "[{\"id\":\"arch.power.health\",\"name_key\":\"l10n.power.health\",\"max_source\":{\"kind\":\"fixed\",\"value\":1000},\"start_full\":true}]"));
        source.Add("creature.tier_definition", Table("creature.tier_definition", "[{\"id\":\"creature.tier.repro\",\"name_key\":\"l10n.tier.repro\",\"stat_multiplier\":1,\"control_immune\":false,\"sort_weight\":0}]"));
        source.Add("creature.template", Table("creature.template", "[{\"id\":\"creature.repro_player\",\"name_key\":\"l10n.creature.repro\",\"level\":1,\"tier\":\"creature.tier.repro\",\"base_stats\":{\"stat.power\":1},\"faction_id\":\"fac.repro\",\"display_ref\":\"display.repro\"}]"));
        source.Add("combat.hit_table_config", Table("combat.hit_table_config", "[]"));
        source.Add("combat.resist_curve", Table("combat.resist_curve", "[]"));
        source.Add("skill.aura_def", Table("skill.aura_def", "[{\"id\":\"" + aura + "\",\"duration\":60,\"max_stacks\":1,\"effects\":[{\"kind\":\"mod_stat\",\"params\":{\"stat\":\"stat.power\",\"op\":\"flat\",\"value\":100}}]}]"));
        source.Add("item.slot_definition", Table("item.slot_definition", "[" +
            "{\"id\":\"item.slot.repro_duplicate\",\"name_key\":\"l10n.dup\"}," +
            "{\"id\":\"item.slot.repro_ordinary\",\"name_key\":\"l10n.ordinary\"}," +
            "{\"id\":\"item.slot.repro_set_one\",\"name_key\":\"l10n.set1\"}," +
            "{\"id\":\"item.slot.repro_set_two\",\"name_key\":\"l10n.set2\"}]"));
        source.Add("item.quality_definition", Table("item.quality_definition", "[{\"id\":\"item.quality.repro\",\"name_key\":\"l10n.quality.repro\"}]"));
        source.Add("item.set", Table("item.set", "[{\"id\":\"item.set.repro\",\"name_key\":\"l10n.set.repro\",\"pieces\":[\"item.repro.set_one\",\"item.repro.set_two\"],\"bonuses\":[{\"count\":2,\"aura_ref\":\"" + aura + "\"}]}]"));
        source.Add("item.template", Table("item.template", "[" +
            "{\"id\":\"item.repro.duplicate\",\"slot\":\"item.slot.repro_duplicate\",\"quality\":\"item.quality.repro\",\"item_level\":1,\"display_ref\":\"display.dup\",\"stack_size\":1,\"name_key\":\"l10n.dup\",\"grants\":{\"auras\":[\"" + aura + "\",\"" + aura + "\"]}}," +
            "{\"id\":\"item.repro.ordinary\",\"slot\":\"item.slot.repro_ordinary\",\"quality\":\"item.quality.repro\",\"item_level\":1,\"display_ref\":\"display.ordinary\",\"stack_size\":1,\"name_key\":\"l10n.ordinary\",\"grants\":{\"auras\":[\"" + aura + "\"]}}," +
            "{\"id\":\"item.repro.set_one\",\"slot\":\"item.slot.repro_set_one\",\"quality\":\"item.quality.repro\",\"item_level\":1,\"display_ref\":\"display.set1\",\"stack_size\":1,\"name_key\":\"l10n.set1\",\"set_id\":\"item.set.repro\"}," +
            "{\"id\":\"item.repro.set_two\",\"slot\":\"item.slot.repro_set_two\",\"quality\":\"item.quality.repro\",\"item_level\":1,\"display_ref\":\"display.set2\",\"stack_size\":1,\"name_key\":\"l10n.set2\",\"set_id\":\"item.set.repro\"}]"));
        source.Add("item.budget_curve", Table("item.budget_curve", "[{\"id\":\"item.budget.default\",\"entries\":[{\"item_level\":1,\"budget\":10}]}]"));
        return source;
    }

    private static DataRegistry BuildInventoryRegistry(IEventBus bus)
    {
        var source = new InMemoryDataSource()
            .Add("item.slot_definition", Table("item.slot_definition", "[{\"id\":\"item.slot.repro\",\"name_key\":\"l10n.slot.repro\"}]"))
            .Add("item.quality_definition", Table("item.quality_definition", "[{\"id\":\"item.quality.repro\",\"name_key\":\"l10n.quality.repro\"}]"))
            .Add("item.template", Table("item.template", "[" +
                "{\"id\":\"item.repro.a\",\"slot\":\"item.slot.repro\",\"quality\":\"item.quality.repro\",\"item_level\":1,\"display_ref\":\"display.repro.a\",\"stack_size\":10,\"name_key\":\"l10n.item.repro.a\"}," +
                "{\"id\":\"item.repro.b\",\"slot\":\"item.slot.repro\",\"quality\":\"item.quality.repro\",\"item_level\":1,\"display_ref\":\"display.repro.b\",\"stack_size\":10,\"name_key\":\"l10n.item.repro.b\"}]"));
        var registry = new DataRegistry(source, bus);
        registry.RegisterSchema(ItemSchemas.Template);
        registry.RegisterSchema(ItemSchemas.SlotDefinition);
        registry.RegisterSchema(ItemSchemas.QualityDefinition);
        var report = registry.LoadAll();
        if (report.IsBlocking) throw new InvalidOperationException(string.Join(";", report.Issues.Select(i => i.ToString())));
        return registry;
    }

    private static string Table(string name, string rows) => $"{{\"table\":\"{name}\",\"schema_version\":1,\"rows\":{rows}}}";

    private sealed class FalseExprFactory : IExprHostFactory
    {
        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new FalseExprHost();
    }
    private sealed class FalseExprHost : IExprHost
    {
        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) => ExprValue.OfBool(false);
    }

    private sealed class OneUnitAccess : IUnitAccess
    {
        private readonly Id _id;
        public OneUnitAccess(Id id) { _id = id; }
        public bool Exists(Id unitId) => unitId.Equals(_id);
        public IReadOnlyList<Id> AllUnits => new[] { _id };
        public Vec2 GetPosition(Id unitId) => Vec2.Zero;
        public void SetPosition(Id unitId, Vec2 position) { }
        public Id GetFaction(Id unitId) => new Id("fac.repro");
        public int GetLevel(Id unitId) => 1;
        public double GetFacing(Id unitId) => 0;
        public bool IsAlive(Id unitId) => true;
        public void SetAlive(Id unitId, bool alive) { }
        public Id? GetTemplateId(Id unitId) => new Id("creature.repro");
        public IReadOnlyList<Id> GetTags(Id unitId) => Array.Empty<Id>();
    }
}



