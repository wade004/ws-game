using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
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
    /// 消费方反馈 P2 缺口 7：冷启动"继续游戏"读档失败。复现：进程刚启动、玩家实体还没加入世界（模板写法只在场景 post_load 钩子里加入）时，
    /// <c>SaveSystem.Load</c> 在 <c>world.current_position</c> 段抛 <c>WorldUnitAccess: 单位不存在</c>，结果为
    /// <see cref="LoadStatus.PersistableThrew"/>。不变量：读档开始前玩家实体一定在世界里（缺失则补加），冷启动读档成功并还原存档时的位置；注册之后再自行加入玩家实体的既有装配写法不受影响（见第二个用例）。
    /// </summary>
    public sealed class GameplayAssemblyColdLoadTests
    {
        private static readonly Id PlayerId = new Id("unit.cold_load_player");
        private static readonly Id PlayerFactionId = new Id("fac.cold_load_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.cold_load_sample");
        private static readonly Id MapA = new Id("world.cold_load_map_a");
        private static readonly Id Slot = new Id("slot.cold_load");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.curve.cold_load\", \"max_level\": 1, \"entries\": [{\"level\": 1, \"xp_to_next\": 0, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"arch.class.cold_load_sample\", \"name_key\": \"l10n.arch.cold_load.name\", \"primary_stat\": \"stat.max_health\", " +
            "\"base_stats\": {\"stat.max_health\": 100}, \"power_types\": [\"arch.power.health\"], \"level_curve_ref\": \"prog.curve.cold_load\"}]";

        private static string Envelope(string table, string rows) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";

        private sealed class Rig
        {
            public WorldSim World = null!;
            public SaveSystem Save = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
        }

        /// <summary>装配一套脱离引擎的游戏世界；<paramref name="fs"/> 共享，模拟"同一台机器上两次进程"。</summary>
        private static Rig Boot(StubFileSystem fs, bool addPlayerBeforeRegister)
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
            source.Add("world.map", Envelope("world.map",
                "[{\"id\": \"world.cold_load_map_a\", \"scene_ref\": \"scene.cold_load_a\", \"nav_ref\": \"nav.cold_load_a\", \"spawn_points\": [{\"id\": \"spawn.cold_load_a.default\", \"position\": {\"x\": 0, \"y\": 0}, \"facing\": 0}]}]"));
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.cold_load_test")), bus);
            var gameplay = new GameplayAssembly(
                bus, registry, new RngHost(1), world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId,
                deathPolicyOptions: new DeathPolicyOptions { Policy = RespawnPolicy.RespawnPoint, AutosaveSlotId = Slot });

            var player = new PlayerUnit(PlayerId, MapA, PlayerFactionId, ArchetypeSample) { Position = new Vec2(5, 5) };
            if (addPlayerBeforeRegister)
            {
                world.AddEntity(player);
                bus.DispatchPending();
            }

            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);
            gameplay.RegisterPersistables(saveSystem, player);
            bus.DispatchPending();
            return new Rig { World = world, Save = saveSystem, Gameplay = gameplay, Player = player };
        }

        [Fact]
        public void ColdStart_LoadBeforeAnyScene_Succeeds_AndRestoresPosition()
        {
            var fs = new StubFileSystem();

            // 第一次进程：玩家在世界里，走到 (9,9) 存档。
            var first = Boot(fs, addPlayerBeforeRegister: true);
            first.Gameplay.Carriers.Units.SetPosition(PlayerId, new Vec2(9, 9));
            var saved = first.Save.Save(new SaveRequest(Slot, "t1"));
            Assert.True(saved.Success, saved.Message);

            // 第二次进程：冷启动，模板写法——玩家实体尚未加入世界，直接读档。
            var second = Boot(fs, addPlayerBeforeRegister: false);
            Assert.Null(second.World.GetEntity(PlayerId));

            var load = second.Save.Load(Slot);
            Assert.True(load.Status == LoadStatus.Loaded || load.Status == LoadStatus.LoadedFromBackup, load.Status + ": " + load.Message);
            Assert.NotNull(second.World.GetEntity(PlayerId));
            Assert.Equal(new Vec2(9, 9), second.Player.Position);
        }

        [Fact]
        public void AddPlayerAfterRegisterPersistables_StillWorks_NoDuplicateEntityId()
        {
            // 既有装配写法：RegisterPersistables 之后再自行把玩家加入世界。补加玩家只发生在读档开始前，这种写法不会撞上"实体 id 重复"。
            var fs = new StubFileSystem();
            var rig = Boot(fs, addPlayerBeforeRegister: false);
            rig.World.AddEntity(rig.Player);
            rig.Gameplay.Carriers.Units.SetPosition(PlayerId, new Vec2(7, 3));
            var saved = rig.Save.Save(new SaveRequest(Slot, "t2"));
            Assert.True(saved.Success, saved.Message);
            var load = rig.Save.Load(Slot);
            Assert.True(load.Status == LoadStatus.Loaded || load.Status == LoadStatus.LoadedFromBackup, load.Status + ": " + load.Message);
            Assert.Equal(new Vec2(7, 3), rig.Player.Position);
        }
    }
}
