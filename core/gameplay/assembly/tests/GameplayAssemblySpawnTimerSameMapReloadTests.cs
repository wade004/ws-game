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
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// C11 复现与根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：怪物死亡、
    /// 刷新倒计时进行到一半时存档；在当前（未读档的）会话里继续放置，直到它重新生成一个新的存活
    /// 实体；随后经 <see cref="GameplayAssembly.RestoreFromSlot"/>（<c>ShellHost.LoadGame</c>/
    /// <c>DeathPolicyHost</c> 共用的"读档 + 必要时切场景"协议，见该方法判断记录）读取旧档——因为
    /// 目标地图与当前地图相同，不会触发 <c>ISceneRouter.LoadScene</c>，也就不会有任何
    /// <c>LeaveMap</c>/<c>EnterMap</c> 周期，<see cref="Core.Gameplay.Spawn.SpawnHost.Load"/> 是
    /// 唯一负责调和"存档记录的倒计时"与"当前世界恰好有个存活实体"这两份互相矛盾状态的地方。
    /// 验收：存档记录的倒计时（本用例 15 秒）读档后仍然有效——不会因为读档时刻当前世界有个"存档时间
    /// 点之后才诞生"的存活实体，就被当成"当前存活、不该有倒计时"处理并清空。
    /// </summary>
    public class GameplayAssemblySpawnTimerSameMapReloadTests
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

        [Fact]
        public void SavedRespawnTimer_SurvivesSameMapReload_EvenWhenCurrentSessionAlreadyRespawnedANewEntity()
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
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

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

            // 1) 进图：timer 刷新点首次 ApplyForMap 立即生成一只怪物（RespawnRemaining 初始为
            //    null，见 SpawnHost.ApplyForMap timer 分支判断记录）。
            var generated = gameplay.Spawn.ApplyForMap(MapId);
            Assert.Single(generated);
            var firstEntityId = generated[0];

            // 2) 怪物死亡：倒计时开始（respawn_timer=30s）。CreatureFactory.Despawn 只 Enqueue
            //    creature.despawned（不立即派发，见该方法判断记录），需要显式 DispatchPending 才会
            //    真正推动 SpawnHost.NotifyDespawn 写入 RespawnRemaining。
            gameplay.Carriers.Creatures.Despawn(firstEntityId, "died");
            bus.DispatchPending();
            var recordAfterDeath = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            Assert.NotNull(recordAfterDeath);
            Assert.Equal(RespawnTimerSeconds, recordAfterDeath!.RespawnRemaining);

            // 3) 倒计时进行到一半（还剩 15 秒）时存档。
            world.Tick(SimStep.Continuous(RespawnTimerSeconds / 2));
            var recordAtSave = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            Assert.Equal(RespawnTimerSeconds / 2, recordAtSave!.RespawnRemaining!.Value, 3);
            var saveResult = saveSystem.Save(new SaveRequest(SlotId, "2026-09-07T00:00:00Z"));
            Assert.True(saveResult.Success, saveResult.Message);

            // 4) 存档之后：不读档，继续在当前会话里推进——倒计时结束，刷新点重新生成一个"存档时间点
            //    之后才诞生"的新存活实体。
            world.Tick(SimStep.Continuous(RespawnTimerSeconds / 2 + 1));
            var recordBeforeLoad = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            Assert.True(recordBeforeLoad!.EntityId.HasValue, "当前会话应当已经重新生成了新的存活实体");
            var respawnedEntityId = recordBeforeLoad.EntityId!.Value;
            Assert.NotEqual(firstEntityId, respawnedEntityId);

            // 5) 经 RestoreFromSlot 读取旧档——目标地图与当前地图相同，不触发 LoadScene/LeaveMap/
            //    EnterMap，SpawnHost.Load 是唯一的调和点。
            var loadResult = gameplay.RestoreFromSlot(SlotId);
            Assert.True(loadResult.Status == LoadStatus.Loaded || loadResult.Status == LoadStatus.LoadedFromBackup, $"读档应当成功，实际：{loadResult.Status}");

            // C11 核心断言：存档记录的倒计时（15 秒）必须被保留，不能因为"当前世界恰好有个存活实体"
            // 就被当成"当前存活、不该有倒计时"处理并清空——旧实现会在这里把 RespawnRemaining 清成
            // null，本刷新点从此既不计时也不会被判定为"需要重生"。
            var recordAfterLoad = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            Assert.NotNull(recordAfterLoad);
            Assert.True(recordAfterLoad!.RespawnRemaining.HasValue, "读档后倒计时不应该被清空");
            Assert.Equal(RespawnTimerSeconds / 2, recordAfterLoad.RespawnRemaining!.Value, 3);

            // 6) 验收"15 秒计时仍然有效"：再推进 15 秒（略多一点，避开浮点边界），刷新点应当据此
            //    重新生成一个实体——不多不少，不提前也不延迟。
            world.Tick(SimStep.Continuous(RespawnTimerSeconds / 2 - 1));
            var recordJustBeforeTimerEnds = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            Assert.False(recordJustBeforeTimerEnds!.EntityId.HasValue, "还差 1 秒，不应该提前重生");

            world.Tick(SimStep.Continuous(2));
            var recordAfterTimerEnds = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            Assert.True(recordAfterTimerEnds!.EntityId.HasValue, "15 秒倒计时结束后应当重新生成");
        }

        /// <summary>
        /// R14 复现与根治（architecture/落地计划/audit-5e779c6-20260907）：接续上面 C11 场景——同图读档
        /// 正确保留了倒计时（上面那条测试的断言），但读档时刻世界里那个"存档之后、读档之前才诞生"的
        /// 存活实体（<c>respawnedEntityId</c>）此前从未被移除，只是脱离了刷新点追踪；15 秒倒计时结束
        /// 后 <see cref="Core.Gameplay.Spawn.SpawnHost"/> 又按刷新表生成一个新实体，同一刷新点、同一
        /// 模板在世界里同时存在两个实体——这才是 R14 真正要根治的"重复生成"，C11 只根治了"倒计时被
        /// 清空"。验收：<c>respawnedEntityId</c> 最终被移除（<c>Despawn</c> 走
        /// <c>IWorldSim.MarkForDestruction</c>，真正从 <c>world.GetEntity</c> 消失要等下一次
        /// <see cref="Core.Foundation.SimLoop.WorldSim.Tick"/> 的生命周期清理阶段，读档瞬间还查得到不
        /// 算 bug），倒计时结束后同模板存活实体数量精确为 1，不是 2。
        /// </summary>
        [Fact]
        public void SavedRespawnTimer_SameMapReload_DespawnsOrphanEntity_NotDuplicated_R14()
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
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

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
            Assert.Single(generated);
            var firstEntityId = generated[0];

            gameplay.Carriers.Creatures.Despawn(firstEntityId, "died");
            bus.DispatchPending();

            world.Tick(SimStep.Continuous(RespawnTimerSeconds / 2));
            var saveResult = saveSystem.Save(new SaveRequest(SlotId, "2026-09-07T00:00:00Z"));
            Assert.True(saveResult.Success, saveResult.Message);

            // 存档之后、读档之前：当前会话继续推进，倒计时结束，刷新点生成一个"存档时间点之后才诞生"
            // 的新存活实体——这就是读档后要被回收的孤儿。
            world.Tick(SimStep.Continuous(RespawnTimerSeconds / 2 + 1));
            var recordBeforeLoad = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            Assert.True(recordBeforeLoad!.EntityId.HasValue);
            var respawnedEntityId = recordBeforeLoad.EntityId!.Value;
            Assert.NotEqual(firstEntityId, respawnedEntityId);
            Assert.NotNull(world.GetEntity(respawnedEntityId));

            var loadResult = gameplay.RestoreFromSlot(SlotId);
            Assert.True(loadResult.Status == LoadStatus.Loaded || loadResult.Status == LoadStatus.LoadedFromBackup, $"读档应当成功，实际：{loadResult.Status}");

            // Despawn 走 MarkForDestruction：读档这一刻孤儿实体仍然查得到，真正的移除要等下一次
            // world.Tick 的生命周期清理阶段——这是既有惯例（同任何其它 Despawn 调用），不是本次修复
            // 要打破的行为。
            Assert.NotNull(world.GetEntity(respawnedEntityId));

            // 15 秒倒计时还没结束：孤儿实体应当已经在这次 tick 的生命周期清理阶段被移除了。
            world.Tick(SimStep.Continuous(RespawnTimerSeconds / 2 - 1));
            Assert.Null(world.GetEntity(respawnedEntityId));
            var recordJustBeforeTimerEnds = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            Assert.False(recordJustBeforeTimerEnds!.EntityId.HasValue, "还差 1 秒，不应该提前重生");

            // 倒计时结束：刷新点重新生成第三个实体——必须只有它一个，不能与已经回收的孤儿并存。
            world.Tick(SimStep.Continuous(2));
            var recordAfterTimerEnds = gameplay.Spawn.GetSpawnRecord(SpawnPointId);
            Assert.True(recordAfterTimerEnds!.EntityId.HasValue, "15 秒倒计时结束后应当重新生成");
            var thirdEntityId = recordAfterTimerEnds.EntityId!.Value;
            Assert.NotEqual(respawnedEntityId, thirdEntityId);

            var sameTemplateEntities = world.QueryEntities(
                new EntityFilter(mapId: MapId, predicate: e => e.TemplateId.HasValue && e.TemplateId.Value.Equals(TemplateId)));
            var onlyEntity = Assert.Single(sameTemplateEntities);
            Assert.Equal(thirdEntityId, onlyEntity.EntityId);
        }
    }
}
