using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Numbers.Faction;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// ADR-0079 验收（消费方反馈第二十二批，框架内部时序缺陷，一次异常永久锁死主循环）：
    /// <see cref="Core.Carriers.Creature.CreatureFactory.Despawn"/> 此前同步注销 Stats/Powers，
    /// 而 <see cref="IWorldSim"/> 对该实体的真正移除要等到某次 <see cref="IWorldSim.Tick"/> 阶段 8
    /// （生命周期清理）——两次调用之间若该实体仍有生效的 AI 移动意图，下一拍
    /// <c>Core.Carriers.Unit.MovementTickHandler.ResolveSpeed</c> 会向已注销的
    /// <c>Core.Numbers.StatBlock.StatHost.GetStat</c> 要属性，抛 <see cref="System.InvalidOperationException"/>
    /// 中止整个 Tick，<see cref="IWorldSim.MarkForDestruction"/> 标记的实体没被真正移除，下一拍同样
    /// 的路径再抛一次，永久卡死。本文件脱离引擎，全程走真实 <see cref="GameplayAssembly"/> 生产装配
    /// 入口，复现消费方给出的"两拍之间 Despawn 一个仍被 AI 驱动追击移动的单位"这一最小场景。
    /// </summary>
    public sealed class ADR0079_DespawnDuringAiMovementTests
    {
        private static readonly Id MapId = new Id("world.adr0079_test_map");
        private static readonly Id PlayerId = new Id("unit.adr0079_test_player");
        private static readonly Id PlayerFactionId = new Id("fac.adr0079_test_player");
        private static readonly Id MonsterFactionId = new Id("fac.adr0079_test_monster");
        private static readonly Id ArchetypeSample = new Id("arch.class.adr0079_test_sample");
        private static readonly Id ProfileId = new Id("ai.behavior.adr0079_test");
        private static readonly Id RotationId = new Id("ai.rotation.adr0079_test");
        private static readonly Id TemplateId = new Id("creature.adr0079_test_wolf");
        private static readonly Id NoAiTemplateId = new Id("creature.adr0079_test_rock");
        private static readonly Id TierId = new Id("creature.tier.adr0079_test");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}," +
            "{\"id\": \"stat.move_speed\", \"name_key\": \"l10n.stat.move_speed.name\", \"group\": \"primary\", \"default_base\": 4}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.adr0079_test_sample" + "\", \"name_key\": \"l10n.arch.class.adr0079_test_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, \"power_types\": [\"arch.power.health\"]}]";

        private const string TierDefinitionRows =
            "[{\"id\": \"" + "creature.tier.adr0079_test" + "\", \"name_key\": \"l10n.creature.tier.adr0079_test.name\", " +
            "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}]";

        private const string RotationRows =
            "[{ \"id\": \"" + "ai.rotation.adr0079_test" + "\", \"entries\": [" +
            "{ \"priority\": 1, \"condition\": \"false\", \"skill_id\": \"skill.adr0079_test_never\" }" +
            "] }]";

        private const string SkillDefRows =
            "[{\"id\": \"skill.adr0079_test_never\", \"school\": \"skill.school.adr0079_test\", \"kind\": \"active\"," +
            " \"range\": 0, \"cast_time\": 0, \"respects_gcd\": true," +
            " \"target_shape_ref\": \"target.adr0079_test\", \"effects\": []}]";

        private const string TargetChainDefRows =
            "[{\"id\": \"target.adr0079_test\", \"source\": \"self\"}]";

        // perception_radius/leash_range 都取一个明显大于本用例出生距离（20）的值，保证生物一出生
        // 就能感知到玩家并转入 Chase，且在本用例全部 tick 数内不会因为"追出租界范围"提前折返
        // （AiOptions.AttackRange 默认 2，MoveSpeed 默认 4，dt=0.1 时每拍位移 0.4，几十拍内追不到
        // 攻击距离，不会转入 Combat，因此本用例全程停留在持续产生 move 意图的 Chase 态）。
        private const string ProfileRows =
            "[{ \"id\": \"" + "ai.behavior.adr0079_test" + "\", \"perception_radius\": 50, \"leash_range\": 200, " +
            "\"combat_return_policy\": \"return_to_spawn\", \"rotation_ref\": \"" + "ai.rotation.adr0079_test" + "\" }]";

        private const string TemplateRows =
            "[{\"id\": \"" + "creature.adr0079_test_wolf" + "\", \"name_key\": \"l10n.creature.adr0079_test_wolf.name\", " +
            "\"level\": 1, \"tier\": \"" + "creature.tier.adr0079_test" + "\", " +
            "\"base_stats\": {\"stat.max_health\": 100, \"stat.move_speed\": 4}, " +
            "\"faction_id\": \"" + "fac.adr0079_test_monster" + "\", \"display_ref\": \"display.adr0079_test_wolf\", " +
            "\"ai_behavior_ref\": \"" + "ai.behavior.adr0079_test" + "\"}," +
            "{\"id\": \"" + "creature.adr0079_test_rock" + "\", \"name_key\": \"l10n.creature.adr0079_test_rock.name\", " +
            "\"level\": 1, \"tier\": \"" + "creature.tier.adr0079_test" + "\", " +
            "\"base_stats\": {\"stat.max_health\": 100, \"stat.move_speed\": 4}, " +
            "\"faction_id\": \"" + "fac.adr0079_test_monster" + "\", \"display_ref\": \"display.adr0079_test_rock\"}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public List<Id> DestroyedEntityIds = new List<Id>();
            public List<(Id EntityId, string Reason)> DespawnedCreatures = new List<(Id, string)>();
        }

        private static Fixture Build()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
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
                    "[{\"id\": \"" + PlayerFactionId.Value + "\", \"name_key\": \"l10n.fac.adr0079_test_player\", \"default_reaction\": \"neutral\"}," +
                    "{\"id\": \"" + MonsterFactionId.Value + "\", \"name_key\": \"l10n.fac.adr0079_test_monster\", \"default_reaction\": \"neutral\"}]"))
                .Add("fac.reaction_matrix", Envelope("fac.reaction_matrix",
                    "[{\"id\": \"fac.adr0079_test_r1\", \"from\": \"" + PlayerFactionId.Value + "\", \"to\": \"" + MonsterFactionId.Value + "\", \"reaction\": \"hostile\"}," +
                    "{\"id\": \"fac.adr0079_test_r2\", \"from\": \"" + MonsterFactionId.Value + "\", \"to\": \"" + PlayerFactionId.Value + "\", \"reaction\": \"hostile\"}]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var saveSystem = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.adr0079_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            var fixture = new Fixture { Bus = bus, World = world, Gameplay = gameplay };
            bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, evt => fixture.DestroyedEntityIds.Add(evt.EntityId));
            bus.Subscribe<Core.Carriers.Common.CreatureDespawnedEvent>(
                Core.Carriers.Common.CarriersEventKeys.CreatureDespawned,
                evt => fixture.DespawnedCreatures.Add((evt.EntityId, evt.Reason)));

            // 首次 tick 把玩家自身的 entity.created 等事件派发掉，不影响后续断言（DestroyedEntityIds/
            // DespawnedCreatures 只关心 destroyed/despawned 两类事件，这里不需要额外清空）。
            world.Tick(SimStep.Continuous(0.1));

            return fixture;
        }

        /// <summary>
        /// 核心验收（消费方复现路径 1 对 1 复现）：两拍之间 Despawn 一个仍被 AI 驱动追击移动的单位。
        /// 改动前：下一拍抛 <see cref="System.InvalidOperationException"/>（见本方法内 Record.Exception
        /// 断言的异常文本），且该单位始终没被移除；改动后：不抛异常，单位在随后的清理阶段被真正移除，
        /// <see cref="EntityDestroyedEvent"/> 正常发出，后续各拍正常推进。
        /// </summary>
        [Fact]
        public void Despawn_UnitWithActiveAiMoveIntent_BetweenTicks_DoesNotThrow_AndUnitIsRemoved()
        {
            var fx = Build();
            var creatureId = fx.Gameplay.Carriers.Creatures.Spawn(TemplateId, MapId, new Vec2(20, 0), facing: System.Math.PI);

            // 建立"正在被 AI 驱动追击移动"这一前提：tick 1 完成 idle -> chase 状态转移，
            // tick 2 起 HandleChase 每拍产生一条 move 意图并经 MovementTickHandler 真正推进位置——
            // 用位置变化断言"确实在被移动"，不是只看 AI 内部状态机。
            for (var i = 0; i < 5; i++)
            {
                fx.World.Tick(SimStep.Continuous(0.1));
            }

            var positionBeforeDespawn = ((Core.Foundation.SimLoop.Entity)fx.World.GetEntity(creatureId)!).Position;
            Assert.NotEqual(new Vec2(20, 0), positionBeforeDespawn); // 确认已经被移动过，不是原地不动。

            // 两拍之间（不在任何 Tick 调用栈内）Despawn——与消费方复现路径完全一致。
            fx.Gameplay.Carriers.Creatures.Despawn(creatureId, "adr0079_test_reason");

            // CreatureDespawnedEvent 的发出时机与内容不变：Despawn 本身只 Enqueue，随后一次 Tick 的
            // 阶段 7（事件派发）才真正送达订阅者。
            Assert.Empty(fx.DespawnedCreatures);

            System.Exception? caught = null;
            for (var i = 0; i < 5 && caught == null; i++)
            {
                caught = Record.Exception(() => fx.World.Tick(SimStep.Continuous(0.1)));
            }

            Assert.Null(caught);

            Assert.Single(fx.DespawnedCreatures);
            Assert.Equal(creatureId, fx.DespawnedCreatures[0].EntityId);
            Assert.Equal("adr0079_test_reason", fx.DespawnedCreatures[0].Reason);

            Assert.Contains(creatureId, fx.DestroyedEntityIds);
            Assert.Null(fx.World.GetEntity(creatureId));

            // 属性/能量宿主在真正移除之后确实已注销，不留泄漏。
            Assert.False(fx.Gameplay.Carriers.Rules.Stats.IsRegistered(creatureId));
            Assert.False(fx.Gameplay.Carriers.Rules.Powers.IsRegistered(creatureId));
        }

        /// <summary>
        /// 同一拍内 Despawn（不是两拍之间）同样不抛、同样能正常移除：借
        /// <see cref="SimTickStartedEvent"/>（<see cref="WorldSim.Tick"/> 阶段 1 之前、PublishImmediate
        /// 立即派发）在某次 Tick 执行期间调用 Despawn，验证同一次 Tick 调用内完成"标记 → 移除"全程
        /// 不抛异常。
        /// </summary>
        [Fact]
        public void Despawn_UnitWithActiveAiMoveIntent_WithinSameTick_DoesNotThrow_AndUnitIsRemoved()
        {
            var fx = Build();
            var creatureId = fx.Gameplay.Carriers.Creatures.Spawn(TemplateId, MapId, new Vec2(20, 0), facing: System.Math.PI);

            for (var i = 0; i < 5; i++)
            {
                fx.World.Tick(SimStep.Continuous(0.1));
            }

            SubscriptionHandle? handle = null;
            handle = fx.Bus.Subscribe<SimTickStartedEvent>(SimEventKeys.TickStarted, _ =>
            {
                fx.Gameplay.Carriers.Creatures.Despawn(creatureId, "adr0079_test_same_tick_reason");
                handle!.Dispose(); // 只触发一次，不影响后续 tick。
            });

            var caught = Record.Exception(() => fx.World.Tick(SimStep.Continuous(0.1)));

            Assert.Null(caught);
            Assert.Single(fx.DespawnedCreatures);
            Assert.Equal("adr0079_test_same_tick_reason", fx.DespawnedCreatures[0].Reason);
            Assert.Contains(creatureId, fx.DestroyedEntityIds);
            Assert.Null(fx.World.GetEntity(creatureId));
        }

        /// <summary>
        /// 普通的"没有 AI 意图的单位被 Despawn"路径行为不变：无 <c>ai_behavior_ref</c> 的生物
        /// Despawn 后，移除时机（阶段 8）与事件顺序（先 <see cref="EntityDestroyedEvent"/>/
        /// <see cref="CreatureDespawnedEvent"/>，后续各拍不再出现）与本次改动之前一致。
        /// </summary>
        [Fact]
        public void Despawn_UnitWithoutAi_BehaviorUnchanged()
        {
            var fx = Build();
            var creatureId = fx.Gameplay.Carriers.Creatures.Spawn(NoAiTemplateId, MapId, new Vec2(5, 0), facing: 0);

            fx.Gameplay.Carriers.Creatures.Despawn(creatureId, "adr0079_test_no_ai_reason");
            Assert.Empty(fx.DespawnedCreatures); // 仍只 Enqueue，未派发。

            var caught = Record.Exception(() => fx.World.Tick(SimStep.Continuous(0.1)));

            Assert.Null(caught);
            Assert.Single(fx.DespawnedCreatures);
            Assert.Equal(creatureId, fx.DespawnedCreatures[0].EntityId);
            Assert.Equal("adr0079_test_no_ai_reason", fx.DespawnedCreatures[0].Reason);
            Assert.Contains(creatureId, fx.DestroyedEntityIds);
            Assert.Null(fx.World.GetEntity(creatureId));
            Assert.False(fx.Gameplay.Carriers.Rules.Stats.IsRegistered(creatureId));
            Assert.False(fx.Gameplay.Carriers.Rules.Powers.IsRegistered(creatureId));
        }
    }
}
