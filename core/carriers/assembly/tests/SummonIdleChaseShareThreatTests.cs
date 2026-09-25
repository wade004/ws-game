using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Creature;
using Core.Carriers.Summon;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Rules.Ai;
using Core.Rules.Combat;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Assembly
{
    /// <summary>
    /// ADR-0087（消费方第三十三批反馈1根治）：召唤物 <c>JoinCombat=true</c>/<c>SyncCombatState=true</c>/
    /// <c>ShareThreat=true</c>，主人在感知范围外交战时，召唤物此前会原地冻结在 <c>idle</c>——
    /// <c>AiHost.HandleIdleOrPatrol</c> 只看感知半径内的敌对单位、不读仇恨表，<c>SummonTickHandler.
    /// TryFollow</c> 又因 <c>ICombatHost.IsInCombat</c> 标志为 true 而跳过跟随，两条路径叠加导致
    /// 108 秒不动、不助战（消费方实测复现）。用真实 <see cref="CarriersAssembly"/> 装配（真实
    /// <see cref="AiHost"/> + <see cref="CombatHost"/> + <see cref="SummonHost"/> +
    /// <see cref="SummonTickHandler"/>，<c>world.Tick</c> 驱动），覆盖：
    /// <list type="bullet">
    /// <item>复现：主人在感知范围外（20 米）交战，<c>ShareThreat</c> 把主人的仇恨来源合并进召唤物
    /// 自己的仇恨表后，召唤物应从 <c>idle</c> 转入 <c>chase</c> 并朝主人的交战目标靠近（决定 1）。</item>
    /// <item>不变量：召唤物 <c>IsInCombat=true</c> 但仇恨表为空、感知范围内也没有敌对单位时，不应该
    /// 被这一个战斗标志挡住跟随——应继续跟向主人当前位置（决定 2：<c>TryFollow</c> 的跳过条件收紧为
    /// "AI 确实处于 chase/combat"）。</item>
    /// </list>
    /// </summary>
    public class SummonIdleChaseShareThreatTests
    {
        private static readonly Id MapId = new Id("map.idle_chase_test");
        private static readonly Id OwnerId = new Id("unit.idle_chase_owner");
        private static readonly Id OwnerFaction = new Id("fac.idle_chase_owner");
        private static readonly Id HostileFaction = new Id("fac.idle_chase_hostile");
        private static readonly Id ArchetypeId = new Id("arch.class.idle_chase_sample");

        private static readonly Id PetTemplateId = new Id("creature.idle_chase_pet");
        private static readonly Id HostileTemplateId = new Id("creature.idle_chase_hostile");
        private static readonly Id TierId = new Id("creature.tier.idle_chase_normal");
        private static readonly Id PetProfileId = new Id("ai.profile.idle_chase_pet");
        private static readonly Id RotationId = new Id("ai.rotation.idle_chase_trivial_false");
        private static readonly Id SkillNeverId = new Id("skill.idle_chase_never");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public CarriersAssembly Assembly = null!;
            public PlayerUnit Owner = null!;
        }

        // perception_radius=8：远小于主人与其交战目标之间的距离（21），复现消费方"召唤物 8 米内无敌"；
        // leash_range=30：覆盖主人所在的 20 米开外，使仇恨表顶端来源落在"在 leash_range 内"的候选范围。
        private static Fixture Build()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();

            source.Add("item.budget_curve",
                "{\"table\": \"item.budget_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.budget.idle_chase_default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]}");
            source.Add("combat.hit_table_config",
                "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.resist_curve",
                "{\"table\": \"combat.resist_curve\", \"schema_version\": 1, \"rows\": []}");

            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.idle_chase_max_health\", \"name_key\": \"l10n.stat.idle_chase_max_health.name\", " +
                "\"group\": \"primary\", \"default_base\": 0}," +
                "{\"id\": \"stat.move_speed\", \"name_key\": \"l10n.stat.move_speed.name\", " +
                "\"group\": \"primary\", \"default_base\": 4}" +
                "]}");
            // id 必须是 WellKnownPowers.Health（"arch.power.health"）本身——PowerHost.RegisterUnit
            // 按精确 id 匹配已登记的资源类型定义（不是随便一个自定义 id 都行，与 CombatTestSupport.
            // MakePowerHost 同一惯例）。
            source.Add("arch.power_type",
                "{\"table\": \"arch.power_type\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + WellKnownPowers.Health + "\", \"name_key\": \"l10n.power.idle_chase_health.name\", " +
                "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.idle_chase_max_health\"}, " +
                "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
                "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}" +
                "]}");

            source.Add("fac.faction",
                "{\"table\": \"fac.faction\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + OwnerFaction + "\", \"name_key\": \"l10n.fac.idle_chase_owner.name\", \"default_reaction\": \"hostile\"}," +
                "{\"id\": \"" + HostileFaction + "\", \"name_key\": \"l10n.fac.idle_chase_hostile.name\", \"default_reaction\": \"hostile\"}" +
                "]}");
            source.Add("fac.reaction_matrix", "{\"table\": \"fac.reaction_matrix\", \"schema_version\": 1, \"rows\": []}");

            source.Add(CreatureSchemas.TierDefinition.Name,
                "{\"table\": \"" + CreatureSchemas.TierDefinition.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + TierId + "\", \"name_key\": \"l10n.creature.tier.idle_chase_normal.name\", \"stat_multiplier\": 1}" +
                "]}");
            source.Add(CreatureSchemas.Template.Name,
                "{\"table\": \"" + CreatureSchemas.Template.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + PetTemplateId + "\", \"name_key\": \"l10n.creature.idle_chase_pet.name\", " +
                "\"level\": 1, \"tier\": \"" + TierId + "\", \"base_stats\": {\"stat.idle_chase_max_health\": 100}, " +
                "\"faction_id\": \"" + OwnerFaction + "\", \"display_ref\": \"display.idle_chase_pet\", " +
                "\"ai_rotation_ref\": \"" + RotationId + "\", \"ai_behavior_ref\": \"" + PetProfileId + "\"}," +
                "{\"id\": \"" + HostileTemplateId + "\", \"name_key\": \"l10n.creature.idle_chase_hostile.name\", " +
                "\"level\": 1, \"tier\": \"" + TierId + "\", \"base_stats\": {\"stat.idle_chase_max_health\": 100}, " +
                "\"faction_id\": \"" + HostileFaction + "\", \"display_ref\": \"display.idle_chase_hostile\"}" +
                "]}");

            source.Add("target.chain_def",
                "{\"table\": \"target.chain_def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"target.idle_chase_test\", \"source\": \"self\"}" +
                "]}");
            source.Add("skill.def",
                "{\"table\": \"skill.def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + SkillNeverId + "\", \"school\": \"skill.school.idle_chase_test\", \"kind\": \"active\", " +
                "\"range\": 0, \"cast_time\": 1, \"respects_gcd\": true, \"target_shape_ref\": \"target.idle_chase_test\", " +
                "\"effects\": []}" +
                "]}");
            source.Add(AiSchemas.Rotation.Name,
                "{\"table\": \"" + AiSchemas.Rotation.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + RotationId + "\", \"entries\": [" +
                "{\"priority\": 1, \"condition\": \"false\", \"skill_id\": \"" + SkillNeverId + "\"}" +
                "]}" +
                "]}");
            source.Add(AiSchemas.BehaviorProfile.Name,
                "{\"table\": \"" + AiSchemas.BehaviorProfile.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + PetProfileId + "\", \"perception_radius\": 8, \"leash_range\": 30, " +
                "\"combat_return_policy\": \"return_to_spawn\", \"rotation_ref\": \"" + RotationId + "\", " +
                "\"decision_interval\": 0.5}" +
                "]}");

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world, bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            var aiOptions = new AiOptions { AttackRange = 2.0, MoveSpeed = 4.0 };
            var combatOptions = new CombatOptions { LeaveCombatDelay = 5.0 };
            var summonOptions = new SummonOptions
            {
                JoinCombat = true,
                SyncCombatState = true,
                ShareThreat = true,
            };

            var assembly = new CarriersAssembly(
                bus, registry, rng, world, spatial, navigation: null, spatialSyncKinds: null,
                worldFlags: null, lootRoller: null, statOptions: null, combatOptions: combatOptions,
                skillOptions: null, targetingOptions: null, aiOptions: aiOptions, inventoryOptions: null,
                itemOptions: null, creatureOptions: null, summonOptions: summonOptions);

            var owner = new PlayerUnit(OwnerId, MapId, OwnerFaction, ArchetypeId) { Position = new Vec2(20, 0) };
            world.AddEntity(owner);
            // 本用例需要对主人调用 NotifyCombatEvent（SummonCombatRechaseTests 只对召唤物调用，
            // 未走到这一步）——CombatHost.NotifyCombatEvent 经 PowerHost.SetInCombat 要求单位已
            // 注册（惯例同 CombatTestSupport.RegisterUnit），不需要完整的 arch.class 装配。
            assembly.Rules.Stats.RegisterUnit(OwnerId);
            assembly.Rules.Powers.RegisterUnit(OwnerId, new[] { WellKnownPowers.Health });

            return new Fixture { World = world, Units = units, Assembly = assembly, Owner = owner };
        }

        [Fact]
        public void Summon_OwnerInCombatOutsidePerception_ShareThreat_TransitionsIdleToChase_TowardOwnersTarget()
        {
            var f = Build();

            // 召唤物在 (0,0) 召唤——perception_radius(8) 覆盖不到主人 (20,0) 附近的交战目标。
            var petId = f.Assembly.Summons.Summon(OwnerId, PetTemplateId, Vec2.Zero);
            Assert.Equal(BehaviorState.Idle, f.Assembly.Rules.Ai.GetBehaviorState(petId));

            // 主人在其交战目标旁边 (21,0)——距召唤物 21 米，远超 perception_radius(8)。
            var hostileId = f.Assembly.Creatures.Spawn(HostileTemplateId, MapId, new Vec2(21, 0), facing: 0);

            // 主人独立进战、独立产生仇恨（消费方场景：主人自己打，不是召唤物打）。
            f.Assembly.Rules.Combat.NotifyCombatEvent(OwnerId, hostileId);
            f.Assembly.Rules.Combat.GetThreatTable(OwnerId).AddThreat(OwnerId, hostileId, 10);
            Assert.True(f.Assembly.Rules.Combat.IsInCombat(OwnerId));

            var spawnPoint = f.Units.GetPosition(petId);

            var reachedChase = false;
            for (var tick = 0; tick < 10 && !reachedChase; tick++)
            {
                f.World.Tick(SimStep.Continuous(0.5));
                if (f.Assembly.Rules.Ai.GetBehaviorState(petId) == BehaviorState.Chase)
                {
                    reachedChase = true;
                }
            }

            Assert.True(reachedChase,
                "修复前的失败症状：召唤物停在 idle 原地不动——ShareThreat 合并进来的主人仇恨应驱动 " +
                "idle_to_chase（ADR-0087 决定 1，仇恨表是战斗中的目标权威，感知半径只用于获取新目标）");
            Assert.Equal(hostileId, f.Assembly.Rules.Ai.GetTarget(petId));

            var hostilePos = f.Units.GetPosition(hostileId);
            var petPosAfterChase = f.Units.GetPosition(petId);
            var distanceBefore = Vec2.Distance(spawnPoint, hostilePos);
            var distanceAfter = Vec2.Distance(petPosAfterChase, hostilePos);
            Assert.True(distanceAfter < distanceBefore,
                $"进入 chase 后应朝主人的交战目标靠近：召唤点距目标 {distanceBefore}，当前距目标 {distanceAfter}");
        }

        [Fact]
        public void Summon_InCombatFlagTrue_ButNoThreatAndNoPerceivedHostile_StillFollowsOwner()
        {
            var f = Build();

            var petId = f.Assembly.Summons.Summon(OwnerId, PetTemplateId, Vec2.Zero);

            // 主人自己进战但没有任何仇恨来源（如踩到一个已经清空仇恨表的战斗触发器）——
            // ShareThreat 无内容可合并，召唤物感知范围（8）内也没有任何敌对单位。
            f.Assembly.Rules.Combat.NotifyCombatEvent(OwnerId);
            Assert.True(f.Assembly.Rules.Combat.IsInCombat(OwnerId));

            var distanceBefore = Vec2.Distance(f.Units.GetPosition(petId), f.Units.GetPosition(OwnerId));

            var sawInCombatFlag = false;
            for (var tick = 0; tick < 6; tick++)
            {
                f.World.Tick(SimStep.Continuous(0.5));
                if (f.Assembly.Rules.Combat.IsInCombat(petId))
                {
                    sawInCombatFlag = true;
                }
                // 不变量核心：仇恨表为空、感知内无敌时，AI 不应该转入 chase/combat——它应该停在
                // idle（否则下面的"应继续跟随"断言就失去了意义，等价于验证了错误的场景）。
                Assert.Equal(BehaviorState.Idle, f.Assembly.Rules.Ai.GetBehaviorState(petId));
            }

            Assert.True(sawInCombatFlag,
                "SyncCombatState 应该让召唤物的 IsInCombat 标志随主人同步为 true（本用例的前提条件）");

            var distanceAfter = Vec2.Distance(f.Units.GetPosition(petId), f.Units.GetPosition(OwnerId));
            Assert.True(distanceAfter < distanceBefore,
                $"不变量：召唤物 IsInCombat=true 但仇恨表为空且感知内无敌时，不应被这一个战斗标志挡住跟随，" +
                $"应继续朝主人靠近（ADR-0087 决定 2）：跟随前距主人 {distanceBefore}，跟随后 {distanceAfter}");
        }
    }
}
