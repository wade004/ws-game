using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Creature;
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
    /// ADR-0088（消费方第三十三批反馈2根治）：目标运行期转为友方后（消费方直接写实体阵营字段，
    /// 因为框架没有入口），仇恨表不清、<c>AiHost.HandleCombat</c> 直接取 <c>GetTopThreat</c> 不检查
    /// 是否仍敌对——AI 一直追这个友方目标，施法 <c>NoValidTarget</c>，对旁边真正敌对的单位 0 出手。
    /// 用真实 <see cref="CarriersAssembly"/> 装配（真实 <see cref="AiHost"/> + <see cref="CombatHost"/> +
    /// <see cref="WorldUnitAccess"/>），验证：
    /// <list type="bullet">
    /// <item>复现：A 仇恨顶端是 B，运行期经新增的 <see cref="WorldUnitAccess.SetFaction"/>（决定4，
    /// 唯一的框架入口，替代消费方此前"直接写字段"的绕行）把 B 改判 A 同阵营后，A 的仇恨表应不再
    /// 含 B（决定5/6：<see cref="IThreatTable.RemoveSource"/> + <c>ThreatTable</c> 订阅
    /// <c>unit.faction_changed</c>），AI 目标应切到下一个仍敌对的 C。</item>
    /// <item>不变量见 <c>Tests.Rules.Ai.AiCombatTargetHostileGuardTests</c>（决定7 的护栏单独覆盖，
    /// 不在本文件重复）。</item>
    /// </list>
    /// </summary>
    public class FactionChangeThreatSyncTests
    {
        private static readonly Id MapId = new Id("map.faction_change_test");
        private static readonly Id FactionA = new Id("fac.faction_change_a");
        private static readonly Id FactionHostile = new Id("fac.faction_change_hostile");

        private static readonly Id MobTemplateId = new Id("creature.faction_change_mob");
        private static readonly Id HostileTemplateId = new Id("creature.faction_change_hostile");
        private static readonly Id TierId = new Id("creature.tier.faction_change_normal");
        private static readonly Id MobProfileId = new Id("ai.profile.faction_change_mob");
        private static readonly Id RotationId = new Id("ai.rotation.faction_change_trivial_false");
        private static readonly Id SkillNeverId = new Id("skill.faction_change_never");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public CarriersAssembly Assembly = null!;
        }

        private static Fixture Build()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();

            source.Add("item.budget_curve",
                "{\"table\": \"item.budget_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.budget.faction_change_default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]}");
            source.Add("combat.hit_table_config",
                "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.resist_curve",
                "{\"table\": \"combat.resist_curve\", \"schema_version\": 1, \"rows\": []}");

            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.faction_change_max_health\", \"name_key\": \"l10n.stat.faction_change_max_health.name\", " +
                "\"group\": \"primary\", \"default_base\": 0}," +
                "{\"id\": \"stat.move_speed\", \"name_key\": \"l10n.stat.move_speed.name\", " +
                "\"group\": \"primary\", \"default_base\": 4}" +
                "]}");
            source.Add("arch.power_type",
                "{\"table\": \"arch.power_type\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + WellKnownPowers.Health + "\", \"name_key\": \"l10n.power.faction_change_health.name\", " +
                "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.faction_change_max_health\"}, " +
                "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
                "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}" +
                "]}");

            source.Add("fac.faction",
                "{\"table\": \"fac.faction\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + FactionA + "\", \"name_key\": \"l10n.fac.faction_change_a.name\", \"default_reaction\": \"hostile\"}," +
                "{\"id\": \"" + FactionHostile + "\", \"name_key\": \"l10n.fac.faction_change_hostile.name\", \"default_reaction\": \"hostile\"}" +
                "]}");
            source.Add("fac.reaction_matrix", "{\"table\": \"fac.reaction_matrix\", \"schema_version\": 1, \"rows\": []}");

            source.Add(CreatureSchemas.TierDefinition.Name,
                "{\"table\": \"" + CreatureSchemas.TierDefinition.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + TierId + "\", \"name_key\": \"l10n.creature.tier.faction_change_normal.name\", \"stat_multiplier\": 1}" +
                "]}");
            source.Add(CreatureSchemas.Template.Name,
                "{\"table\": \"" + CreatureSchemas.Template.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + MobTemplateId + "\", \"name_key\": \"l10n.creature.faction_change_mob.name\", " +
                "\"level\": 1, \"tier\": \"" + TierId + "\", \"base_stats\": {\"stat.faction_change_max_health\": 100}, " +
                "\"faction_id\": \"" + FactionA + "\", \"display_ref\": \"display.faction_change_mob\", " +
                "\"ai_rotation_ref\": \"" + RotationId + "\", \"ai_behavior_ref\": \"" + MobProfileId + "\"}," +
                "{\"id\": \"" + HostileTemplateId + "\", \"name_key\": \"l10n.creature.faction_change_hostile.name\", " +
                "\"level\": 1, \"tier\": \"" + TierId + "\", \"base_stats\": {\"stat.faction_change_max_health\": 100}, " +
                "\"faction_id\": \"" + FactionHostile + "\", \"display_ref\": \"display.faction_change_hostile\"}" +
                "]}");

            source.Add("target.chain_def",
                "{\"table\": \"target.chain_def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"target.faction_change_test\", \"source\": \"self\"}" +
                "]}");
            source.Add("skill.def",
                "{\"table\": \"skill.def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + SkillNeverId + "\", \"school\": \"skill.school.faction_change_test\", \"kind\": \"active\", " +
                "\"range\": 0, \"cast_time\": 1, \"respects_gcd\": true, \"target_shape_ref\": \"target.faction_change_test\", " +
                "\"effects\": []}" +
                "]}");
            source.Add(AiSchemas.Rotation.Name,
                "{\"table\": \"" + AiSchemas.Rotation.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + RotationId + "\", \"entries\": [" +
                "{\"priority\": 1, \"condition\": \"false\", \"skill_id\": \"" + SkillNeverId + "\"}" +
                "]}" +
                "]}");
            // perception_radius 给很小值：全程用 ForceState + ThreatTable 摆入 combat，不依赖感知拾取。
            source.Add(AiSchemas.BehaviorProfile.Name,
                "{\"table\": \"" + AiSchemas.BehaviorProfile.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + MobProfileId + "\", \"perception_radius\": 0.01, \"leash_range\": 20, " +
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

            var assembly = new CarriersAssembly(
                bus, registry, rng, world, spatial, navigation: null, spatialSyncKinds: null,
                worldFlags: null, lootRoller: null, statOptions: null, combatOptions: null,
                skillOptions: null, targetingOptions: null, aiOptions: aiOptions, inventoryOptions: null,
                itemOptions: null, creatureOptions: null, summonOptions: null);

            return new Fixture { World = world, Units = units, Assembly = assembly };
        }

        [Fact]
        public void RuntimeFactionChange_PrunesThreatTable_AndAiRetargetsToNextHostile()
        {
            var f = Build();

            var mobId = f.Assembly.Creatures.Spawn(MobTemplateId, MapId, Vec2.Zero, facing: 0);
            var topHostileId = f.Assembly.Creatures.Spawn(HostileTemplateId, MapId, new Vec2(1, 0), facing: 0);
            var otherHostileId = f.Assembly.Creatures.Spawn(HostileTemplateId, MapId, new Vec2(-1, 0), facing: 0);

            f.Assembly.Rules.Ai.ForceState(mobId, BehaviorState.Combat);
            f.Assembly.Rules.Combat.GetThreatTable(mobId).AddThreat(mobId, topHostileId, 10);
            f.Assembly.Rules.Combat.GetThreatTable(mobId).AddThreat(mobId, otherHostileId, 5);

            f.World.Tick(SimStep.Continuous(0.5));
            Assert.Equal(topHostileId, f.Assembly.Rules.Ai.GetTarget(mobId));

            // 消费方场景：运行期把仇恨顶端 topHostileId 改判与 mobId 同阵营（唯一的框架入口，
            // 替代此前"直接写 Unit.FactionId 字段"的绕行）。
            f.Assembly.Units.SetFaction(topHostileId, FactionA);

            // 复现的第一部分（决定5/6）：不需要等下一 tick——SetFaction 用 PublishImmediate 同步
            // 派发 unit.faction_changed，ThreatTable 的订阅应已经现场清理完毕。
            var remaining = f.Assembly.Rules.Combat.GetThreatTable(mobId).GetAll(mobId);
            Assert.DoesNotContain(remaining, e => e.source == topHostileId);
            Assert.Contains(remaining, e => e.source == otherHostileId);

            // 复现的第二部分：AI 目标应切到下一个仍敌对的单位，不再是那个已经变友方的 topHostileId
            // （修复前的失败症状：目标恒为 topHostileId，Rotation 求值恒 NoValidTarget）。
            f.World.Tick(SimStep.Continuous(0.5));
            Assert.Equal(otherHostileId, f.Assembly.Rules.Ai.GetTarget(mobId));
        }
    }
}
