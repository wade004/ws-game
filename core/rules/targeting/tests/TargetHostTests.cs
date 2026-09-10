using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Rules.Common;
using Core.Rules.Targeting;
using Xunit;

namespace Tests.Rules.Targeting
{
    public class TargetHostTests
    {
        private static readonly Id HeroFaction = new Id("fac.target_test_hero");
        private static readonly Id MonsterFaction = new Id("fac.target_test_monster");

        // -----------------------------------------------------------------
        // 公共夹具
        // -----------------------------------------------------------------

        private static IFactionMatrix BuildFactions(IEventBus bus)
        {
            const string factionRows = @"[
                { ""id"": ""fac.target_test_hero"", ""name_key"": ""l10n.fac.hero.name"", ""default_reaction"": ""hostile"" },
                { ""id"": ""fac.target_test_monster"", ""name_key"": ""l10n.fac.monster.name"", ""default_reaction"": ""hostile"" }
            ]";
            const string reactionRows = @"[
                { ""id"": ""fac.reaction.hero_monster"", ""from"": ""fac.target_test_hero"", ""to"": ""fac.target_test_monster"", ""reaction"": ""hostile"" },
                { ""id"": ""fac.reaction.monster_hero"", ""from"": ""fac.target_test_monster"", ""to"": ""fac.target_test_hero"", ""reaction"": ""hostile"" }
            ]";

            var source = new InMemoryDataSource()
                .Add("fac.faction", TargetingTestSupport.Envelope("fac.faction", factionRows))
                .Add("fac.reaction_matrix", TargetingTestSupport.Envelope("fac.reaction_matrix", reactionRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(FacSchemas.Faction);
            registry.RegisterSchema(FacSchemas.ReactionMatrix);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);

            return new FactionMatrix(registry, bus);
        }

        private static PowerHost BuildPowerHost(IEventBus bus)
        {
            return new PowerHost(new[] { TargetingTestSupport.HealthPowerType() }, bus);
        }

        private sealed class Fixture
        {
            public TargetHost Host = null!;
            public FakeUnitAccess Units = null!;
            public StubSpatialQuery Spatial = null!;
            public PowerHost Powers = null!;
            public FakeThreatTable Threat = null!;
            public IEventBus Bus = null!;
        }

        private static Fixture Build(
            string chainRowsJson,
            Action<FakeUnitAccess, StubSpatialQuery, PowerHost, FakeThreatTable>? setup = null,
            TargetStrategyRegistry? strategies = null,
            TargetingOptions? options = null,
            bool useThreat = true)
        {
            var bus = TargetingTestSupport.MakeBus();
            var factions = BuildFactions(bus);
            var powers = BuildPowerHost(bus);
            var units = new FakeUnitAccess();
            var spatial = new StubSpatialQuery();
            var threat = new FakeThreatTable();

            setup?.Invoke(units, spatial, powers, threat);

            strategies ??= DefaultStrategies();

            var chainRegistry = TargetingTestSupport.BuildChainRegistry(bus, chainRowsJson, strategies);
            var chainReport = chainRegistry.LoadAll();
            Assert.False(chainReport.IsBlocking);

            var exprFactory = new TestExprHostFactory(units, powers);

            var host = new TargetHost(
                strategies,
                chainRegistry,
                units,
                spatial,
                factions,
                powers,
                exprFactory,
                threat: useThreat ? threat : null,
                eventBus: bus,
                options: options);

            return new Fixture { Host = host, Units = units, Spatial = spatial, Powers = powers, Threat = threat, Bus = bus };
        }

        private static TargetStrategyRegistry DefaultStrategies()
        {
            var registry = new TargetStrategyRegistry();
            BuiltinTargetStrategies.RegisterAll(registry);
            return registry;
        }

