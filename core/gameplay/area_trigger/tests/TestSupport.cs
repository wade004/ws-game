using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SceneRouter;
using Core.Gameplay.AreaTrigger;
using Core.Rules.Common;

namespace Tests.Gameplay.AreaTrigger
{
    /// <summary>JSON 树构造帮助方法（惯例同 <c>core/carriers/gobj/tests</c> 的同名 <c>J</c> 类；测试
    /// 数据一律用 <c>area.sample_*</c> 命名，不出现任何具体游戏代号）。</summary>
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

    /// <summary>最小 <see cref="IExprHost"/> 假实现：按 "group#key" 存取一个固定 <see cref="ExprValue"/>，
    /// 未配置的 key 按 04 第 6.3 节"缺失 -&gt; 默认值"惯例返回 <see cref="ExprValue.OfBool(bool)"/>
    /// <c>false</c>。</summary>
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

    /// <summary><see cref="IExprHostFactory"/> 的最小测试假实现：全部调用共用同一个
    /// <see cref="Host"/>，只记录调用参数供断言。</summary>
    internal sealed class FakeExprHostFactory : IExprHostFactory
    {
        public FakeExprHost Host { get; } = new FakeExprHost();

        public List<(Id SelfId, Id? TargetId)> Calls { get; } = new List<(Id, Id?)>();

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent)
        {
            Calls.Add((selfId, targetId));
            return Host;
        }
    }

    /// <summary><see cref="IUnitAccess"/> 的最小测试假实现（惯例同
    /// <c>core/carriers/gobj/tests</c> 的 <c>FakeUnitAccess</c>）。</summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private readonly Dictionary<Id, Vec2> _positions = new Dictionary<Id, Vec2>();

        public FakeUnitAccess Add(Id id, Vec2 position)
        {
            _positions[id] = position;
            return this;
        }

        public void Move(Id id, Vec2 position) => _positions[id] = position;

        public bool Exists(Id unitId) => _positions.ContainsKey(unitId);

        public IReadOnlyList<Id> AllUnits => _positions.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).ToList();

        public Vec2 GetPosition(Id unitId) => _positions[unitId];

        public void SetPosition(Id unitId, Vec2 position) => _positions[unitId] = position;

        public Id GetFaction(Id unitId) => new Id("faction.sample");

        public int GetLevel(Id unitId) => 1;

        public double GetFacing(Id unitId) => 0;

        public bool IsAlive(Id unitId) => true;

        public void SetAlive(Id unitId, bool alive)
        {
        }

        public Id? GetTemplateId(Id unitId) => null;

        public IReadOnlyList<Id> GetTags(Id unitId) => Array.Empty<Id>();
    }

    /// <summary><see cref="ISceneRouter"/> 的最小测试假实现：只记录 <see cref="LoadScene"/> 调用，
    /// 其余成员按"够用即可"返回固定值（惯例同 <c>core/carriers/gobj/tests</c> 的 <c>FakeSkillHost</c>）。</summary>
    internal sealed class FakeSceneRouter : ISceneRouter
    {
        public List<Id> LoadSceneCalls { get; } = new List<Id>();

        public Exception? ThrowOnLoad { get; set; }

        public void LoadScene(Id sceneId)
        {
            if (ThrowOnLoad != null)
            {
                throw ThrowOnLoad;
            }

            LoadSceneCalls.Add(sceneId);
        }

        public Id? GetCurrentScene() => null;

        public SubscriptionHandle RegisterPreUnloadHook(SceneHookCallback callback) => new SubscriptionHandle(() => { });

        public SubscriptionHandle RegisterPostLoadHook(SceneHookCallback callback) => new SubscriptionHandle(() => { });

        public void Update()
        {
        }

        public double LoadProgress => 1.0;

        public SceneRouterState State => SceneRouterState.Idle;
    }

    /// <summary>本模块测试的公共设施：构造一个非严格模式（<see cref="EventBusOptions.StrictCatalog"/>
    /// = false）的 <see cref="IEventBus"/>（惯例同 <c>core/carriers/gobj/tests</c>），以及一份最小的
    /// <c>area.trigger_def</c> 表 <see cref="IDataRegistry"/>。</summary>
    internal static class AreaTriggerTestSupport
    {
        public static IEventBus NewEventBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        public static IDataRegistry BuildRegistry(IEventBus bus, params JsonObject[] rows)
        {
            var source = new InMemoryDataSource();
            var root = J.O(
                ("table", J.S(AreaTriggerSchemas.TriggerDef.Name)),
                ("schema_version", J.N(1)),
                ("rows", new JsonArray(rows.Cast<JsonValue>())));
            source.Add(AreaTriggerSchemas.TriggerDef.Name, JsonWriter.Write(root));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(AreaTriggerSchemas.TriggerDef);
            return registry;
        }

        public static JsonObject MapTransitionRow(string id, string mapId, string targetMap, string? spawnPoint = null, bool oneShot = false, string? condition = null) =>
            J.O(
                ("id", J.S(id)),
                ("map_id", J.S(mapId)),
                ("shape", CircleShape(0, 0, 5)),
                ("trigger_type", J.S("map_transition")),
                ("one_shot", J.B(oneShot)),
                ("params", spawnPoint == null
                    ? J.O(("target_map", J.S(targetMap)))
                    : J.O(("target_map", J.S(targetMap)), ("spawn_point", J.S(spawnPoint)))),
                ("condition", condition == null ? JsonNull.Instance : J.S(condition)));

        public static JsonObject QuestExploreRow(string id, string mapId, bool oneShot = false) =>
            J.O(
                ("id", J.S(id)),
                ("map_id", J.S(mapId)),
                ("shape", CircleShape(0, 0, 5)),
                ("trigger_type", J.S("quest_explore")),
                ("one_shot", J.B(oneShot)),
                ("params", J.O()));

        public static JsonObject EncounterStartRow(string id, string mapId, string encounterRef) =>
            J.O(
                ("id", J.S(id)),
                ("map_id", J.S(mapId)),
                ("shape", CircleShape(0, 0, 5)),
                ("trigger_type", J.S("encounter_start")),
                ("params", J.O(("encounter_ref", J.S(encounterRef)))));

        public static JsonObject ScriptRow(string id, string mapId, string hookId) =>
            J.O(
                ("id", J.S(id)),
                ("map_id", J.S(mapId)),
                ("shape", CircleShape(0, 0, 5)),
                ("trigger_type", J.S("script")),
                ("params", J.O(("hook_id", J.S(hookId)))));

        public static JsonObject CircleShape(double x, double y, double radius) =>
            J.O(("kind", J.S("circle")), ("radius", J.N(radius)), ("center", J.O(("x", J.N(x)), ("y", J.N(y)))));
    }
}
