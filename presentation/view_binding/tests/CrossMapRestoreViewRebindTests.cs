using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Gameplay.Loot;
using Core.Rules.Common;
using Presentation.Common;
using Presentation.ViewBinding;
using Xunit;

namespace Tests.PresentationViewBinding
{
    /// <summary>
    /// ADR-0085 时序护栏：宿主 post_load 钩子<b>不</b>冲刷事件队列、<see cref="SceneRouter.LoadScene"/>
    /// 一返回就断言（不推进任何 tick）时，"存活实体 ⟺ 已建 View"仍须成立——覆盖跨图读档（地图 A
    /// 存档 → 传送 B → 从 B 读档切回 A）、同图读档、普通进图三条路径。完整宿主帧循环下的端到端
    /// 用例见 <c>CrossMapRestoreProductionAssemblyTests</c>。本文件用真实
    /// <see cref="GameplayAssembly"/>/<see cref="SceneRouter"/>/<see cref="ViewBinder"/>/真实
    /// <see cref="SaveSystem"/>，夹具结构复用
    /// <c>PRES180_SaveLoadViewReconciliationTests.PRES180_04</c> 同一套 world.map 登记方式。
    /// </summary>
    public sealed class CrossMapRestoreViewRebindTests
    {
        private static readonly Id WorldMapA = new Id("world.rebind_cross_map_a");
        private static readonly Id WorldMapB = new Id("world.rebind_cross_map_b");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.rebind.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.rebind.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.rebind_sample\", \"max_level\": 1, " +
            "\"entries\": [{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"arch.class.rebind_sample\", \"name_key\": \"l10n.arch.class.rebind_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.rebind_sample\"}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static string WorldMapRow(Id mapId, string sceneRef, string navRef) =>
            "{\"id\": \"" + mapId.Value + "\", \"scene_ref\": \"" + sceneRef + "\", \"nav_ref\": \"" + navRef + "\", " +
            "\"spawn_points\": [{\"id\": \"spawn." + mapId.Value + ".default\", \"position\": {\"x\": 0, \"y\": 0}, \"facing\": 0}]}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public SceneRouter Router = null!;
            public ViewBinder Binder = null!;
            public FakeViewFactory Factory = null!;
            public PlayerUnit Player = null!;
            public Id PlayerId;
            public SaveSystem SaveSystem = null!;

            /// <summary>生产惯例（FrameworkResidentHost.HandlePostLoad 同款判断记录）：ClearAll 之后
            /// 玩家实体若已缺失则重新 AddEntity 并立即 DispatchPending 一次；随后调用 EnterMap——
            /// 与生产代码一致，<b>不</b>在 EnterMap 之后额外手工 DispatchPending（生产代码那一步只在
            /// "首次进图"时才做一次，见 FrameworkResidentHost.HandlePostLoad 的 `_worldEverEntered`
            /// 门槛），本夹具故意不复制那个一次性门槛之外的手工 flush，模拟"重复跨图/读档"这一更
            /// 常见的中后期场景。</summary>
            public void PostLoad(Id mapId)
            {
                if (World.GetEntity(PlayerId) == null)
                {
                    World.AddEntity(Player);
                    Bus.DispatchPending();
                }

                Gameplay.EnterMap(mapId, PlayerId);
            }
        }

        private static Fixture Build()
        {
            var playerId = new Id("unit.rebind_cross_map_player");
            var playerFactionId = new Id("fac.rebind_cross_map_player");
            var archetypeId = new Id("arch.class.rebind_sample");

            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

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
            registry.RegisterSchema(WorldMapSchema.Table);
            source.Add("world.map", Envelope("world.map",
                "[" + WorldMapRow(WorldMapA, "scene.rebind_a", "nav.rebind_a") + "," +
                WorldMapRow(WorldMapB, "scene.rebind_b", "nav.rebind_b") + "]"));

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.rebind_cross_map_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => playerId,
                playerFactionId: playerFactionId);

