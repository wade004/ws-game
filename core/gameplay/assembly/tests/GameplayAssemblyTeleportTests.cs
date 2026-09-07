using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Gameplay.Dialog;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// N14 复现与根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：gossip
    /// <c>teleport</c> 动作跨图此前只改 <c>entity.MapId</c> 一个字段，不触碰场景资源、不清理旧图
    /// 实体、不进入新图（<see cref="GameplayAssembly.EnterMap"/> 从未被调用）。本文件脱离引擎，用
    /// 真实 <see cref="GameplayAssembly"/> + 真实 <see cref="Core.Foundation.SceneRouter.SceneRouter"/>
    /// 复现"玩家在 A 图触发 gossip 传送到 B 图"这条完整链路，惯例同
    /// <see cref="GameplayAssemblyDeathReloadTests"/>（同一个 Build 手法，本文件按 teleport 场景
    /// 单独精简搭建，不共享私有 Fixture 类型）。
    /// </summary>
    public sealed class GameplayAssemblyTeleportTests
    {
        private static readonly Id PlayerId = new Id("unit.teleport_test_player");
        private static readonly Id PlayerFactionId = new Id("fac.teleport_test_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.teleport_test_sample");
        private static readonly Id MapA = new Id("world.teleport_test_map_a");
        private static readonly Id MapB = new Id("world.teleport_test_map_b");
        private static readonly Id OldMapResidentId = new Id("unit.teleport_test_old_resident");
        private static readonly Id NpcId = new Id("unit.teleport_test_npc");
        private static readonly Id MenuId = new Id("dialog.teleport_test_menu");
        private static readonly Id SameMapMenuId = new Id("dialog.teleport_test_same_map_menu");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.teleport_test_sample\", \"max_level\": 1, " +
            "\"entries\": [{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.teleport_test_sample" + "\", \"name_key\": \"l10n.arch.class.teleport_test_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.teleport_test_sample\"}]";

        private static string WorldMapRow(Id mapId, string sceneRef, string navRef, double spawnX, double spawnY) =>
            "{\"id\": \"" + mapId.Value + "\", \"scene_ref\": \"" + sceneRef + "\", \"nav_ref\": \"" + navRef + "\", " +
            "\"spawn_points\": [{\"id\": \"spawn." + mapId.Value + ".default\", \"position\": {\"x\": " + spawnX +
            ", \"y\": " + spawnY + "}, \"facing\": 0}]}";

        private static string GossipMenuRow(Id menuId, Id teleportTargetRef) =>
            "{\"id\": \"" + menuId.Value + "\", \"options\": [{\"text_key\": \"l10n.teleport_test.option\", " +
            "\"actions\": [{\"kind\": \"teleport\", \"ref\": \"" + teleportTargetRef.Value + "\"}]}]}";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
            public Core.Foundation.SceneRouter.SceneRouter Router = null!;
        }

        private static Fixture Build()
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
            registry.RegisterSchema(Core.Foundation.SceneRouter.WorldMapSchema.Table);

            var mapRows = "[" + WorldMapRow(MapA, "scene.teleport_test_a", "nav.teleport_test_a", 1, 2) + "," +
                          WorldMapRow(MapB, "scene.teleport_test_b", "nav.teleport_test_b", 30, 40) + "]";
            source.Add("world.map", Envelope("world.map", mapRows));
            var gossipRows = "[" + GossipMenuRow(MenuId, MapB) + "," + GossipMenuRow(SameMapMenuId, MapA) + "]";
            source.Add("dialog.gossip_menu", Envelope("dialog.gossip_menu", gossipRows));
            source.Add("dialog.story_tree", Envelope("dialog.story_tree", "[]"));

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.teleport_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            // 起始位置故意不等于 MapA 的 spawn_points[0]（1,2），这样"同图内传送到本图默认出生点"
            // 用例能观察到位置确实发生了变化，不是碰巧数值相同。
            var player = new PlayerUnit(PlayerId, MapA, PlayerFactionId, ArchetypeSample) { Position = new Vec2(5, 5) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            // A 图的"旧图实体"占位（N14 验收"旧图实体不可见"）：不需要真实 creature.template/AI，
            // 复用 PlayerUnit 类型当一个普通世界实体即可——本测试只关心 IWorldSim.ClearAll 这一层
            // 通用生命周期行为，不关心它具体是玩家还是生物。
            var oldMapResident = new PlayerUnit(OldMapResidentId, MapA, PlayerFactionId, ArchetypeSample) { Position = new Vec2(9, 9) };
            world.AddEntity(oldMapResident);

            var loader = new StubResourceLoader();
            loader.Register(new Id("scene.teleport_test_a"));
            loader.Register(new Id("nav.teleport_test_a"));
            loader.Register(new Id("scene.teleport_test_b"));
            loader.Register(new Id("nav.teleport_test_b"));

            var appState = gameplay.AppState;
            var hooks = gameplay.Hooks;
            var router = new Core.Foundation.SceneRouter.SceneRouter(registry, loader, appState, world, hooks, bus);

            // 惯例同 GameplayAssemblyDeathReloadTests.Build：pre_unload → LeaveMap，post_load →
            // （缺玩家实体则重新 AddEntity）→ EnterMap，与生产装配（GameBootstrap）的既有接线一致。
            router.RegisterPreUnloadHook(mapId => gameplay.LeaveMap(mapId));
            router.RegisterPostLoadHook(mapId =>
            {
                if (world.GetEntity(PlayerId) == null)
                {
                    world.AddEntity(player);
                    bus.DispatchPending();
                }
                gameplay.EnterMap(mapId, PlayerId);
            });

            gameplay.AttachSceneRouter(router);

            appState.RequestTransition(Core.Foundation.AppLifecycle.AppState.MainMenu);
            router.LoadScene(MapA);
            router.Update();
            Assert.Equal(MapA, router.GetCurrentScene());

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay, Player = player, Router = router };
        }

        /// <summary>N14 核心验收：gossip teleport 跨图后——(1) 旧图实体不可见（<see cref="IWorldSim.ClearAll"/>
        /// 真的被触发，不是旧实现那种只改字段）；(2) 玩家实体确实"进入"了新图（<see
        /// cref="Core.Foundation.SceneRouter.SceneRouter.GetCurrentScene"/> 与玩家自身 MapId 都等于
        /// 目标地图）；(3) 位置正确（落在目标地图 <c>spawn_points[0]</c>，不是停留在旧位置）。</summary>
        [Fact]
        public void GossipTeleport_CrossMap_ClearsOldMapEntities_EntersNewMap_PositionsAtTargetSpawnPoint()
        {
            var fx = Build();
            Assert.NotNull(fx.World.GetEntity(OldMapResidentId)); // 传送前，旧图占位实体确实存在。

            fx.Gameplay.Dialog.OpenGossip(PlayerId, NpcId, MenuId);
            var chosen = fx.Gameplay.Dialog.ChooseOption(PlayerId, 0);
            Assert.True(chosen);

            // ISceneRouter.LoadScene 只是发起一次异步加载，桩资源加载器同步解析出的结果要显式
            // Update() 一次才会真正生效、触发 post_load 钩子（同 GameplayAssemblyDeathReloadTests
            // 跨图用例判断记录）。
            fx.Router.Update();

            Assert.Equal(MapB, fx.Router.GetCurrentScene());
            Assert.Equal(MapB, fx.Player.MapId);
            Assert.Equal(new Vec2(30, 40), fx.Player.Position); // 目标地图 spawn_points[0]。

            var playerEntity = fx.World.GetEntity(PlayerId);
            Assert.NotNull(playerEntity);
            Assert.Equal(MapB, playerEntity!.MapId);

            // 旧图占位实体已经随 ClearAll 一并清空——不是旧实现那种"玩家 MapId 变了、但整个世界
            // （含旧图其它实体）原封不动"。
            Assert.Null(fx.World.GetEntity(OldMapResidentId));
        }

        /// <summary>同图内传送（只挪点位、不切地图，<see cref="SameMapMenuId"/> 指向 <see cref="MapA"/>
        /// 自身——两段式 <c>teleport_target_ref</c> 解析为该地图 <c>spawn_points[0]</c>）不应该发起
        /// 任何场景重载——旧图占位实体不受影响，场景路由的"当前场景"也不应该变化，只有玩家自身
        /// 位置改变（同 <see cref="TeleportUnit"/> 判断记录"同图内传送……不应该发起一次整场景
        /// 重载"）。</summary>
        [Fact]
        public void GossipTeleport_SameMap_OnlyMovesPosition_DoesNotTriggerSceneReload()
        {
            var fx = Build();

            fx.Gameplay.Dialog.OpenGossip(PlayerId, NpcId, SameMapMenuId);
            var chosen = fx.Gameplay.Dialog.ChooseOption(PlayerId, 0);
            Assert.True(chosen);

            Assert.Equal(MapA, fx.Router.GetCurrentScene()); // 场景从未切换。
            Assert.Equal(MapA, fx.Player.MapId);
            Assert.Equal(new Vec2(1, 2), fx.Player.Position); // 移到了本图默认出生点，位置确实变化。

            // 没有触发 ClearAll：旧图占位实体全程未受影响。
            Assert.NotNull(fx.World.GetEntity(OldMapResidentId));
        }
    }
}
