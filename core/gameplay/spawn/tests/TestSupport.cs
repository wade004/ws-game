using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Spawn;
using Core.Rules.Common;

namespace Tests.Gameplay.Spawn
{
    /// <summary>JSON 树构造帮助方法（惯例同 <c>core/gameplay/area_trigger/tests</c> 的同名 <c>J</c>
    /// 类；测试数据一律用 <c>spawn.sample_*</c>/<c>creature.sample_*</c>/<c>gobj.sample_*</c> 命名）。</summary>
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
    }

    /// <summary>最小 <see cref="IExprHost"/> 假实现（惯例同
    /// <c>core/gameplay/area_trigger/tests</c> 的同名类型）。</summary>
    internal sealed class FakeExprHost : IExprHost
    {
        private readonly Dictionary<string, ExprValue> _values = new Dictionary<string, ExprValue>();

        public FakeExprHost Set(string group, string key, ExprValue value)
        {
            _values[group + "#" + key] = value;
            return this;
        }

        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) =>
            _values.TryGetValue(group + "#" + key, out var v) ? v : ExprValue.OfBool(false);
    }

    internal sealed class FakeExprHostFactory : IExprHostFactory
    {
        public FakeExprHost Host { get; } = new FakeExprHost();

        public List<Id> SelfIds { get; } = new List<Id>();

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent)
        {
            SelfIds.Add(selfId);
            return Host;
        }
    }

    /// <summary><see cref="ICreatureFactory"/> 的最小测试假实现：按调用顺序生成确定性实体 id，
    /// <see cref="Despawn"/> 按 07/05 契约文档发出 <c>creature.despawned</c>（供
    /// <c>SpawnHost</c> 的自动订阅测试使用）。</summary>
    internal sealed class FakeCreatureFactory : ICreatureFactory
    {
        private readonly IEventBus _bus;
        private int _next = 1;

        public List<(Id TemplateId, Id MapId, Vec2 Position, double Facing)> SpawnCalls { get; } =
            new List<(Id, Id, Vec2, double)>();

        public FakeCreatureFactory(IEventBus bus)
        {
            _bus = bus;
        }

        public Id Spawn(Id templateId, Id mapId, Vec2 position, double facing, Id? ownerId = null)
        {
            SpawnCalls.Add((templateId, mapId, position, facing));
            var entityId = new Id($"creature.inst_{_next++}");
            _bus.Enqueue(new CreatureSpawnedEvent(entityId, templateId));
            return entityId;
        }

        public void Despawn(Id entityId, string reason)
        {
            _bus.PublishImmediate(new CreatureDespawnedEvent(entityId, reason));
        }
    }

    /// <summary><see cref="ICreatureTemplateQuery"/> 的最小测试假实现：只支持
    /// <see cref="HasFlag"/>（<see cref="SpawnSummonOnlyCreatureRule"/> 唯一用到的成员），
    /// <see cref="Get"/> 未实现（本模块测试不需要）。</summary>
    internal sealed class FakeCreatureTemplateQuery : ICreatureTemplateQuery
    {
        private readonly Dictionary<Id, HashSet<NpcFlag>> _flags = new Dictionary<Id, HashSet<NpcFlag>>();

        public FakeCreatureTemplateQuery WithFlags(Id templateId, params NpcFlag[] flags)
        {
            _flags[templateId] = new HashSet<NpcFlag>(flags);
            return this;
        }

        public CreatureTemplate Get(Id templateId) => throw new NotSupportedException("测试假实现未实现 Get");

        public bool HasFlag(Id templateId, NpcFlag flag)
        {
            if (!_flags.TryGetValue(templateId, out var set))
            {
                throw new ArgumentException($"未登记的模板：\"{templateId}\"");
            }

            return set.Contains(flag);
        }
    }

    internal static class SpawnTestSupport
    {
        public static IEventBus NewEventBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        public static IDataRegistry BuildRegistry(IEventBus bus, params JsonObject[] rows)
        {
            var source = new InMemoryDataSource();
            var root = J.O(
                ("table", J.S(SpawnSchemas.Table.Name)),
                ("schema_version", J.N(1)),
                ("rows", new JsonArray(rows.Cast<JsonValue>())));
            source.Add(SpawnSchemas.Table.Name, JsonWriter.Write(root));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(SpawnSchemas.Table);
            return registry;
        }

        public static JsonObject Row(
            string id, string mapId, string contentRef, string policy,
            double x = 0, double y = 0, double facing = 0, double? respawnTimer = null, string? condition = null) =>
            J.O(
                ("id", J.S(id)),
                ("map_id", J.S(mapId)),
                ("content_ref", J.S(contentRef)),
                ("position", J.O(("x", J.N(x)), ("y", J.N(y)))),
                ("facing", J.N(facing)),
                ("respawn_policy", J.S(policy)),
                ("respawn_timer", respawnTimer.HasValue ? J.N(respawnTimer.Value) : JsonNull.Instance),
                ("condition", condition == null ? JsonNull.Instance : J.S(condition)));
    }
}
