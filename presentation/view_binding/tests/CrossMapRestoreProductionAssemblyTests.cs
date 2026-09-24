using System;
using System.Collections.Generic;
using System.Text;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Presentation.Assembly;
using Xunit;

namespace Tests.PresentationViewBinding
{
    /// <summary>
    /// 跨图读档之后"存活实体 ⟺ 已建 View"不变量的生产装配端到端护栏（ADR-0085）：真实
    /// <see cref="GameplayAssembly"/>、真实 <see cref="SceneRouter"/>（与
    /// <see cref="GameplayAssembly.Hooks"/> 共用同一个钩子注册表）、真实
    /// <see cref="PresentationAssembly"/>（其内部构造的 <c>ViewBinder</c>）、真实
    /// <see cref="GameplayAssembly.RestoreFromSlot"/>。宿主侧 pre_unload/post_load 钩子逐行照抄
    /// <c>games/_template/Runtime/GameBootstrap</c> 的 <c>HandlePreUnload</c>/<c>HandlePostLoad</c>
    /// （引擎侧 <c>DestroyAllCreatedViews</c> 一行除外——那是 Unity 视图工厂的协作方法，桩工厂没有）。
    /// 生物一律经生产路径生成：进图经 <see cref="GameplayAssembly.EnterMap"/> →
    /// <c>Spawn.ApplyForMap</c>，读档之后的剧情生成经 <c>Spawn.SpawnNow</c> 与
    /// <c>Carriers.Creatures.Spawn</c>。每一"帧"按宿主惯例 <c>SceneRouter.Update</c> +
    /// <c>world.Tick</c> + <c>DispatchPending</c>，测试本身不额外手工冲刷事件队列。
    /// </summary>
    public sealed class CrossMapRestoreProductionAssemblyTests
    {
        private static readonly Id MapA = new Id("world.xmap_prod_a");
        private static readonly Id MapB = new Id("world.xmap_prod_b");
        private static readonly Id PlayerId = new Id("unit.xmap_prod_player");
        private static readonly Id PlayerFactionId = new Id("fac.xmap_prod_player");
        private static readonly Id ArchetypeId = new Id("arch.class.xmap_prod");
        private static readonly Id GuardTemplateId = new Id("creature.xmap_prod_guard");
        private static readonly Id StorySpawnId = new Id("spawn.xmap_prod.story_guard");

        private const double FixedStep = 1.0 / 60.0;

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static string WorldMapRow(Id mapId) =>
            "{\"id\": \"" + mapId.Value + "\", \"scene_ref\": \"scene." + mapId.Value + "\", \"nav_ref\": \"nav." + mapId.Value + "\", " +
            "\"spawn_points\": [{\"id\": \"spawn." + mapId.Value + ".default\", \"position\": {\"x\": 0, \"y\": 0}, \"facing\": 0}]}";

        private static string SpawnRow(string id, Id mapId, double x, string policy) =>
            "{\"id\": \"" + id + "\", \"map_id\": \"" + mapId.Value + "\", \"content_ref\": \"" + GuardTemplateId.Value + "\", " +
            "\"position\": {\"x\": " + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", \"y\": 3}, " +
            "\"respawn_policy\": \"" + policy + "\"}";

        private sealed class Host
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public SceneRouter Router = null!;
            public PresentationAssembly Presentation = null!;
            public FakeViewFactory Factory = null!;
            public PlayerUnit Player = null!;
            public bool TickWhileNotInWorld;
            public bool FlushAfterEnterMap = true;
            private bool _worldEverEntered;

            // games/_template GameBootstrap.HandlePreUnload（去掉引擎侧 DestroyAllCreatedViews）。
            public void HandlePreUnload(Id mapId)
            {
                Gameplay.LeaveMap(mapId);
                _worldEverEntered = false;
            }

            // games/_template GameBootstrap.HandlePostLoad 逐行照抄。
            public void HandlePostLoad(Id mapId)
            {
                if (World.GetEntity(PlayerId) == null)
                {
                    World.AddEntity(Player);
                    Bus.DispatchPending();
                }

                Gameplay.EnterMap(mapId, PlayerId);

                if (!_worldEverEntered)
                {
                    if (FlushAfterEnterMap)
                    {
                        Bus.DispatchPending();
                    }

                    _worldEverEntered = true;
                }
            }

            /// <summary>宿主一帧：场景路由推进 + 一个固定步（InWorld 才推进，除非
            /// <see cref="TickWhileNotInWorld"/>——部分消费方的固定步驱动不看应用状态）。</summary>
            public void Frame()
            {
                Router.Update();
                if (TickWhileNotInWorld || Gameplay.AppState.GetState() == AppState.InWorld)
                {
                    World.Tick(SimStep.Continuous(FixedStep));
                    Bus.DispatchPending();
                }
            }

