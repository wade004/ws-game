using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Gameplay.Death;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// 消费方反馈 P2 缺口 1 回归：<c>respawn_point</c> 策略此前只能在死亡单位所属地图复活
    /// （<c>DeathPolicyHost</c> 忽略 <c>ResolveDefaultSpawn</c> 返回的地图 id，只用坐标），"死在地牢、
    /// 回城镇复活"做不到。修复后 <c>DeathPolicyOptions.RespawnMapId</c> 指定复活地图，跨图时经场景路由
    /// 落地再复活。本文件用真实 <see cref="GameplayAssembly"/> + 真实 <c>SceneRouter</c> 复现。
    /// </summary>
    public sealed class GameplayAssemblyDeathRespawnOtherMapTests
    {
        private static readonly Id PlayerId = new Id("unit.death_reload_player");
        private static readonly Id PlayerFactionId = new Id("fac.death_reload_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.death_reload_sample");
        private static readonly Id MapA = new Id("world.death_reload_map_a");
        private static readonly Id MapB = new Id("world.death_reload_map_b");
        private static readonly Id AutosaveSlot = new Id("slot.death_reload_autosave");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.death_reload_sample\", \"max_level\": 1, " +
            "\"entries\": [{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.death_reload_sample" + "\", \"name_key\": \"l10n.arch.class.death_reload_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.death_reload_sample\"}]";

        private static string WorldMapRow(Id mapId, string sceneRef, string navRef) =>
            "{\"id\": \"" + mapId.Value + "\", \"scene_ref\": \"" + sceneRef + "\", \"nav_ref\": \"" + navRef + "\", " +
            "\"spawn_points\": [{\"id\": \"spawn." + mapId.Value + ".default\", \"position\": {\"x\": 0, \"y\": 0}, \"facing\": 0}]}";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public StubFileSystem Fs = null!;
            public SaveSystem SaveSystem = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
            public Core.Foundation.SceneRouter.SceneRouter Router = null!;
            public StubResourceLoader Loader = null!;

            /// <summary>模拟 <c>core/rules/combat.Resolver</c> 死亡结算那一刻的状态变化（见该类型
            /// 第 205~229 行"步骤 8：落地"）：把生命值砍到 0、置 Alive=false、发布
            /// <see cref="UnitDiedEvent"/>——本文件脱离真实战斗管线，直接复现死亡那一刻的最小可观察
            /// 后果，不依赖 core/rules/combat 的完整命中判定。</summary>
            public void KillPlayer(Id mapId)
            {
                var current = Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, WellKnownPowers.Health);
                if (current > 0)
                {
                    Gameplay.Carriers.Rules.Powers.ModifyPower(PlayerId, WellKnownPowers.Health, -current, PlayerId);
                }
                Gameplay.Carriers.Units.SetAlive(PlayerId, false);
                Bus.PublishImmediate(new UnitDiedEvent(PlayerId, null, mapId, Gameplay.Carriers.Units.GetPosition(PlayerId)));
            }
        }

        /// <summary><paramref name="withSceneRouter"/>：跨地图用例需要真实 <c>ISceneRouter</c>
        /// （含两张 <c>world.map</c> 记录）；同地图用例不需要，省去这份搭建。</summary>
        private static Fixture Build(RespawnPolicy policy, bool withSceneRouter, Id? respawnMap = null)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("prog.level_curve", Envelope("prog.level_curve", LevelCurveRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);

            // world.map 无论是否需要真实 ISceneRouter 都要登记：DeathPolicyHost 的 respawn_point
            // 兜底路径（见 NoAutosave 用例）经 TeleportTargetResolver.Resolve 解析死亡地图的默认
            // 复活点，同样要查这张表，与是否装配场景路由无关。
            StubResourceLoader loader = null!;
            registry.RegisterSchema(Core.Foundation.SceneRouter.WorldMapSchema.Table);
            var mapRows = "[" + WorldMapRow(MapA, "scene.death_reload_a", "nav.death_reload_a") + "," +
                          WorldMapRow(MapB, "scene.death_reload_b", "nav.death_reload_b") + "]";
            source.Add("world.map", Envelope("world.map", mapRows));

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.death_reload_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId,
                deathPolicyOptions: new DeathPolicyOptions { Policy = policy, AutosaveSlotId = AutosaveSlot, RespawnMapId = respawnMap });

            var player = new PlayerUnit(PlayerId, MapA, PlayerFactionId, ArchetypeSample) { Position = new Vec2(1, 2) };
            world.AddEntity(player);
            // RulesAssembly.RegisterUnit 驱动 Stats.RegisterUnit + Archetypes.ApplyTo；后者按
            // arch.class 行的 power_types 字段经 PowerRegistrar 回调一并调用 Powers.RegisterUnit
            // （见 ArchetypeRegistry.ApplyTo/RulesAssembly 第 1 步 archPowerRegistrar 判断记录），
            // 同时也是 ProgressionPersistable.Save 依赖的 Progression.RegisterUnit 得以被调用的
            // 路径（level_curve_ref 非空时）——不需要再手工调用 Powers.RegisterUnit。
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            gameplay.RegisterPersistables(saveSystem, player);

            var fx = new Fixture
            {
                Bus = bus,
                World = world,
                Fs = fs,
                SaveSystem = saveSystem,
                Gameplay = gameplay,
                Player = player,
            };

            if (withSceneRouter)
            {
                loader = new StubResourceLoader();
                loader.Register(new Id("scene.death_reload_a"));
                loader.Register(new Id("nav.death_reload_a"));
                loader.Register(new Id("scene.death_reload_b"));
                loader.Register(new Id("nav.death_reload_b"));

                var appState = gameplay.AppState;
                var hooks = gameplay.Hooks;
                var router = new Core.Foundation.SceneRouter.SceneRouter(registry, loader, appState, world, hooks, bus);

                router.RegisterPostLoadHook(mapId =>
                {
                    // 同生产装配（FrameworkResidentHost/GameBootstrap.HandlePostLoad）的既有惯例：
                    // ClearAll 之后玩家实体若已缺失则重新 AddEntity，再统一调用 EnterMap（内部已经
                    // 接了 Loot.ReattachToWorld，见外部审核阻塞项 1 收口）。
                    if (world.GetEntity(PlayerId) == null)
                    {
                        world.AddEntity(player);
                        bus.DispatchPending();
                    }
                    gameplay.EnterMap(mapId, PlayerId);
                });

                gameplay.AttachSceneRouter(router);
                fx.Router = router;
                fx.Loader = loader;

                // 应用状态机需要先转入 MainMenu（AppStateMachineConfig.Default 只登记
                // Boot→MainMenu→Loading→InWorld 这条链路，见 core/foundation/app_lifecycle）；
                // Loading 转移由 SceneRouter.LoadScene 自己发起，这里不用手工转。
                appState.RequestTransition(Core.Foundation.AppLifecycle.AppState.MainMenu);
                router.LoadScene(MapA);
                router.Update();
                Assert.Equal(MapA, router.GetCurrentScene());
            }

            return fx;
        }

        /// <summary>复现：玩家死在 B 图，RespawnMapId=A。修复前复活在 B 图（死亡地图）的默认复活点、
        /// 场景不切；修复后场景切到 A、玩家实体 MapId=A、位置为 A 图 spawn_points[0]、存活且满血。</summary>
        [Fact]
        public void PlayerDiesOnMapB_RespawnMapIsA_RespawnsOnMapAThroughSceneRouter()
        {
            var fx = Build(RespawnPolicy.RespawnPoint, withSceneRouter: true, respawnMap: MapA);
            fx.Player.MapId = MapB;
            fx.Player.Position = new Vec2(9, 9);
            fx.Router.LoadScene(MapB);
            fx.Router.Update();
            Assert.Equal(MapB, fx.Router.GetCurrentScene());

            fx.KillPlayer(MapB);
            Assert.False(fx.Gameplay.Carriers.Units.IsAlive(PlayerId));

            fx.World.Tick(SimStep.Continuous(0.1));
            fx.Router.Update();

            Assert.Equal(MapA, fx.Router.GetCurrentScene());
            Assert.True(fx.Gameplay.Carriers.Units.IsAlive(PlayerId), "应在复活地图复活");
            Assert.Equal(MapA, fx.Player.MapId);
            Assert.Equal(new Vec2(0, 0), fx.Player.Position);
            Assert.Equal(100.0, fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, WellKnownPowers.Health));
            Assert.NotNull(fx.World.GetEntity(PlayerId));
        }

        /// <summary>不变量：不设置 RespawnMapId 时行为逐位不变——仍在死亡地图复活，场景不切。</summary>
        [Fact]
        public void PlayerDiesOnMapB_NoRespawnMap_StillRespawnsOnMapB_NoSceneChange()
        {
            var fx = Build(RespawnPolicy.RespawnPoint, withSceneRouter: true);
            fx.Player.MapId = MapB;
            fx.Router.LoadScene(MapB);
            fx.Router.Update();

            fx.KillPlayer(MapB);
            fx.World.Tick(SimStep.Continuous(0.1));
            fx.Router.Update();

            Assert.Equal(MapB, fx.Router.GetCurrentScene());
            Assert.True(fx.Gameplay.Carriers.Units.IsAlive(PlayerId));
            Assert.Equal(MapB, fx.Player.MapId);
        }

        /// <summary>不变量：复活地图就是死亡地图时同样走既有同图路径，不发起整场景重载。</summary>
        [Fact]
        public void PlayerDiesOnMapA_RespawnMapIsA_SameMapPath_NoReload()
        {
            var fx = Build(RespawnPolicy.RespawnPoint, withSceneRouter: true, respawnMap: MapA);
            fx.KillPlayer(MapA);
            fx.World.Tick(SimStep.Continuous(0.1));
            Assert.True(fx.Gameplay.Carriers.Units.IsAlive(PlayerId));
            Assert.Equal(MapA, fx.Router.GetCurrentScene());
            Assert.Equal(Core.Foundation.SceneRouter.SceneRouterState.Idle, fx.Router.State); // 没有发起重载。
        }
    }
}
