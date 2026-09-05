using Adapters.Stub;
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
    /// 阶段 3 集成收尾"事项一"烟雾测试（惯例同 <c>core/carriers/assembly/tests/CarriersAssemblyTests.cs</c>）：
    /// <see cref="GameplayAssembly"/> 按 <see cref="GameplaySchemaCatalog.RegisterAll"/> 注册的全部
    /// L0～L4 schema 构造一份空数据的 <see cref="DataRegistry"/>，验证构造期全部装配（十个 L4 宿主 +
    /// 回调接线 + tick 处理器挂载）不抛异常，以及 <see cref="GameplayAssembly.EnterMap"/> 对一张空图
    /// （零条 <c>spawn.table</c>/<c>area.trigger_def</c>/<c>econ.vendor</c> 记录）不抛异常。
    /// </summary>
    public class GameplayAssemblyTests
    {
        /// <summary>惯例同 <see cref="Tests.Carriers.Assembly.CarriersAssemblyTests.AddMinimalRequiredTables"/>：
        /// 几张"哪怕零行也必须在数据源里出现过"的表先垫上。</summary>
        private static void AddMinimalRequiredTables(InMemoryDataSource source)
        {
            source.Add("item.budget_curve",
                "{\"table\": \"item.budget_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]}");
            source.Add("stat.definition", "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.hit_table_config",
                "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.resist_curve",
                "{\"table\": \"combat.resist_curve\", \"schema_version\": 1, \"rows\": []}");
        }

        private static GameplayAssembly BuildEmpty(out WorldSim world)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalRequiredTables(source);
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            var playerId = new Id("unit.smoke_player");
            var playerFaction = new Id("fac.smoke_player");
            var saveSystem = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.smoke_test")));

            return new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => playerId,
                playerFactionId: playerFaction);
        }

        [Fact]
        public void Construct_WithEmptyRegistry_DoesNotThrow_AndExposesAllHosts()
        {
            var assembly = BuildEmpty(out _);

            Assert.NotNull(assembly.Carriers);
            Assert.NotNull(assembly.AppState);
            Assert.NotNull(assembly.Hooks);
            Assert.NotNull(assembly.WorldState);
            Assert.NotNull(assembly.Loot);
            Assert.NotNull(assembly.Economy);
            Assert.NotNull(assembly.Quest);
            Assert.NotNull(assembly.Dialog);
            Assert.NotNull(assembly.Encounter);
            Assert.NotNull(assembly.Level);
            Assert.NotNull(assembly.Difficulty);
            Assert.NotNull(assembly.Achievement);
            Assert.NotNull(assembly.AreaTrigger);
            Assert.NotNull(assembly.Spawn);
            Assert.NotNull(assembly.Reward);
            Assert.NotNull(assembly.ExprHostFactory);
        }

        [Fact]
        public void EnterMap_OnEmptyMap_DoesNotThrow()
        {
            var assembly = BuildEmpty(out _);
            var mapId = new Id("world.smoke_empty_map");
            var playerId = new Id("unit.smoke_player");

            var ex = Record.Exception(() => assembly.EnterMap(mapId, playerId));
            Assert.Null(ex);
        }

        [Fact]
        public void Construct_Then_TickSeveralTimes_DoesNotThrow_WithNoUnitsRegistered()
        {
            var assembly = BuildEmpty(out var world);

            for (var i = 0; i < 5; i++)
            {
                world.Tick(SimStep.Continuous(0.1));
            }

            Assert.NotNull(assembly);
        }
    }
}
