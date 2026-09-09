using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Tests.Carriers.Gobj
{
    /// <summary>JSON 树构造帮助方法（供测试直接手搭 <c>gobj.*</c>/<c>stat.definition</c> 表行，惯例同
    /// <c>core/rules/skill/tests</c> 的同名 <c>J</c> 类；测试数据一律用 <c>gobj.sample_*</c> 命名，
    /// 不出现任何具体游戏代号）。</summary>
    internal static class J
    {
        public static JsonValue S(string s) => new JsonString(s);

        public static JsonValue N(double n) => new JsonNumber(n);

        public static JsonValue B(bool b) => JsonBool.Of(b);

        public static JsonObject O(params (string Key, JsonValue Value)[] fields)
        {
            var builder = new JsonObjectBuilder();
            foreach (var (key, value) in fields)
            {
                builder.Add(key, value);
            }

            return builder.Build();
        }

        public static JsonArray A(params JsonValue[] items) => new JsonArray(items);

        public static JsonValue Ids(params string[] ids) => new JsonArray(ids.Select(id => (JsonValue)new JsonString(id)));
    }

    /// <summary><see cref="Core.Carriers.Common.IWorldFlags"/> 的最小测试假实现：按 flagKey 存取一个
    /// <see cref="ExprValue"/>（惯例同 <c>core/carriers/common</c> README"由 L4……或游戏组装根实现"，
    /// 测试用内存字典代替真实 <c>core/gameplay/world_state</c> 实现）。</summary>
    internal sealed class FakeWorldFlags : IWorldFlags
    {
        private readonly Dictionary<Id, ExprValue> _values = new Dictionary<Id, ExprValue>();

        public ExprValue? Get(Id flagKey) => _values.TryGetValue(flagKey, out var v) ? v : (ExprValue?)null;

        public void Set(Id flagKey, ExprValue value, Id writerId) => _values[flagKey] = value;

        public bool Has(Id flagKey) => _values.ContainsKey(flagKey);
    }

    /// <summary><see cref="IUnitAccess"/> 的最小测试假实现（惯例同 <c>core/rules/*/tests</c> 各模块
    /// 自己的 <c>FakeUnitAccess</c>）：只覆盖本模块测试实际用到的行为。</summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private sealed class Rec
        {
            public Vec2 Position;
            public bool Alive = true;
            public Id Faction;
            public int Level = 1;
        }

        private readonly Dictionary<Id, Rec> _units = new Dictionary<Id, Rec>();

        public FakeUnitAccess Add(Id id, Vec2 position)
        {
            _units[id] = new Rec { Position = position, Faction = new Id("faction.sample") };
            return this;
        }

        public bool Exists(Id unitId) => _units.ContainsKey(unitId);

        public IReadOnlyList<Id> AllUnits => _units.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).ToList();

        public Vec2 GetPosition(Id unitId) => Require(unitId).Position;

        public void SetPosition(Id unitId, Vec2 position) => Require(unitId).Position = position;

        public Id GetFaction(Id unitId) => Require(unitId).Faction;

        public int GetLevel(Id unitId) => Require(unitId).Level;

        public double GetFacing(Id unitId) => 0;

        public bool IsAlive(Id unitId) => Require(unitId).Alive;

        public void SetAlive(Id unitId, bool alive) => Require(unitId).Alive = alive;

        public Id? GetTemplateId(Id unitId) => null;

        public IReadOnlyList<Id> GetTags(Id unitId) => Array.Empty<Id>();

        private Rec Require(Id unitId) =>
            _units.TryGetValue(unitId, out var rec) ? rec : throw new InvalidOperationException($"FakeUnitAccess: 单位 \"{unitId}\" 不存在");
    }

    /// <summary><see cref="IInventoryHost"/> 的最小测试假实现：按单位持有一组 <see cref="ItemInstance"/>，
    /// 实例 id 用一个全局自增计数器拼出 <c>"item.inst_&lt;n&gt;"</c>（惯例同
    /// <c>IWorldSim.AllocateEntityId</c> 的确定性 id 格式，但本假实现不依赖 <see cref="IWorldSim"/>）。</summary>
    internal sealed class FakeInventoryHost : IInventoryHost
    {
        private readonly Dictionary<Id, List<ItemInstance>> _items = new Dictionary<Id, List<ItemInstance>>();
        private int _nextInstance = 1;

        public bool AddItem(Id unitId, Id templateId, int count)
        {
            if (!_items.TryGetValue(unitId, out var list))
            {
                list = new List<ItemInstance>();
                _items[unitId] = list;
            }

            var instanceId = new Id($"item.inst_{_nextInstance++}");
            list.Add(new ItemInstance(instanceId, templateId, count));
            return true;
        }

        public bool RemoveItem(Id unitId, Id instanceId, int count)
        {
            if (!_items.TryGetValue(unitId, out var list))
            {
                return false;
            }

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].InstanceId.Equals(instanceId))
                {
                    var remaining = list[i].Count - count;
                    if (remaining > 0)
                    {
                        list[i] = new ItemInstance(instanceId, list[i].TemplateId, remaining);
                    }
                    else
                    {
                        list.RemoveAt(i);
                    }

                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<ItemInstance> ListItems(Id unitId) =>
            _items.TryGetValue(unitId, out var list) ? list : (IReadOnlyList<ItemInstance>)Array.Empty<ItemInstance>();

        public int CountOf(Id unitId, Id templateId)
        {
            if (!_items.TryGetValue(unitId, out var list))
            {
                return 0;
            }

            var total = 0;
            foreach (var item in list)
            {
                if (item.TemplateId.Equals(templateId))
                {
                    total += item.Count;
                }
            }

            return total;
        }

        public ItemInstance? FindInstance(Id unitId, Id instanceId)
        {
            if (!_items.TryGetValue(unitId, out var list))
            {
                return null;
            }

            foreach (var item in list)
            {
                if (item.InstanceId.Equals(instanceId))
                {
                    return item;
                }
            }

            return null;
        }
    }

    /// <summary><see cref="ILootRoller"/> 的最小测试假实现：按 <c>lootTableId</c> 登记一组固定的
    /// <see cref="ItemStack"/>，<see cref="Roll"/> 原样返回（不做随机化，测试不需要）。</summary>
    internal sealed class FakeLootRoller : ILootRoller
    {
        private readonly Dictionary<Id, List<ItemStack>> _tables = new Dictionary<Id, List<ItemStack>>();
        public readonly List<(Id LootTableId, Id SourceUnitId, Id? KillerId)> Calls = new List<(Id, Id, Id?)>();

        public FakeLootRoller Table(Id lootTableId, params ItemStack[] stacks)
        {
            _tables[lootTableId] = new List<ItemStack>(stacks);
            return this;
        }

        public IReadOnlyList<ItemStack> Roll(Id lootTableId, Id sourceUnitId, Id? killerId)
        {
            Calls.Add((lootTableId, sourceUnitId, killerId));
            return _tables.TryGetValue(lootTableId, out var stacks) ? stacks : (IReadOnlyList<ItemStack>)Array.Empty<ItemStack>();
        }
    }

    /// <summary><see cref="ISkillHost"/> 的最小测试假实现：只记录 <see cref="CastSkill"/> 调用
    /// （<c>on_use: skill</c>/<c>trap</c> 两处测试用到），其余成员按"够用即可"返回固定值，不代表
    /// <c>core/rules/skill</c> 正式实现的完整语义（惯例同 <c>core/carriers/unit/tests</c> 的
    /// <c>FakeStatHost</c>）。</summary>
    internal sealed class FakeSkillHost : ISkillHost
    {
        public readonly List<(Id CasterId, Id SkillId, IReadOnlyList<Id> Targets)> CastCalls =
            new List<(Id, Id, IReadOnlyList<Id>)>();

        private int _nextCastInstance = 1;

        public Vec2 GetPosition(Id unitId) => Vec2.Zero;

        public IReadOnlyList<Id> FindUnits(Shape shape, Vec2 origin, UnitFilter filter) => Array.Empty<Id>();

        public void ApplyStatMod(Id sourceId, Id unitId, Id stat, StatModifierOp op, double value)
        {
        }

        public CastResult CastSkill(Id casterId, Id skillId, IReadOnlyList<Id> targets)
        {
            CastCalls.Add((casterId, skillId, targets));
            return CastResult.Ok(new Id($"skill.cast_{_nextCastInstance++}"));
        }

        public double GetCooldown(Id unitId, Id skillId) => 0;

        public bool IsCasting(Id unitId) => false;

        public void Interrupt(Id unitId, Id interrupterId, Id? lockSchool, double lockDuration)
        {
        }
    }

    /// <summary>已构建好的一整套测试宿主：真实 <see cref="WorldSim"/>/<see cref="EventBus"/>/
    /// <see cref="StatHost"/>/<see cref="DataRegistry"/>（经 <see cref="InMemoryDataSource"/>）+ 假
    /// 实现 <see cref="FakeWorldFlags"/>/<see cref="FakeUnitAccess"/>/<see cref="FakeInventoryHost"/>/
    /// <see cref="FakeSkillHost"/>/<see cref="FakeLootRoller"/>。</summary>
    internal sealed class GobjWorld
    {
        public IDataRegistry Registry = default!;
        public IEventBus Bus = default!;
        public IWorldSim World = default!;
        public FakeWorldFlags Flags = default!;
        public FakeUnitAccess Units = default!;
        public FakeInventoryHost Inventory = default!;
        public IStatHost Stats = default!;
        public FakeSkillHost Skills = default!;
        public FakeLootRoller Loot = default!;
        public GobjOptions Options = default!;
        public GameObjectFactory Factory = default!;
        public GameObjectHost Host = default!;
        public InMemoryGobjDiagnostics Diagnostics = default!;

        /// <summary>可变的模拟时间盒，供测试驱动 <see cref="GobjOptions.SimTime"/>（见
        /// <c>GameObjectHostTests</c> gather_node 刷新场景）。</summary>
        public double SimTimeBox;

        public readonly List<IEvent> Events = new List<IEvent>();

        public void Flush() => Bus.DispatchPending();

        public IEnumerable<T> Of<T>() where T : IEvent => Events.OfType<T>();

        /// <summary>登记一个测试单位：同时接入 <see cref="Units"/>（假实现）与真实
        /// <see cref="Stats"/>（<see cref="IStatHost.RegisterUnit"/>——<c>skill_check</c> 锁判定要经
        /// <see cref="IStatHost.GetStat"/>，未注册的单位会抛异常，见 <c>StatHost.RequireUnit</c>）。</summary>
        public void AddUnit(Id id, Vec2 position)
        {
            Units.Add(id, position);
            Stats.RegisterUnit(id);
        }

        /// <summary>按 <c>gobj.template</c> 记录生成一个实体，<c>lockId</c> 取模板声明的
        /// <see cref="GameObjectTemplate.LockId"/>（测试便捷封装：<see cref="GameObjectFactory.Spawn"/>
        /// 本身不读模板字段，见该类型顶部判断记录，正式调用方——如场景加载/刷新表——同样需要自己先
        /// 解析模板再决定传给 <c>Spawn</c> 的 <c>lockId</c>）。</summary>
        public Id SpawnFromTemplate(Id templateId, Id mapId, Vec2 position, double facing = 0)
        {
            var record = Registry.Get(GobjSchemas.Template.Name, templateId)
                ?? throw new InvalidOperationException($"gobj.template \"{templateId}\" 不存在");
            var template = GameObjectTemplate.FromRecord(record);
            return Factory.Spawn(templateId, mapId, position, facing, template.LockId);
        }
    }

    internal sealed class GobjWorldBuilder
    {
        private readonly InMemoryDataSource _source = new InMemoryDataSource();
        private readonly List<JsonObject> _templates = new List<JsonObject>();
        private readonly List<JsonObject> _locks = new List<JsonObject>();
        private readonly List<JsonObject> _statDefs = new List<JsonObject>();
        private readonly List<JsonObject> _itemDefs = new List<JsonObject>();
        private readonly List<JsonObject> _itemSlotDefs = new List<JsonObject>();
        private readonly List<JsonObject> _itemQualityDefs = new List<JsonObject>();
        private readonly List<JsonObject> _skillDefs = new List<JsonObject>();
        private readonly List<IValidationRule> _extraRules = new List<IValidationRule>();

        public GobjOptions Options { get; } = new GobjOptions();

        public GobjWorldBuilder Template(JsonObject row) { _templates.Add(row); return this; }

        public GobjWorldBuilder Lock(JsonObject row) { _locks.Add(row); return this; }

        public GobjWorldBuilder Stat(string id, double defaultBase = 0)
        {
            _statDefs.Add(J.O(
                ("id", J.S(id)),
                ("name_key", J.S("l10n.stat." + LastSegment(id) + ".name")),
                ("group", J.S("primary")),
                ("default_base", J.N(defaultBase))));
            return this;
        }

        public GobjWorldBuilder ValidationRule(IValidationRule rule) { _extraRules.Add(rule); return this; }

        /// <summary>ADR-0019 F1c：登记一条最小合法 <c>item.template</c>（含其 <c>slot</c>/
        /// <c>quality</c> 依赖），供 <c>gobj.lock.requirement.item_key.item_id</c> 现登记为
        /// <c>Reference(item.template)</c> 后满足引用完整性——本模块测试只关心 <c>item_id</c> 是否
        /// 原样传给 <c>IInventoryHost</c>，不依赖 <c>core/carriers/item</c> 的真实解析行为。</summary>
        public GobjWorldBuilder Item(string id)
        {
            var slot = "item.slot.gobj_cov_" + LastSegment(id);
            var quality = "item.quality.gobj_cov";
            _itemSlotDefs.Add(J.O(("id", J.S(slot)), ("name_key", J.S("l10n." + slot.Replace('.', '_')))));
            if (_itemQualityDefs.Count == 0)
            {
                _itemQualityDefs.Add(J.O(("id", J.S(quality)), ("name_key", J.S("l10n." + quality.Replace('.', '_')))));
            }
            _itemDefs.Add(J.O(
                ("id", J.S(id)), ("slot", J.S(slot)), ("quality", J.S(quality)), ("item_level", J.N(1)),
                ("display_ref", J.S("display." + LastSegment(id))), ("stack_size", J.N(1)),
                ("name_key", J.S("l10n." + id.Replace('.', '_')))));
            return this;
        }

        /// <summary>ADR-0019 F1c：登记一条最小合法 <c>skill.def</c>，供 <c>type_data.skill_id</c>
        /// （<c>trap</c>）/<c>on_use.ref</c>（<c>skill</c>）现登记为 <c>Reference(skill.def)</c>
        /// 后满足引用完整性。</summary>
        public GobjWorldBuilder Skill(string id)
        {
            _skillDefs.Add(J.O(
                ("id", J.S(id)), ("school", J.S("skill.school.gobj_cov")), ("kind", J.S("active")),
                ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(true)),
                ("target_shape_ref", J.S("target.gobj_cov")), ("effects", J.A())));
            return this;
        }

        /// <summary>只跑到"注册 schema/校验规则 + LoadAll"这一步，返回校验报告，不构造
        /// <see cref="GameObjectHost"/> 及其余真实宿主（惯例同 <c>core/rules/skill/tests</c> 的
        /// <c>SkillWorldBuilder.Validate</c>）。</summary>
        public ValidationReport Validate()
        {
            var (_, registry) = BuildRegistry();
            return registry.LoadAll();
        }

        public GobjWorld Build()
        {
            var (bus, registry) = BuildRegistry();
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "测试数据未通过校验：\n" + string.Join("\n", report.Issues.Select(i => i.ToString())));
            }

            var world = new WorldSim(bus);
            var flags = new FakeWorldFlags();
            var units = new FakeUnitAccess();
            var inventory = new FakeInventoryHost();
            var stats = new StatHost(registry, bus);
            var skills = new FakeSkillHost();
            var loot = new FakeLootRoller();
            var diagnostics = new InMemoryGobjDiagnostics();
            var factory = new GameObjectFactory(world);

            var gobjWorld = new GobjWorld
            {
                Registry = registry,
                Bus = bus,
                World = world,
                Flags = flags,
                Units = units,
                Inventory = inventory,
                Stats = stats,
                Skills = skills,
                Loot = loot,
                Options = Options,
                Factory = factory,
                Diagnostics = diagnostics,
            };

            Options.SimTime = () => gobjWorld.SimTimeBox;

            gobjWorld.Host = new GameObjectHost(
                registry, world, bus, flags, units, inventory, stats, skills, loot, Options, diagnostics);

            SubscribeAll(bus, gobjWorld.Events);
            return gobjWorld;
        }

        private (IEventBus Bus, IDataRegistry Registry) BuildRegistry()
        {
            _source.Add("gobj.template", TableJson("gobj.template", _templates));
            _source.Add("gobj.lock", TableJson("gobj.lock", _locks));
            _source.Add("stat.definition", TableJson("stat.definition", _statDefs));
            // ADR-0019 F1c：gobj.lock.requirement.item_key.item_id / type_data.skill_id /
            // on_use.ref（skill 分支）现登记为 Reference(item.template)/Reference(skill.def)，
            // 无条件加载这三张表（即使为空）满足引用完整性，见 Item()/Skill() 判断记录。
            _source.Add("item.template", TableJson("item.template", _itemDefs));
            _source.Add("item.slot_definition", TableJson("item.slot_definition", _itemSlotDefs));
            _source.Add("item.quality_definition", TableJson("item.quality_definition", _itemQualityDefs));
            _source.Add("skill.def", TableJson("skill.def", _skillDefs));

            var bus = CreateBus();
            var registry = new DataRegistry(_source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(GobjSchemas.Template);
            registry.RegisterSchema(GobjSchemas.Lock);
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Template);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.SlotDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.QualityDefinition);
            registry.RegisterSchema(Core.Rules.Skill.SkillSchemas.Def);

            foreach (var rule in _extraRules)
            {
                registry.RegisterValidationRule(rule);
            }

            return (bus, registry);
        }

        private static string TableJson(string name, IReadOnlyList<JsonObject> rows)
        {
            var root = J.O(
                ("table", J.S(name)),
                ("schema_version", J.N(1)),
                ("rows", new JsonArray(rows.Cast<JsonValue>())));
            return JsonWriter.Write(root);
        }

        private static string LastSegment(string id)
        {
            var idx = id.LastIndexOf('.');
            return idx >= 0 ? id.Substring(idx + 1) : id;
        }

        internal static IEventBus CreateBus()
        {
            var definitions = new[]
            {
                new EventDefinition(CarriersEventKeys.GobjInteracted, "gobj", new[] { "unitId", "gobjInstanceId" }),
                new EventDefinition(CarriersEventKeys.GobjStateChanged, "gobj", new[] { "gobjInstanceId", "stateKey", "oldValue", "newValue" }),
                // GameObjectFactory.Spawn/Despawn 经 IWorldSim.AddEntity/MarkForDestruction 间接发出
                // entity.created/entity.destroyed（见 03 第 5 节），EventBus 默认 StrictCatalog=true，
                // 未登记会在 Enqueue 时抛异常，本模块测试同样需要登记这两个 sim_loop 通用事件。
                new EventDefinition(SimEventKeys.EntityCreated, "entity", new[] { "entityId", "kind", "displayId" }),
                new EventDefinition(SimEventKeys.EntityDestroyed, "entity", new[] { "entityId" }),
            };

            var catalog = EventCatalog.FromDefinitions(definitions);
            // 惯例同 core/rules/skill/tests、core/carriers/unit/tests 的 CreateBus：非严格模式——
            // DataRegistry.LoadAll/WorldSim.AddEntity 等间接发出的通用事件（entity.created、
            // found.data_load_completed 等）未必都在下面登记，未登记时只记警告、不阻断测试。
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }

        private static void SubscribeAll(IEventBus bus, List<IEvent> sink)
        {
            bus.Subscribe(CarriersEventKeys.GobjInteracted, evt => sink.Add(evt));
            bus.Subscribe(CarriersEventKeys.GobjStateChanged, evt => sink.Add(evt));
        }
    }
}
