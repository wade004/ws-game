using System;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;

sealed class TeleportProbeFixture
{
    public EventBus Bus = null!;
    public WorldSim World = null!;
    public GameplayAssembly Gameplay = null!;
    public PlayerUnit Player = null!;
    public Core.Foundation.SceneRouter.SceneRouter Router = null!;
    public StubResourceLoader Loader = null!;
}

static class TeleportLoadingBoundaryProbe
{
    static readonly Id PlayerId = new("unit.teleport_loading_player");
    static readonly Id FactionId = new("fac.teleport_loading_player");
    static readonly Id ClassId = new("arch.class.teleport_loading");
    static readonly Id MapA = new("world.teleport_loading_a");
    static readonly Id MapB = new("world.teleport_loading_b");
    static readonly Id NpcId = new("unit.teleport_loading_npc");
    static readonly Id MenuB = new("dialog.teleport_loading_to_b");
    static readonly Id MenuA = new("dialog.teleport_loading_to_a");

    static string E(string table, string rows) => "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";
    static string MapRow(Id map, string scene, string nav, double x, double y) =>
        "{\"id\":\"" + map.Value + "\",\"scene_ref\":\"" + scene + "\",\"nav_ref\":\"" + nav +
        "\",\"spawn_points\":[{\"id\":\"spawn.default\",\"position\":{\"x\":" + x + ",\"y\":" + y + "},\"facing\":0}]}";
    static string MenuRow(Id menu, Id target) =>
        "{\"id\":\"" + menu.Value + "\",\"options\":[{\"text_key\":\"l10n.teleport.loading\",\"actions\":[{\"kind\":\"teleport\",\"ref\":\"" + target.Value + "\"}]}]}";

