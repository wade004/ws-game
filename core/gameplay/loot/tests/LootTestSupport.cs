using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Gameplay.Loot;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>供本模块测试共用的最小装配帮助（惯例同
    /// <c>core/carriers/creature/tests/CreatureTestSupport.cs</c>）。</summary>
    internal static class LootTestSupport
    {
        public static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        public static IEventBus NewEventBus() =>
            new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        /// <summary>装配一个已加载 <c>loot.table</c> 的 <see cref="DataRegistry"/>（可选登记
        /// <see cref="LootContentValidationRule"/>，成环测试需要关闭默认断言以便自行检查报告）。</summary>
        public static DataRegistry MakeRegistry(IEventBus bus, string lootTableRowsJson, bool registerValidationRule = true)
        {
            var source = new InMemoryDataSource()
                .Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, lootTableRowsJson));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            if (registerValidationRule)
            {
                registry.RegisterValidationRule(new LootContentValidationRule());
            }

            var report = registry.LoadAll();
            if (registerValidationRule)
            {
                Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            }

            return registry;
        }

        public static IWorldSim NewWorld(IEventBus bus) => new WorldSim(bus);

        public static PlayerUnit AddPlayer(IWorldSim world, Id entityId, Id mapId, Vec2 position)
        {
            var unit = new PlayerUnit(entityId, mapId, new Id("fac.test_player"), new Id("archetype.test"))
            {
                Position = position,
            };
            world.AddEntity(unit);
            return unit;
        }

        public static CreatureUnit AddCreature(IWorldSim world, Id entityId, Id mapId, Id templateId, Vec2 position)
        {
            var unit = new CreatureUnit(entityId, mapId, new Id("fac.test_monster"), templateId)
            {
                Position = position,
            };
            world.AddEntity(unit);
            return unit;
        }

        /// <summary>按 <see cref="CreatureTemplate.FromRecord"/> 构造一条最小合法模板（供
        /// <see cref="FakeCreatureTemplateQuery"/> 使用），不需要真正加载 <c>creature.tier_definition</c>
        /// 表——<c>FromRecord</c> 本身只解析字段，不做引用完整性检查（那是
        /// <c>IDataRegistry.LoadAll</c> 校验阶段的职责）。</summary>
        public static CreatureTemplate MakeCreatureTemplate(Id templateId, Id? lootTableRef)
        {
            var json = "{\"id\": \"" + templateId.Value + "\", \"name_key\": \"l10n.creature.sample.name\", " +
                "\"level\": 1, \"tier\": \"creature.tier.normal\", \"base_stats\": {}, " +
                "\"faction_id\": \"fac.test_monster\", \"display_ref\": \"display.sample\"" +
                (lootTableRef.HasValue ? ", \"loot_table_ref\": \"" + lootTableRef.Value.Value + "\"" : string.Empty) +
                "}";
            var obj = (JsonObject)JsonReader.Parse(json);
            var record = new DataRecord(CreatureSchemas.Template, templateId.Value, templateId, obj);
            return CreatureTemplate.FromRecord(record);
        }
    }

    /// <summary>最小 <see cref="ICreatureTemplateQuery"/> 假实现：只支持 <see cref="Add"/> 过的模板。</summary>
    internal sealed class FakeCreatureTemplateQuery : ICreatureTemplateQuery
    {
        private readonly Dictionary<Id, CreatureTemplate> _templates = new Dictionary<Id, CreatureTemplate>();

        public void Add(Id templateId, Id? lootTableRef) =>
            _templates[templateId] = LootTestSupport.MakeCreatureTemplate(templateId, lootTableRef);

        public CreatureTemplate Get(Id templateId) =>
            _templates.TryGetValue(templateId, out var t) ? t : throw new ArgumentException($"未登记的模板 \"{templateId}\"");

        public bool HasFlag(Id templateId, NpcFlag flag) => false;
    }

    /// <summary>最小 <see cref="IInventoryHost"/> 假实现：按 <see cref="MaxTotalItems"/>（null=不限）
    /// 限制每个单位背包的物品总数量，超出部分按"能放多少放多少"处理（真正的 Reject/Partial 整体语义
    /// 由 <c>LootHost</c> 自己在这之上实现，见 loot README 判断记录 7）。</summary>
    internal sealed class FakeInventoryHost : IInventoryHost
    {
        public int? MaxTotalItems { get; set; }

        private readonly Dictionary<Id, List<ItemInstance>> _bags = new Dictionary<Id, List<ItemInstance>>();
        private long _seq = 1;

        public bool AddItem(Id unitId, Id templateId, int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

            var bag = Bag(unitId);
            var total = TotalCount(unitId);
            var room = MaxTotalItems.HasValue ? Math.Max(0, MaxTotalItems.Value - total) : count;
            var toAdd = Math.Min(count, room);
            if (toAdd <= 0)
            {
                return false;
            }

            bag.Add(new ItemInstance(new Id($"item.inst_{_seq++}"), templateId, toAdd));
            return true;
        }

        public bool RemoveItem(Id unitId, Id instanceId, int count)
        {
            var bag = Bag(unitId);
            for (var i = 0; i < bag.Count; i++)
            {
                if (!bag[i].InstanceId.Equals(instanceId))
                {
                    continue;
                }

                if (count > bag[i].Count)
                {
                    return false;
                }

                if (count == bag[i].Count)
                {
                    bag.RemoveAt(i);
                }
                else
                {
                    bag[i] = new ItemInstance(bag[i].InstanceId, bag[i].TemplateId, bag[i].Count - count);
                }

                return true;
            }

            return false;
        }

        public IReadOnlyList<ItemInstance> ListItems(Id unitId) => Bag(unitId).ToList();

        public int CountOf(Id unitId, Id templateId) => TotalCount(unitId, templateId);

        public ItemInstance? FindInstance(Id unitId, Id instanceId)
        {
            foreach (var instance in Bag(unitId))
            {
                if (instance.InstanceId.Equals(instanceId))
                {
                    return instance;
                }
            }

            return null;
        }

        private int TotalCount(Id unitId, Id? templateId = null)
        {
            var total = 0;
            foreach (var instance in Bag(unitId))
            {
                if (!templateId.HasValue || instance.TemplateId.Equals(templateId.Value))
                {
                    total += instance.Count;
                }
            }

            return total;
        }

        private List<ItemInstance> Bag(Id unitId)
        {
            if (!_bags.TryGetValue(unitId, out var bag))
            {
                bag = new List<ItemInstance>();
                _bags[unitId] = bag;
            }

            return bag;
        }
    }

    /// <summary>最小 <see cref="IExprHostFactory"/> 假实现：只支持 <c>self.is_alive</c>（Bool，
    /// RulesExprSchema 精确登记的既有键，见 <c>core/rules/expr_host.RulesExprSchema.Build</c>），
    /// 按 <see cref="AliveFlags"/> 以 <c>selfId</c>（<c>context.KillerId ?? context.SourceUnitId</c>）
    /// 为 key 返回配置值；其余分组/键一律 <c>Bool(false)</c>。供 <c>LootEntry.Condition</c> 一类测试
    /// 用例做"条件为真/假"的开关。</summary>
    internal sealed class FakeExprHostFactory : IExprHostFactory
    {
        public readonly Dictionary<Id, bool> AliveFlags = new Dictionary<Id, bool>();

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new Host(this, selfId);

        private sealed class Host : IExprHost
        {
            private readonly FakeExprHostFactory _f;
            private readonly Id _self;

            public Host(FakeExprHostFactory f, Id self)
            {
                _f = f;
                _self = self;
            }

            public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
            {
                if (group == ExprGroups.Self && key == "is_alive")
                {
                    return ExprValue.OfBool(_f.AliveFlags.TryGetValue(_self, out var v) && v);
                }

                return ExprValue.OfBool(false);
            }
        }
    }
}