        private static void RegisterHealth(PowerHost powers, Id unitId, double currentPct)
        {
            powers.RegisterUnit(unitId, new[] { WellKnownPowers.Health });
            var max = powers.GetPowerMax(unitId, WellKnownPowers.Health);
            var target = max * currentPct;
            var delta = target - powers.GetPower(unitId, WellKnownPowers.Health);
            if (Math.Abs(delta) > 1e-9)
            {
                powers.ModifyPower(unitId, WellKnownPowers.Health, delta, new Id("test.setup"));
            }
        }

        // -----------------------------------------------------------------
        // 1. 自动选最近敌对目标
        // -----------------------------------------------------------------

        [Fact]
        public void NearestInShape_WithHostileAndAliveFilters_SelectsNearestHostile()
        {
            const string rows = @"[
                { ""id"": ""target.chain.nearest_hostile"", ""source"": ""nearest_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 },
                  ""filters"": [""relation:hostile"", ""alive""] }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.hostile_far"), MonsterFaction, new Vec2(30, 0));
                units.Add(new Id("unit.hostile_near"), MonsterFaction, new Vec2(5, 0));
                units.Add(new Id("unit.hostile_mid"), MonsterFaction, new Vec2(15, 0));
                units.Add(new Id("unit.friendly_closest"), HeroFaction, new Vec2(1, 0));

