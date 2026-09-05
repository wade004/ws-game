using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Assembly
{
    /// <summary>
    /// 阶段 3 整理"事项四"烟雾测试：<see cref="CarriersAssembly"/> 按
    /// <see cref="CarriersSchemaCatalog.RegisterAll"/> 注册的全部 L0～L3 schema 构造一份空数据的
    /// <see cref="DataRegistry"/>（不提供任何行——<see cref="DataRegistry.LoadAll"/> 只加载数据源里
    /// 实际存在的表，未提供数据的已注册 schema 就是零条记录，见该类型判断记录），验证：
    /// <list type="bullet">
    /// <item>构造期全部装配（<see cref="Core.Rules.Assembly.RulesAssembly"/> + 五个 L3 宿主 +
    /// <see cref="Core.Rules.Assembly.DeferredEffectExtension"/> 换绑 + tick 处理器挂载顺序）不抛
    /// 异常——这是本类型接线量最大的一段代码，编译期检查不到构造期的空引用/顺序错误，必须跑一次
    /// 真正的构造才能发现。</item>
    /// <item><see cref="RegisterTickHandlers"/> 与 <c>SummonTickHandler</c> 的注册顺序（先
    /// SummonTickHandler 后 AiTickHandler）不产生重复挂载或异常：世界模拟空跑几个 tick（无任何
    /// 单位/召唤物/游戏对象）应正常完成。</item>
    /// </list>
    /// </summary>
    public class CarriersAssemblyTests
    {
        /// <summary>
        /// 两类"必须有数据"的前置条件，与本测试真正关心的"空 registry 能否装配"无关，先垫上：
        /// <list type="bullet">
        /// <item><see cref="Core.Carriers.Item.ItemBudgetValidationRule"/> 无条件要求
        /// <see cref="CarriersSchemaCatalog.DefaultItemBudgetCurveId"/> 指向的记录存在（即便
        /// <c>item.template</c> 一条记录都没有）。</item>
        /// <item><c>Core.Numbers.StatBlock.StatHost</c>/<c>Core.Rules.Combat.CombatDataLoader</c>
        /// 要求 <c>stat.definition</c>/<c>combat.hit_table_config</c>/<c>combat.resist_curve</c>
        /// 三张表必须在数据源里"加载过"（哪怕零行）——只注册 schema、不出现在数据源里会在构造期
        /// 直接抛异常，与"表存在但零行"是两回事（见这两个类型的判断记录）。</item>
        /// </list>
        /// </summary>
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

        [Fact]
        public void Construct_WithEmptyRegistry_DoesNotThrow_AndExposesAllHosts()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalRequiredTables(source);
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var navigation = new StubNavigation2D();
            var rng = new RngHost(1);

            var assembly = new CarriersAssembly(bus, registry, rng, world, spatial, navigation);

            Assert.NotNull(assembly.Rules);
            Assert.NotNull(assembly.Units);
            Assert.NotNull(assembly.Inventory);
            Assert.NotNull(assembly.Equipment);
            Assert.NotNull(assembly.Creatures);
            Assert.NotNull(assembly.Summons);
            Assert.NotNull(assembly.GameObjects);
            Assert.NotNull(assembly.GameObjectInteractions);
            Assert.NotNull(assembly.Movement);
            Assert.NotNull(assembly.SkillBindings);
        }

        /// <summary>缺口 4 接线验收：<see cref="CarriersAssembly.SkillBindings"/> 的
        /// <see cref="Core.Carriers.Unit.KnownSkillQuery"/> 委托确实接的是
        /// <c>Rules.Skill.Knows</c>（不是测试假实现）——未学会时 Bind 被拒绝，
        /// <c>Rules.Skill.LearnSkill</c> 之后同一次 Bind 才成功。</summary>
        [Fact]
        public void SkillBindings_Bind_UsesRulesSkillKnows_ForKnownSkillValidation()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalRequiredTables(source);
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var assembly = new CarriersAssembly(bus, registry, rng, world, spatial);

            var unitId = new Id("unit.skill_binding_smoke");
            var skillId = new Id("skill.fireball");

            Assert.False(assembly.SkillBindings.Bind(unitId, "slot_0", skillId));

            assembly.Rules.Skill.LearnSkill(unitId, skillId);
            Assert.True(assembly.SkillBindings.Bind(unitId, "slot_0", skillId));
        }

        [Fact]
        public void Construct_Then_TickSeveralTimes_DoesNotThrow_WithNoUnitsRegistered()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalRequiredTables(source);
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            registry.LoadAll();

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            _ = new CarriersAssembly(bus, registry, rng, world, spatial);

            for (var i = 0; i < 5; i++)
            {
                world.Tick(SimStep.Continuous(0.1));
            }
        }
    }
}
