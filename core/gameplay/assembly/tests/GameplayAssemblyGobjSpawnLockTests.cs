using Core.Carriers.Common;
using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// 消费方反馈第十三批根治：<c>GameplayAssembly</c> 第 11 步给 <c>SpawnOptions.GobjSpawner</c>
    /// 装的默认委托（本文件改动前约 938 行）只转发 <c>Spawn</c> 的 4 个位置参数，<c>lockId</c> 恒为
    /// <c>null</c>——经 <c>spawn.table</c>（<c>on_map_enter</c> 策略）刷新出的 <c>gobj</c> 物件，无论
    /// <c>gobj.template.lock_id</c> 登记了什么锁，生成的 <see cref="GameObjectEntity"/> 都不上锁。
    /// <para>
    /// 本文件经真实 <see cref="GameplayAssembly"/> 装配（不手工调用 <see
    /// cref="GameObjectFactory.Spawn"/>/<see cref="GameObjectFactory.SpawnFromTemplate"/>），登记一条
    /// <c>content_ref</c> 指向 <c>gobj.template</c> 的 <c>spawn.table</c> 行（<c>on_map_enter</c>），
    /// 该模板声明 <c>lock_id</c> 指向一把 <c>world_flag</c> 条件锁；调用
    /// <see cref="GameplayAssembly.EnterMap"/>（内部调 <c>Spawn.ApplyForMap</c>，即
    /// <c>SpawnHost</c> 走到 <c>SpawnOptions.GobjSpawner</c> 委托的生产路径）验收生成实体确实带锁、
    /// 锁条件不满足时交互返回 <c>Locked</c>、满足后可交互；另用一条无 <c>lock_id</c> 的模板验收"模板
    /// 无锁的物件仍无锁"回归不受影响。
    /// </para>
    /// </summary>
    public sealed class GameplayAssemblyGobjSpawnLockTests
    {
        private static readonly Id PlayerId = new Id("unit.gobj_spawn_lock_test_player");
        private static readonly Id PlayerFactionId = new Id("fac.gobj_spawn_lock_test_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.gobj_spawn_lock_test_sample");
        private static readonly Id MapId = new Id("world.gobj_spawn_lock_test_map");
        private static readonly Id LockedTemplateId = new Id("gobj.gobj_spawn_lock_test_locked_chest");
        private static readonly Id UnlockedTemplateId = new Id("gobj.gobj_spawn_lock_test_unlocked_chest");
        private static readonly Id LockId = new Id("gobj.lock.gobj_spawn_lock_test_flag_lock");
        private static readonly Id LockedSpawnPointId = new Id("spawn.gobj_spawn_lock_test.locked_chest");
        private static readonly Id UnlockedSpawnPointId = new Id("spawn.gobj_spawn_lock_test.unlocked_chest");
        private static readonly Id FlagKey = new Id("world.gobj_spawn_lock_test.opened_flag");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.gobj_spawn_lock_test_sample\", \"max_level\": 1, " +
            "\"entries\": [{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.gobj_spawn_lock_test_sample" + "\", \"name_key\": \"l10n.arch.class.gobj_spawn_lock_test_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.gobj_spawn_lock_test_sample\"}]";

        private static string TemplateRow(Id templateId, string? lockId) =>
            "{\"id\": \"" + templateId.Value + "\", \"name_key\": \"l10n." + templateId.Value.Replace('.', '_') + ".name\", " +
            "\"kind\": \"door\", \"type_data\": {}, " +
            (lockId != null ? "\"lock_id\": \"" + lockId + "\", " : "") +
            "\"display_ref\": \"display.gobj_spawn_lock_test\"}";

        private static string LockRow(Id lockId, Id flagKey) =>
            "{\"id\": \"" + lockId.Value + "\", \"requirement\": {\"kind\": \"world_flag\", \"flag_key\": \"" +
            flagKey.Value + "\", \"expected\": true}, \"consume_key\": false}";

        private static string SpawnRow(Id spawnPointId, Id mapId, Id templateId, double x, double y) =>
            "{\"id\": \"" + spawnPointId.Value + "\", \"map_id\": \"" + mapId.Value + "\", " +
            "\"content_ref\": \"" + templateId.Value + "\", \"position\": {\"x\": " + x + ", \"y\": " + y + "}, " +
            "\"respawn_policy\": \"on_map_enter\"}";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
        }

        private static Fixture Build()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

            var spawnRows = "[" +
                SpawnRow(LockedSpawnPointId, MapId, LockedTemplateId, 1, 1) + "," +
                SpawnRow(UnlockedSpawnPointId, MapId, UnlockedTemplateId, 2, 2) +
                "]";

            var gobjTemplateRows = "[" +
                TemplateRow(LockedTemplateId, LockId.Value) + "," +
                TemplateRow(UnlockedTemplateId, null) +
                "]";

            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("prog.level_curve", Envelope("prog.level_curve", LevelCurveRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"))
                .Add("gobj.template", Envelope("gobj.template", gobjTemplateRows))
                .Add("gobj.lock", Envelope("gobj.lock", "[" + LockRow(LockId, FlagKey) + "]"))
                .Add("dialog.gossip_menu", Envelope("dialog.gossip_menu", "[]"))
                .Add("dialog.story_tree", Envelope("dialog.story_tree", "[]"))
                .Add("spawn.table", Envelope("spawn.table", spawnRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new Adapters.Stub.StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new Adapters.Stub.StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.gobj_spawn_lock_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new Core.Carriers.Unit.PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = Vec2.Zero };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay };
        }

        /// <summary>核心复现与验收：<c>spawn.table</c>（<c>on_map_enter</c>）刷出的、模板登记了
        /// <c>lock_id</c> 的 gobj，生成的运行期实体 <see cref="GameObjectEntity.LockId"/> 必须等于
        /// 模板的锁——修复前恒为 <c>null</c>。</summary>
        [Fact]
        public void EnterMap_SpawnsGobjFromTable_AppliesTemplateLockId()
        {
            var fx = Build();

            fx.Gameplay.EnterMap(MapId, PlayerId);

            var record = fx.Gameplay.Spawn.GetSpawnRecord(LockedSpawnPointId);
            Assert.NotNull(record);
            Assert.True(record!.EntityId.HasValue, "刷新点应已生成实体");

            var entity = fx.World.GetEntity(record.EntityId!.Value) as GameObjectEntity;
            Assert.NotNull(entity);
            Assert.Equal(LockId, entity!.LockId);
        }

        /// <summary>回归：模板未登记 <c>lock_id</c> 的物件，经同一条默认生成路径刷出后仍然无锁。</summary>
        [Fact]
        public void EnterMap_SpawnsGobjFromTable_TemplateWithoutLockId_EntityRemainsUnlocked()
        {
            var fx = Build();

            fx.Gameplay.EnterMap(MapId, PlayerId);

            var record = fx.Gameplay.Spawn.GetSpawnRecord(UnlockedSpawnPointId);
            Assert.NotNull(record);
            Assert.True(record!.EntityId.HasValue, "刷新点应已生成实体");

            var entity = fx.World.GetEntity(record.EntityId!.Value) as GameObjectEntity;
            Assert.NotNull(entity);
            Assert.Null(entity!.LockId);
        }

        /// <summary>运行时可观测验收：锁条件（<c>world_flag</c>）不满足时交互返回
        /// <see cref="InteractOutcome.Locked"/>；满足后可交互成功。</summary>
        [Fact]
        public void EnterMap_SpawnsGobjFromTable_Interact_RespectsTemplateLock_UntilFlagSet()
        {
            var fx = Build();
            fx.Gameplay.EnterMap(MapId, PlayerId);

            var record = fx.Gameplay.Spawn.GetSpawnRecord(LockedSpawnPointId);
            var gobjInstanceId = record!.EntityId!.Value;

            var beforeUnlock = fx.Gameplay.Carriers.GameObjectInteractions.Interact(PlayerId, gobjInstanceId);
            Assert.False(beforeUnlock.Success);
            Assert.Equal(InteractOutcome.Locked, beforeUnlock.Outcome);

            fx.Gameplay.WorldState.Set(FlagKey, ExprValue.OfBool(true), writerId: PlayerId);

            Assert.True(fx.Gameplay.Carriers.GameObjectInteractions.TryUnlock(PlayerId, gobjInstanceId));
        }
    }
}
