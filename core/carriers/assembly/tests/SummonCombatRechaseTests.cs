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
    /// ADR-0084 决定 4：召唤物联动实测（消费方反馈第二十九批"召唤物 JoinCombat=true 在战斗中不
    /// 跟随主人"叠加根治前的 combat 卡死，全程 0 输出）。用真实 <see cref="CarriersAssembly"/>
    /// 装配（真实 <see cref="AiHost"/> + 真实 <see cref="CombatHost"/> + <see cref="SummonHost"/> +
    /// <see cref="SummonTickHandler"/>，`world.Tick` 驱动，不手工模拟位移应用），验证：
    /// <list type="bullet">
    /// <item>召唤物（<c>JoinCombat=true</c>）战斗目标走出攻击范围后，召唤物自己的 <c>AiHost</c>
    /// 按 ADR-0084 的 <c>combat_to_chase</c> 回追（不再像根治前那样原地冻结）。</item>
    /// <item>回追超出召唤物自身的 <c>leash_range</c> 时按既有 <c>chase_to_return</c> 脱战——落点是
    /// <c>RegisterUnit</c> 传入的 <c>spawnPoint</c>（<c>SummonHost.Summon</c> 调用时的召唤位置，
    /// 本用例里等于主人召唤那一刻的位置），不是主人当前位置——即便主人此时已经走远。</item>
    /// <item><c>ICombatHost.IsInCombat</c> 与 <c>AiHost.BehaviorState</c> 是两条独立时间线：召唤物
    /// 回到 <c>idle</c> 之后，只要不再有新的战斗事件，<c>IsInCombat</c> 会在
    /// <c>CombatOptions.LeaveCombatDelay</c> 内自然转 false，<c>SummonTickHandler.TryFollow</c>
    /// 随即恢复跟随——且是跟向主人"当前"位置（不是旧的召唤位置），最终会跟上主人。</item>
    /// </list>
    /// </summary>
    public class SummonCombatRechaseTests
    {
        private static readonly Id MapId = new Id("map.rechase_test");
        private static readonly Id OwnerId = new Id("unit.rechase_owner");
        private static readonly Id OwnerFaction = new Id("fac.rechase_owner");
        private static readonly Id HostileFaction = new Id("fac.rechase_hostile");
        private static readonly Id ArchetypeId = new Id("arch.class.rechase_sample");

        private static readonly Id PetTemplateId = new Id("creature.rechase_pet");
        private static readonly Id HostileTemplateId = new Id("creature.rechase_hostile");
        private static readonly Id TierId = new Id("creature.tier.rechase_normal");
        private static readonly Id PetProfileId = new Id("ai.profile.rechase_pet");
        private static readonly Id RotationId = new Id("ai.rotation.rechase_trivial_false");
        private static readonly Id SkillNeverId = new Id("skill.rechase_never");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public CarriersAssembly Assembly = null!;
            public PlayerUnit Owner = null!;
        }

        private static Fixture Build()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();

            // 两类"必须已加载（哪怕零行）"的前置表，惯例同 CarriersAssemblyTests.AddMinimalRequiredTables。
            source.Add("item.budget_curve",
                "{\"table\": \"item.budget_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.budget.rechase_default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]}");
            source.Add("combat.hit_table_config",
                "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.resist_curve",
                "{\"table\": \"combat.resist_curve\", \"schema_version\": 1, \"rows\": []}");

            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.rechase_max_health\", \"name_key\": \"l10n.stat.rechase_max_health.name\", " +
                "\"group\": \"primary\", \"default_base\": 0}," +
                // MovementTickHandler.ResolveSpeed 按 MovementOptions.MoveSpeedStat（默认
                // "stat.move_speed"）读 StatHost——真实位移应用管线不复用 AiOptions.MoveSpeed，
                // 必须显式登记该属性（见该方法判断记录"速度来源"）。default_base=4，与
                // AiOptions.MoveSpeed 取同一数值只是方便计算预期位移，两者本无耦合关系。
                "{\"id\": \"stat.move_speed\", \"name_key\": \"l10n.stat.move_speed.name\", " +
                "\"group\": \"primary\", \"default_base\": 4}" +
                "]}");
            source.Add("arch.power_type",
                "{\"table\": \"arch.power_type\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"arch.power.rechase_health\", \"name_key\": \"l10n.power.rechase_health.name\", " +
                "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.rechase_max_health\"}, " +
                "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
                "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}" +
                "]}");

            source.Add("fac.faction",
                "{\"table\": \"fac.faction\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + OwnerFaction + "\", \"name_key\": \"l10n.fac.rechase_owner.name\", \"default_reaction\": \"hostile\"}," +
                "{\"id\": \"" + HostileFaction + "\", \"name_key\": \"l10n.fac.rechase_hostile.name\", \"default_reaction\": \"hostile\"}" +
                "]}");
            source.Add("fac.reaction_matrix", "{\"table\": \"fac.reaction_matrix\", \"schema_version\": 1, \"rows\": []}");

            source.Add(CreatureSchemas.TierDefinition.Name,
                "{\"table\": \"" + CreatureSchemas.TierDefinition.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + TierId + "\", \"name_key\": \"l10n.creature.tier.rechase_normal.name\", \"stat_multiplier\": 1}" +
                "]}");
            source.Add(CreatureSchemas.Template.Name,
                "{\"table\": \"" + CreatureSchemas.Template.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + PetTemplateId + "\", \"name_key\": \"l10n.creature.rechase_pet.name\", " +
                "\"level\": 1, \"tier\": \"" + TierId + "\", \"base_stats\": {\"stat.rechase_max_health\": 100}, " +
                "\"faction_id\": \"" + OwnerFaction + "\", \"display_ref\": \"display.rechase_pet\", " +
                "\"ai_rotation_ref\": \"" + RotationId + "\", \"ai_behavior_ref\": \"" + PetProfileId + "\"}," +
                "{\"id\": \"" + HostileTemplateId + "\", \"name_key\": \"l10n.creature.rechase_hostile.name\", " +
                "\"level\": 1, \"tier\": \"" + TierId + "\", \"base_stats\": {\"stat.rechase_max_health\": 100}, " +
                "\"faction_id\": \"" + HostileFaction + "\", \"display_ref\": \"display.rechase_hostile\"}" +
                "]}");

            source.Add("target.chain_def",
                "{\"table\": \"target.chain_def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"target.rechase_test\", \"source\": \"self\"}" +
                "]}");
            source.Add("skill.def",
                "{\"table\": \"skill.def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + SkillNeverId + "\", \"school\": \"skill.school.rechase_test\", \"kind\": \"active\", " +
                "\"range\": 0, \"cast_time\": 1, \"respects_gcd\": true, \"target_shape_ref\": \"target.rechase_test\", " +
                "\"effects\": []}" +
                "]}");
            source.Add(AiSchemas.Rotation.Name,
                "{\"table\": \"" + AiSchemas.Rotation.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + RotationId + "\", \"entries\": [" +
                "{\"priority\": 1, \"condition\": \"false\", \"skill_id\": \"" + SkillNeverId + "\"}" +
                "]}" +
                "]}");
            // leash_range=3：远小于 hostile 出生距离（10），迫使召唤物回追几步后必被拴绳拦下——
            // 聚焦验证"回追落点"，不是"追到目标"。perception_radius 给一个很小值：本用例全程直接
            // 用 ForceState+ThreatTable 摆入 combat，不依赖 idle_to_chase 的感知拾取。
            source.Add(AiSchemas.BehaviorProfile.Name,
                "{\"table\": \"" + AiSchemas.BehaviorProfile.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + PetProfileId + "\", \"perception_radius\": 0.01, \"leash_range\": 3, " +
                "\"combat_return_policy\": \"return_to_spawn\", \"rotation_ref\": \"" + RotationId + "\", " +
                "\"decision_interval\": 0.5}" +
                "]}");

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            var aiOptions = new AiOptions { AttackRange = 2.0, MoveSpeed = 4.0, CombatChaseHysteresis = 1.0 };
            var combatOptions = new CombatOptions { LeaveCombatDelay = 1.0 };
            var summonOptions = new SummonOptions { JoinCombat = true, SyncCombatState = false };

            var assembly = new CarriersAssembly(
                bus, registry, rng, world, spatial, navigation: null, spatialSyncKinds: null,
                worldFlags: null, lootRoller: null, statOptions: null, combatOptions: combatOptions,
                skillOptions: null, targetingOptions: null, aiOptions: aiOptions, inventoryOptions: null,
                itemOptions: null, creatureOptions: null, summonOptions: summonOptions);

            var owner = new PlayerUnit(OwnerId, MapId, OwnerFaction, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(owner);

            return new Fixture { World = world, Units = units, Assembly = assembly, Owner = owner };
        }

        [Fact]
        public void Summon_JoinCombatTrue_RechasesTarget_ThenLeashReturnsToSummonPoint_ThenResumesFollowingOwnersCurrentPosition()
        {
            var f = Build();

            // 主人在 (0,0) 召唤宠物——召唤位置即 RegisterUnit 的 spawnPoint。
            var petId = f.Assembly.Summons.Summon(OwnerId, PetTemplateId, Vec2.Zero);
            // 敌对目标在 (10,0)：远超 AttackRange+Hysteresis(3) 也远超 leash_range(3)，逼迫"回追几步
            // 后被拴绳拦下"这条路径。
            var hostileId = f.Assembly.Creatures.Spawn(HostileTemplateId, MapId, new Vec2(10, 0), facing: 0);

            // 摆入 combat：与消费方最小构造等价（"与敌对 B 相距不超过 AttackRange 使 A 进 combat，
            // 仇恨表里有 B"），只是这里额外验证真实 CombatHost.IsInCombat 联动 SummonTickHandler。
            f.Assembly.Rules.Ai.ForceState(petId, BehaviorState.Combat);
            f.Assembly.Rules.Combat.GetThreatTable(petId).AddThreat(petId, hostileId, 10);
            f.Assembly.Rules.Combat.NotifyCombatEvent(petId, hostileId);
            Assert.True(f.Assembly.Rules.Combat.IsInCombat(petId));

            // 主人在召唤后立刻走远——用于区分"回到召唤点"还是"跟着主人当前位置"。
            f.Units.SetPosition(OwnerId, new Vec2(50, 50));

            // -----------------------------------------------------------------
            // 阶段一：combat -> chase -> return -> idle（leash 拦下回追），全程真实 world.Tick 驱动
            // （SummonTickHandler 先于 AiTickHandler 执行，MovementAndNavigation 阶段真实应用位移，
            // 不手工模拟）。
            // -----------------------------------------------------------------
            var sawChase = false;
            var reachedIdle = false;
            for (var tick = 0; tick < 20 && !reachedIdle; tick++)
            {
                f.World.Tick(SimStep.Continuous(0.5));

                var state = f.Assembly.Rules.Ai.GetBehaviorState(petId);
                if (state == BehaviorState.Chase) sawChase = true;
                if (state == BehaviorState.Idle && sawChase)
                {
                    reachedIdle = true;
                }

                // 全程战斗态未清空仇恨表前，IsInCombat 应保持 true——JoinCombat=true 时 TryFollow
                // 完全跳过跟随，不会有第二股"朝主人移动"的意图与 AI 自己的回追/脱战移动打架。
                Assert.True(f.Assembly.Rules.Combat.IsInCombat(petId));
            }

            Assert.True(sawChase, "目标脱离攻击范围后召唤物应回追（ADR-0084 combat_to_chase，根治前的失败症状是全程冻结）");
            Assert.True(reachedIdle, "回追超出 leash_range 后应被既有 chase_to_return 拦下并回到 idle");

            var petPosAfterReturn = f.Units.GetPosition(petId);
            Assert.True(Vec2.Distance(petPosAfterReturn, Vec2.Zero) < 0.6,
                $"回追脱战后的落点应是召唤点 (0,0)（RegisterUnit 的 spawnPoint），不是主人当前位置 (50,50)；实测 {petPosAfterReturn}");

            // -----------------------------------------------------------------
            // 阶段二：模拟"战斗真正结束"（清空仇恨表——现实中对应命中判定的战斗系统在这一刻不再
            // 产生新的战斗事件），CombatHost 的脱战计时器在 LeaveCombatDelay 内自然让 IsInCombat
            // 转 false，SummonTickHandler.TryFollow 随即恢复跟随——且是跟向主人"当前"位置。
            // -----------------------------------------------------------------
            f.Assembly.Rules.Combat.GetThreatTable(petId).Clear(petId);

            var leftCombat = false;
            for (var tick = 0; tick < 10 && !leftCombat; tick++)
            {
                f.World.Tick(SimStep.Continuous(0.5));
                if (!f.Assembly.Rules.Combat.IsInCombat(petId))
                {
                    leftCombat = true;
                }
            }
            Assert.True(leftCombat, $"清空仇恨表后 IsInCombat 应在 LeaveCombatDelay({1.0}) 内自然转 false");

            var distanceToOwnerBeforeFollow = Vec2.Distance(f.Units.GetPosition(petId), f.Units.GetPosition(OwnerId));

            for (var tick = 0; tick < 6; tick++)
            {
                f.World.Tick(SimStep.Continuous(0.5));
            }

            var finalPetPos = f.Units.GetPosition(petId);
            var distanceToOwnerAfterFollow = Vec2.Distance(finalPetPos, f.Units.GetPosition(OwnerId));

            Assert.True(distanceToOwnerAfterFollow < distanceToOwnerBeforeFollow,
                $"IsInCombat 转 false 后应恢复跟随并朝主人\"当前\"位置 (50,50) 靠拢，不应停在召唤点：" +
                $"跟随前距主人 {distanceToOwnerBeforeFollow}，跟随后 {distanceToOwnerAfterFollow}，召唤物落点 {finalPetPos}");
        }
    }
}
