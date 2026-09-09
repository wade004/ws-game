using System;
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
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// 第十三轮外部审核复核（architecture/落地计划/audit-6739f50-20260909，core/core-findings.md）
    /// TP-111-01（P2，主审已确认）的复现与根治验收——全部用真实 <see cref="GameplayAssembly"/>/
    /// <see cref="Core.Gameplay.Dialog.DialogHost"/>（公共 Gossip 入口）/真实 <see cref="SceneRouter"/>/
    /// <see cref="StubResourceLoader"/>（<c>DeferCallbacks=true</c> 驱动"加载中途"）与生产形状的
    /// post-load 钩子（<c>WorldSim</c> 被 <c>ClearAll</c> 后重新 <c>AddEntity</c>，再调用
    /// <c>Gameplay.EnterMap</c>）组合。数据/断言取值沿用报告归档的独立探针
    /// （<c>architecture/落地计划/audit-6739f50-20260909/core/repro/TeleportLoadingBoundaryProbe.cs</c>）
    /// 已经验证过的 fixture 与 ORACLE-CURRENT 结论"场景与玩家 map/位置必须保持一致"。
    /// <para>
    /// TP-111-01：<c>GameplayAssembly.ApplyResolvedTeleport</c> 修复前先把 <c>entity.MapId</c>/位置
    /// 改写成目标，再调用 <see cref="ISceneRouter.LoadScene"/>；<see cref="SceneRouter"/> 按合同拒绝
    /// Loading 中的第二次调用（抛 <see cref="InvalidOperationException"/>），但异常被吞掉，此时字段
    /// 已经改写完毕。首个请求随后正常完成，最终 <c>Router.CurrentScene</c> 停在首个请求的目标地图，
    /// <see cref="IWorldSim"/> 里的玩家实体 <c>MapId</c>/位置却是被拒绝的第二个请求的目标，两者永久
    /// 分叉。
    /// </para>
    /// </summary>
    public sealed class TP_111_FollowupAuditTests
    {
        private static readonly Id PlayerId = new Id("unit.tp_111_followup_player");
        private static readonly Id FactionId = new Id("fac.tp_111_followup_player");
        private static readonly Id ClassId = new Id("arch.class.tp_111_followup");
        private static readonly Id MapA = new Id("world.tp_111_followup_a");
        private static readonly Id MapB = new Id("world.tp_111_followup_b");
        private static readonly Id NpcId = new Id("unit.tp_111_followup_npc");
        private static readonly Id MenuToB = new Id("dialog.tp_111_followup_to_b");
        private static readonly Id MenuToA = new Id("dialog.tp_111_followup_to_a");
        private static readonly Id SceneA = new Id("scene.tp_111_followup_a");
        private static readonly Id NavA = new Id("nav.tp_111_followup_a");
        private static readonly Id SceneB = new Id("scene.tp_111_followup_b");
        private static readonly Id NavB = new Id("nav.tp_111_followup_b");

        private sealed class Fixture
        {
            public EventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
            public SceneRouter Router = null!;
            public StubResourceLoader Loader = null!;
        }

        private static string E(string table, string rows) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";

        private static string MapRow(Id map, Id scene, Id nav, double x, double y) =>
            "{\"id\":\"" + map.Value + "\",\"scene_ref\":\"" + scene.Value + "\",\"nav_ref\":\"" + nav.Value +
            "\",\"spawn_points\":[{\"id\":\"spawn.default\",\"position\":{\"x\":" + x + ",\"y\":" + y + "},\"facing\":0}]}";

        private static string MenuRow(Id menu, Id target) =>
            "{\"id\":\"" + menu.Value + "\",\"options\":[{\"text_key\":\"l10n.teleport\",\"actions\":[{\"kind\":\"teleport\",\"ref\":\"" + target.Value + "\"}]}]}";

        private static Fixture Build()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var source = new InMemoryDataSource()
                .Add("stat.definition", E("stat.definition", "[{\"id\":\"stat.max_health\",\"name_key\":\"l10n.max\",\"group\":\"primary\",\"default_base\":100}]"))
                .Add("arch.power_type", E("arch.power_type", "[{\"id\":\"arch.power.tp_111_followup_health\",\"name_key\":\"l10n.health\",\"max_source\":{\"kind\":\"stat\",\"stat\":\"stat.max_health\"},\"start_full\":true}]"))
                .Add("prog.level_curve", E("prog.level_curve", "[{\"id\":\"prog.curve.tp_111_followup\",\"max_level\":1,\"entries\":[{\"level\":1,\"xp_to_next\":100,\"growth\":{}}]}]"))
                .Add("arch.class", E("arch.class", "[{\"id\":\"" + ClassId.Value + "\",\"name_key\":\"l10n.class\",\"primary_stat\":\"stat.max_health\",\"base_stats\":{},\"power_types\":[\"arch.power.tp_111_followup_health\"],\"level_curve_ref\":\"prog.curve.tp_111_followup\"}]"))
                .Add("combat.hit_table_config", E("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", E("combat.resist_curve", "[]"))
                .Add("item.budget_curve", E("item.budget_curve", "[{\"id\":\"item.budget.default\",\"entries\":[{\"item_level\":1,\"budget\":10}]}]"))
                .Add("world.map", E("world.map", "[" + MapRow(MapA, SceneA, NavA, 1, 2) + "," + MapRow(MapB, SceneB, NavB, 30, 40) + "]"))
                .Add("dialog.gossip_menu", E("dialog.gossip_menu", "[" + MenuRow(MenuToB, MapB) + "," + MenuRow(MenuToA, MapA) + "]"))
                .Add("dialog.story_tree", E("dialog.story_tree", "[]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            registry.RegisterSchema(WorldMapSchema.Table);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join(";", report.Issues));

            var world = new WorldSim(bus);
            var loader = new StubResourceLoader { DeferCallbacks = true };
            loader.Register(SceneA);
            loader.Register(NavA);
            loader.Register(SceneB);
            loader.Register(NavB);
            var save = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.tp_111_followup")), bus);
            var gameplay = new GameplayAssembly(
                bus, registry, new RngHost(1), world, new StubSpatialQuery(), save,
                playerUnitProvider: () => PlayerId, playerFactionId: FactionId);

            var player = new PlayerUnit(PlayerId, MapA, FactionId, ClassId) { Position = new Vec2(5, 5) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ClassId, raceId: null, level: 1);

            var router = new SceneRouter(registry, loader, gameplay.AppState, world, gameplay.Hooks, bus);
            router.RegisterPreUnloadHook(map => gameplay.LeaveMap(map));
            router.RegisterPostLoadHook(map =>
            {
                // 与生产 GameBootstrap 一致：ClearAll 会把玩家实体一并移除，post-load 钩子负责
                // 重新 AddEntity，再调用 GameplayAssembly.EnterMap。
                if (world.GetEntity(PlayerId) == null)
                {
                    world.AddEntity(player);
                    bus.DispatchPending();
                }
                gameplay.EnterMap(map, PlayerId);
            });
            gameplay.AttachSceneRouter(router);
            gameplay.AppState.RequestTransition(AppState.MainMenu);

            router.LoadScene(MapA);
            loader.CompletePending(SceneA);
            loader.CompletePending(NavA);
            router.Update();

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay, Player = player, Router = router, Loader = loader };
        }

        /// <summary>
        /// TP-111-01 核心复现与根治：首个跨图 Gossip 传送请求进入 Loading（B 的场景/导航资源尚未
        /// 完成）期间，公共 Dialog 入口立刻发起第二个跨图传送请求（回到 A）。根治前
        /// <c>ApplyResolvedTeleport</c> 会在调用 <see cref="ISceneRouter.LoadScene"/> 之前就把玩家
        /// 字段改写成第二个请求的目标，路由拒绝（Loading 中）被吞掉后字段已经"越权"提交；首个请求
        /// 完成后 <c>Router.CurrentScene</c> 与玩家实体 MapId/位置永久分叉。根治后：第二个请求应被
        /// 拒绝且不产生任何字段副作用，首个请求完成后场景与玩家字段必须保持一致（同图）。
        /// </summary>
        [Fact]
        public void TP_111_01_ConsecutiveGossipTeleportWhileLoading_KeepsSceneAndPlayerMapConsistent()
        {
            var fx = Build();

            fx.Gameplay.Dialog.OpenGossip(PlayerId, NpcId, MenuToB);
            var firstChosen = fx.Gameplay.Dialog.ChooseOption(PlayerId, 0);
            Assert.True(firstChosen);
            Assert.Equal(SceneRouterState.Loading, fx.Router.State);
            // 首个请求已经落地到 Loading：场景仍是 A（还没完成),地图字段已经指向 B（跨图请求接受
            // 后立即提交，这是预期行为——修复的是"第二个被拒绝的请求"，不是"第一个被接受的请求"。
            Assert.Equal(MapA, fx.Router.GetCurrentScene());
            Assert.Equal(MapB, fx.Player.MapId);
            Assert.True(fx.Loader.HasPending(SceneB));

            // 第二个请求：Loading 中回到 A。SceneRouter 会拒绝（抛 InvalidOperationException），
            // DialogHost.ChooseOption 对拒绝的传送动作仍返回 true（选项本身被"选中"执行了，动作
            // 内部的路由拒绝不影响 ChooseOption 的返回值——同探针 second_chosen=True 的既有观测）。
            fx.Gameplay.Dialog.OpenGossip(PlayerId, NpcId, MenuToA);
            var secondChosen = fx.Gameplay.Dialog.ChooseOption(PlayerId, 0);
            Assert.True(secondChosen);

            // TP-111-01 核心断言：第二个被路由拒绝的请求不能提交任何玩家字段——字段必须停留在
            // 第一个（唯一被接受的）请求已经落地的 MapB/(30,40)，不能变成被拒绝请求的目标
            // MapA/(1,2)（修复前的实际行为）。
            Assert.Equal(MapB, fx.Player.MapId);
            Assert.Equal(new Vec2(30, 40), fx.Player.Position);
            Assert.Equal(SceneRouterState.Loading, fx.Router.State);

            // 完成首个（唯一被接受的）请求。
            fx.Loader.CompletePending(SceneB);
            fx.Loader.CompletePending(NavB);
            fx.Router.Update();

            // 最终不变量：场景与玩家实体的 MapId/位置必须保持一致——不再出现 Router 在 B、WorldSim
            // 玩家实体却停在 A 的分叉。
            Assert.Equal(MapB, fx.Router.GetCurrentScene());
            Assert.Equal(MapB, fx.Player.MapId);

            var worldPlayer = fx.World.GetEntity(PlayerId);
            Assert.NotNull(worldPlayer);
            Assert.Equal(MapB, worldPlayer!.MapId);
            Assert.Equal(MapB, fx.Player.MapId);
        }
    }
}
