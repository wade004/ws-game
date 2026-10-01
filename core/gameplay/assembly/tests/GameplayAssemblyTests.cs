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

        private static GameplayAssembly BuildEmpty(out WorldSim world) => BuildEmpty(out world, out _);

        private static GameplayAssembly BuildEmpty(out WorldSim world, out IEventBus busOut)
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
            busOut = bus;
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
            var assembly = BuildEmpty(out var world);
            var mapId = new Id("world.smoke_empty_map");
            var playerId = new Id("unit.smoke_player");

            var ex = Record.Exception(() => assembly.EnterMap(mapId, playerId));
            Assert.Null(ex);

            // T-M13：空图进图没有任何内容可摆放——不应凭空产生实体或掉落物，重复进图（幂等）同样如此。
            Assert.Empty(world.QueryEntities(new EntityFilter()));
            Assert.Empty(assembly.Loot.ActiveLootIds);
            Assert.Null(Record.Exception(() => assembly.EnterMap(mapId, playerId)));
            Assert.Empty(world.QueryEntities(new EntityFilter()));
            Assert.Empty(assembly.Loot.ActiveLootIds);
        }

        [Fact]
        public void Construct_Then_TickSeveralTimes_DoesNotThrow_WithNoUnitsRegistered()
        {
            var assembly = BuildEmpty(out var world, out var bus);
            var ticks = new System.Collections.Generic.List<SimTickStartedEvent>();
            bus.Subscribe<SimTickStartedEvent>(SimEventKeys.TickStarted, e => ticks.Add(e));
            const int tickCount = 5;
            const double step = 0.1;

            for (var i = 0; i < tickCount; i++)
            {
                world.Tick(SimStep.Continuous(step));
            }

            // T-M13：不止"不抛"——五次 tick 确实全部跑过装配挂载的阶段处理器链：tick 序号连续递增、
            // 每次携带的 dt 即步长，且空世界里没有任何实体因此被创建。
            Assert.Equal(tickCount, ticks.Count);
            for (var i = 1; i < ticks.Count; i++)
            {
                Assert.Equal(ticks[i - 1].TickIndex + 1, ticks[i].TickIndex);
            }

            Assert.All(ticks, e => Assert.Equal(step, e.Dt));
            Assert.Empty(world.QueryEntities(new EntityFilter()));
            Assert.Empty(assembly.Loot.ActiveLootIds);
        }

        // ------------------------------------------------------------------
        // T-L14（测试覆盖剩余项 2026-10-01）：未装配离散模式（未传 clockHost）时的转发方法边界
        // ------------------------------------------------------------------

        [Fact]
        public void Advance_WithoutClockHost_ThrowsInvalidOperation_NamingTheMissingParameter()
        {
            var assembly = BuildEmpty(out _);

            var ex = Assert.Throws<System.InvalidOperationException>(() => assembly.Advance(0.1));

            Assert.Contains("clockHost", ex.Message);
        }

        [Fact]
        public void PlaybackForwarding_WithoutDiscreteWiring_PacingIsNull_AndForwardersAreSafe()
        {
            var assembly = BuildEmpty(out _);
            Assert.Null(assembly.Pacing);

            // 空探针始终是调用方错误；有合法探针但无节奏策略可接时是空操作（不抛）。
            Assert.Throws<System.ArgumentNullException>(() => assembly.SetPendingPlaybackProbe(null!));
            Assert.Null(Record.Exception(() => assembly.SetPendingPlaybackProbe(() => true)));
            Assert.Null(Record.Exception(() => assembly.NotifyPlaybackFinished()));
        }
    }
}
