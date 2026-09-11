using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Rules.Assembly;
using Core.Rules.Common;
using Tests.Rules.Integration;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// ADR-0027《地面坐标施法请求》验收（见 architecture/落地计划/消费方反馈-2026-09-11-地面坐标施法.md）：
    /// 命名前缀 C10b 沿用本模块既有验收测试惯例（<see cref="C10_ResolveTraceTests"/> 已用 C10），本文件
    /// 按任务书要求经真实 <see cref="RulesAssembly"/> 装配根 + 真实 <see cref="Core.Rules.Skill.SkillHost.CastSkillAtGround"/>
    /// 端到端验证；<see cref="ISpatialQuery"/>/<see cref="INavigation2D"/> 均为带墙的可配置桩实现（见
    /// <see cref="WallAwareSpatialQuery"/>、<see cref="StubNavigation2D"/>），不是恒真/恒假的最小假实现。
    /// <para>
    /// 判断记录（LOS 墙与可行走墙分离配置）：<see cref="Core.Rules.Skill.CastPipeline.ValidateGroundPoint"/>
    /// 的视线校验复用 <see cref="ISpatialQuery.HasLineOfSight"/>、可行走校验复用
    /// <see cref="INavigation2D.IsWalkable"/>，两者是独立的两个依赖、各自可选注入。本文件的
    /// <see cref="WallAwareSpatialQuery"/>（LOS）与直接注入 <see cref="RulesAssembly"/> 的
    /// <see cref="StubNavigation2D"/>（可行走）各自持有独立的一份阻挡矩形登记，供
    /// "视线被挡但落点本身可行走"（<see cref="PointBehindWall_Rejects_GroundTargetNoLineOfSight"/>）与
    /// "视线畅通但落点本身不可行走"（<see cref="InfeasiblePoint_Rejects_GroundTargetUnreachable"/>）
    /// 两条独立分支各自精确复现，不互相牵连（一个填满矩形区域的真实几何阻挡通常会同时挡视线与挡通行，
    /// 分离配置是刻意的测试隔离手段，不代表生产环境两者应当分开配置）。
    /// </para>
    /// </summary>
    public sealed class C10b_GroundCastTests
    {
        private static readonly Id MapId = new Id("map.c10b_ground_test");
        private static readonly Id Caster = new Id("unit.c10b_caster");
        private static readonly Id FactionAll = new Id("fac.c10b_all");
        private static readonly Id ClassId = new Id("arch.class.c10b_sample");
        private static readonly Id StatId = new Id("stat.c10b_sample_primary");
        private static readonly Id ChainAllInShape = new Id("target.chain.c10b_all_in_shape");

        private static readonly Id SkillGroundInstant = new Id("skill.c10b_ground_instant");
        private static readonly Id SkillGroundDelayed = new Id("skill.c10b_ground_delayed");
        private static readonly Id SkillNoGround = new Id("skill.c10b_no_ground");
        private static readonly Id SkillShortRange = new Id("skill.c10b_short_range");

        private const double InstantBaseValue = 7;
        private const double TargetMaxHp = 100000;

        // -----------------------------------------------------------------
        // ISpatialQuery 的带墙可配置桩：QueryShape/Register 等一律转发内部 StubSpatialQuery；
        // HasLineOfSight 改用一份独立的 StubNavigation2D 做线段-矩形相交判定（同 StubNavigation2D
        // 既有算法，不重新发明一遍几何计算），供本文件按需分别配置"挡视线"的阻挡矩形——与真正注入
        // RulesAssembly 的 INavigation2D（挡可行走）各自独立，见类型顶部判断记录。
        // -----------------------------------------------------------------
        private sealed class WallAwareSpatialQuery : ISpatialQuery
        {
            private readonly StubSpatialQuery _inner = new StubSpatialQuery();
            private readonly StubNavigation2D _losNav = new StubNavigation2D();
            private readonly Id _mapId;

            public WallAwareSpatialQuery(Id mapId, IReadOnlyList<Rect>? losBlockingRects)
            {
                _mapId = mapId;
                _losNav.BuildNavMesh(mapId);
                if (losBlockingRects != null && losBlockingRects.Count > 0)
                {
                    _losNav.SetBlocking(mapId, losBlockingRects);
                }
            }

            public void Register(Id id, Vec2 position, double radius) => _inner.Register(id, position, radius);

            public IReadOnlyList<Id> QueryRadius(Vec2 center, double radius, QueryFilter filter) => _inner.QueryRadius(center, radius, filter);
            public IReadOnlyList<Id> QueryCone(Vec2 origin, double direction, double angle, double range, QueryFilter filter) => _inner.QueryCone(origin, direction, angle, range, filter);
            public IReadOnlyList<Id> QueryLine(Vec2 from, Vec2 to, QueryFilter filter) => _inner.QueryLine(from, to, filter);
            public IReadOnlyList<Id> QueryRect(Vec2 min, Vec2 max, QueryFilter filter) => _inner.QueryRect(min, max, filter);
            public IReadOnlyList<Id> QueryShape(Shape shape, QueryFilter filter) => _inner.QueryShape(shape, filter);
            public Id? Nearest(Vec2 point, QueryFilter filter) => _inner.Nearest(point, filter);

            /// <summary>视线是否受阻改按 <see cref="_losNav"/> 登记的阻挡矩形做 Raycast 判定，
            /// 不是 <see cref="StubSpatialQuery"/> 恒真的默认实现（见该类型判断记录"如需模拟遮挡，
            /// 可在测试子类中覆盖或另行扩展"）。</summary>
            public bool HasLineOfSight(Vec2 from, Vec2 to) => _losNav.Raycast(_mapId, from, to) == null;

            public void Register(Id id, Vec2 position, double radius, IReadOnlyList<string> tags) => _inner.Register(id, position, radius, tags);
            public void UpdatePosition(Id id, Vec2 position) => _inner.UpdatePosition(id, position);
            public void Unregister(Id id) => _inner.Unregister(id);
            public void Clear() => _inner.Clear();
        }

        // -----------------------------------------------------------------
        // 世界装配：真实 RulesAssembly + 带墙的导航/空间桩
        // -----------------------------------------------------------------

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public RulesAssembly Rules = null!;
            public List<IEvent> Events = null!;
            public StubNavigation2D Navigation = null!;

            public IEnumerable<T> Of<T>() where T : IEvent => Events.OfType<T>();
        }

        private static JsonObject SkillDef(
            string id, double range, double castTime, bool groundTarget, double baseValue) =>
            J.O(
                ("id", J.S(id)),
                ("school", J.S("school.physical")),
                ("kind", J.S("active")),
                ("range", J.N(range)),
                ("cast_time", J.N(castTime)),
                ("respects_gcd", J.B(false)),
                ("ground_target", J.B(groundTarget)),
                ("target_shape_ref", J.S(ChainAllInShape.Value)),
                ("effects", J.A(J.O(
                    ("kind", J.S("school_damage")),
                    ("params", J.O(("base_value", J.N(baseValue)), ("coefficient", J.N(0))))))));

        private static string TableJson(string name, IReadOnlyList<JsonObject> rows) => JsonWriter.Write(J.O(
            ("table", J.S(name)),
            ("schema_version", J.N(1)),
            ("rows", new JsonArray(rows.Cast<JsonValue>()))));

        private static string EmptyTableJson(string name) => TableJson(name, Array.Empty<JsonObject>());

        /// <summary>
        /// 装配一份真实的 <see cref="RulesAssembly"/>：<paramref name="losBlockingRects"/> 配置
        /// <see cref="WallAwareSpatialQuery"/>（挡视线）、<paramref name="navBlockingRects"/> 配置
        /// 真正注入的 <see cref="StubNavigation2D"/>（挡可行走），两者默认均为空（无墙）。
        /// <paramref name="units"/> 是除施法者外要登记的单位（id, position），均登记进同一个
        /// <see cref="FactionAll"/>。
        /// </summary>
        private static Fixture BuildAssembly(
            IReadOnlyList<(Id Id, Vec2 Position)> units,
            IReadOnlyList<Rect>? losBlockingRects = null,
            IReadOnlyList<Rect>? navBlockingRects = null)
        {
            var statDefinitionJson = TableJson("stat.definition", new[]
            {
                J.O(
                    ("id", J.S(StatId.Value)),
                    ("name_key", J.S("l10n.stat.c10b_sample_primary.name")),
                    ("group", J.S("primary")),
                    ("default_base", J.N(0))),
            });

            var powerTypeJson = TableJson("arch.power_type", new[]
            {
                J.O(
                    ("id", J.S(WellKnownPowers.Health.Value)),
                    ("name_key", J.S("l10n.power.health.name")),
                    ("max_source", J.O(("kind", J.S("fixed")), ("value", J.N(TargetMaxHp)))),
                    ("start_full", J.B(true)),
                    ("allow_overflow", J.B(false))),
            });

            var classJson = TableJson("arch.class", new[]
            {
                J.O(
                    ("id", J.S(ClassId.Value)),
                    ("name_key", J.S("l10n.arch.class.c10b_sample.name")),
                    ("primary_stat", J.S(StatId.Value)),
                    ("base_stats", J.O()),
                    ("power_types", J.Ids(WellKnownPowers.Health.Value))),
            });

            var factionJson = TableJson("fac.faction", new[]
            {
                J.O(("id", J.S(FactionAll.Value)), ("name_key", J.S("l10n.fac.c10b_all.name")), ("default_reaction", J.S("friendly"))),
            });

            var hitTableJson = TableJson("combat.hit_table_config", new[]
            {
                J.O(
                    ("id", J.S("combat.hit_table.default")),
                    ("miss", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("dodge", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("parry", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("glancing_blow", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("block", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("crit", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("crit_multiplier_base", J.N(2.0))),
            });

            var targetChainJson = TableJson("target.chain_def", new[]
            {
                J.O(
                    ("id", J.S(ChainAllInShape.Value)),
                    ("source", J.S("all_in_shape")),
                    ("shape", J.O(("kind", J.S("circle")), ("radius", J.N(3)))),
                    ("filters", J.A(J.S("alive")))),
            });

            var skillDefsJson = TableJson("skill.def", new[]
            {
                SkillDef(SkillGroundInstant.Value, range: 20, castTime: 0, groundTarget: true, baseValue: InstantBaseValue),
                SkillDef(SkillGroundDelayed.Value, range: 20, castTime: 1.0, groundTarget: true, baseValue: InstantBaseValue),
                SkillDef(SkillNoGround.Value, range: 20, castTime: 0, groundTarget: false, baseValue: InstantBaseValue),
                SkillDef(SkillShortRange.Value, range: 2, castTime: 0, groundTarget: true, baseValue: InstantBaseValue),
            });

            var source = new InMemoryDataSource()
                .Add("stat.definition", statDefinitionJson)
                .Add("arch.power_type", powerTypeJson)
                .Add("arch.class", classJson)
                .Add("fac.faction", factionJson)
                .Add("fac.reaction_matrix", EmptyTableJson("fac.reaction_matrix"))
                .Add("combat.hit_table_config", hitTableJson)
                .Add("combat.resist_curve", EmptyTableJson("combat.resist_curve"))
                .Add("target.chain_def", targetChainJson)
                .Add("skill.def", skillDefsJson)
                .Add("skill.aura_def", EmptyTableJson("skill.aura_def"))
                .Add("skill.proc_def", EmptyTableJson("skill.proc_def"));

            var bus = FightWorldBuilder.BuildEventBus(out _);
            var events = new List<IEvent>();
            foreach (var key in EventKeys.All)
            {
                bus.Subscribe(key, e => events.Add(e));
            }

            var registry = new DataRegistry(source, bus, RulesSchemaCatalog.CreateOptions());
            RulesSchemaCatalog.RegisterAll(registry);

            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "C10b 测试夹具数据未通过校验：\n" + string.Join("\n", report.Issues.Select(i => i.ToString())));
            }

            var rng = new RngHost(20260911UL);
            var world = new WorldSim(bus);
            world.AddEntity(new TestUnit(Caster, MapId, FactionAll) { Position = new Vec2(0, 0) });
            foreach (var (id, position) in units)
            {
                world.AddEntity(new TestUnit(id, MapId, FactionAll) { Position = position });
            }

            var unitsAccess = new WorldUnitAccess(world);
            var spatial = new WallAwareSpatialQuery(MapId, losBlockingRects);
            spatial.Register(Caster, new Vec2(0, 0), 0.1);
            foreach (var (id, position) in units)
            {
                spatial.Register(id, position, 0.1);
            }

            var navigation = new StubNavigation2D();
            navigation.BuildNavMesh(MapId);
            if (navBlockingRects != null && navBlockingRects.Count > 0)
            {
                navigation.SetBlocking(MapId, navBlockingRects);
            }

            var rules = new RulesAssembly(bus, registry, rng, unitsAccess, spatial, world, navigation: navigation);
            rules.RegisterUnit(Caster, ClassId, raceId: null, level: 1);
            foreach (var (id, _) in units)
            {
                rules.RegisterUnit(id, ClassId, raceId: null, level: 1);
            }

            return new Fixture { Bus = bus, Rules = rules, Events = events, Navigation = navigation };
        }

        // -----------------------------------------------------------------
        // 1) 合法点：效果以点为原点命中范围内单位，不命中施法者附近但不在点范围内的单位；
        //    生命周期事件携带 CastInstanceId 与 GroundPoint。
        // -----------------------------------------------------------------

        [Fact]
        public void LegalPoint_HitsUnitsNearPoint_NotNearCaster_AndEventsCarryInstanceIdAndGroundPoint()
        {
            var point = new Vec2(10, 0);
            var unitNearPoint = new Id("unit.c10b_near_point");
            var unitNearCaster = new Id("unit.c10b_near_caster");
            var fx = BuildAssembly(new[]
            {
                (unitNearPoint, new Vec2(10.5, 0)), // 落在 point 半径 3 的圆内
                (unitNearCaster, new Vec2(0.5, 0)), // 落在施法者附近，但不在 point 的圆内
            });

            var request = new GroundCastRequest(point, GroundCastSource.Explicit);
            var result = fx.Rules.Skill.CastSkillAtGround(Caster, SkillGroundInstant, request);
            fx.Bus.DispatchPending();

            Assert.True(result.Success);
            Assert.NotNull(result.CastInstanceId);

            var dealt = Assert.Single(fx.Of<CombatDamageDealtEvent>());
            Assert.Equal(unitNearPoint, dealt.TargetId);
            Assert.Equal(InstantBaseValue, dealt.Amount);

            var start = Assert.Single(fx.Of<SkillCastStartEvent>());
            Assert.Equal(result.CastInstanceId, start.CastInstanceId);
            Assert.Equal(point, start.GroundPoint);

            var success = Assert.Single(fx.Of<SkillCastSuccessEvent>());
            Assert.Equal(result.CastInstanceId, success.CastInstanceId);
            Assert.Equal(point, success.GroundPoint);
            Assert.Equal(new[] { unitNearPoint }, success.Targets);
        }

        // -----------------------------------------------------------------
        // 2) 墙后点：拒绝 GroundTargetNoLineOfSight，不消费资源/不进冷却（未真正开始读条）。
        // -----------------------------------------------------------------

        [Fact]
        public void PointBehindWall_Rejects_GroundTargetNoLineOfSight()
        {
            var point = new Vec2(5, 0);
            var wall = new Rect(new Vec2(2, -1), new Vec2(3, 1)); // 挡在 (0,0)->(5,0) 连线上
            var fx = BuildAssembly(Array.Empty<(Id, Vec2)>(), losBlockingRects: new[] { wall });

            var request = new GroundCastRequest(point);
            var result = fx.Rules.Skill.CastSkillAtGround(Caster, SkillGroundInstant, request);
            fx.Bus.DispatchPending();

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.GroundTargetNoLineOfSight, result.Reason);
            Assert.Empty(fx.Of<CombatDamageDealtEvent>());
            Assert.Empty(fx.Of<SkillCastStartEvent>());

            var failed = Assert.Single(fx.Of<SkillCastFailedEvent>());
            Assert.Equal(CastFailureReason.GroundTargetNoLineOfSight, failed.ReasonCode);
            Assert.Null(failed.CastInstanceId); // 校验阶段失败不分配实例 id（既有惯例）。
        }

        // -----------------------------------------------------------------
        // 3) 不可行点：拒绝 GroundTargetUnreachable（视线畅通，落点本身不可行走）。
        // -----------------------------------------------------------------

        [Fact]
        public void InfeasiblePoint_Rejects_GroundTargetUnreachable()
        {
            var point = new Vec2(5, 0);
            var unwalkable = new Rect(new Vec2(4.5, -0.5), new Vec2(5.5, 0.5)); // 恰好盖住落点本身
            var fx = BuildAssembly(Array.Empty<(Id, Vec2)>(), navBlockingRects: new[] { unwalkable });

            var request = new GroundCastRequest(point);
            var result = fx.Rules.Skill.CastSkillAtGround(Caster, SkillGroundInstant, request);
            fx.Bus.DispatchPending();

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.GroundTargetUnreachable, result.Reason);
            Assert.Empty(fx.Of<CombatDamageDealtEvent>());
        }

        // -----------------------------------------------------------------
        // 4) 超射程点：拒绝 OutOfRange（复用既有原因码）。
        // -----------------------------------------------------------------

        [Fact]
        public void PointBeyondRange_Rejects_OutOfRange()
        {
            var point = new Vec2(10, 0); // skill.c10b_short_range 的 range=2
            var fx = BuildAssembly(Array.Empty<(Id, Vec2)>());

            var request = new GroundCastRequest(point);
            var result = fx.Rules.Skill.CastSkillAtGround(Caster, SkillShortRange, request);
            fx.Bus.DispatchPending();

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.OutOfRange, result.Reason);
        }

        // -----------------------------------------------------------------
        // 5)/6) 快照策略：AtRequest 恒用请求时坐标；AtRelease 在效果落地那一刻改用 Sampler 重新采样。
        // -----------------------------------------------------------------

        [Fact]
        public void SnapshotPolicy_AtRequest_UsesRequestTimePoint_EvenIfPointMovesDuringCast()
        {
            var pointA = new Vec2(5, 0);
            var pointB = new Vec2(-5, 0);
            var unitA = new Id("unit.c10b_at_point_a");
            var unitB = new Id("unit.c10b_at_point_b");
            var fx = BuildAssembly(new[]
            {
                (unitA, new Vec2(5.5, 0)),
                (unitB, new Vec2(-5.5, 0)),
            });

            // Sampler 刻意返回一个不同的点（模拟"施法期间点已经移动到别处"），AtRequest 策略下
            // 效果落地时必须忽略它，仍使用请求时的 pointA。
            var request = new GroundCastRequest(
                pointA, GroundCastSource.Explicit, snapshotPolicy: GroundCastSnapshotPolicy.AtRequest,
                sampler: () => pointB);

            var result = fx.Rules.Skill.CastSkillAtGround(Caster, SkillGroundDelayed, request);
            fx.Bus.DispatchPending();
            Assert.True(result.Success);

            fx.Rules.Skill.Update(1.0); // 推进到读条完成（cast_time=1.0）。
            fx.Bus.DispatchPending();

            var dealt = Assert.Single(fx.Of<CombatDamageDealtEvent>());
            Assert.Equal(unitA, dealt.TargetId);

            var success = Assert.Single(fx.Of<SkillCastSuccessEvent>());
            Assert.Equal(pointA, success.GroundPoint);
        }

        [Fact]
        public void SnapshotPolicy_AtRelease_ResamplesAtCompletion_WhenPointMovesDuringCast()
        {
            var pointA = new Vec2(5, 0);
            var pointB = new Vec2(-5, 0);
            var unitA = new Id("unit.c10b_at_point_a");
            var unitB = new Id("unit.c10b_at_point_b");
            var fx = BuildAssembly(new[]
            {
                (unitA, new Vec2(5.5, 0)),
                (unitB, new Vec2(-5.5, 0)),
            });

            var request = new GroundCastRequest(
                pointA, GroundCastSource.Explicit, snapshotPolicy: GroundCastSnapshotPolicy.AtRelease,
                sampler: () => pointB);

            var result = fx.Rules.Skill.CastSkillAtGround(Caster, SkillGroundDelayed, request);
            fx.Bus.DispatchPending();
            Assert.True(result.Success);

            fx.Rules.Skill.Update(1.0);
            fx.Bus.DispatchPending();

            var dealt = Assert.Single(fx.Of<CombatDamageDealtEvent>());
            Assert.Equal(unitB, dealt.TargetId);

            var success = Assert.Single(fx.Of<SkillCastSuccessEvent>());
            Assert.Equal(pointB, success.GroundPoint);
        }

        // -----------------------------------------------------------------
        // 7) 技能未声明允许地面目标：拒绝 GroundTargetUnsupported。
        // -----------------------------------------------------------------

        [Fact]
        public void SkillNotDeclaringGroundTarget_Rejects_GroundTargetUnsupported()
        {
            var fx = BuildAssembly(Array.Empty<(Id, Vec2)>());

            var request = new GroundCastRequest(new Vec2(1, 0));
            var result = fx.Rules.Skill.CastSkillAtGround(Caster, SkillNoGround, request);
            fx.Bus.DispatchPending();

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.GroundTargetUnsupported, result.Reason);
        }

        // -----------------------------------------------------------------
        // 8) 互斥：CastSkillAtGround 与既有 CastSkill（单位目标）是两条独立入口，互不替代——
        //    同一个声明了 ground_target 的技能，仍可经 CastSkill 以显式单位目标正常施放
        //    （既有单位目标语义不受地面坐标能力影响，两条入口各走各的裁决路径）。
        // -----------------------------------------------------------------

        [Fact]
        public void GroundCastRequest_DoesNotSubstituteExplicitUnitTargetPath()
        {
            var unitNearCaster = new Id("unit.c10b_explicit_target");
            var fx = BuildAssembly(new[] { (unitNearCaster, new Vec2(0.5, 0)) });

            var unitResult = fx.Rules.Skill.CastSkill(Caster, SkillGroundInstant, new[] { unitNearCaster });
            fx.Bus.DispatchPending();

            Assert.True(unitResult.Success);
            var dealt = Assert.Single(fx.Of<CombatDamageDealtEvent>());
            Assert.Equal(unitNearCaster, dealt.TargetId);

            // 单位目标路径产生的事件不携带 GroundPoint（既有语义不变）。
            var start = Assert.Single(fx.Of<SkillCastStartEvent>());
            Assert.Null(start.GroundPoint);
            var success = Assert.Single(fx.Of<SkillCastSuccessEvent>());
            Assert.Null(success.GroundPoint);
        }
    }
}
