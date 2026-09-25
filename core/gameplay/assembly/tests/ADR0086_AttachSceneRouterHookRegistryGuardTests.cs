using System;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.HookRegistry;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// ADR-0086 回归（消费方第三十二批阻塞项第 4 条）：<see cref="GameplayAssembly.AttachSceneRouter"/>
    /// 在其上注册的 <c>ScenePostLoad</c> 钩子（补发跨图读档 <c>save.loaded</c> 通知，见该方法判断
    /// 记录）要生效，前提是传入的 <see cref="SceneRouter"/> 构造时使用的
    /// <see cref="IHookRegistry"/> 与 <see cref="GameplayAssembly.Hooks"/> 是同一个实例——不同实例时
    /// <c>SceneRouter</c> 触发的是另一份注册表的 <c>ScenePostLoad</c> 钩子点，本类型注册在自己
    /// <c>Hooks</c> 上的回调永远不会被调用，读档会一直挂起且没有任何异常/诊断，只有在跨图读档实际
    /// 卡死时才会被发现。本文件验证新加的运行期同实例校验：不同实例立即抛异常（异常消息写清原因），
    /// 同一实例（生产装配与全部既有测试夹具的既有用法）不受影响、正常工作。
    /// </summary>
    public sealed class ADR0086_AttachSceneRouterHookRegistryGuardTests
    {
        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public IDataRegistryView Registry = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
        }

        /// <summary>惯例同 <c>GameplayAssemblyTests.BuildEmpty</c>：几张"哪怕零行也必须在数据源里
        /// 出现过"的表先垫上，构造一个空数据的最小 <see cref="GameplayAssembly"/>——本文件只关心
        /// <see cref="GameplayAssembly.AttachSceneRouter"/> 本身的同实例校验，不需要任何真实内容。</summary>
        private static Fixture BuildEmpty()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            source.Add("item.budget_curve",
                "{\"table\": \"item.budget_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]}");
            source.Add("stat.definition", "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.hit_table_config",
                "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.resist_curve",
                "{\"table\": \"combat.resist_curve\", \"schema_version\": 1, \"rows\": []}");

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var playerId = new Id("unit.adr0086_player");
            var playerFaction = new Id("fac.adr0086_player");
            var saveSystem = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.adr0086_test")));

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => playerId,
                playerFactionId: playerFaction);

            return new Fixture { Bus = bus, Registry = registry, World = world, Gameplay = gameplay };
        }

        [Fact]
        public void AttachSceneRouter_DifferentHookRegistryInstance_ThrowsImmediately()
        {
            var fx = BuildEmpty();
            var loader = new StubResourceLoader();
            // 故意用另一份独立的 IHookRegistry 构造 SceneRouter（生产代码里的真实错误形状：装配
            // 顺序改动导致 SceneRouter 拿到的 hooks 不再是 gameplay.Hooks）。
            var otherHooks = new HookRegistry();
            var router = new SceneRouter(fx.Registry, loader, fx.Gameplay.AppState, fx.World, otherHooks, fx.Bus);

            var ex = Assert.Throws<InvalidOperationException>(() => fx.Gameplay.AttachSceneRouter(router));

            Assert.Contains("IHookRegistry", ex.Message);
            Assert.Contains("AttachSceneRouter", ex.Message);
        }

        [Fact]
        public void AttachSceneRouter_SameHookRegistryInstance_Succeeds()
        {
            var fx = BuildEmpty();
            var loader = new StubResourceLoader();
            var router = new SceneRouter(fx.Registry, loader, fx.Gameplay.AppState, fx.World, fx.Gameplay.Hooks, fx.Bus);

            var ex = Record.Exception(() => fx.Gameplay.AttachSceneRouter(router));

            Assert.Null(ex);
        }
    }
}
