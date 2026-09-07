using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Gameplay.Spawn;
namespace Repro
{
    public static class ReproSpawnWorld
    {
        private static readonly Id MapId = new Id("world.spawn_timer_reload_test_map");
        private static readonly Id PlayerId = new Id("unit.spawn_timer_reload_test_player");
        private static readonly Id PlayerFactionId = new Id("fac.spawn_timer_reload_test_player");
        private static readonly Id ArchetypeId = new Id("arch.class.spawn_timer_reload_test_sample");
        private static readonly Id SlotId = new Id("slot.spawn_timer_reload_test_autosave");
        private static readonly Id SpawnPointId = new Id("spawn.spawn_timer_reload_test.wolf");
        private static readonly Id TemplateId = new Id("creature.spawn_timer_reload_test_wolf");

        private const double RespawnTimerSeconds = 30.0;

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 10}," +
            "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.spawn_timer_reload_test_sample\", \"max_level\": 1, " +
            "\"entries\": [{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"arch.class.spawn_timer_reload_test_sample\", \"name_key\": \"l10n.arch.class.spawn_timer_reload_test_sample.name\", " +
            "\"primary_stat\": \"stat.power\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.spawn_timer_reload_test_sample\"}]";

        private const string FactionRows =
            "[{\"id\": \"fac.spawn_timer_reload_test_player\", \"name_key\": \"l10n.fac.spawn_timer_reload_test_player.name\", \"default_reaction\": \"neutral\"}," +
            "{\"id\": \"fac.spawn_timer_reload_test_monster\", \"name_key\": \"l10n.fac.spawn_timer_reload_test_monster.name\", \"default_reaction\": \"hostile\"}]";

        private const string TierDefinitionRows =
            "[{\"id\": \"creature.tier.normal\", \"name_key\": \"l10n.creature.tier.normal.name\", " +
            "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}]";

        private const string RotationId = "ai.rotation.spawn_timer_reload_test";
        private const string RotationRows =
            "[{ \"id\": \"" + RotationId + "\", \"entries\": [" +
            "{ \"priority\": 1, \"condition\": \"false\", \"skill_id\": \"skill.never\" }" +
            "] }]";

        private const string ProfileId = "ai.behavior.spawn_timer_reload_test";
        private const string ProfileRows =
            "[{ \"id\": \"" + ProfileId + "\", \"perception_radius\": 5, \"leash_range\": 10, " +
            "\"combat_return_policy\": \"return_to_spawn\", \"rotation_ref\": \"" + RotationId + "\" }]";

        private static string CreatureTemplateRows =>
            "[{\"id\": \"" + TemplateId.Value + "\", \"name_key\": \"l10n.creature.spawn_timer_reload_test_wolf.name\", " +
            "\"level\": 1, \"tier\": \"creature.tier.normal\", " +
            "\"base_stats\": {\"stat.power\": 10, \"stat.max_health\": 50}, " +
            "\"faction_id\": \"fac.spawn_timer_reload_test_monster\", \"display_ref\": \"display.spawn_timer_reload_test_wolf\", " +
            "\"ai_behavior_ref\": \"" + ProfileId + "\"}]";

