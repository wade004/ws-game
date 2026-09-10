using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Core.Rules.Skill;
using Adapters.Stub;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 静态差距根治回归测试（外部审计 audit-c9ff301-20260909）：<see cref="ISkillHost.FindUnits"/>
    /// 此前恒返回空列表。覆盖圆/扇形/矩形三种 <see cref="Shape"/> 与阵营过滤（<see cref="RelationFilter"/>）
    /// ——按 06 契约用注入的 <see cref="ISpatialQuery"/> + 形状/过滤条件实现，复用引擎适配层
    /// <see cref="StubSpatialQuery"/> 既有的形状判定，不在测试里重新验证几何算法本身。
    /// </summary>
    public sealed class ISkillHost_FindUnitsTests
    {
        private static readonly Id Caster = new Id("unit.p_findunits.caster");

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class World
        {
            public SkillHost Host = default!;
            public FakeUnitAccess Units = default!;
            public StubSpatialQuery Spatial = default!;
        }

        private static World Build(bool injectSpatial = true, bool injectFactions = true, SkillOptions? options = null)
        {
            var source = new InMemoryDataSource()
                .Add("skill.def", Envelope("skill.def", "[]"))
                .Add("skill.aura_def", Envelope("skill.aura_def", "[]"))
                .Add("skill.proc_def", Envelope("skill.proc_def", "[]"))
                .Add("skill.spell_mod_def", Envelope("skill.spell_mod_def", "[]"))
                .Add("skill.book", Envelope("skill.book", "[]"))
                .Add("stat.definition", Envelope("stat.definition", "[]"))
                .Add("fac.faction", Envelope("fac.faction",
                    "[{\"id\": \"fac.p_findunits.player\", \"name_key\": \"l10n.fac.player\", \"default_reaction\": \"neutral\"}," +
                    "{\"id\": \"fac.p_findunits.enemy\", \"name_key\": \"l10n.fac.enemy\", \"default_reaction\": \"neutral\"}," +
                    "{\"id\": \"fac.p_findunits.ally\", \"name_key\": \"l10n.fac.ally\", \"default_reaction\": \"neutral\"}]"))
                .Add("fac.reaction_matrix", Envelope("fac.reaction_matrix",
                    "[{\"id\": \"fac.reaction.p_findunits_1\", \"from\": \"fac.p_findunits.player\", \"to\": \"fac.p_findunits.enemy\", \"reaction\": \"hostile\"}," +
                    "{\"id\": \"fac.reaction.p_findunits_2\", \"from\": \"fac.p_findunits.player\", \"to\": \"fac.p_findunits.ally\", \"reaction\": \"friendly\"}]"));

            var bus = SkillWorldBuilder.CreateBus();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(SkillSchemas.Def);
            registry.RegisterSchema(SkillSchemas.AuraDef);
            registry.RegisterSchema(SkillSchemas.ProcDef);
            registry.RegisterSchema(SkillSchemas.SpellModDef);
            registry.RegisterSchema(SkillSchemas.Book);
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(FacSchemas.Faction);
            registry.RegisterSchema(FacSchemas.ReactionMatrix);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var stats = new StatHost(registry, bus);
            var powers = new PowerHost(Array.Empty<PowerTypeDefinition>(), bus);
            var rng = new RngHost(1);
            var units = new FakeUnitAccess();
            var combat = new FakeCombatHost();
            var targets = new FakeTargetHost();
            var exprs = new FakeExprHostFactory();
            var diagnostics = new InMemorySkillDiagnostics();
            var spatial = new StubSpatialQuery();
            var factions = new FactionMatrix(registry, bus);

            var host = new SkillHost(
                registry, bus, units, stats, powers, rng, combat, targets, exprs,
                injectSpatial ? spatial : null,
                options: options,
                diagnostics: diagnostics,
                factions: injectFactions ? factions : null);

            units.Add(Caster, Vec2.Zero, faction: new Id("fac.p_findunits.player"));

            return new World { Host = host, Units = units, Spatial = spatial };
        }

        [Fact]
        public void FindUnits_NoSpatialQueryInjected_ReturnsEmptyAndWarns()
        {
            var world = Build(injectSpatial: false);
            var shape = Shape.Circle(Vec2.Zero, 10);

            var result = world.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);

            Assert.Empty(result);
        }

        [Fact]
        public void FindUnits_Circle_ReturnsUnitsWithinRadius()
        {
            var world = Build();
            var inRange = new Id("unit.p_findunits.in_range");
            var outOfRange = new Id("unit.p_findunits.out_of_range");
            world.Units.Add(inRange, new Vec2(5, 0));
            world.Units.Add(outOfRange, new Vec2(50, 0));
            world.Spatial.Register(inRange, new Vec2(5, 0), 0);
            world.Spatial.Register(outOfRange, new Vec2(50, 0), 0);

            var shape = Shape.Circle(Vec2.Zero, 10);
            var result = world.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);

            Assert.Contains(inRange, result);
            Assert.DoesNotContain(outOfRange, result);
        }

        [Fact]
        public void FindUnits_Cone_ReturnsOnlyUnitsInsideAngle()
        {
            var world = Build();
            var inFront = new Id("unit.p_findunits.in_front");
            var behind = new Id("unit.p_findunits.behind");
            world.Units.Add(inFront, new Vec2(5, 0));
            world.Units.Add(behind, new Vec2(-5, 0));
            world.Spatial.Register(inFront, new Vec2(5, 0), 0);
            world.Spatial.Register(behind, new Vec2(-5, 0), 0);

            // 朝向 +X 轴（direction=0），半角约 45 度（angle=PI/2 全张角），半径 10。
            var shape = Shape.Cone(Vec2.Zero, direction: 0, angle: Math.PI / 2, radius: 10);
            var result = world.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);

            Assert.Contains(inFront, result);
            Assert.DoesNotContain(behind, result);
        }

        [Fact]
        public void FindUnits_Rect_ReturnsUnitsInsideBox()
        {
            var world = Build();
            var inside = new Id("unit.p_findunits.inside_box");
            var outside = new Id("unit.p_findunits.outside_box");
            world.Units.Add(inside, new Vec2(2, 2));
            world.Units.Add(outside, new Vec2(20, 20));
            world.Spatial.Register(inside, new Vec2(2, 2), 0);
            world.Spatial.Register(outside, new Vec2(20, 20), 0);

            var shape = Shape.Rect(Vec2.Zero, new Vec2(5, 5), rotation: 0);
            var result = world.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);

            Assert.Contains(inside, result);
            Assert.DoesNotContain(outside, result);
        }

        [Fact]
        public void FindUnits_ExcludesTriggerOnlyTaggedObjects()
        {
            var world = Build();
            var unit = new Id("unit.p_findunits.real_unit");
            var trigger = new Id("area.p_findunits.trigger");
            world.Units.Add(unit, Vec2.Zero);
            world.Spatial.Register(unit, Vec2.Zero, 0);
            world.Spatial.Register(trigger, Vec2.Zero, 0, new[] { CollisionLayers.TriggerOnly });

            var shape = Shape.Circle(Vec2.Zero, 10);
            var result = world.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);

            Assert.Contains(unit, result);
            Assert.DoesNotContain(trigger, result);
        }

        [Fact]
        public void FindUnits_AliveOnly_ExcludesDeadUnits()
        {
            var world = Build();
            var dead = new Id("unit.p_findunits.dead");
            world.Units.Add(dead, Vec2.Zero, alive: false);
            world.Spatial.Register(dead, Vec2.Zero, 0);

            var shape = Shape.Circle(Vec2.Zero, 10);
            var result = world.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);

            Assert.DoesNotContain(dead, result);
        }

        [Fact]
        public void FindUnits_RelationHostile_OnlyReturnsHostileFaction()
        {
            var world = Build();
            var enemy = new Id("unit.p_findunits.enemy");
            var ally = new Id("unit.p_findunits.ally");
            world.Units.Add(enemy, Vec2.Zero, faction: new Id("fac.p_findunits.enemy"));
            world.Units.Add(ally, Vec2.Zero, faction: new Id("fac.p_findunits.ally"));
            world.Spatial.Register(enemy, Vec2.Zero, 0);
            world.Spatial.Register(ally, Vec2.Zero, 0);

            var shape = Shape.Circle(Vec2.Zero, 10);
            var filter = new UnitFilter(RelationFilter.Hostile, aliveOnly: true, exclude: Caster);
            var result = world.Host.FindUnits(shape, Vec2.Zero, filter);

            Assert.Contains(enemy, result);
            Assert.DoesNotContain(ally, result);
        }

        [Fact]
        public void FindUnits_RelationFriendly_OnlyReturnsFriendlyFaction()
        {
            var world = Build();
            var enemy = new Id("unit.p_findunits.enemy");
            var ally = new Id("unit.p_findunits.ally");
            world.Units.Add(enemy, Vec2.Zero, faction: new Id("fac.p_findunits.enemy"));
            world.Units.Add(ally, Vec2.Zero, faction: new Id("fac.p_findunits.ally"));
            world.Spatial.Register(enemy, Vec2.Zero, 0);
            world.Spatial.Register(ally, Vec2.Zero, 0);

            var shape = Shape.Circle(Vec2.Zero, 10);
            var filter = new UnitFilter(RelationFilter.Friendly, aliveOnly: true, exclude: Caster);
            var result = world.Host.FindUnits(shape, Vec2.Zero, filter);

            Assert.Contains(ally, result);
            Assert.DoesNotContain(enemy, result);
        }

        [Fact]
        public void FindUnits_RelationRequiresReferenceAndFactions_NoExclude_ExcludesAll()
        {
            var world = Build();
            var enemy = new Id("unit.p_findunits.enemy");
            world.Units.Add(enemy, Vec2.Zero, faction: new Id("fac.p_findunits.enemy"));
            world.Spatial.Register(enemy, Vec2.Zero, 0);

            var shape = Shape.Circle(Vec2.Zero, 10);
            // 未提供 Exclude（参照单位）：Hostile/Friendly/Neutral 一律判不通过，见 SkillHost.PassesRelation
            // 判断记录"宁可漏收，不误纳"。
            var filter = new UnitFilter(RelationFilter.Hostile, aliveOnly: true);
            var result = world.Host.FindUnits(shape, Vec2.Zero, filter);

            Assert.Empty(result);
        }

        [Fact]
        public void FindUnits_NoFactionsInjected_RelationHostile_ExcludesAll()
        {
            var world = Build(injectFactions: false);
            var enemy = new Id("unit.p_findunits.enemy");
            world.Units.Add(enemy, Vec2.Zero, faction: new Id("fac.p_findunits.enemy"));
            world.Spatial.Register(enemy, Vec2.Zero, 0);

            var shape = Shape.Circle(Vec2.Zero, 10);
            var filter = new UnitFilter(RelationFilter.Hostile, aliveOnly: true, exclude: Caster);
            var result = world.Host.FindUnits(shape, Vec2.Zero, filter);

            Assert.Empty(result);
        }

        [Fact]
        public void FindUnits_Self_And_NotSelf()
        {
            var world = Build();
            var other = new Id("unit.p_findunits.other");
            world.Units.Add(other, Vec2.Zero);
            world.Spatial.Register(Caster, Vec2.Zero, 0);
            world.Spatial.Register(other, Vec2.Zero, 0);

            var shape = Shape.Circle(Vec2.Zero, 10);

            var selfOnly = world.Host.FindUnits(shape, Vec2.Zero, new UnitFilter(RelationFilter.Self, exclude: Caster));
            Assert.Equal(new[] { Caster }, selfOnly);

            var notSelf = world.Host.FindUnits(shape, Vec2.Zero, new UnitFilter(RelationFilter.NotSelf, exclude: Caster));
            Assert.DoesNotContain(Caster, notSelf);
            Assert.Contains(other, notSelf);
        }

        [Fact]
        public void FindUnits_ResultIsSortedById()
        {
            var world = Build();
            var b = new Id("unit.p_findunits.b");
            var a = new Id("unit.p_findunits.a");
            world.Units.Add(b, Vec2.Zero);
            world.Units.Add(a, Vec2.Zero);
            world.Spatial.Register(b, Vec2.Zero, 0);
            world.Spatial.Register(a, Vec2.Zero, 0);

            var shape = Shape.Circle(Vec2.Zero, 10);
            var result = world.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);

            var idx_a = result.ToList().IndexOf(a);
            var idx_b = result.ToList().IndexOf(b);
            Assert.True(idx_a < idx_b);
        }

        // -----------------------------------------------------------------
        // 格子吸附（ADR-0013 决策 6、04 第 3.1 节 grid_snap，codex 第十八轮）：SkillOptions.
        // IsDiscreteStep 与 GridSnapCellSize 都装配时，FindUnits 按候选所属格子中心点判定，而不是
        // 原始坐标——用例参数与 TargetHostTests 的同名场景保持一致（同一段几何计算，两个不同调用侧）。
        // -----------------------------------------------------------------

        [Fact]
        public void FindUnits_GridSnapEnabled_RawPositionInside_CellCenterOutside_ExcludesCandidate()
        {
            // 候选 (5.9, 2.0) 到原点距离 ≈6.2298，落在半径 6.3 的圆内；cellSize=4 时它所属格子
            // 中心是 (6.0, 2.0)，到原点距离 ≈6.3246，超出半径 6.3——吸附后应被排除。
            var edge = new Id("unit.p_findunits.edge_inside");
            var shape = Shape.Circle(Vec2.Zero, 6.3);

            var withoutOptions = Build();
            withoutOptions.Units.Add(edge, new Vec2(5.9, 2.0));
            withoutOptions.Spatial.Register(edge, new Vec2(5.9, 2.0), 0);
            var withoutResult = withoutOptions.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);
            Assert.Contains(edge, withoutResult); // 对照组：未启用时按原始坐标命中。

            var gridSnapOptions = new SkillOptions { IsDiscreteStep = () => true, GridSnapCellSize = 4.0 };
            var withOptions = Build(options: gridSnapOptions);
            withOptions.Units.Add(edge, new Vec2(5.9, 2.0));
            withOptions.Spatial.Register(edge, new Vec2(5.9, 2.0), 0);
            var withResult = withOptions.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);
            Assert.DoesNotContain(edge, withResult);
        }

        [Fact]
        public void FindUnits_GridSnapEnabled_RawPositionOutside_CellCenterInside_IncludesCandidate()
        {
            // 候选 (7.9, 3.9) 到原点距离 ≈8.81，超出半径 6.33（未吸附时压根查不到）；它所属格子
            // （cellSize=4）与上一条用例同一个格子，中心同样是 (6.0, 2.0)，到原点距离 ≈6.3246，
            // 落在半径 6.33 内——吸附后应被纳入。
            var edge = new Id("unit.p_findunits.edge_outside");
            var shape = Shape.Circle(Vec2.Zero, 6.33);

            var withoutOptions = Build();
            withoutOptions.Units.Add(edge, new Vec2(7.9, 3.9));
            withoutOptions.Spatial.Register(edge, new Vec2(7.9, 3.9), 0);
            var withoutResult = withoutOptions.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);
            Assert.DoesNotContain(edge, withoutResult); // 对照组：未启用时按原始坐标查不到。

            var gridSnapOptions = new SkillOptions { IsDiscreteStep = () => true, GridSnapCellSize = 4.0 };
            var withOptions = Build(options: gridSnapOptions);
            withOptions.Units.Add(edge, new Vec2(7.9, 3.9));
            withOptions.Spatial.Register(edge, new Vec2(7.9, 3.9), 0);
            var withResult = withOptions.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);
            Assert.Contains(edge, withResult);
        }

        [Fact]
        public void FindUnits_GridSnapCellSizeSetButIsDiscreteStepNotConfigured_DoesNotSnap()
        {
            // IsDiscreteStep 未装配（默认 null，恒视为连续模式）时，即便 GridSnapCellSize 已配置，
            // 也不应吸附。
            var edge = new Id("unit.p_findunits.edge_no_discrete");
            var shape = Shape.Circle(Vec2.Zero, 6.3);

            var options = new SkillOptions { GridSnapCellSize = 4.0 }; // IsDiscreteStep 未装配。
            var world = Build(options: options);
            world.Units.Add(edge, new Vec2(5.9, 2.0));
            world.Spatial.Register(edge, new Vec2(5.9, 2.0), 0);

            var result = world.Host.FindUnits(shape, Vec2.Zero, UnitFilter.Default);

            Assert.Contains(edge, result); // 与未启用格子吸附时结果一致。
        }
    }
}