                spatial.Register(new Id("unit.hostile_far"), new Vec2(30, 0), 0);
                spatial.Register(new Id("unit.hostile_near"), new Vec2(5, 0), 0);
                spatial.Register(new Id("unit.hostile_mid"), new Vec2(15, 0), 0);
                spatial.Register(new Id("unit.friendly_closest"), new Vec2(1, 0), 0);
            });

            var result = fx.Host.Resolve(new Id("target.chain.nearest_hostile"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.hostile_near") }, result);
        }

        // -----------------------------------------------------------------
        // 2. 距离平局按 Id
        // -----------------------------------------------------------------

        [Fact]
        public void NearestInShape_DistanceTie_BreaksByIdAscending()
        {
            const string rows = @"[
                { ""id"": ""target.chain.nearest_any"", ""source"": ""nearest_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 } }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.z_candidate"), MonsterFaction, new Vec2(10, 0));
                units.Add(new Id("unit.a_candidate"), MonsterFaction, new Vec2(10, 0));

                spatial.Register(new Id("unit.z_candidate"), new Vec2(10, 0), 0);
                spatial.Register(new Id("unit.a_candidate"), new Vec2(10, 0), 0);
            });

            var result = fx.Host.Resolve(new Id("target.chain.nearest_any"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.a_candidate") }, result);
        }

        // -----------------------------------------------------------------
        // 2.5 加固任务（05 §3.6 碰撞层落地）：trigger_only 标签的对象登记进空间索引后，
        // nearest_in_shape/all_in_shape 必须排除它，不能崩溃、也不能被当成候选目标。
        // -----------------------------------------------------------------

        [Fact]
        public void NearestInShape_ExcludesTriggerOnlyTaggedObject_DoesNotThrow()
        {
            const string rows = @"[
                { ""id"": ""target.chain.nearest_any"", ""source"": ""nearest_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 } }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.hostile"), MonsterFaction, new Vec2(10, 0));
                spatial.Register(new Id("unit.hostile"), new Vec2(10, 0), 0);

                // 模拟区域触发实体：只登记进空间索引（不登记进 FakeUnitAccess），且比
                // unit.hostile 更靠近施法者——若 NearestInShapeStrategy 不排除
                // CollisionLayers.TriggerOnly，ctx.Units.GetPosition(id) 会因为这个 id 根本不是
                // 已知单位而抛异常（同 WorldUnitAccess.Require 在生产代码里的行为），而不是"选错
                // 目标"这种更温和的失败。
                spatial.Register(new Id("area.trap_1"), new Vec2(1, 0), 0, new[] { Core.Foundation.EngineAdapter.CollisionLayers.TriggerOnly });
            });

            var result = fx.Host.Resolve(new Id("target.chain.nearest_any"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.hostile") }, result);
        }

        [Fact]
        public void AllInShape_ExcludesTriggerOnlyTaggedObject()
        {
            const string rows = @"[
                { ""id"": ""target.chain.all_any"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 } }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.hostile"), MonsterFaction, new Vec2(10, 0));
                spatial.Register(new Id("unit.hostile"), new Vec2(10, 0), 0);
                spatial.Register(new Id("area.trap_1"), new Vec2(1, 0), 0, new[] { Core.Foundation.EngineAdapter.CollisionLayers.TriggerOnly });
            });

            var result = fx.Host.Resolve(new Id("target.chain.all_any"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.hostile") }, result);
        }

        // -----------------------------------------------------------------
        // 3. sort_by hp_pct asc 选血量最低友方（含 party_lowest_hp_pct 自然序）
        // -----------------------------------------------------------------

        [Fact]
        public void PartyLowestHpPct_SelectsLowestHpFriendlyIncludingSelf()
        {
            const string rows = @"[
                { ""id"": ""target.chain.lowest_hp"", ""source"": ""party_lowest_hp_pct"" }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.ally_high"), HeroFaction, new Vec2(1, 0));
                units.Add(new Id("unit.ally_low"), HeroFaction, new Vec2(2, 0));
                units.Add(new Id("unit.enemy"), MonsterFaction, new Vec2(3, 0));

                RegisterHealth(powers, new Id("unit.caster"), 1.0);
                RegisterHealth(powers, new Id("unit.ally_high"), 0.9);
                RegisterHealth(powers, new Id("unit.ally_low"), 0.2);
                RegisterHealth(powers, new Id("unit.enemy"), 0.01);
            });

            var result = fx.Host.Resolve(new Id("target.chain.lowest_hp"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.ally_low") }, result);
        }

        [Fact]
        public void AllInShape_WithExplicitSortByHpPctAsc_SelectsLowestHpFriendly()
        {
            const string rows = @"[
                { ""id"": ""target.chain.lowest_hp_explicit"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 },
                  ""filters"": [""relation:friendly""],
                  ""sort_by"": { ""key"": ""hp_pct"", ""direction"": ""asc"" } }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.ally_high"), HeroFaction, new Vec2(1, 0));
                units.Add(new Id("unit.ally_low"), HeroFaction, new Vec2(2, 0));

                RegisterHealth(powers, new Id("unit.caster"), 1.0);
                RegisterHealth(powers, new Id("unit.ally_high"), 0.9);
                RegisterHealth(powers, new Id("unit.ally_low"), 0.2);

                spatial.Register(new Id("unit.caster"), new Vec2(0, 0), 0);
                spatial.Register(new Id("unit.ally_high"), new Vec2(1, 0), 0);
                spatial.Register(new Id("unit.ally_low"), new Vec2(2, 0), 0);
            });

            var result = fx.Host.Resolve(new Id("target.chain.lowest_hp_explicit"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.ally_low") }, result);
        }

        // -----------------------------------------------------------------
        // 4. threat_top
        // -----------------------------------------------------------------

        [Fact]
        public void ThreatTop_SelectsTopThreatSource()
        {
            const string rows = @"[
                { ""id"": ""target.chain.threat_top"", ""source"": ""threat_top"" }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.attacker_a"), MonsterFaction, new Vec2(1, 0));
                units.Add(new Id("unit.attacker_b"), MonsterFaction, new Vec2(2, 0));

                threat.AddThreat(new Id("unit.caster"), new Id("unit.attacker_a"), 10);
                threat.AddThreat(new Id("unit.caster"), new Id("unit.attacker_b"), 50);
            });

            var result = fx.Host.Resolve(new Id("target.chain.threat_top"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.attacker_b") }, result);
        }

        // -----------------------------------------------------------------
        // 5/6. current_target 存在/不存在（回退到 fallback 链）
        // -----------------------------------------------------------------

        [Fact]
        public void CurrentTarget_WhenAliveAndPresent_ReturnsIt()
        {
            const string rows = @"[
                { ""id"": ""target.chain.current"", ""source"": ""current_target"" }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.marked"), MonsterFaction, new Vec2(1, 0));
            });

            var result = fx.Host.Resolve(new Id("target.chain.current"), new Id("unit.caster"), new Id("unit.marked"));

            Assert.Equal(new[] { new Id("unit.marked") }, result);
        }

        [Fact]
        public void CurrentTarget_WhenAbsent_FallsBackToFallbackChain()
        {
            const string rows = @"[
                { ""id"": ""target.chain.current_with_fallback"", ""source"": ""current_target"",
                  ""fallback"": ""target.chain.self_fallback"" },
                { ""id"": ""target.chain.self_fallback"", ""source"": ""self"" }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
            });

            var result = fx.Host.Resolve(new Id("target.chain.current_with_fallback"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.caster") }, result);
        }

        [Fact]
        public void CurrentTarget_WhenDead_TreatedAsAbsent()
        {
            const string rows = @"[
                { ""id"": ""target.chain.current_with_fallback2"", ""source"": ""current_target"",
                  ""fallback"": ""target.chain.self_fallback2"" },
                { ""id"": ""target.chain.self_fallback2"", ""source"": ""self"" }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.dead_target"), MonsterFaction, new Vec2(1, 0), alive: false);
            });

            var result = fx.Host.Resolve(
                new Id("target.chain.current_with_fallback2"), new Id("unit.caster"), new Id("unit.dead_target"));

            Assert.Equal(new[] { new Id("unit.caster") }, result);
        }

        // -----------------------------------------------------------------
        // 7. all_in_shape + max_targets
        // -----------------------------------------------------------------

        [Fact]
        public void AllInShape_WithMaxTargets_LimitsResultCount()
        {
            const string rows = @"[
                { ""id"": ""target.chain.all_capped"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 },
                  ""filters"": [""relation:hostile""],
                  ""max_targets"": 2 }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                for (int i = 0; i < 5; i++)
                {
                    var id = new Id($"unit.enemy_{i}");
                    units.Add(id, MonsterFaction, new Vec2(i + 1, 0));
                    spatial.Register(id, new Vec2(i + 1, 0), 0);
                }
            });

            var result = fx.Host.Resolve(new Id("target.chain.all_capped"), new Id("unit.caster"));

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void AllInShape_WithMaxTargetsZero_ReturnsUnlimited()
        {
            const string rows = @"[
                { ""id"": ""target.chain.all_unlimited"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 },
                  ""filters"": [""relation:hostile""],
                  ""max_targets"": 0 }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                for (int i = 0; i < 5; i++)
                {
                    var id = new Id($"unit.enemy_{i}");
                    units.Add(id, MonsterFaction, new Vec2(i + 1, 0));
                    spatial.Register(id, new Vec2(i + 1, 0), 0);
                }
            });

            var result = fx.Host.Resolve(new Id("target.chain.all_unlimited"), new Id("unit.caster"));

            Assert.Equal(5, result.Count);
        }

        // -----------------------------------------------------------------
        // 8. cone 形状只含扇区内单位
        // -----------------------------------------------------------------

        [Fact]
        public void ConeShape_OnlyIncludesUnitsInSector()
        {
            const string rows = @"[
                { ""id"": ""target.chain.cone"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""cone"", ""radius"": 50, ""angle"": 1.5707963267948966 } }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                // 施法者朝向 +X（facing=0）。
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0), facing: 0);
                units.Add(new Id("unit.in_front"), MonsterFaction, new Vec2(10, 0));
                units.Add(new Id("unit.behind"), MonsterFaction, new Vec2(-10, 0));

                spatial.Register(new Id("unit.in_front"), new Vec2(10, 0), 0);
                spatial.Register(new Id("unit.behind"), new Vec2(-10, 0), 0);
            });

            var result = fx.Host.Resolve(new Id("target.chain.cone"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.in_front") }, result);
        }

        // -----------------------------------------------------------------
        // 9. Expr 过滤（target.hp_pct < 0.5）
        // -----------------------------------------------------------------

        [Fact]
        public void ExprFilter_HpPctBelowThreshold_FiltersCorrectly()
        {
            const string rows = @"[
                { ""id"": ""target.chain.expr_hp"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 },
                  ""filters"": [""relation:hostile"", ""target.hp_pct < 0.5""],
                  ""max_targets"": 0 }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.low_hp"), MonsterFaction, new Vec2(1, 0));
                units.Add(new Id("unit.high_hp"), MonsterFaction, new Vec2(2, 0));

                RegisterHealth(powers, new Id("unit.low_hp"), 0.3);
                RegisterHealth(powers, new Id("unit.high_hp"), 0.9);

                spatial.Register(new Id("unit.low_hp"), new Vec2(1, 0), 0);
                spatial.Register(new Id("unit.high_hp"), new Vec2(2, 0), 0);
            });

            var result = fx.Host.Resolve(new Id("target.chain.expr_hp"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.low_hp") }, result);
        }

        // -----------------------------------------------------------------
        // 10. tag: 过滤
        // -----------------------------------------------------------------

        [Fact]
        public void TagFilter_OnlyIncludesTaggedUnits()
        {
            const string rows = @"[
                { ""id"": ""target.chain.tagged"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 },
                  ""filters"": [""tag:item.marked""],
                  ""max_targets"": 0 }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.marked"), MonsterFaction, new Vec2(1, 0), tags: TargetingTestSupport.Ids("item.marked"));
                units.Add(new Id("unit.plain"), MonsterFaction, new Vec2(2, 0));

                spatial.Register(new Id("unit.marked"), new Vec2(1, 0), 0);
                spatial.Register(new Id("unit.plain"), new Vec2(2, 0), 0);
            });

            var result = fx.Host.Resolve(new Id("target.chain.tagged"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.marked") }, result);
        }

        // -----------------------------------------------------------------
        // 11. 死亡单位被 alive 排除
        // -----------------------------------------------------------------

        [Fact]
        public void AliveFilter_ExcludesDeadUnits()
        {
            const string rows = @"[
                { ""id"": ""target.chain.alive_only"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 },
                  ""filters"": [""alive""],
                  ""max_targets"": 0 }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.dead"), MonsterFaction, new Vec2(1, 0), alive: false);
                units.Add(new Id("unit.living"), MonsterFaction, new Vec2(2, 0));

                spatial.Register(new Id("unit.dead"), new Vec2(1, 0), 0);
                spatial.Register(new Id("unit.living"), new Vec2(2, 0), 0);
            });

            var result = fx.Host.Resolve(new Id("target.chain.alive_only"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.living") }, result);
        }

        // -----------------------------------------------------------------
        // 12/13. 自定义策略注册并被链引用 / 重名注册抛异常
        // -----------------------------------------------------------------

        private sealed class FurthestInShapeStrategy : ITargetSourceStrategy
        {
            public string Name => "furthest_in_shape";

            public IReadOnlyList<Id> Collect(TargetContext ctx)
            {
                var shape = ctx.Shape!.Value;
                return ctx.Spatial.QueryShape(shape, Core.Foundation.EngineAdapter.QueryFilter.None)
                    .Select(id => (Id: id, Dist: Vec2.Distance(ctx.Origin, ctx.Units.GetPosition(id))))
                    .OrderByDescending(x => x.Dist)
                    .ThenBy(x => x.Id)
                    .Select(x => x.Id)
                    .ToList();
            }
        }

        [Fact]
        public void CustomStrategy_RegisteredAndReferencedByChain_Works()
        {
            const string rows = @"[
                { ""id"": ""target.chain.furthest"", ""source"": ""furthest_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 } }
            ]";

            var strategies = DefaultStrategies();
            strategies.Register(new FurthestInShapeStrategy());

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.near"), MonsterFaction, new Vec2(1, 0));
                units.Add(new Id("unit.far"), MonsterFaction, new Vec2(20, 0));

                spatial.Register(new Id("unit.near"), new Vec2(1, 0), 0);
                spatial.Register(new Id("unit.far"), new Vec2(20, 0), 0);
            }, strategies: strategies);

            var result = fx.Host.Resolve(new Id("target.chain.furthest"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.far") }, result);
        }

        [Fact]
        public void RegisterAll_ThenRegisterDuplicateBuiltinName_Throws()
        {
            var strategies = DefaultStrategies();
            Assert.Throws<InvalidOperationException>(() => strategies.Register(new FurthestSameName()));
        }

        private sealed class FurthestSameName : ITargetSourceStrategy
        {
            public string Name => BuiltinTargetStrategies.Self;
            public IReadOnlyList<Id> Collect(TargetContext ctx) => Array.Empty<Id>();
        }

        // -----------------------------------------------------------------
        // 15. 运行期回退深度保护
        // -----------------------------------------------------------------

        [Fact]
        public void MutualFallback_ExceedsMaxDepth_Throws()
        {
            const string rows = @"[
                { ""id"": ""target.chain.loop_a"", ""source"": ""threat_top"", ""fallback"": ""target.chain.loop_b"" },
                { ""id"": ""target.chain.loop_b"", ""source"": ""threat_top"", ""fallback"": ""target.chain.loop_a"" }
            ]";

            // 不注册 ChainDefValidationRule：结构上互相引用对数据校验而言只是两条合法的
            // Reference，本测试专门验证运行期的 MaxFallbackDepth 独立防线（见 targeting/README.md
            // 判断记录 11）。
            var bus = TargetingTestSupport.MakeBus();
            var factions = BuildFactions(bus);
            var powers = BuildPowerHost(bus);
            var units = new FakeUnitAccess();
            units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
            var spatial = new StubSpatialQuery();
            var strategies = DefaultStrategies();

            var chainRegistry = TargetingTestSupport.BuildChainRegistry(bus, rows, strategies, withValidationRule: false);
            var report = chainRegistry.LoadAll();
            Assert.False(report.IsBlocking);

            var exprFactory = new TestExprHostFactory(units, powers);
            var host = new TargetHost(
                strategies, chainRegistry, units, spatial, factions, powers, exprFactory,
                threat: null, eventBus: bus, options: new TargetingOptions { MaxFallbackDepth = 8 });

            Assert.Throws<InvalidOperationException>(() =>
                host.Resolve(new Id("target.chain.loop_a"), new Id("unit.caster")));
        }

        // -----------------------------------------------------------------
        // 17/18. EmitResolvedEvent
        // -----------------------------------------------------------------

        [Fact]
        public void EmitResolvedEvent_DefaultFalse_DoesNotPublish()
        {
            const string rows = @"[
                { ""id"": ""target.chain.self_only"", ""source"": ""self"" }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
            });

            var received = new List<TargetingResolvedEvent>();
            fx.Bus.Subscribe<TargetingResolvedEvent>(RulesEventKeys.TargetingResolved, e => received.Add(e));

            fx.Host.Resolve(new Id("target.chain.self_only"), new Id("unit.caster"));
            fx.Bus.DispatchPending();

            Assert.Empty(received);
        }

        [Fact]
        public void EmitResolvedEvent_WhenTrue_PublishesWithCorrectFields()
        {
            const string rows = @"[
                { ""id"": ""target.chain.self_only2"", ""source"": ""self"" }
            ]";

            var fx = Build(
                rows,
                (units, spatial, powers, threat) => units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0)),
                options: new TargetingOptions { EmitResolvedEvent = true });

            var received = new List<TargetingResolvedEvent>();
            fx.Bus.Subscribe<TargetingResolvedEvent>(RulesEventKeys.TargetingResolved, e => received.Add(e));

            var result = fx.Host.Resolve(new Id("target.chain.self_only2"), new Id("unit.caster"));
            fx.Bus.DispatchPending();

            Assert.Single(received);
            Assert.Equal(new Id("unit.caster"), received[0].UnitId);
            Assert.Equal(new Id("target.chain.self_only2"), received[0].ChainId);
            Assert.Equal(result, received[0].TargetIds);
        }

        [Fact]
        public void Constructor_EmitResolvedEventTrueWithoutEventBus_Throws()
        {
            var bus = TargetingTestSupport.MakeBus();
            var factions = BuildFactions(bus);
            var powers = BuildPowerHost(bus);
            var units = new FakeUnitAccess();
            var spatial = new StubSpatialQuery();
            var strategies = DefaultStrategies();
            var chainRegistry = TargetingTestSupport.BuildChainRegistry(bus, "[]", strategies);
            chainRegistry.LoadAll();
            var exprFactory = new TestExprHostFactory(units, powers);

            Assert.Throws<ArgumentException>(() => new TargetHost(
                strategies, chainRegistry, units, spatial, factions, powers, exprFactory,
                threat: null, eventBus: null, options: new TargetingOptions { EmitResolvedEvent = true }));
        }

        // -----------------------------------------------------------------
        // 19. 确定性：同输入两次结果一致
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_SameInputTwice_ProducesSameResult()
        {
            const string rows = @"[
                { ""id"": ""target.chain.determinism"", ""source"": ""nearest_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 },
                  ""filters"": [""relation:hostile"", ""alive""] }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.hostile_a"), MonsterFaction, new Vec2(5, 0));
                units.Add(new Id("unit.hostile_b"), MonsterFaction, new Vec2(15, 0));

                spatial.Register(new Id("unit.hostile_a"), new Vec2(5, 0), 0);
                spatial.Register(new Id("unit.hostile_b"), new Vec2(15, 0), 0);
            });

            var first = fx.Host.Resolve(new Id("target.chain.determinism"), new Id("unit.caster"));
            var second = fx.Host.Resolve(new Id("target.chain.determinism"), new Id("unit.caster"));

            Assert.Equal(first, second);
        }

        // -----------------------------------------------------------------
        // 额外：链未声明 shape 时退化为 circle radius = Options.DefaultRadius
        // -----------------------------------------------------------------

        [Fact]
        public void NoShapeDeclared_FallsBackToDefaultRadiusCircle()
        {
            const string rows = @"[
                { ""id"": ""target.chain.no_shape"", ""source"": ""all_in_shape"", ""max_targets"": 0 }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.within_default"), MonsterFaction, new Vec2(3, 0));

                spatial.Register(new Id("unit.within_default"), new Vec2(3, 0), 0);
            }, options: new TargetingOptions { DefaultRadius = 5 });

            var result = fx.Host.Resolve(new Id("target.chain.no_shape"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.within_default") }, result);
        }

        // -----------------------------------------------------------------
        // 未知链抛异常
        // -----------------------------------------------------------------

        [Fact]
        public void Resolve_UnknownChain_Throws()
        {
            var fx = Build("[]", (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
            });

            Assert.Throws<ArgumentException>(() =>
                fx.Host.Resolve(new Id("target.chain.does_not_exist"), new Id("unit.caster")));
        }

        // -----------------------------------------------------------------
        // 格子吸附（ADR-0013 决策 6、04 第 3.1 节 grid_snap，codex 第十八轮）："范围形状按格子中心
        // 采样"：候选原始坐标在范围边缘时，是否命中取决于它所属格子的中心点而不是原始坐标本身。
        // 两条用例都取施法者在原点、圆形范围半径的链，只改候选坐标与半径，制造"原始坐标在内、格子
        // 中心在外"与"原始坐标在外、格子中心在内"两种相反的采样口径变化，并各自对照同一份坐标在
        // 未启用格子吸附时的结果，证明差异确由 GridSnapCellSize 是否装配决定。
        // -----------------------------------------------------------------

        private static TargetingOptions GridSnapOptions(double cellSize) => new TargetingOptions
        {
            IsDiscreteStep = () => true,
            GridSnapCellSize = cellSize,
        };

        [Fact]
        public void AllInShape_GridSnapEnabled_RawPositionInside_CellCenterOutside_ExcludesCandidate()
        {
            const string rows = @"[
                { ""id"": ""target.chain.grid_snap_exclude"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 6.3 } }
            ]";

            // 候选 (5.9, 2.0) 到原点距离 ≈6.2298，落在半径 6.3 的圆内；cellSize=4 时它所属格子
            // 中心是 (6.0, 2.0)，到原点距离 = sqrt(40) ≈6.3246，超出半径 6.3——吸附后应被排除。
            void Setup(FakeUnitAccess units, StubSpatialQuery spatial, PowerHost powers, FakeThreatTable threat)
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.edge"), MonsterFaction, new Vec2(5.9, 2.0));
                spatial.Register(new Id("unit.edge"), new Vec2(5.9, 2.0), 0);
            }

            var withoutSnap = Build(rows, Setup);
            var withoutResult = withoutSnap.Host.Resolve(new Id("target.chain.grid_snap_exclude"), new Id("unit.caster"));
            Assert.Equal(new[] { new Id("unit.edge") }, withoutResult); // 对照组：未启用时按原始坐标命中。

            var withSnap = Build(rows, Setup, options: GridSnapOptions(4.0));
            var withResult = withSnap.Host.Resolve(new Id("target.chain.grid_snap_exclude"), new Id("unit.caster"));
            Assert.Empty(withResult);
        }

        [Fact]
        public void AllInShape_GridSnapEnabled_RawPositionOutside_CellCenterInside_IncludesCandidate()
        {
            const string rows = @"[
                { ""id"": ""target.chain.grid_snap_include"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 6.33 } }
            ]";

            // 候选 (7.9, 3.9) 到原点距离 ≈8.81，超出半径 6.33（未吸附时压根查不到，不只是判定为
            // 假）；它所属格子（cellSize=4）与上一条用例同一个格子，中心同样是 (6.0, 2.0)，到原点
            // 距离 ≈6.3246，落在半径 6.33 内——吸附后应被纳入。
            void Setup(FakeUnitAccess units, StubSpatialQuery spatial, PowerHost powers, FakeThreatTable threat)
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.edge"), MonsterFaction, new Vec2(7.9, 3.9));
                spatial.Register(new Id("unit.edge"), new Vec2(7.9, 3.9), 0);
            }

            var withoutSnap = Build(rows, Setup);
            var withoutResult = withoutSnap.Host.Resolve(new Id("target.chain.grid_snap_include"), new Id("unit.caster"));
            Assert.Empty(withoutResult); // 对照组：未启用时按原始坐标查不到。

            var withSnap = Build(rows, Setup, options: GridSnapOptions(4.0));
            var withResult = withSnap.Host.Resolve(new Id("target.chain.grid_snap_include"), new Id("unit.caster"));
            Assert.Equal(new[] { new Id("unit.edge") }, withResult);
        }

        [Fact]
        public void AllInShape_GridSnapCellSizeSetButIsDiscreteStepFalse_DoesNotSnap()
        {
            // IsDiscreteStep 未装配（默认 null，等价于恒连续模式）时，即便 GridSnapCellSize 已配置，
            // 也不应吸附——同 Core.Rules.Skill.SkillOptions.IsDiscreteStep 判断记录"两者都为真才
            // 生效"，只不过这里只设置了 GridSnapCellSize、不设置 IsDiscreteStep。
            const string rows = @"[
                { ""id"": ""target.chain.grid_snap_no_discrete"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 6.3 } }
            ]";

            var fx = Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                units.Add(new Id("unit.edge"), MonsterFaction, new Vec2(5.9, 2.0));
                spatial.Register(new Id("unit.edge"), new Vec2(5.9, 2.0), 0);
            }, options: new TargetingOptions { GridSnapCellSize = 4.0 }); // IsDiscreteStep 未装配。

            var result = fx.Host.Resolve(new Id("target.chain.grid_snap_no_discrete"), new Id("unit.caster"));

            Assert.Equal(new[] { new Id("unit.edge") }, result); // 与未启用格子吸附时结果一致。
        }
    }
}