        private static string SpawnTableRows =>
            "[{\"id\": \"" + SpawnPointId.Value + "\", \"map_id\": \"" + MapId.Value + "\", " +
            "\"content_ref\": \"" + TemplateId.Value + "\", \"position\": {\"x\": 1, \"y\": 1}, " +
            "\"respawn_policy\": \"timer\", \"respawn_timer\": " + RespawnTimerSeconds + "}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        public static bool Run()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("prog.level_curve", Envelope("prog.level_curve", LevelCurveRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("fac.faction", Envelope("fac.faction", FactionRows))
                .Add("creature.tier_definition", Envelope("creature.tier_definition", TierDefinitionRows))
                .Add("creature.template", Envelope("creature.template", CreatureTemplateRows))
                .Add("ai.behavior_profile", Envelope("ai.behavior_profile", ProfileRows))
                .Add("ai.rotation", Envelope("ai.rotation", RotationRows))
                .Add("ai.patrol_path", Envelope("ai.patrol_path", "[]"))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"))
                .Add("spawn.table", Envelope("spawn.table", SpawnTableRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            RAssert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.spawn_timer_reload_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new Core.Carriers.Unit.PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeId, raceId: null, level: 1);
            gameplay.RegisterPersistables(saveSystem, player);

            var generated = gameplay.Spawn.ApplyForMap(MapId);
            RAssert.Single(generated);
            var firstEntityId = generated[0];

            gameplay.Carriers.Creatures.Despawn(firstEntityId, "died");
            bus.DispatchPending();
            var recordAfterDeath = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            RAssert.NotNull(recordAfterDeath);
            RAssert.Equal(RespawnTimerSeconds, recordAfterDeath!.RespawnRemaining);

            world.Tick(SimStep.Continuous(RespawnTimerSeconds / 2));
            var recordAtSave = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            RAssert.Equal(RespawnTimerSeconds / 2, recordAtSave!.RespawnRemaining!.Value, 3);
            var saveResult = saveSystem.Save(new SaveRequest(SlotId, "2026-09-07T00:00:00Z"));
            RAssert.True(saveResult.Success, saveResult.Message);

            world.Tick(SimStep.Continuous(RespawnTimerSeconds / 2 + 1));
            var recordBeforeLoad = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            RAssert.True(recordBeforeLoad!.EntityId.HasValue, "当前会话应当已经重新生成了新的存活实体");
            var respawnedEntityId = recordBeforeLoad.EntityId!.Value;
            RAssert.NotEqual(firstEntityId, respawnedEntityId);

            var loadResult = gameplay.RestoreFromSlot(SlotId);
            RAssert.True(loadResult.Status == LoadStatus.Loaded || loadResult.Status == LoadStatus.LoadedFromBackup, $"读档应当成功，实际：{loadResult.Status}");

            var recordAfterLoad = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            RAssert.NotNull(recordAfterLoad);
            RAssert.True(recordAfterLoad!.RespawnRemaining.HasValue, "读档后倒计时不应该被清空");
            RAssert.Equal(RespawnTimerSeconds / 2, recordAfterLoad.RespawnRemaining!.Value, 3);
            var orphanBeforeNewTimerEntity = world.GetEntity(respawnedEntityId) != null;

            world.Tick(SimStep.Continuous(RespawnTimerSeconds / 2 - 1));
            var recordJustBeforeTimerEnds = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            RAssert.False(recordJustBeforeTimerEnds!.EntityId.HasValue, "还差 1 秒，不应该提前重生");

            world.Tick(SimStep.Continuous(2));
            var recordAfterTimerEnds = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            RAssert.True(recordAfterTimerEnds!.EntityId.HasValue, "15 秒倒计时结束后应当重新生成");
            var thirdEntityId = recordAfterTimerEnds.EntityId!.Value;
            var sameTemplateEntities = world.QueryEntities(new EntityFilter(mapId: MapId, predicate: e => e.TemplateId.HasValue && e.TemplateId.Value.Equals(TemplateId)));
            var orphanAfterNewTimerEntity = world.GetEntity(respawnedEntityId) != null;
            var duplicatedWorldEntity = orphanBeforeNewTimerEntity && orphanAfterNewTimerEntity && !thirdEntityId.Equals(respawnedEntityId) && sameTemplateEntities.Count >= 2;
            Console.WriteLine("ReproSpawnWorld expected=restoring_same_map_snapshot_must_reconcile_post_snapshot_respawned_entity_before_timer_generates_again");
            Console.WriteLine($"ReproSpawnWorld actual=orphanBeforeNew:{orphanBeforeNewTimerEntity} orphanAfterNew:{orphanAfterNewTimerEntity} respawned:{respawnedEntityId} third:{thirdEntityId} sameTemplateCount:{sameTemplateEntities.Count} reproduced:{duplicatedWorldEntity}");
            Console.WriteLine("ReproSpawnWorld boundary=SpawnHost preserves saved respawn timer but has no world removal step for the already-respawned entity, so timer expiry creates another live entity");
            return duplicatedWorldEntity;
        }
    }
    internal static class RAssert
    {
        public static void True(bool v, string? m = "") { if (!v) throw new InvalidOperationException(m); }
        public static void False(bool v, string? m = "") { if (v) throw new InvalidOperationException(m); }
        public static void NotNull(object? v, string? m = "") { if (v == null) throw new InvalidOperationException(m); }
        public static void NotEqual<T>(T a, T b) { if (EqualityComparer<T>.Default.Equals(a, b)) throw new InvalidOperationException("values unexpectedly equal"); }
        public static void Equal<T>(T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) throw new InvalidOperationException($"expected {a}, actual {b}"); }
        public static void Equal(double a, double b, int precision) { if (Math.Abs(a-b) > Math.Pow(10, -precision)) throw new InvalidOperationException($"expected {a}, actual {b}"); }
        public static void Single<T>(IReadOnlyCollection<T> values) { if (values.Count != 1) throw new InvalidOperationException($"expected one, actual {values.Count}"); }
    }}






