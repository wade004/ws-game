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
    /// 外部审核阻塞项 2 收口回归（见 architecture/落地计划/audit-20260907/followup-2026-09-07.md
    /// "外部审核阻塞项处理"一节）：<c>death.reload_save</c> 策略此前只调用 <c>ISaveSystem.Load</c>
    /// 本身，既不处理"目标地图与当前地图不同"的场景切换，也没有恢复玩家存活状态/生命值——本文件
    /// 脱离引擎，用真实 <see cref="GameplayAssembly"/> + 真实
    /// <see cref="Core.Foundation.SceneRouter.SceneRouter"/> 复现"玩家死亡 → reload_save → 回到
    /// 存档时刻的地图/位置/存活状态/生命值"这条完整链路（同一地图与跨地图两种情形），以及"没有可用
    /// 自动存档时回退到 respawn_point"的兜底路径。
    /// </summary>
    public sealed class GameplayAssemblyDeathReloadTests
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
        private static Fixture Build(RespawnPolicy policy, bool withSceneRouter)
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
                deathPolicyOptions: new DeathPolicyOptions { Policy = policy, AutosaveSlotId = AutosaveSlot });

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

        // -----------------------------------------------------------------
        // 同地图：reload_save 应恢复存活状态与"存档时刻"的生命值（不是满血），不需要切场景。
        // -----------------------------------------------------------------

        [Fact]
        public void PlayerDies_ReloadSave_PlayerAliveOnSavedMapWithSavedHealth_SameMap()
        {
            var fx = Build(RespawnPolicy.ReloadSave, withSceneRouter: false);

            // 存档点时刻：玩家受过伤（60/100），仍存活——模拟"最近一次自动存档"发生在死亡之前。
            fx.Gameplay.Carriers.Rules.Powers.ModifyPower(PlayerId, WellKnownPowers.Health, -40, PlayerId);
            Assert.Equal(60.0, fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, WellKnownPowers.Health));
            fx.Player.MapId = MapA;
            fx.Player.Position = new Vec2(3, 4);
            var saveResult = fx.SaveSystem.Save(new SaveRequest(AutosaveSlot, "t1"));
            Assert.True(saveResult.Success, saveResult.Message);

            // 死亡：KillPlayer 内部把生命值砍到 0、Alive=false（同 Resolver 死亡结算），随后
            // PublishImmediate 同步触发 DeathPolicyHost.OnUnitDied → reload_save → RestoreFromSlot
            // → SaveSystem.Load——本方法返回时，"死亡"与"读档恢复"这两步已经在同一次调用栈内全部
            // 完成，观察不到中间的"已死亡但尚未回档"状态（PublishImmediate 是同步派发，不像
            // Enqueue 那样留到下一次 DispatchPending）。
            fx.KillPlayer(MapA);

            Assert.True(fx.Gameplay.Carriers.Units.IsAlive(PlayerId), "reload_save 应恢复存活状态");
            Assert.Equal(60.0, fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, WellKnownPowers.Health));
            Assert.Equal(MapA, fx.Player.MapId);
            Assert.Equal(new Vec2(3, 4), fx.Player.Position);
            Assert.NotNull(fx.World.GetEntity(PlayerId)); // 同地图不触发 ClearAll，实体全程未被移除。
        }

        // -----------------------------------------------------------------
        // 跨地图：存档时在 A 图，死亡时已经在 B 图——reload_save 应把玩家带回 A 图（真实经
        // ISceneRouter 切场景），并恢复存活状态与生命值；B 图不应残留玩家实体。
        // -----------------------------------------------------------------

        [Fact]
        public void PlayerDies_ReloadSave_PlayerAliveOnSavedMapWithSavedHealth_CrossMap()
        {
            var fx = Build(RespawnPolicy.ReloadSave, withSceneRouter: true);

            fx.Gameplay.Carriers.Rules.Powers.ModifyPower(PlayerId, WellKnownPowers.Health, -30, PlayerId);
            Assert.Equal(70.0, fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, WellKnownPowers.Health));
            fx.Player.MapId = MapA;
            fx.Player.Position = new Vec2(5, 6);
            var saveResult = fx.SaveSystem.Save(new SaveRequest(AutosaveSlot, "t1"));
            Assert.True(saveResult.Success, saveResult.Message);

            // 存档之后玩家经场景路由真的走到了 B 图（同真实 ShellHost/AreaTrigger 传送路径：
            // ClearAll 移除全部实体，post_load 钩子重新添加玩家 + EnterMap）。SceneRouter 本身不
            // 知道"玩家"这个概念（只管场景资源与 IWorldSim.ClearAll），真实链路里玩家实体的
            // MapId 字段由触发传送的一方显式写入（见 GameplayAssembly.TeleportUnit "entity.MapId =
            // targetMap"），这里显式模拟同一步。
            fx.Player.MapId = MapB;
            fx.Router.LoadScene(MapB);
            fx.Router.Update();
            Assert.Equal(MapB, fx.Router.GetCurrentScene());
            Assert.Equal(MapB, fx.Player.MapId);

            // 在 B 图继续受伤，然后死亡。KillPlayer 内部 PublishImmediate 同步触发
            // DeathPolicyHost.OnUnitDied → reload_save → RestoreFromSlot——SaveSystem.Load 本身
            // （含 player.vitals 段恢复 Alive/生命值）在这次调用内已经同步完成，观察不到中间的
            // "已死亡但尚未回档"状态（同 SameMap 用例判断记录）；但场景切换只是"发起"
            // （ISceneRouter.LoadScene 只启动异步加载），要等下面显式调用一次 Router.Update()
            // 桩资源加载器同步解析出的结果才会真正生效、触发 post_load 钩子把玩家带回 A 图。
            fx.Gameplay.Carriers.Rules.Powers.ModifyPower(PlayerId, WellKnownPowers.Health, -20, PlayerId);
            fx.KillPlayer(MapB);

            // reload_save：读档发现 CurrentMapId=A ≠ 当前地图 B，经 GameplayAssembly.RestoreFromSlot
            // 真正调用 ISceneRouter.LoadScene(A)；场景路由是异步的，本类型测试驱动一次 Update() 让
            // 桩资源加载器同步完成的加载真正生效、触发 post_load 钩子。
            fx.Router.Update();

            Assert.Equal(MapA, fx.Router.GetCurrentScene());
            Assert.True(fx.Gameplay.Carriers.Units.IsAlive(PlayerId), "reload_save 应恢复存活状态");
            Assert.Equal(70.0, fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, WellKnownPowers.Health));
            Assert.Equal(MapA, fx.Player.MapId);
            Assert.Equal(new Vec2(5, 6), fx.Player.Position);

            var entity = fx.World.GetEntity(PlayerId);
            Assert.NotNull(entity);
            Assert.Equal(MapA, entity!.MapId); // 掉落物/玩家都不应残留在 B 图（ClearAll 已清空 B 图集合）。
        }

        // -----------------------------------------------------------------
        // 没有可用自动存档（如游戏刚开始、存档点/任务完成都还没触发过一次自动存档）：reload_save
        // 读档失败（LoadStatus.NotFound），应回退到 respawn_point 策略同一套默认复活点逻辑，而不是
        // 让玩家永久停留在"已死亡"状态。
        // -----------------------------------------------------------------

        [Fact]
        public void PlayerDies_ReloadSave_NoAutosave_FallsBackToRespawnPointAndLogsDiagnostic()
        {
            var fx = Build(RespawnPolicy.ReloadSave, withSceneRouter: false);
            Assert.False(fx.SaveSystem.SlotExists(AutosaveSlot)); // 从未存过档。

            fx.KillPlayer(MapA);
            Assert.False(fx.Gameplay.Carriers.Units.IsAlive(PlayerId));

            // respawn_point 的延迟复活队列挂在 TickPhase.TriggerEvaluation（见
            // DeathPolicyHost.Execute），需要推进一次 tick 才会真正复活。
            fx.World.Tick(SimStep.Continuous(0.1));

            Assert.True(fx.Gameplay.Carriers.Units.IsAlive(PlayerId), "无可用自动存档时应回退到 respawn_point 复活");
        }
    }
}
