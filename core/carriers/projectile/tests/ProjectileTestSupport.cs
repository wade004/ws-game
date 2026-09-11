using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Projectile;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.Faction;
using Core.Rules.Common;

namespace Tests.Carriers.Projectile
{
    /// <summary>JSON 树构造帮助方法（惯例同 <c>core/carriers/gobj/tests</c> 的同名 <c>J</c> 类）。</summary>
    internal static class J
    {
        public static JsonValue S(string s) => new JsonString(s);

        public static JsonValue N(double n) => new JsonNumber(n);

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
    }

    /// <summary><see cref="IEffectSink"/> 的最小测试假实现：记录每次 <see cref="ApplyEffect"/> 调用
    /// 的完整 <see cref="EffectContext"/>，供测试断言"命中后效果确实被交回效果管线"（惯例同
    /// <c>core/rules/skill/tests</c> 的 <c>FakeCombatHost</c>：只记录调用，不做真实结算）。</summary>
    internal sealed class FakeEffectSink : IEffectSink
    {
        public readonly List<EffectContext> Applied = new List<EffectContext>();

        public ResolveResult ApplyEffect(EffectContext context)
        {
            Applied.Add(context);
            return new ResolveResult(
                HitResult.Hit, context.BaseValue, context.BaseValue, 0, immune: false,
                isHeal: context.Kind == EffectKind.Heal);
        }

        public AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null) =>
            throw new NotSupportedException("FakeEffectSink 不支持 ApplyAura（本模块测试不需要）");

        public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef)
        {
        }
    }

    /// <summary>
    /// ADR-0028 测试用最小 <see cref="IFactionMatrix"/> 桩：按显式登记的 <c>(from, to)</c> 键值对
    /// 查询反应，同阵营恒 Friendly（同 <see cref="IFactionMatrix.GetReaction"/> 文档约定），未登记
    /// 的组合缺省 <see cref="Reaction.Neutral"/>（不是本测试关心的取值，够用即可，不追求覆盖真实
    /// <c>FactionMatrix</c> 的全部裁决优先级——那部分已由 <c>core/numbers/faction/tests</c> 覆盖）。
    /// </summary>
    internal sealed class StubFactionMatrix : IFactionMatrix
    {
        private readonly Dictionary<(Id From, Id To), Reaction> _reactions = new Dictionary<(Id, Id), Reaction>();
        private readonly HashSet<Id> _factions = new HashSet<Id>();

        public void Set(Id from, Id to, Reaction reaction)
        {
            _reactions[(from, to)] = reaction;
            _factions.Add(from);
            _factions.Add(to);
        }

        public Reaction GetReaction(Id from, Id to)
        {
            if (from.Equals(to)) return Reaction.Friendly;
            return _reactions.TryGetValue((from, to), out var reaction) ? reaction : Reaction.Neutral;
        }

        public void SetReaction(Id from, Id to, Reaction reaction) => Set(from, to, reaction);

        public void ResetOverrides() => _reactions.Clear();

        public bool IsHostile(Id a, Id b) => GetReaction(a, b) == Reaction.Hostile;

        public IReadOnlyList<Id> Factions => _factions.ToList();
    }

    /// <summary>已构建好的一整套测试宿主：真实 <see cref="WorldSim"/>/<see cref="EventBus"/>/
    /// <see cref="WorldUnitAccess"/>（本类型正是 <c>IUnitAccess</c> 的正式实现，见其判断记录），
    /// 桩 <see cref="StubSpatialQuery"/>/<see cref="StubNavigation2D"/>，真实
    /// <see cref="Core.Carriers.Projectile.ProjectileHost"/>。</summary>
    internal sealed class ProjectileWorld
    {
        public IEventBus Bus = default!;
        public WorldSim World = default!;
        public WorldUnitAccess Units = default!;
        public StubSpatialQuery Spatial = default!;
        public StubNavigation2D Navigation = default!;
        public InMemoryProjectileDiagnostics Diagnostics = default!;
        public ProjectileOptions Options = default!;
        public Core.Carriers.Projectile.ProjectileHost Host = default!;
        public FakeEffectSink Sink = default!;

        public readonly List<IEvent> Events = new List<IEvent>();

        public void Flush() => Bus.DispatchPending();

        /// <summary>登记一个存活的测试单位：接入真实 <see cref="World"/>（<c>PlayerUnit</c> 子类，
        /// 满足 <see cref="WorldUnitAccess.Exists"/> 的 <c>is Unit</c> 检查）与
        /// <see cref="Spatial"/>（打 <c>"unit"</c> 标签，匹配 <see cref="ProjectileOptions.HitQueryTags"/>
        /// 默认值，惯例同 <c>Core.Carriers.Assembly.CarriersAssembly.DefaultSpatialSyncKinds</c>）。</summary>
        public Id AddUnit(string idText, Vec2 position, Id? faction = null)
        {
            var id = new Id(idText);
            var unit = new PlayerUnit(id, MapId, faction ?? new Id("fac.sample"), new Id("arch.class.sample"))
            {
                Position = position,
            };
            World.AddEntity(unit);
            Spatial.Register(id, position, 0.1, new[] { "unit" });
            return id;
        }

        public static readonly Id MapId = new Id("world.projectile_sample");

        public void Tick(double dt)
        {
            World.Tick(SimStep.Continuous(dt));
            Flush();
        }
    }

    internal static class ProjectileWorldBuilder
    {
        public static ProjectileWorld Build(ProjectileOptions? options = null)
        {
            var bus = CreateBus();
            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var navigation = new StubNavigation2D();
            var units = new WorldUnitAccess(world, spatial);
            var diagnostics = new InMemoryProjectileDiagnostics();
            var resolvedOptions = options ?? new ProjectileOptions();
            var host = new Core.Carriers.Projectile.ProjectileHost(world, units, spatial, navigation, resolvedOptions, diagnostics);

            var projectileWorld = new ProjectileWorld
            {
                Bus = bus,
                World = world,
                Units = units,
                Spatial = spatial,
                Navigation = navigation,
                Diagnostics = diagnostics,
                Options = resolvedOptions,
                Host = host,
                Sink = new FakeEffectSink(),
            };

            SubscribeAll(bus, projectileWorld.Events);
            return projectileWorld;
        }

        private static IEventBus CreateBus()
        {
            var definitions = new[]
            {
                new EventDefinition(SimEventKeys.EntityCreated, "entity", new[] { "entityId", "kind", "displayId" }),
                new EventDefinition(SimEventKeys.EntityDestroyed, "entity", new[] { "entityId" }),
                new EventDefinition(SimEventKeys.TickStarted, "sim", new[] { "tickIndex", "dt" }),
                new EventDefinition(SimEventKeys.TickFinished, "sim", new[] { "tickIndex" }),
            };

            var catalog = EventCatalog.FromDefinitions(definitions);
            // 惯例同 core/carriers/gobj/tests：非严格模式，未登记的间接事件只记警告、不阻断测试。
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }

        private static void SubscribeAll(IEventBus bus, List<IEvent> sink)
        {
            bus.Subscribe(SimEventKeys.EntityCreated, evt => sink.Add(evt));
            bus.Subscribe(SimEventKeys.EntityDestroyed, evt => sink.Add(evt));
        }
    }
}
