using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Common;
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
    /// <see cref="DataRegistry"/>（不提供任何行——<c>DataRegistry.LoadAll</c>（两个重载统称，具体见
    /// <see cref="DataRegistry.LoadAll()"/>/<see cref="DataRegistry.LoadAll(System.Collections.Generic.IReadOnlyList{IDataSource})"/>）
    /// 只加载数据源里实际存在的表，未提供数据的已注册 schema 就是零条记录，见该类型判断记录），验证：
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

        /// <summary>ADR-0063《装备宿主契约补模板 id 查询》验收：消费方反馈——游戏接入方第五批第 2
        /// 条——装备面板要显示"槽位名 + 已装备物品名"，需要经 <see cref="CarriersAssembly.Equipment"/>
        /// （生产装配入口，不是模块自己的测试夹具）拿到已装备物品的模板 id。<see
        /// cref="CarriersAssembly.Equipment"/> 的公开类型是具体类 <c>EquipmentHost</c>（不是接口），
        /// 按任务书"公开类型若是接口就走接口"的对偶情形——这里改为显式声明一个
        /// <see cref="IEquipmentHost"/> 局部变量承接（赋值是安全的向上转型，不是向下转型），全部断言
        /// 经该接口变量发起，验证真正命中的是 <c>EquipmentHost</c> 的显式接口实现而不是接口默认降级
        /// 值。</summary>
        [Fact]
        public void Equipment_GetEquippedTemplateId_ViaIEquipmentHostInterface_ReturnsTemplateId_AndClearsOnUnequip()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalRequiredTables(source);
            source.Add("item.slot_definition",
                "{\"table\": \"item.slot_definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.slot.adr0063_smoke\", \"name_key\": \"l10n.item.slot.adr0063_smoke\"}" +
                "]}");
            source.Add("item.quality_definition",
                "{\"table\": \"item.quality_definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.quality.adr0063_common\", \"name_key\": \"l10n.item.quality.adr0063_common\"}" +
                "]}");
            source.Add("item.template",
                "{\"table\": \"item.template\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.adr0063_smoke_shirt\", \"slot\": \"item.slot.adr0063_smoke\", " +
                "\"quality\": \"item.quality.adr0063_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.adr0063_smoke\", \"stack_size\": 1, " +
                "\"name_key\": \"l10n.item.adr0063_smoke\"}" +
                "]}");

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var navigation = new StubNavigation2D();
            var rng = new RngHost(1);
            var assembly = new CarriersAssembly(bus, registry, rng, world, spatial, navigation);

            var unitId = new Id("unit.adr0063_smoke_player");
            var slot = new Id("item.slot.adr0063_smoke");
            var templateId = new Id("item.adr0063_smoke_shirt");

            // Equip/Unequip 联动经 IStatHost.RemoveModifiersBySource 要求单位先在 StatHost 注册
            // （即便本例物品模板不带 stats/armor，Unequip 仍无条件调用 RevertGrants → RemoveModifiersBySource）；
            // 本测试只关心装备契约新成员，不需要完整的 RulesAssembly.RegisterUnit（那还要求
            // arch.class 记录），直接调用 StatHost.RegisterUnit 即可满足前置条件。
            assembly.Rules.Stats.RegisterUnit(unitId);

            assembly.Inventory.AddItem(unitId, templateId, 1);
            var items = assembly.Inventory.ListItems(unitId);
            var instanceId = items[items.Count - 1].InstanceId;

            var equipResult = assembly.Equipment.Equip(unitId, instanceId, slot);
            Assert.True(equipResult.Success, $"装备应当成功：{equipResult.Reason}");

            IEquipmentHost host = assembly.Equipment;

            Assert.Equal(templateId, host.GetEquippedTemplateId(unitId, slot));
            Assert.Equal(instanceId, host.GetEquipped(unitId, slot)!.Value.InstanceId);
            Assert.Equal(templateId, host.GetAllEquippedIdentities(unitId)[slot].TemplateId);
            Assert.Equal(instanceId, host.GetAllEquippedIdentities(unitId)[slot].InstanceId);

            host.Unequip(unitId, slot);

            Assert.Null(host.GetEquippedTemplateId(unitId, slot));
            Assert.Null(host.GetEquipped(unitId, slot));
            Assert.Empty(host.GetAllEquippedIdentities(unitId));
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
