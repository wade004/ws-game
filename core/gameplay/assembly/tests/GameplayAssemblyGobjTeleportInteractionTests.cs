using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// R04 复现与根治（architecture/落地计划/audit-5e779c6-20260907）：<see
    /// cref="Core.Carriers.Gobj.InteractIntentTickHandler"/>（挂在 <c>TickPhase.TriggerEvaluation</c>，
    /// 由 <c>CarriersAssembly</c> 注册，不在本任务允许改动的目录范围内）只检查
    /// <c>InteractResult.Success</c>，从未读取 <see
    /// cref="Core.Carriers.Gobj.GameObjectHost.Interact"/> 对跨地图 <c>teleporter</c> 返回的
    /// <c>InteractResult.DispatchedRef</c>（见 <c>GameObjectHost.DoTeleport</c> 判断记录"……由调用方
    /// （L4）驱动真正的场景切换"）——直接交互 <c>kind=teleporter</c> 的 gobj（不经 gossip 的 teleport
    /// 动作，那条路径已由 N14/<see cref="GameplayAssemblyTeleportTests"/> 覆盖且正确）时，跨地图目标
    /// 在 <c>DoTeleport</c> 内部被正确解析出 <c>(MapId, Position)</c> 却原样丢在返回值里，没有任何 L4
    /// 代码接手，玩家的地图/位置完全不变，也不会触发 <c>ISceneRouter.LoadScene</c>。
    /// <para>
    /// 本文件复用 <see cref="GameplayAssemblyTeleportTests"/> 同一套双地图 Fixture 手法，额外登记一条
    /// <c>gobj.template</c>（<c>kind=teleporter</c>）并在 A 图生成一个实例，直接调用
    /// <c>Carriers.GameObjectInteractions.Interact</c>（与 <c>InteractIntentTickHandler</c> 内部调用
    /// 完全相同的入口，等价于玩家提交一次 <c>"interact"</c> 意图后该 tick 处理器消费的效果）触发交互，
    /// 验收跨地图传送与 gossip teleport 路径一样完整生效。
    /// </para>
    /// </summary>
    public sealed class GameplayAssemblyGobjTeleportInteractionTests
    {
        private static readonly Id PlayerId = new Id("unit.gobj_teleport_test_player");
        private static readonly Id PlayerFactionId = new Id("fac.gobj_teleport_test_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.gobj_teleport_test_sample");
        private static readonly Id MapA = new Id("world.gobj_teleport_test_map_a");
        private static readonly Id MapB = new Id("world.gobj_teleport_test_map_b");
        private static readonly Id OldMapResidentId = new Id("unit.gobj_teleport_test_old_resident");
        private static readonly Id TeleporterTemplateId = new Id("gobj.gobj_teleport_test_teleporter");
        private static readonly Id SameMapTeleporterTemplateId = new Id("gobj.gobj_teleport_test_same_map_teleporter");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.gobj_teleport_test_sample\", \"max_level\": 1, " +
            "\"entries\": [{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.gobj_teleport_test_sample" + "\", \"name_key\": \"l10n.arch.class.gobj_teleport_test_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.gobj_teleport_test_sample\"}]";

        private static string WorldMapRow(Id mapId, string sceneRef, string navRef, double spawnX, double spawnY) =>
            "{\"id\": \"" + mapId.Value + "\", \"scene_ref\": \"" + sceneRef + "\", \"nav_ref\": \"" + navRef + "\", " +
            "\"spawn_points\": [{\"id\": \"spawn." + mapId.Value + ".default\", \"position\": {\"x\": " + spawnX +
            ", \"y\": " + spawnY + "}, \"facing\": 0}]}";

        private static string TeleporterTemplateRow(Id templateId, Id teleportTargetRef) =>
            "{\"id\": \"" + templateId.Value + "\", \"name_key\": \"l10n." + templateId.Value.Replace('.', '_') + ".name\", " +
            "\"kind\": \"teleporter\", \"type_data\": {\"teleport_target_ref\": \"" + teleportTargetRef.Value + "\"}, " +
            "\"display_ref\": \"display.gobj_teleport_test\"}";

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

        private static Fixture Build(Core.Carriers.Gobj.GobjOptions? gobjOptions = null)
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

            var mapRows = "[" + WorldMapRow(MapA, "scene.gobj_teleport_test_a", "nav.gobj_teleport_test_a", 1, 2) + "," +
                          WorldMapRow(MapB, "scene.gobj_teleport_test_b", "nav.gobj_teleport_test_b", 30, 40) + "]";
            source.Add("world.map", Envelope("world.map", mapRows));

            var gobjTemplateRows = "[" + TeleporterTemplateRow(TeleporterTemplateId, MapB) + "," +
                                    TeleporterTemplateRow(SameMapTeleporterTemplateId, MapA) + "]";
            source.Add("gobj.template", Envelope("gobj.template", gobjTemplateRows));
            source.Add("gobj.lock", Envelope("gobj.lock", "[]"));
            source.Add("dialog.gossip_menu", Envelope("dialog.gossip_menu", "[]"));
            source.Add("dialog.story_tree", Envelope("dialog.story_tree", "[]"));

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.gobj_teleport_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId,
                gobjOptions: gobjOptions);

            var player = new PlayerUnit(PlayerId, MapA, PlayerFactionId, ArchetypeSample) { Position = new Vec2(5, 5) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            // A 图的"旧图实体"占位（同 GameplayAssemblyTeleportTests 惯例，验收 ClearAll 真的被触发）。
            var oldMapResident = new PlayerUnit(OldMapResidentId, MapA, PlayerFactionId, ArchetypeSample) { Position = new Vec2(9, 9) };
            world.AddEntity(oldMapResident);

            var loader = new StubResourceLoader();
            loader.Register(new Id("scene.gobj_teleport_test_a"));
            loader.Register(new Id("nav.gobj_teleport_test_a"));
            loader.Register(new Id("scene.gobj_teleport_test_b"));
            loader.Register(new Id("nav.gobj_teleport_test_b"));

            var appState = gameplay.AppState;
            var hooks = gameplay.Hooks;
            var router = new Core.Foundation.SceneRouter.SceneRouter(registry, loader, appState, world, hooks, bus);

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

        /// <summary>R04 核心验收：直接交互 A 图一个 <c>kind=teleporter</c>、目标为 B 图的 gobj（不经
        /// gossip）——(1) 旧图实体不可见（<see cref="IWorldSim.ClearAll"/> 真的被触发）；(2) 玩家实体
        /// 确实"进入"了新图；(3) 位置正确（落在 B 图 <c>spawn_points[0]</c>）。修复前：三项全部落空，
        /// 玩家原地不动、<see cref="Core.Foundation.SceneRouter.SceneRouter.GetCurrentScene"/> 仍是
        /// A 图。</summary>
        [Fact]
        public void InteractWithTeleporterGobj_CrossMap_ClearsOldMapEntities_EntersNewMap_PositionsAtTargetSpawnPoint()
        {
            var fx = Build();
            var gobjInstanceId = fx.Gameplay.Carriers.GameObjects.Spawn(TeleporterTemplateId, MapA, new Vec2(5, 4), 0);
            Assert.NotNull(fx.World.GetEntity(OldMapResidentId));

            var result = fx.Gameplay.Carriers.GameObjectInteractions.Interact(PlayerId, gobjInstanceId);
            Assert.True(result.Success);

            // GobjInteractedEvent 走 Enqueue，需要显式 DispatchPending 才会送达本次修复新增的订阅者。
            fx.Bus.DispatchPending();

            // ISceneRouter.LoadScene 只是发起一次异步加载，桩资源加载器同步解析出的结果要显式
            // Update() 一次才会真正生效、触发 post_load 钩子（同 GameplayAssemblyTeleportTests 判断记录）。
            fx.Router.Update();

            Assert.Equal(MapB, fx.Router.GetCurrentScene());
            Assert.Equal(MapB, fx.Player.MapId);
            Assert.Equal(new Vec2(30, 40), fx.Player.Position);

            var playerEntity = fx.World.GetEntity(PlayerId);
            Assert.NotNull(playerEntity);
            Assert.Equal(MapB, playerEntity!.MapId);

            Assert.Null(fx.World.GetEntity(OldMapResidentId));
        }

        /// <summary>同图内直接交互传送 gobj（只挪点位、不切地图）不应该发起任何场景重载——旧图占位
        /// 实体不受影响，场景路由的"当前场景"也不应该变化，只有玩家自身位置改变（同 <see
        /// cref="GameplayAssemblyTeleportTests.GossipTeleport_SameMap_OnlyMovesPosition_DoesNotTriggerSceneReload"/>
        /// 判断记录，验证本次修复的补发调用在同图场景下是幂等的、不产生副作用）。</summary>
        [Fact]
        public void InteractWithTeleporterGobj_SameMap_OnlyMovesPosition_DoesNotTriggerSceneReload()
        {
            var fx = Build();
            var gobjInstanceId = fx.Gameplay.Carriers.GameObjects.Spawn(SameMapTeleporterTemplateId, MapA, new Vec2(5, 4), 0);

            var result = fx.Gameplay.Carriers.GameObjectInteractions.Interact(PlayerId, gobjInstanceId);
            Assert.True(result.Success);
            fx.Bus.DispatchPending();
            fx.Router.Update();

            Assert.Equal(MapA, fx.Router.GetCurrentScene());
            Assert.Equal(MapA, fx.Player.MapId);
            Assert.Equal(new Vec2(1, 2), fx.Player.Position); // 移到了本图默认出生点。

            Assert.NotNull(fx.World.GetEntity(OldMapResidentId));
        }

        // ==== CR130-05（外部审计 audit-5c444f1-20260908，P2）：customTeleportResolver 与本文件 R04
        // 新增的 gobj.interacted 监听不应双重消费同一次交互 ====

        /// <summary>核心复现：调用方注入自定义 <c>GobjOptions.TeleportResolver</c>，同图判定把玩家挪到
        /// <c>(99,88)</c>——<c>GameObjectHost.DoTeleport</c> 已经原地 <c>SetPosition</c> 完毕。修复前
        /// 本文件的 <c>gobj.interacted</c> 监听不管三七二十一，只要 kind 是 teleporter 就无条件再用
        /// 本装配根自己的默认 <c>_teleportTargetResolver</c> 重新解析一遍同一个 <c>teleport_target_ref</c>
        /// ——自定义结果 <c>(99,88)</c> 会被内置默认出生点 <c>(1,2)</c> 覆盖。修复后监听只在
        /// <c>GobjInteractedEvent.TeleportTargetRef</c> 非空（即 <c>DoTeleport</c> 判定"这是一次真正
        /// 跨地图、需要 L4 补完场景切换"）时才接手；<c>DoTeleport</c> 已经同图落地的情形，事件里这个
        /// 字段固定为 <c>null</c>，监听不做任何事，自定义结果原样保留。</summary>
        [Fact]
        public void InteractWithTeleporterGobj_SameMap_CustomResolverResult_IsNotOverwrittenByBuiltinListener()
        {
            var customTarget = new Vec2(99, 88);
            var gobjOptions = new Core.Carriers.Gobj.GobjOptions
            {
                TeleportResolver = _ => (MapA, customTarget),
            };
            var fx = Build(gobjOptions);
            var gobjInstanceId = fx.Gameplay.Carriers.GameObjects.Spawn(SameMapTeleporterTemplateId, MapA, new Vec2(5, 4), 0);

            var result = fx.Gameplay.Carriers.GameObjectInteractions.Interact(PlayerId, gobjInstanceId);
            Assert.True(result.Success);
            fx.Bus.DispatchPending();
            fx.Router.Update();

            Assert.Equal(customTarget, fx.Player.Position);
            Assert.Equal(MapA, fx.Router.GetCurrentScene());
            Assert.NotNull(fx.World.GetEntity(OldMapResidentId)); // 同图不应触发任何场景重载。
        }

        /// <summary>自定义 resolver 显式返回 <c>null</c>（判定"这次不该传送"）：<c>DoTeleport</c> 不
        /// 产生任何位移，事件的 <c>TeleportTargetRef</c> 也应为 <c>null</c>——修复前监听会退化成走
        /// 本装配根自己的默认 resolver，把玩家传送去默认出生点，等于无视了调用方显式的拒绝判定。</summary>
        [Fact]
        public void InteractWithTeleporterGobj_CustomResolverReturnsNull_DoesNotFallBackToBuiltinTeleport()
        {
            var gobjOptions = new Core.Carriers.Gobj.GobjOptions
            {
                TeleportResolver = _ => null,
            };
            var fx = Build(gobjOptions);
            var gobjInstanceId = fx.Gameplay.Carriers.GameObjects.Spawn(SameMapTeleporterTemplateId, MapA, new Vec2(5, 4), 0);
            var originalPosition = fx.Player.Position;

            var result = fx.Gameplay.Carriers.GameObjectInteractions.Interact(PlayerId, gobjInstanceId);
            Assert.True(result.Success); // Interact 本身仍成功（NoAction），只是没有产生传送副作用。
            fx.Bus.DispatchPending();
            fx.Router.Update();

            Assert.Equal(originalPosition, fx.Player.Position);
            Assert.Equal(MapA, fx.Player.MapId);
            Assert.Equal(MapA, fx.Router.GetCurrentScene());
        }

        /// <summary>跨地图仍然是唯一权威路径：不注入自定义 resolver（走本装配根默认的
        /// <c>_teleportTargetResolver</c>）时，跨地图传送应继续和修复前一样正确生效——本条只是确认
        /// CR130-05 的收紧没有连带破坏 R04 本来要修的跨地图直接交互路径。</summary>
        [Fact]
        public void InteractWithTeleporterGobj_CrossMap_NoCustomResolver_StillTeleportsViaListener()
        {
            var fx = Build();
            var gobjInstanceId = fx.Gameplay.Carriers.GameObjects.Spawn(TeleporterTemplateId, MapA, new Vec2(5, 4), 0);

            var result = fx.Gameplay.Carriers.GameObjectInteractions.Interact(PlayerId, gobjInstanceId);
            Assert.True(result.Success);
            fx.Bus.DispatchPending();
            fx.Router.Update();

            Assert.Equal(MapB, fx.Router.GetCurrentScene());
            Assert.Equal(MapB, fx.Player.MapId);
            Assert.Equal(new Vec2(30, 40), fx.Player.Position);
        }

        // ==== CR140-03（外部审计 audit-c86bfa9-20260908，P2）：跨图自定义 resolver 的结果不应被
        // gobj.interacted 监听重新解析覆盖 ====

        /// <summary>核心复现（同审计探针 crossmap_custom_resolver.log：expected=(99,88)
        /// actual=(30,40)）：调用方注入自定义 <c>GobjOptions.TeleportResolver</c>，跨图判定把玩家送到
        /// B 图的 <c>(99,88)</c>——<c>GameObjectHost.DoTeleport</c> 已经用这个自定义结果判定"需要跨
        /// 图"，把解析出的 <c>(MapId, Position)</c> 经 <c>GobjInteractedEvent.ResolvedTeleportTarget</c>
        /// 带出。修复前本文件的 <c>gobj.interacted</c> 监听只拿到原始 <c>teleport_target_ref</c>，转手
        /// 交给 <c>TeleportUnit</c> 用本装配根自己的默认 <c>_teleportTargetResolver</c> 重新解析一遍——
        /// 默认结果（B 图 <c>spawn_points[0]</c>，即 <c>(30,40)</c>）覆盖掉自定义结果。修复后监听直接
        /// 落地事件携带的 <c>ResolvedTeleportTarget</c>，不再重新解析，自定义结果原样保留。</summary>
        [Fact]
        public void InteractWithTeleporterGobj_CrossMap_CustomResolverPosition_IsPreserved()
        {
            var customTarget = new Vec2(99, 88);
            var gobjOptions = new Core.Carriers.Gobj.GobjOptions
            {
                TeleportResolver = _ => (MapB, customTarget),
            };
            var fx = Build(gobjOptions);
            var gobjInstanceId = fx.Gameplay.Carriers.GameObjects.Spawn(TeleporterTemplateId, MapA, new Vec2(5, 4), 0);

            var result = fx.Gameplay.Carriers.GameObjectInteractions.Interact(PlayerId, gobjInstanceId);
            Assert.True(result.Success);
            fx.Bus.DispatchPending();
            fx.Router.Update();

            Assert.Equal(MapB, fx.Router.GetCurrentScene());
            Assert.Equal(MapB, fx.Player.MapId);
            Assert.Equal(customTarget, fx.Player.Position);
            Assert.Null(fx.World.GetEntity(OldMapResidentId)); // 真正切了场景，不是同图分支的幂等 no-op。
        }
    }
}
