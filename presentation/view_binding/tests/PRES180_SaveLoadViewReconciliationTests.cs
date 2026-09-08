using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
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
    /// PRES-180 回归（architecture/落地计划/audit-e070e3f-20260908/presentation/
    /// presentation-findings.md"存档抑制与掉落物 View 候选"）：<c>SaveSystem.Load</c> 把逐段 Load
    /// 包在 <c>IEventBus.SuppressDispatch</c> 作用域内，作用域内经 <c>Enqueue</c>/<c>PublishImmediate</c>
    /// 提交的事件被直接丢弃——若某段 <c>Load</c> 期间往 <c>IWorldSim</c> 加/删实体（如同图读档恢复
    /// 地面掉落物，见 <c>DroppedLootPersistable.Load</c>/<c>LootHost.RestoreDropped</c>），
    /// <c>ViewBinder</c> 原本"只在构造期订阅 entity.created/entity.destroyed"的做法会漏掉这批变化。
    /// 本文件用真实 <see cref="SaveSystem"/>、<see cref="DroppedLootPersistable"/>、
    /// <see cref="LootHost"/>、<see cref="WorldSim"/>、<see cref="ViewBinder"/>（<see cref="IViewFactory"/>
    /// 用记录型 <see cref="FakeViewFactory"/> stub）复现该候选并验证根治（<see cref="ViewBinder"/>
    /// 订阅 <c>save.loaded</c> 后做一次以 <see cref="ISimSnapshot"/> 为准的全量对账，见其类型注释
    /// "PRES-180 根治"）。
    /// </summary>
    public sealed class PRES180_SaveLoadViewReconciliationTests
    {
        private static readonly Id MapA = new Id("map.pres180_a");

        // PRES180-04 专用：跨图用例经真实 ISceneRouter 登记 world.map 表行，该表主键要求 domain
        // 前缀等于表名首段 "world"（见 DataRegistry 主键校验），与 PRES180-01～03 用不到该表校验的
        // MapA 不同名，避免同一个 Id 值在两类用例间产生"是否需要满足 world.map 校验"的混淆。
        private static readonly Id WorldMapA = new Id("world.pres180_cross_map_a");
        private static readonly Id WorldMapB = new Id("world.pres180_cross_map_b");

        // -----------------------------------------------------------------
        // 最小 LootHost 依赖桩（惯例同
        // architecture/落地计划/audit-e070e3f-20260908/presentation/SaveLootViewBindingProbe.cs，
        // 本类型只用到 Drop/RestoreDropped 路径，不涉及条件表达式/单位查询/背包，六个依赖接口
        // 全部给最小空实现）。
        // -----------------------------------------------------------------

        private sealed class NullUnitAccess : IUnitAccess
        {
            public bool Exists(Id id) => false;
            public IReadOnlyList<Id> AllUnits => Array.Empty<Id>();
            public Vec2 GetPosition(Id id) => throw new InvalidOperationException();
            public void SetPosition(Id id, Vec2 value) => throw new InvalidOperationException();
            public Id GetFaction(Id id) => throw new InvalidOperationException();
            public int GetLevel(Id id) => throw new InvalidOperationException();
            public double GetFacing(Id id) => throw new InvalidOperationException();
            public bool IsAlive(Id id) => false;
            public void SetAlive(Id id, bool alive) => throw new InvalidOperationException();
            public Id? GetTemplateId(Id id) => null;
            public IReadOnlyList<Id> GetTags(Id id) => Array.Empty<Id>();
        }

        private sealed class NullInventory : IInventoryHost
        {
            public bool AddItem(Id unitId, Id templateId, int count) => false;
            public bool RemoveItem(Id unitId, Id instanceId, int count) => false;
            public IReadOnlyList<ItemInstance> ListItems(Id unitId) => Array.Empty<ItemInstance>();
            public int CountOf(Id unitId, Id templateId) => 0;
            public ItemInstance? FindInstance(Id unitId, Id instanceId) => null;
        }

        private sealed class NullExprHostFactory : IExprHostFactory
        {
            private sealed class Host : IExprHost
            {
                public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) => default;
            }

            public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new Host();
        }

        /// <summary>读档中把整个世界立即清空的最小合成段——用于 PRES180-03：验证"某段 Load 在
        /// SuppressDispatch 作用域内同步移除实体"这一类问题也由同一份对账逻辑兜底，不需要区分是
        /// 哪个具体持久化段触发的（见 <see cref="ViewBinder"/> 类型注释"PRES-180 根治"最后一句）。
        /// 当前仓库任何一个真实 <c>IPersistable</c> 都不会在 <c>Load</c> 内部直接调用
        /// <c>IWorldSim.ClearAll</c>（该方法目前只被 <c>SceneRouter.FinishLoading</c> 调用，发生在
        /// <c>SaveSystem.Load</c> 返回之后，不在抑制作用域内，见 <see cref="ClearAll"/> 调用点排查），
        /// 本类型是一个防御性场景驱动器，不代表已知可复现的生产缺陷。</summary>
        private sealed class WorldClearingPersistable : IPersistable
        {
            private readonly IWorldSim _world;

            public WorldClearingPersistable(IWorldSim world)
            {
                _world = world;
            }

            public string SectionKey => "test.pres180_world_clearer";

            public JsonValue Save() => JsonNull.Instance;

            public void Load(JsonValue data) => _world.ClearAll();
        }

        // -----------------------------------------------------------------
        // 最小 LootHost + SaveSystem + ViewBinder 夹具（同图场景，PRES180-01～03 共用）。
        // -----------------------------------------------------------------

        private sealed class MinimalFixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public LootHost Loot = null!;
            public SaveSystem SaveSystem = null!;
            public ViewBinder Binder = null!;
            public FakeViewFactory Factory = null!;
        }

        private static MinimalFixture BuildMinimal()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var registry = new DataRegistry(new InMemoryDataSource(), bus, new DataRegistryOptions());
            registry.LoadAll();
            var loot = new LootHost(
                registry, new RngHost(7), bus, world, new NullUnitAccess(), new NullInventory(),
                new NullExprHostFactory(), () => 0, new LootOptions());

            var factory = new FakeViewFactory();
            var displayInfo = new FakeDisplayInfoRegistry();
            var binder = new ViewBinder(bus, factory, new WorldSimSnapshot(world), displayInfo);

            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.pres180_test")), bus);

            return new MinimalFixture
            {
                Bus = bus,
                World = world,
                Loot = loot,
                SaveSystem = saveSystem,
                Binder = binder,
                Factory = factory,
            };
        }

        [Fact]
        public void PRES180_01_SameMapLoad_SuppressedEntityCreated_ReconciledImmediately_WithoutExtraTick()
        {
            var fx = BuildMinimal();
            fx.SaveSystem.RegisterPersistable(new DroppedLootPersistable(fx.Loot));

            var lootId = fx.Loot.Drop(MapA, new Vec2(3, 4), new[] { new ItemStack(new Id("item.pres180_sample"), 1) });
            fx.World.Tick(SimStep.Continuous(0.016));
            Assert.Equal(1, fx.Binder.Count);
            Assert.Single(fx.Factory.Calls);

            var slot = new Id("slot.pres180_same_map");
            var saved = fx.SaveSystem.Save(new SaveRequest(slot, "t1"));
            Assert.True(saved.Success, saved.Message);

            // 同图场景下，逻辑实体在读档发生前已经从 WorldSim/绑定表消失（例如玩家已经拾取过、
            // 又读了一份更早的存档）——这是探针复现的确切前提条件，见 SaveLootViewBindingProbe.cs
            // 判断记录"Simulate an already-cleared same-map world"。
            fx.Loot.ClearDroppedExcept(Array.Empty<Id>());
            fx.World.Tick(SimStep.Continuous(0.016));
            Assert.Equal(0, fx.Binder.Count);

            var loaded = fx.SaveSystem.Load(slot);

            Assert.Equal(LoadStatus.Loaded, loaded.Status);
            Assert.NotNull(fx.World.GetEntity(lootId)); // 逻辑实体已恢复到 WorldSim。
            Assert.True(fx.Loot.TryGetDropped(lootId, out var droppedEntity));
            Assert.Equal(lootId, droppedEntity.EntityId); // LootHost 自身跟踪表同一个 id。

            // 核心断言（根治点）：不需要任何一次额外 world.Tick 补发事件，Load() 返回的这一刻
            // View 就已经补建完毕——SaveLoadedEvent 在 SuppressDispatch 作用域外正常派发，
            // ViewBinder.OnSaveLoaded 同步完成对账。
            Assert.Equal(1, fx.Binder.Count);
            Assert.True(fx.Binder.TryGetView(lootId, out var view));
            Assert.NotNull(view);
            Assert.True(((FakeView)view!).IsAlive);
        }

        [Fact]
        public void PRES180_02_RepeatedSameMapLoad_DoesNotCreateDuplicateView()
        {
            var fx = BuildMinimal();
            fx.SaveSystem.RegisterPersistable(new DroppedLootPersistable(fx.Loot));

            var lootId = fx.Loot.Drop(MapA, new Vec2(1, 1), new[] { new ItemStack(new Id("item.pres180_sample"), 1) });
            fx.World.Tick(SimStep.Continuous(0.016));

            var slot = new Id("slot.pres180_repeat");
            Assert.True(fx.SaveSystem.Save(new SaveRequest(slot, "t1")).Success);

            fx.Loot.ClearDroppedExcept(Array.Empty<Id>());
            fx.World.Tick(SimStep.Continuous(0.016));
            Assert.Equal(0, fx.Binder.Count);

            var firstLoad = fx.SaveSystem.Load(slot);
            Assert.Equal(LoadStatus.Loaded, firstLoad.Status);
            Assert.Equal(1, fx.Binder.Count);
            var createdCallsAfterFirstLoad = fx.Factory.Calls.Count;

            // 第二次读同一个槽位（如玩家在菜单里反复点"读取存档"，或本次读档紧接一次
            // SaveMigratedEvent+SaveLoadedEvent 双发）：实体已经在 WorldSim 里、View 也已绑定，
            // 对账两个方向都应判定"无需变化"，不重复建 View（OnEntityCreated 的
            // _views.ContainsKey 幂等守卫，见类型判断记录）。
            var secondLoad = fx.SaveSystem.Load(slot);
            Assert.Equal(LoadStatus.Loaded, secondLoad.Status);
            Assert.Equal(1, fx.Binder.Count);
            Assert.Equal(createdCallsAfterFirstLoad, fx.Factory.Calls.Count);
            Assert.True(fx.Binder.TryGetView(lootId, out var view));
            Assert.Same(fx.Factory.CreatedByEntityId[lootId], view);
        }

        [Fact]
        public void PRES180_03_EntityDestroyedSuppressedDuringLoad_StaleViewReconciledOnSaveLoaded()
        {
            var fx = BuildMinimal();
            fx.SaveSystem.RegisterPersistable(new WorldClearingPersistable(fx.World));

            var entity = new TestEntity(new Id("unit.pres180_stale"), MapA);
            fx.World.AddEntity(entity);
            fx.World.Tick(SimStep.Continuous(0.016));
            Assert.Equal(1, fx.Binder.Count);
            Assert.True(fx.Binder.TryGetView(entity.EntityId, out var boundView));
            var view = (FakeView)boundView!;

            var slot = new Id("slot.pres180_stale_view");
            Assert.True(fx.SaveSystem.Save(new SaveRequest(slot, "t1")).Success);

            // WorldClearingPersistable.Load 在 SuppressDispatch 作用域内调用 IWorldSim.ClearAll——
            // 立即把实体从 WorldSim 移除，本应伴随的 entity.destroyed 被抑制作用域直接丢弃（不是
            // 延后补发，见 IEventBus.SuppressDispatch 判断记录），且 ClearAll 本身不会在未来任何一次
            // Tick 里补发这批事件（_entities/_pendingDestruction 都已经清空）——若 ViewBinder 不做
            // save.loaded 对账，这个 View 会永久残留。
            var loaded = fx.SaveSystem.Load(slot);

            Assert.Equal(LoadStatus.Loaded, loaded.Status);
            Assert.Null(fx.World.GetEntity(entity.EntityId));
            Assert.Equal(0, fx.Binder.Count); // 根治点：save.loaded 对账立即销毁残留 View。
            Assert.False(fx.Binder.TryGetView(entity.EntityId, out _));
            Assert.True(view.Destroyed);
        }

        // -----------------------------------------------------------------
        // PRES180-04：跨图路径不回归——真实 GameplayAssembly + 真实 SceneRouter，验证本次改动
        // （ViewBinder 订阅 save.loaded）不影响既有"读档目标地图 ≠ 当前地图 → 切场景 → post_load
        // 钩子 → EnterMap → Loot.ReattachToWorld"链路的最终收敛结果。
        // -----------------------------------------------------------------

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.pres180.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.pres180.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.pres180_sample\", \"max_level\": 1, " +
            "\"entries\": [{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"arch.class.pres180_sample\", \"name_key\": \"l10n.arch.class.pres180_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.pres180_sample\"}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static string WorldMapRow(Id mapId, string sceneRef, string navRef) =>
            "{\"id\": \"" + mapId.Value + "\", \"scene_ref\": \"" + sceneRef + "\", \"nav_ref\": \"" + navRef + "\", " +
            "\"spawn_points\": [{\"id\": \"spawn." + mapId.Value + ".default\", \"position\": {\"x\": 0, \"y\": 0}, \"facing\": 0}]}";

        [Fact]
        public void PRES180_04_CrossMapRestore_LootViewReconciliation_DoesNotRegress()
        {
            var playerId = new Id("unit.pres180_cross_map_player");
            var playerFactionId = new Id("fac.pres180_cross_map_player");
            var archetypeId = new Id("arch.class.pres180_sample");
            var slot = new Id("slot.pres180_cross_map");

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
                "[" + WorldMapRow(WorldMapA, "scene.pres180_a", "nav.pres180_a") + "," +
                WorldMapRow(WorldMapB, "scene.pres180_b", "nav.pres180_b") + "]"));

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.pres180_cross_map_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => playerId,
                playerFactionId: playerFactionId);

            var player = new PlayerUnit(playerId, WorldMapA, playerFactionId, archetypeId) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(playerId, archetypeId, raceId: null, level: 1);
            gameplay.RegisterPersistables(saveSystem, player); // 含 DroppedLootPersistable(gameplay.Loot)。

            var factory = new FakeViewFactory();
            var displayInfo = new FakeDisplayInfoRegistry();
            var binder = new ViewBinder(bus, factory, new WorldSimSnapshot(world), displayInfo);

            var loader = new StubResourceLoader();
            loader.Register(new Id("scene.pres180_a"));
            loader.Register(new Id("nav.pres180_a"));
            loader.Register(new Id("scene.pres180_b"));
            loader.Register(new Id("nav.pres180_b"));

            var router = new SceneRouter(registry, loader, gameplay.AppState, world, gameplay.Hooks, bus);
            router.RegisterPostLoadHook(mapId =>
            {
                // 同生产装配（FrameworkResidentHost/GameBootstrap.HandlePostLoad）的既有惯例：
                // ClearAll 之后玩家实体若已缺失则重新 AddEntity，再统一调用 EnterMap（内部已经接了
                // Loot.ReattachToWorld，见外部审核阻塞项 1 收口）；末尾显式 DispatchPending 把
                // EnterMap 内部经 AddEntity 排入队列、尚未派发的 entity.created（含本用例关心的
                // 掉落物重新接回世界）立即送达 ViewBinder，不依赖调用方随后再手工 tick 一次。
                if (world.GetEntity(playerId) == null)
                {
                    world.AddEntity(player);
                }
                gameplay.EnterMap(mapId, playerId);
                bus.DispatchPending();
            });
            gameplay.AttachSceneRouter(router);

            gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.MainMenu);
            router.LoadScene(WorldMapA);
            router.Update();
            Assert.Equal(WorldMapA, router.GetCurrentScene());
            bus.DispatchPending();
            Assert.Equal(1, binder.Count); // 只有玩家。

            // 在 A 图掉落一件战利品并建立 View。
            var lootId = gameplay.Loot.Drop(WorldMapA, new Vec2(2, 2), new[] { new ItemStack(new Id("item.pres180_cross_map_sample"), 1) });
            world.Tick(SimStep.Continuous(0.016));
            Assert.Equal(2, binder.Count); // 玩家 + 掉落物。

            var saved = saveSystem.Save(new SaveRequest(slot, "t1"));
            Assert.True(saved.Success, saved.Message);

            // 切到 B 图：真实 ClearAll，非抑制期间，entity.destroyed 正常派发——两个 View
            // （玩家、掉落物）都应正常销毁；post_load 钩子只把玩家带回来，B 图没有这件掉落物
            // （LootHost.ReattachToWorld 按 mapId 过滤，见其判断记录）。
            player.MapId = WorldMapB;
            router.LoadScene(WorldMapB);
            router.Update();
            Assert.Equal(WorldMapB, router.GetCurrentScene());
            Assert.Equal(1, binder.Count); // 只剩玩家（回到 B 图）。
            Assert.Null(world.GetEntity(lootId));

            // 读一份记录着"A 图 + 这件掉落物"的旧档：目标地图（A）≠ 当前地图（B），
            // GameplayAssembly.RestoreFromSlot 内部会先完成 SaveSystem.Load（其间 ViewBinder 收到
            // save.loaded 做一次对账——此刻 WorldSim 里已经因为 DroppedLootPersistable.Load 同步
            // 恢复了这件掉落物，但玩家仍在 B 图、场景尚未切换，属于预期之内的短暂中间态，不是本用例
            // 断言点），随后发起真正的 LoadScene(A)。
            var loadResult = gameplay.RestoreFromSlot(slot);
            Assert.Equal(LoadStatus.Loaded, loadResult.Status);
            Assert.Equal(WorldMapA, loadResult.CurrentMapId);

            // 场景路由是异步的，驱动一次 Update 让桩资源加载器同步完成的加载真正生效。
            router.Update();

            Assert.Equal(WorldMapA, router.GetCurrentScene());
            var restoredPlayer = world.GetEntity(playerId);
            Assert.NotNull(restoredPlayer);
            Assert.Equal(WorldMapA, restoredPlayer!.MapId);

            var restoredLoot = world.GetEntity(lootId);
            Assert.NotNull(restoredLoot);
            Assert.Equal(WorldMapA, restoredLoot!.MapId);

            // 终态断言（不回归）：玩家 + 掉落物各自恰好一个 View，绑定的是同一个 lootId/playerId，
            // 没有因为本次 save.loaded 对账在 B 图产生任何残留或重复绑定。
            Assert.Equal(2, binder.Count);
            Assert.True(binder.TryGetView(playerId, out var playerView));
            Assert.True(((FakeView)playerView!).IsAlive);
            Assert.True(binder.TryGetView(lootId, out var lootView));
            Assert.True(((FakeView)lootView!).IsAlive);
        }
    }
}