            public void Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    Frame();
                }
            }

            /// <summary>全部存活实体（区域触发体除外，设计上无外观）都应当有一个未销毁的 View，
            /// 且 View 数量与之相等（不残留指向已不存在实体的 View）。</summary>
            public void AssertLiveEntitiesMatchViews(string stage)
            {
                var live = World.QueryEntities(new EntityFilter());
                var missing = new StringBuilder();
                var expected = 0;
                foreach (var entity in live)
                {
                    if (string.Equals(entity.Kind, EntityKinds.AreaTrigger, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    expected++;
                    if (!Presentation.ViewBinder.TryGetView(entity.EntityId, out var view) || !((FakeView)view!).IsAlive)
                    {
                        missing.Append(entity.EntityId).Append(' ');
                    }
                }

                Assert.True(missing.Length == 0, $"[{stage}] 存活但没有 View 的实体：{missing}");
                Assert.Equal(expected, Presentation.ViewBinder.Count);
            }
        }

        private static Host Build(bool tickWhileNotInWorld, bool flushAfterEnterMap)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition",
                    "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.xmap_prod.name\", \"group\": \"primary\", \"default_base\": 100}]"))
                .Add("arch.power_type", Envelope("arch.power_type",
                    "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.xmap_prod.name\", " +
                    "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]"))
                .Add("prog.level_curve", Envelope("prog.level_curve",
                    "[{\"id\": \"prog.level_curve.xmap_prod\", \"max_level\": 1, " +
                    "\"entries\": [{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}]}]"))
                .Add("arch.class", Envelope("arch.class",
                    "[{\"id\": \"" + ArchetypeId.Value + "\", \"name_key\": \"l10n.arch.class.xmap_prod.name\", " +
                    "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, \"power_types\": [\"arch.power.health\"], " +
                    "\"level_curve_ref\": \"prog.level_curve.xmap_prod\"}]"))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"))
                .Add("creature.tier_definition", Envelope("creature.tier_definition",
                    "[{\"id\": \"creature.tier.xmap_prod\", \"name_key\": \"l10n.creature.tier.xmap_prod.name\", " +
                    "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}]"))
                .Add("creature.template", Envelope("creature.template",
                    "[{\"id\": \"" + GuardTemplateId.Value + "\", \"name_key\": \"l10n.creature.xmap_prod_guard.name\", " +
                    "\"level\": 1, \"tier\": \"creature.tier.xmap_prod\", \"base_stats\": {\"stat.max_health\": 100}, " +
                    "\"faction_id\": \"fac.xmap_prod_monster\", \"display_ref\": \"display.xmap_prod_guard\"}]"))
                .Add("spawn.table", Envelope("spawn.table", "[" +
                    SpawnRow("spawn.xmap_prod.guard_a", MapA, 3, "on_map_enter") + "," +
                    SpawnRow(StorySpawnId.Value, MapA, 5, "never") + "," +
                    SpawnRow("spawn.xmap_prod.guard_b", MapB, 4, "on_map_enter") + "]"))
                .Add("world.map", Envelope("world.map", "[" + WorldMapRow(MapA) + "," + WorldMapRow(MapB) + "]"))
                .Add("l10n.locale", Envelope("l10n.locale", "[{\"id\": \"l10n.locale.xmap_prod\", \"is_default\": true}]"));

            var registryOptions = PresentationSchemaCatalog.CreateOptions();
            registryOptions.FailOnUnknownTable = false;
            var registry = new DataRegistry(source, bus, registryOptions);
            PresentationSchemaCatalog.RegisterAll(registry);
            registry.RegisterSchema(WorldMapSchema.Table);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var engine = new StubEngine();
            var saveSystem = new SaveSystem(engine.FileSystem, new SaveSystemOptions(new Id("game.xmap_prod")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, new RngHost(1), world, new StubSpatialQuery(), saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            // 宿主惯例（games/_template GameBootstrap/FrameworkResidentHost）：玩家对象只构造一次，
            // 进图时由 post_load 钩子按需 AddEntity；规则层登记与存档段注册在构造期做一次。
            var player = new PlayerUnit(PlayerId, MapA, PlayerFactionId, ArchetypeId) { Position = Vec2.Zero };
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeId, raceId: null, level: 1);
            gameplay.RegisterPersistables(saveSystem, player);

            var loader = new StubResourceLoader();
            foreach (var mapId in new[] { MapA, MapB })
            {
                loader.Register(new Id("scene." + mapId.Value));
                loader.Register(new Id("nav." + mapId.Value));
            }

            var router = new SceneRouter(registry, loader, gameplay.AppState, world, gameplay.Hooks, bus);
            gameplay.AttachSceneRouter(router);

            var factory = new FakeViewFactory();
            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, new RngHost(2), factory,
                engine.Renderer2D, engine.Camera, engine.Audio, engine.FileSystem, router);

            var host = new Host
            {
                Bus = bus,
                World = world,
                Gameplay = gameplay,
                Router = router,
                Presentation = presentation,
                Factory = factory,
                Player = player,
                TickWhileNotInWorld = tickWhileNotInWorld,
                FlushAfterEnterMap = flushAfterEnterMap,
            };
            router.RegisterPreUnloadHook(host.HandlePreUnload);
            router.RegisterPostLoadHook(host.HandlePostLoad);

            Assert.True(gameplay.AppState.RequestTransition(AppState.MainMenu));
            return host;
        }

        /// <param name="tickWhileNotInWorld">固定步是否不看应用状态照常推进世界（部分宿主如此）。</param>
        /// <param name="flushAfterEnterMap">post_load 钩子在 EnterMap 之后是否自己补一次冲刷
        /// （模板宿主会；不照抄模板的宿主可能不会）。</param>
        [Theory]
        [InlineData(false, true)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(true, false)]
        public void CrossMapRestore_ThenLaterStorySpawns_AllLiveEntitiesKeepViews(bool tickWhileNotInWorld, bool flushAfterEnterMap)
        {
            var host = Build(tickWhileNotInWorld, flushAfterEnterMap);
            var slot = new Id("slot.xmap_prod");

            // ---- 新游戏进 A：玩家 + A 图 on_map_enter 守卫。 ----
            host.Router.LoadScene(MapA);
            host.Frames(3);
            Assert.Equal(MapA, host.Router.GetCurrentScene());
            host.AssertLiveEntitiesMatchViews("进 A 图");

            // ---- A 图存档，传送到 B 图。 ----
            Assert.True(host.Gameplay.SaveSystem.Save(new SaveRequest(slot, "t1")).Success);
            host.Player.MapId = MapB;
            host.Router.LoadScene(MapB);
            host.Frames(3);
            Assert.Equal(MapB, host.Router.GetCurrentScene());
            host.AssertLiveEntitiesMatchViews("传送到 B 图");

            // ---- 在 B 图读 A 图的档：RestoreFromSlot 跨图分支。 ----
            var result = host.Gameplay.RestoreFromSlot(slot);
            Assert.Equal(LoadStatus.Loaded, result.Status);
            host.Frames(3);
            Assert.Equal(MapA, host.Router.GetCurrentScene());
            Assert.Equal(AppState.InWorld, host.Gameplay.AppState.GetState());
            host.AssertLiveEntitiesMatchViews("跨图读档完成");

            // ---- 剧情推进若干帧之后，经生产生成路径新生成两只生物。 ----
            host.Frames(300);
            var storyGenerated = host.Gameplay.Spawn.SpawnNow(StorySpawnId, MapA);
            Assert.Single(storyGenerated);
            var directSpawn = host.Gameplay.Carriers.Creatures.Spawn(GuardTemplateId, MapA, new Vec2(7, 3), 0.0);
            host.Frames(3);

            Assert.NotNull(host.World.GetEntity(storyGenerated[0]));
            Assert.NotNull(host.World.GetEntity(directSpawn));
            Assert.True(host.Presentation.ViewBinder.TryGetView(storyGenerated[0], out _),
                "跨图读档之后经 Spawn.SpawnNow 新生成的生物应当有 View");
            Assert.True(host.Presentation.ViewBinder.TryGetView(directSpawn, out _),
                "跨图读档之后经 CreatureFactory.Spawn 新生成的生物应当有 View");
            host.AssertLiveEntitiesMatchViews("读档后剧情生成");

            // ---- 同图再存读一次档（不经 RestoreFromSlot，直接 SaveSystem.Load），再推进一帧。 ----
            var slot2 = new Id("slot.xmap_prod_same_map");
            Assert.True(host.Gameplay.SaveSystem.Save(new SaveRequest(slot2, "t2")).Success);
            Assert.Equal(LoadStatus.Loaded, host.Gameplay.SaveSystem.Load(slot2).Status);
            host.Frames(1);
            host.AssertLiveEntitiesMatchViews("同图存读档之后");
        }
    }
}
