using System;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Assembly
{
    /// <summary>
    /// ADR-0026《技能位移的连续模式》端到端验收：真实 <see cref="CarriersAssembly"/>（内含真实
    /// <see cref="Core.Rules.Assembly.RulesAssembly"/>/<see cref="Core.Rules.Skill.SkillHost"/>/
    /// <see cref="Core.Rules.Skill.EffectDispatcher"/> 与真实 <see cref="MovementHost"/>/
    /// <see cref="MovementTickHandler"/>，二者经本次新增的
    /// <c>CarriersAssembly</c> 构造期一行 <c>Rules.Skill.DisplacementSink = Movement;</c> 接通）
    /// + 带阻挡的 <see cref="StubNavigation2D"/>，完整复现消费方反馈（M-C10 第 1 节）的最小场景：
    /// 合法墙前目标 <c>(1,0)</c> 使用 static leap <c>(3,0)</c>，墙区间 <c>x=1.5..2</c>——瞬移模式
    /// （<see cref="FeedbackMinimalScenario_Instant_JumpsDirectlyThroughWall_ExistingBehaviorUnchanged"/>）
    /// 复现反馈描述的"终点直接跳变"；连续模式
    /// （<see cref="FeedbackMinimalScenario_Continuous_Stop_StopsBeforeWall"/>）验证新增的阻挡裁决。
    /// </summary>
    public sealed class C10a_ContinuousMoveEndToEndTests
    {
        private static readonly Id Map = new Id("map.c10a_e2e");
        private static readonly Id PlayerId = new Id("unit.c10a_player");
        private static readonly Id FactionId = new Id("fac.c10a_player");
        private static readonly Id ClassId = new Id("arch.class.c10a_sample");
        private static readonly Id ChainSelf = new Id("target.chain.c10a_self");
        private static readonly Id LeapContinuousSkill = new Id("skill.c10a_leap_continuous");
        private static readonly Id LeapContinuousRevertSkill = new Id("skill.c10a_leap_continuous_revert");
        private static readonly Id LeapInstantSkill = new Id("skill.c10a_leap_instant");

        private sealed class Fixture : IDisposable
        {
            public WorldSim World = null!;
            public CarriersAssembly Carriers = null!;
            public PlayerUnit Player = null!;

            public void Dispose() => World.Dispose();
        }

        private static void AddMinimalRequiredTables(InMemoryDataSource source)
        {
            // 同 core/carriers/assembly/tests/CarriersAssemblyTests.cs AddMinimalRequiredTables 判断
            // 记录：四张表即便零行也必须"加载过"，否则 ItemBudgetValidationRule/StatHost/
            // CombatDataLoader 在构造期直接抛异常，与本测试真正关心的"连续位移"无关，先垫上。
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

        private static Fixture Build(StubNavigation2D navigation)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalRequiredTables(source);

            // target.chain_def：source=self，命中施法者自身——leap/charge 位移施法者本身，最小场景
            // 不需要第二个单位/阵营矩阵（同消费方反馈"static leap"最小复现，只有一个施法者）。
            source.Add("target.chain_def",
                "{\"table\": \"target.chain_def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + ChainSelf.Value + "\", \"source\": \"self\", \"max_targets\": 1}" +
                "]}");

            source.Add("skill.def",
                "{\"table\": \"skill.def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + LeapContinuousSkill.Value + "\", \"school\": \"school.c10a\", \"kind\": \"active\", " +
                "\"range\": 0, \"cast_time\": 0, \"respects_gcd\": false, \"target_shape_ref\": \"" + ChainSelf.Value + "\", " +
                "\"effects\": [{\"kind\": \"move\", \"params\": {\"mode\": \"leap\", \"point\": {\"x\": 3, \"y\": 0}, " +
                "\"motion\": \"continuous\", \"speed\": 5, \"blocking\": \"stop\"}}]}," +
                "{\"id\": \"" + LeapContinuousRevertSkill.Value + "\", \"school\": \"school.c10a\", \"kind\": \"active\", " +
                "\"range\": 0, \"cast_time\": 0, \"respects_gcd\": false, \"target_shape_ref\": \"" + ChainSelf.Value + "\", " +
                "\"effects\": [{\"kind\": \"move\", \"params\": {\"mode\": \"leap\", \"point\": {\"x\": 3, \"y\": 0}, " +
                "\"motion\": \"continuous\", \"speed\": 5, \"blocking\": \"revert\"}}]}," +
                "{\"id\": \"" + LeapInstantSkill.Value + "\", \"school\": \"school.c10a\", \"kind\": \"active\", " +
                "\"range\": 0, \"cast_time\": 0, \"respects_gcd\": false, \"target_shape_ref\": \"" + ChainSelf.Value + "\", " +
                "\"effects\": [{\"kind\": \"move\", \"params\": {\"mode\": \"leap\", \"point\": {\"x\": 3, \"y\": 0}}}]}" +
                "]}");

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "C10a_ContinuousMoveEndToEndTests 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(20260911UL);

            var carriers = new CarriersAssembly(bus, registry, rng, world, spatial, navigation);

            // 消费方反馈最小场景坐标：目标（此处即施法者自身）在墙前 (1,0)。
            var player = new PlayerUnit(PlayerId, Map, FactionId, ClassId) { Position = new Vec2(1, 0) };
            world.AddEntity(player);
            bus.DispatchPending();

            return new Fixture { World = world, Carriers = carriers, Player = player };
        }

        // -----------------------------------------------------------------
        // 消费方反馈最小场景复现：(1,0) -> static leap (3,0)，墙 x=1.5..2。
        // -----------------------------------------------------------------

        [Fact]
        public void FeedbackMinimalScenario_Instant_JumpsDirectlyThroughWall_ExistingBehaviorUnchanged()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(Map, new[] { new Rect(new Vec2(1.5, -1), new Vec2(2, 1)) });
            using var fx = Build(nav);

            var result = fx.Carriers.Rules.Skill.CastSkill(PlayerId, LeapInstantSkill, Array.Empty<Id>());
            fx.Carriers.Rules.Bus.DispatchPending();

            Assert.True(result.Success);
            // 反馈原句"终点发生一次位置跳变且 walkable=true"：瞬移分支不做任何寻路/碰撞裁决，效果
            // 结算当下就已经落地在声明的目标点，即便中途穿过墙体——既有行为逐字节不变。
            Assert.Equal(new Vec2(3, 0), fx.Carriers.Units.GetPosition(PlayerId));
        }

        [Fact]
        public void FeedbackMinimalScenario_Continuous_Stop_StopsBeforeWall()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(Map, new[] { new Rect(new Vec2(1.5, -1), new Vec2(2, 1)) });
            using var fx = Build(nav);

            var result = fx.Carriers.Rules.Skill.CastSkill(PlayerId, LeapContinuousSkill, Array.Empty<Id>());
            fx.Carriers.Rules.Bus.DispatchPending();
            Assert.True(result.Success);

            // CastSkill 当下只组装并转交 ControlledDisplacementRequest（见 EffectDispatcher.
            // ApplyContinuousMove），真正的逐 tick 推进要等 MovementTickHandler 在下一次 world.Tick
            // 才发生——效果结算这一刻，逻辑位置尚未改变。
            Assert.Equal(new Vec2(1, 0), fx.Carriers.Units.GetPosition(PlayerId));

            fx.World.Tick(SimStep.Continuous(1.0));

            var pos = fx.Carriers.Units.GetPosition(PlayerId);
            Assert.True(pos.X < 1.5, $"应停在墙前，实际 x={pos.X}");
            Assert.True(pos.X > 1.0, $"应确有推进，实际 x={pos.X}");
        }

        [Fact]
        public void FeedbackMinimalScenario_Continuous_Revert_ReturnsToOrigin()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(Map, new[] { new Rect(new Vec2(1.5, -1), new Vec2(2, 1)) });
            using var fx = Build(nav);

            var result = fx.Carriers.Rules.Skill.CastSkill(PlayerId, LeapContinuousRevertSkill, Array.Empty<Id>());
            fx.Carriers.Rules.Bus.DispatchPending();
            Assert.True(result.Success);

            fx.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(new Vec2(1, 0), fx.Carriers.Units.GetPosition(PlayerId));
        }

        // -----------------------------------------------------------------
        // 兼容性：无阻挡时连续模式与瞬移模式对同一输入终点一致（ADR-0026 兼容性 4）。
        // -----------------------------------------------------------------

        [Fact]
        public void NoObstruction_ContinuousAndInstant_ArriveAtSameFinalPosition()
        {
            using var fxInstant = Build(new StubNavigation2D());
            fxInstant.Carriers.Rules.Skill.CastSkill(PlayerId, LeapInstantSkill, Array.Empty<Id>());
            fxInstant.Carriers.Rules.Bus.DispatchPending();
            var instantFinal = fxInstant.Carriers.Units.GetPosition(PlayerId);

            using var fxContinuous = Build(new StubNavigation2D());
            fxContinuous.Carriers.Rules.Skill.CastSkill(PlayerId, LeapContinuousSkill, Array.Empty<Id>());
            fxContinuous.Carriers.Rules.Bus.DispatchPending();
            for (var i = 0; i < 10; i++)
            {
                fxContinuous.World.Tick(SimStep.Continuous(1.0));
            }

            var continuousFinal = fxContinuous.Carriers.Units.GetPosition(PlayerId);

            Assert.Equal(instantFinal, continuousFinal);
        }
    }
}