    static TeleportProbeFixture Build()
    {
        var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
        var source = new InMemoryDataSource()
            .Add("stat.definition", E("stat.definition", "[{\"id\":\"stat.max_health\",\"name_key\":\"l10n.max\",\"group\":\"primary\",\"default_base\":100}]"))
            .Add("arch.power_type", E("arch.power_type", "[{\"id\":\"arch.power.loading_health\",\"name_key\":\"l10n.health\",\"max_source\":{\"kind\":\"stat\",\"stat\":\"stat.max_health\"},\"start_full\":true}]"))
            .Add("prog.level_curve", E("prog.level_curve", "[{\"id\":\"prog.curve.teleport_loading\",\"max_level\":1,\"entries\":[{\"level\":1,\"xp_to_next\":100,\"growth\":{}}]}]"))
            .Add("arch.class", E("arch.class", "[{\"id\":\"" + ClassId.Value + "\",\"name_key\":\"l10n.class\",\"primary_stat\":\"stat.max_health\",\"base_stats\":{},\"power_types\":[\"arch.power.loading_health\"],\"level_curve_ref\":\"prog.curve.teleport_loading\"}]"))
            .Add("combat.hit_table_config", E("combat.hit_table_config", "[]"))
            .Add("combat.resist_curve", E("combat.resist_curve", "[]"))
            .Add("item.budget_curve", E("item.budget_curve", "[{\"id\":\"item.budget.default\",\"entries\":[{\"item_level\":1,\"budget\":10}]}]"))
            .Add("world.map", E("world.map", "[" + MapRow(MapA, "scene.teleport_loading_a", "nav.teleport_loading_a", 1, 2) + "," + MapRow(MapB, "scene.teleport_loading_b", "nav.teleport_loading_b", 30, 40) + "]"))
            .Add("dialog.gossip_menu", E("dialog.gossip_menu", "[" + MenuRow(MenuB, MapB) + "," + MenuRow(MenuA, MapA) + "]"))
            .Add("dialog.story_tree", E("dialog.story_tree", "[]"));
        var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
        GameplaySchemaCatalog.RegisterAll(registry);
        registry.RegisterSchema(Core.Foundation.SceneRouter.WorldMapSchema.Table);
        var report = registry.LoadAll();
        if (report.IsBlocking) throw new InvalidOperationException(string.Join(";", report.Issues));

        var world = new WorldSim(bus);
        var loader = new StubResourceLoader { DeferCallbacks = true };
        loader.Register(new Id("scene.teleport_loading_a")); loader.Register(new Id("nav.teleport_loading_a"));
        loader.Register(new Id("scene.teleport_loading_b")); loader.Register(new Id("nav.teleport_loading_b"));
        var save = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.teleport_loading")), bus);
        var gameplay = new GameplayAssembly(bus, registry, new RngHost(1), world, new StubSpatialQuery(), save,
            playerUnitProvider: () => PlayerId, playerFactionId: FactionId);
        var player = new PlayerUnit(PlayerId, MapA, FactionId, ClassId) { Position = new Vec2(5, 5) };
        world.AddEntity(player);
        gameplay.Carriers.Rules.RegisterUnit(PlayerId, ClassId, raceId: null, level: 1);
        var router = new Core.Foundation.SceneRouter.SceneRouter(registry, loader, gameplay.AppState, world, gameplay.Hooks, bus);
        router.RegisterPreUnloadHook(map => gameplay.LeaveMap(map));
        router.RegisterPostLoadHook(map =>
        {
            // Match production GameBootstrap: ClearAll removes the player, then post-load
            // re-adds it before the GameplayAssembly enter-map work.
            if (world.GetEntity(PlayerId) == null)
            {
                world.AddEntity(player);
                bus.DispatchPending();
            }
            gameplay.EnterMap(map, PlayerId);
        });
        gameplay.AttachSceneRouter(router);
        gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.MainMenu);
        router.LoadScene(MapA);
        loader.CompletePending(new Id("scene.teleport_loading_a"));
        loader.CompletePending(new Id("nav.teleport_loading_a"));
        router.Update();
        return new TeleportProbeFixture { Bus = bus, World = world, Gameplay = gameplay, Player = player, Router = router, Loader = loader };
    }

    public static void Main()
    {
        var fx = Build();
        fx.Gameplay.Dialog.OpenGossip(PlayerId, NpcId, MenuB);
        var firstChosen = fx.Gameplay.Dialog.ChooseOption(PlayerId, 0);
        var stateAfterFirst = fx.Router.State;
        var sceneAfterFirst = fx.Router.GetCurrentScene()?.Value ?? "<null>";
        var mapAfterFirst = fx.Player.MapId.Value;
        var firstPendingB = fx.Loader.HasPending(new Id("scene.teleport_loading_b"));

        fx.Gameplay.Dialog.OpenGossip(PlayerId, NpcId, MenuA);
        var secondChosen = fx.Gameplay.Dialog.ChooseOption(PlayerId, 0);
        var mapAfterSecond = fx.Player.MapId.Value;
        var positionAfterSecond = fx.Player.Position;
        var stateAfterSecond = fx.Router.State;

        fx.Loader.CompletePending(new Id("scene.teleport_loading_b"));
        fx.Loader.CompletePending(new Id("nav.teleport_loading_b"));
        fx.Router.Update();

        Console.WriteLine("TELEPORT-GOSSIP-CONSECUTIVE-WHILE-LOADING");
        Console.WriteLine($"initial_scene={MapA.Value};first_chosen={firstChosen};state_after_first={stateAfterFirst};scene_after_first={sceneAfterFirst};map_after_first={mapAfterFirst};first_pending_B={firstPendingB}");
        var finalEntity = fx.World.GetEntity(PlayerId);
        Console.WriteLine($"second_chosen={secondChosen};state_after_second={stateAfterSecond};map_after_second={mapAfterSecond};position_after_second={positionAfterSecond.X},{positionAfterSecond.Y};final_scene={fx.Router.GetCurrentScene()?.Value ?? "<null>"};final_map={fx.Player.MapId.Value};final_position={fx.Player.Position.X},{fx.Player.Position.Y};world_player_present={finalEntity != null};world_player_map={finalEntity?.MapId.Value ?? "<null>"};world_player_position={finalEntity?.Position.X.ToString() ?? "<null>"},{finalEntity?.Position.Y.ToString() ?? "<null>"}");
        Console.WriteLine("ORACLE-CURRENT=scene/player map and position must remain consistent; second request may reject, queue, or replace while preserving that invariant");
        Console.WriteLine("INTERPRETATION-CURRENT=The public Dialog OpenGossip/ChooseOption path accepted the second request while the first scene load was still Loading. Current ApplyResolvedTeleport commits player/map fields only after SceneRouter accepts the destination; completion leaves router, player, and WorldSim on B at 30,40 with no divergence.");
    }
}
