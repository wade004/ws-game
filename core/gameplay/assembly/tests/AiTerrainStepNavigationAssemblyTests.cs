using System;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// 手感落地 M4-W1a 补丁（AI 转向感知地形）的生产装配级复现：走真实 <see cref="GameplayAssembly"/>（竖直轴 + 地形高度 + 台阶高度 + 桩导航），
    /// 一只被 AI 驱动的生物从台阶墙另一侧追击玩家。AI 取路点与移动层同一口径（带台阶约束的 <c>FindPath</c>）时绕过墙进入攻击范围；
    /// 把装配赋给 AI 的约束清掉（旧行为）时它一路直冲，被移动层的台阶阻挡顶在墙前。另验证装配只在启用台阶阻挡时才赋值约束（缺省保持 null）。
    /// </summary>
    public sealed class AiTerrainStepNavigationAssemblyTests
    {
        private static readonly Id MapId = new Id("world.ai_terrain_nav_map");
        private static readonly Id PlayerId = new Id("unit.ai_terrain_nav_player");
        private static readonly Id PlayerFactionId = new Id("fac.ai_terrain_nav_player");
        private static readonly Id MonsterFactionId = new Id("fac.ai_terrain_nav_monster");
        private static readonly Id ArchetypeSample = new Id("arch.class.ai_terrain_nav_sample");
        private static readonly Id TemplateId = new Id("creature.ai_terrain_nav_wolf");

        private const double WallMinX = 8.0;
        private const double WallMaxX = 12.0;
        private const double WallHalfWidth = 4.0;
        private const double WallHeight = 2.0;
        private const double StepHeight = 0.5;

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}," +
            "{\"id\": \"stat.move_speed\", \"name_key\": \"l10n.stat.move_speed.name\", \"group\": \"primary\", \"default_base\": 4}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"arch.class.ai_terrain_nav_sample\", \"name_key\": \"l10n.arch.class.ai_terrain_nav_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, \"power_types\": [\"arch.power.health\"]}]";

        private const string TierDefinitionRows =
            "[{\"id\": \"creature.tier.ai_terrain_nav\", \"name_key\": \"l10n.creature.tier.ai_terrain_nav.name\", " +
            "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}]";

        private const string RotationRows =
            "[{ \"id\": \"ai.rotation.ai_terrain_nav\", \"entries\": [" +
            "{ \"priority\": 1, \"condition\": \"false\", \"skill_id\": \"skill.ai_terrain_nav_never\" }" +
            "] }]";

        private const string SkillDefRows =
            "[{\"id\": \"skill.ai_terrain_nav_never\", \"school\": \"skill.school.ai_terrain_nav\", \"kind\": \"active\"," +
            " \"range\": 0, \"cast_time\": 0, \"respects_gcd\": true," +
            " \"target_shape_ref\": \"target.ai_terrain_nav\", \"effects\": []}]";

        private const string TargetChainDefRows =
            "[{\"id\": \"target.ai_terrain_nav\", \"source\": \"self\"}]";

        // perception_radius/leash_range 取明显大于出生距离（20）的值：生物一出生就感知到玩家转入追击，本用例 tick 数内不会因脱离租界折返。
        private const string ProfileRows =
            "[{ \"id\": \"ai.behavior.ai_terrain_nav\", \"perception_radius\": 50, \"leash_range\": 200, " +
            "\"combat_return_policy\": \"return_to_spawn\", \"rotation_ref\": \"ai.rotation.ai_terrain_nav\" }]";

        private const string TemplateRows =
            "[{\"id\": \"creature.ai_terrain_nav_wolf\", \"name_key\": \"l10n.creature.ai_terrain_nav_wolf.name\", " +
            "\"level\": 1, \"tier\": \"creature.tier.ai_terrain_nav\", " +
            "\"base_stats\": {\"stat.max_health\": 100, \"stat.move_speed\": 4}, " +
            "\"faction_id\": \"fac.ai_terrain_nav_monster\", \"display_ref\": \"display.ai_terrain_nav_wolf\", " +
            "\"ai_behavior_ref\": \"ai.behavior.ai_terrain_nav\"}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public Id WolfId;
            public double MaxAbsY;
        }

        /// <summary>构造夹具：<paramref name="withTerrain"/> 为假时没有竖直轴；<paramref name="stepHeight"/> 为 null 时有地形但不启用台阶阻挡。</summary>
        private static Fixture Build(bool withTerrain, double? stepHeight)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("creature.tier_definition", Envelope("creature.tier_definition", TierDefinitionRows))
                .Add("creature.template", Envelope("creature.template", TemplateRows))
                .Add("ai.behavior_profile", Envelope("ai.behavior_profile", ProfileRows))
                .Add("ai.rotation", Envelope("ai.rotation", RotationRows))
                .Add("ai.patrol_path", Envelope("ai.patrol_path", "[]"))
                .Add("skill.def", Envelope("skill.def", SkillDefRows))
                .Add("target.chain_def", Envelope("target.chain_def", TargetChainDefRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"))
                .Add("gobj.template", Envelope("gobj.template", "[]"))
                .Add("gobj.lock", Envelope("gobj.lock", "[]"))
                .Add("dialog.gossip_menu", Envelope("dialog.gossip_menu", "[]"))
                .Add("dialog.story_tree", Envelope("dialog.story_tree", "[]"))
                .Add("fac.faction", Envelope("fac.faction",
                    "[{\"id\": \"" + PlayerFactionId.Value + "\", \"name_key\": \"l10n.fac.ai_terrain_nav_player\", \"default_reaction\": \"neutral\"}," +
                    "{\"id\": \"" + MonsterFactionId.Value + "\", \"name_key\": \"l10n.fac.ai_terrain_nav_monster\", \"default_reaction\": \"neutral\"}]"))
                .Add("fac.reaction_matrix", Envelope("fac.reaction_matrix",
                    "[{\"id\": \"fac.ai_terrain_nav_r1\", \"from\": \"" + PlayerFactionId.Value + "\", \"to\": \"" + MonsterFactionId.Value + "\", \"reaction\": \"hostile\"}," +
                    "{\"id\": \"fac.ai_terrain_nav_r2\", \"from\": \"" + MonsterFactionId.Value + "\", \"to\": \"" + PlayerFactionId.Value + "\", \"reaction\": \"hostile\"}]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var saveSystem = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.ai_terrain_nav")), bus);

            MovementOptions? movement = null;
            if (withTerrain)
            {
                var terrain = new MapTerrainHeights();
                terrain.SetRegions(MapId, new[]
                {
                    new TerrainRegion(new Vec2(-1000, -1000), new Vec2(1000, 1000), ground: 0.0),
                    new TerrainRegion(new Vec2(WallMinX, -WallHalfWidth), new Vec2(WallMaxX, WallHalfWidth), ground: WallHeight),
                });
                movement = new MovementOptions { Vertical = new VerticalAxisOptions { Terrain = terrain, StepHeight = stepHeight } };
            }

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId,
                navigation: new StubNavigation2D(),
                movementOptions: movement);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            var wolfId = gameplay.Carriers.Creatures.Spawn(TemplateId, MapId, new Vec2(20, 0), facing: Math.PI);
            world.Tick(SimStep.Continuous(0.1));
            return new Fixture { World = world, Gameplay = gameplay, WolfId = wolfId };
        }

        private static Vec2 WolfPosition(Fixture fx) => ((Entity)fx.World.GetEntity(fx.WolfId)!).Position;

        private static void Run(Fixture fx, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                fx.World.Tick(SimStep.Continuous(0.1));
                fx.MaxAbsY = Math.Max(fx.MaxAbsY, Math.Abs(WolfPosition(fx).Y));
            }
        }

        [Fact]
        public void Chase_BehindAStepWall_DetoursToTheTarget_AndWithoutTheAssembledConstraintItStaysAtTheWall()
        {
            var attackRange = new AiOptions().AttackRange;

            // 新行为：装配把台阶约束交给 AI，追击绕过墙进入攻击范围。
            var withConstraint = Build(withTerrain: true, stepHeight: StepHeight);
            Assert.NotNull(withConstraint.Gameplay.Carriers.Rules.Ai.TerrainStepConstraint);
            Run(withConstraint, 400);
            var reached = Vec2.Distance(WolfPosition(withConstraint), Vec2.Zero);
            Assert.True(reached <= attackRange + 0.5, $"应绕过台阶墙追到玩家（攻击范围 {attackRange}），实际距离 {reached}，位置 {WolfPosition(withConstraint)}");
            Assert.True(withConstraint.MaxAbsY > WallHalfWidth, $"绕行必须走出墙的侧面（|y| > {WallHalfWidth}），最大 |y|={withConstraint.MaxAbsY}");

            // 旧行为（把装配赋给 AI 的约束清掉）：直冲台阶墙，被移动层的台阶阻挡顶在墙前（墙东面 x = WallMaxX）。
            var oldBehavior = Build(withTerrain: true, stepHeight: StepHeight);
            oldBehavior.Gameplay.Carriers.Rules.Ai.TerrainStepConstraint = null;
            Run(oldBehavior, 400);
            var stuck = WolfPosition(oldBehavior);
            Assert.True(stuck.X >= WallMaxX - 1e-6 && stuck.X < WallMaxX + 1.0, $"应顶在墙前，实际 {stuck}");
            Assert.True(Vec2.Distance(stuck, Vec2.Zero) > attackRange + 1.0);
            Assert.True(oldBehavior.MaxAbsY < WallHalfWidth, "旧行为不会绕行");
        }

        [Fact]
        public void Assembly_OnlyHandsTheConstraintToAi_WhenStepBlockingIsActive()
        {
            // 没有竖直轴 / 有地形但没有台阶高度：AI 的寻路调用保持旧重载（约束为 null），逐位不变。
            Assert.Null(Build(withTerrain: false, stepHeight: null).Gameplay.Carriers.Rules.Ai.TerrainStepConstraint);
            Assert.Null(Build(withTerrain: true, stepHeight: null).Gameplay.Carriers.Rules.Ai.TerrainStepConstraint);

            var active = Build(withTerrain: true, stepHeight: StepHeight);
            Assert.Same(active.Gameplay.Carriers.VerticalMotion, active.Gameplay.Carriers.Rules.Ai.TerrainStepConstraint);
        }
    }
}