            var player = new PlayerUnit(playerId, WorldMapA, playerFactionId, archetypeId) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(playerId, archetypeId, raceId: null, level: 1);
            gameplay.RegisterPersistables(saveSystem, player);

            var factory = new FakeViewFactory();
            var displayInfo = new FakeDisplayInfoRegistry();
            var binder = new ViewBinder(bus, factory, new WorldSimSnapshot(world), displayInfo);

            var loader = new StubResourceLoader();
            loader.Register(new Id("scene.rebind_a"));
            loader.Register(new Id("nav.rebind_a"));
            loader.Register(new Id("scene.rebind_b"));
            loader.Register(new Id("nav.rebind_b"));

            var router = new SceneRouter(registry, loader, gameplay.AppState, world, gameplay.Hooks, bus);
            var fx = new Fixture
            {
                Bus = bus,
                World = world,
                Gameplay = gameplay,
                Router = router,
                Binder = binder,
                Factory = factory,
                Player = player,
                PlayerId = playerId,
                SaveSystem = saveSystem,
            };
            router.RegisterPostLoadHook(fx.PostLoad);
            gameplay.AttachSceneRouter(router);

            gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.MainMenu);
            return fx;
        }

        [Fact]
        public void CrossMapRestore_SnapshotEntityAndFreshlySpawnedEntity_BothRebindViews()
        {
            var fx = Build();
            var slot = new Id("slot.rebind_cross_map");

            // ---- 地图 A：玩家 + 一件掉落物（真实经 IPersistable 存档的实体，代表"读档快照里原本
            // 就有的实体"），确认都建好了 View。 ----
            fx.Router.LoadScene(WorldMapA);
            fx.Router.Update();
            Assert.Equal(WorldMapA, fx.Router.GetCurrentScene());
            fx.Bus.DispatchPending();
            Assert.Equal(1, fx.Binder.Count); // 只有玩家。

            var lootId = fx.Gameplay.Loot.Drop(WorldMapA, new Vec2(2, 2), new[] { new ItemStack(new Id("item.rebind_cross_map_sample"), 1) });
            fx.World.Tick(SimStep.Continuous(0.016));
            Assert.Equal(2, fx.Binder.Count); // 玩家 + 掉落物。
            Assert.True(fx.Binder.TryGetView(lootId, out _));
            Assert.True(fx.Binder.TryGetView(fx.PlayerId, out _));

            var saved = fx.SaveSystem.Save(new SaveRequest(slot, "t1"));
            Assert.True(saved.Success, saved.Message);

            // ---- 传送到地图 B：真实 ClearAll，两个 View 都应正常销毁；B 图没有这件掉落物。 ----
            fx.Player.MapId = WorldMapB;
            fx.Router.LoadScene(WorldMapB);
            fx.Router.Update();
            Assert.Equal(WorldMapB, fx.Router.GetCurrentScene());
            Assert.Equal(1, fx.Binder.Count); // 只剩玩家（回到 B 图）。
            Assert.Null(fx.World.GetEntity(lootId));

            // ---- 从 B 读一份"A 图 + 掉落物"的旧档：目标地图（A）≠ 当前地图（B），触发
            // GameplayAssembly.RestoreFromSlot 的跨图分支。 ----
            var loadResult = fx.Gameplay.RestoreFromSlot(slot);
            Assert.Equal(LoadStatus.Loaded, loadResult.Status);
            Assert.Equal(WorldMapA, loadResult.CurrentMapId);

            fx.Router.Update(); // 场景路由异步加载，桩资源加载器同步完成，这里让它真正生效。
            Assert.Equal(WorldMapA, fx.Router.GetCurrentScene());

            var restoredPlayer = fx.World.GetEntity(fx.PlayerId);
            Assert.NotNull(restoredPlayer);
            Assert.Equal(WorldMapA, restoredPlayer!.MapId);
            var restoredLoot = fx.World.GetEntity(lootId);
            Assert.NotNull(restoredLoot);
            Assert.Equal(WorldMapA, restoredLoot!.MapId);

            // 断言 1（读档快照里原本就有的实体）：router.Update() 一返回就立刻断言，不额外手工
            // 推进任何 tick——这正是"读档完成"那一刻真正对外可观察的状态。这里故意不在断言前插
            // fx.World.Tick：那一次 tick 自带的 DispatchPending 会冲掉 post_load 钩子新增实体的
            // 创建事件，本用例就不再约束"LoadScene 完成时即已建 View"这一时序。玩家与掉落物都应
            // 重新拥有 View。
            Assert.True(fx.Binder.TryGetView(fx.PlayerId, out var playerView));
            Assert.True(((FakeView)playerView!).IsAlive);
            Assert.True(fx.Binder.TryGetView(lootId, out var lootView));
            Assert.True(((FakeView)lootView!).IsAlive);

            // ---- 断言 2（关键）：读档完成后，剧情继续推进时才全新生成的实体，同样应该建好 View。
            // 用 world.AddEntity 模拟生产里"生成一个新生物"的正常路径（Spawn.ApplyForMap/召唤/
            // 生成脚本最终都归结为这一次调用）——不是读档快照的一部分，纯粹是读档完成之后的新实体。
            // 这里的 world.Tick 不是掩盖性的额外冲刷：它模拟的是"读档完成之后、剧情正常继续推进的
            // 下一个游戏帧"（OnFixedStep → Gameplay.Advance → 连续模式下最终驱动 world.Tick），
            // 与断言 1 刻意不加 tick 的场景（"读档这一刻立即断言"）是两个不同的观察时间点，互不冲突。
            var freshCreatureId = new Id("unit.rebind_cross_map_fresh_creature");
            var freshCreature = new TestEntity(freshCreatureId, WorldMapA, EntityKinds.Creature);
            fx.World.AddEntity(freshCreature);
            fx.World.Tick(SimStep.Continuous(0.016));

            Assert.True(fx.World.GetEntity(freshCreatureId) != null); // 逻辑实体确实存活。
            Assert.True(
                fx.Binder.TryGetView(freshCreatureId, out var freshView),
                "跨图读档之后全新生成的实体应当照常建立 View（TryGetView 不应返回 false）");
            Assert.True(((FakeView)freshView!).IsAlive);
        }

        /// <summary>
        /// ADR-0085 回归护栏之一："存活实体 ⟺ 已建 View" 这条不变量不能只在跨图读档分支成立。
        /// 同图读档（<see cref="GameplayAssembly.RestoreFromSlot"/> 判断目标地图等于当前地图，不
        /// 触发 <see cref="SceneRouter.LoadScene"/>、不经过本次新增的 post_load 钩子）走的是
        /// <c>SaveSystem.Load(slotId, deferLoadedNotification: true)</c> 之后立即在
        /// <see cref="GameplayAssembly.RestoreFromSlot"/> 内联调用 <c>NotifyLoaded</c> 的分支——
        /// 与跨图分支是两条独立代码路径，必须单独一例验证没有被误一并推迟。
        /// </summary>
        [Fact]
        public void SameMapReload_SnapshotEntityAndFreshlySpawnedEntity_BothRebindViews()
        {
            var fx = Build();
            var slot = new Id("slot.rebind_same_map");

            fx.Router.LoadScene(WorldMapA);
            fx.Router.Update();
            Assert.Equal(WorldMapA, fx.Router.GetCurrentScene());
            fx.Bus.DispatchPending();

            var lootId = fx.Gameplay.Loot.Drop(WorldMapA, new Vec2(3, 3), new[] { new ItemStack(new Id("item.rebind_same_map_sample"), 1) });
            fx.World.Tick(SimStep.Continuous(0.016));
            Assert.True(fx.Binder.TryGetView(lootId, out _));
            Assert.True(fx.Binder.TryGetView(fx.PlayerId, out _));

            var saved = fx.SaveSystem.Save(new SaveRequest(slot, "t1"));
            Assert.True(saved.Success, saved.Message);

            // ---- 同图读档：目标地图（A）＝当前地图（A），RestoreFromSlot 不发起 LoadScene，
            // 不经过场景路由，也就不经过本次新增的 post_load 钩子；save.loaded 走的是
            // RestoreFromSlot 内联的立即补发分支。读档一返回就立刻断言，不额外手工推进 tick。 ----
            var loadResult = fx.Gameplay.RestoreFromSlot(slot);
            Assert.Equal(LoadStatus.Loaded, loadResult.Status);
            Assert.Equal(WorldMapA, loadResult.CurrentMapId);

            Assert.True(fx.Binder.TryGetView(fx.PlayerId, out var playerView));
            Assert.True(((FakeView)playerView!).IsAlive);
            Assert.True(fx.Binder.TryGetView(lootId, out var lootView));
            Assert.True(((FakeView)lootView!).IsAlive);

            // ---- 同图读档完成之后，剧情继续推进时才全新生成的实体，同样应该建好 View。 ----
            var freshCreatureId = new Id("unit.rebind_same_map_fresh_creature");
            var freshCreature = new TestEntity(freshCreatureId, WorldMapA, EntityKinds.Creature);
            fx.World.AddEntity(freshCreature);
            fx.World.Tick(SimStep.Continuous(0.016));

            Assert.True(
                fx.Binder.TryGetView(freshCreatureId, out var freshView),
                "同图读档之后全新生成的实体应当照常建立 View（TryGetView 不应返回 false）");
            Assert.True(((FakeView)freshView!).IsAlive);
        }

        /// <summary>
        /// ADR-0085 回归护栏之二："存活实体 ⟺ 已建 View" 这条不变量在普通进图（不涉及任何存档/
        /// 读档）路径下也要成立——本用例专门验证 <see cref="GameplayAssembly.AttachSceneRouter"/>
        /// 新注册的 post_load 钩子（<c>OnScenePostLoadFlushAndNotify</c>）在 post_load 钩子之后补的
        /// 那一次 DispatchPending（<c>SceneRouter.FinishLoading</c> 自身在 post_load 之后不冲刷），
        /// 不依赖任何与存档相关的前提（<see cref="_pendingRestoreNotification"/> 为空时这个钩子
        /// 仍然要执行 DispatchPending 这一半）。
        /// </summary>
        [Fact]
        public void NormalMapEntry_FreshlySpawnedEntity_RebindsView()
        {
            var fx = Build();

            // ---- 普通进图：第一次 LoadScene，不涉及任何存档/读档。EnterMap 在 post_load 钩子里
            // 新增的玩家实体应当立即（LoadScene 完成时）就有 View，不额外手工推进 tick。 ----
            fx.Router.LoadScene(WorldMapA);
            fx.Router.Update();
            Assert.Equal(WorldMapA, fx.Router.GetCurrentScene());

            Assert.True(
                fx.Binder.TryGetView(fx.PlayerId, out var playerView),
                "普通进图完成后玩家应当立即建好 View（TryGetView 不应返回 false）");
            Assert.True(((FakeView)playerView!).IsAlive);

            // ---- 进图完成之后，剧情继续推进时才全新生成的实体，同样应该建好 View。 ----
            var freshCreatureId = new Id("unit.rebind_normal_entry_fresh_creature");
            var freshCreature = new TestEntity(freshCreatureId, WorldMapA, EntityKinds.Creature);
            fx.World.AddEntity(freshCreature);
            fx.World.Tick(SimStep.Continuous(0.016));

            Assert.True(
                fx.Binder.TryGetView(freshCreatureId, out var freshView),
                "普通进图之后全新生成的实体应当照常建立 View（TryGetView 不应返回 false）");
            Assert.True(((FakeView)freshView!).IsAlive);
        }
    }
}
